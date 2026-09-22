using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.ExamSessions;

namespace HealthExam.Application.RegistrationForms;

public interface ISaveRegistrationFormSectionHandler
{
    Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(
        SaveRegistrationFormSectionCommand command, CancellationToken ct = default);
}

public class SaveRegistrationFormSectionHandler : ISaveRegistrationFormSectionHandler
{
    private readonly IRegistrationFormRepository _repo;
    private readonly IHisEmrClient _client;
    private readonly IHisFormDefinitionCache _cache;
    private readonly IHisCredentialOptions _options;
    private readonly IUnitOfWork _uow;
    private readonly IGetRegistrationFormHandler _getHandler;
    private readonly IEnsureHisAdmission _ensureAdmission;

    public SaveRegistrationFormSectionHandler(
        IRegistrationFormRepository repo,
        IHisEmrClient client,
        IHisFormDefinitionCache cache,
        IHisCredentialOptions options,
        IUnitOfWork uow,
        IGetRegistrationFormHandler getHandler,
        IEnsureHisAdmission ensureAdmission = null)
    {
        _repo = repo;
        _client = client;
        _cache = cache;
        _options = options;
        _uow = uow;
        _getHandler = getHandler;
        _ensureAdmission = ensureAdmission;
    }

    public async Task<ApplicationResult<RegistrationRecordForm>> HandleAsync(
        SaveRegistrationFormSectionCommand command, CancellationToken ct = default)
    {
        var normalizedKind = (command.SectionKind ?? "").Trim().ToUpperInvariant();
        if (normalizedKind != "HISTORY" && normalizedKind != "EXTRA_INFO")
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.BadRequest, "Phân đoạn không hợp lệ (chỉ hỗ trợ HISTORY hoặc EXTRA_INFO)");
        }

        var record = await _repo.GetRecordWithSessionAsync(command.DivisionId, command.RecordId, forUpdate: true, ct);
        if (record == null)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        // Spec §2: hồ sơ đã ký kết luận là bất biến — PDF đã ký chốt cả phiếu đăng ký.
        if (record.SignStatus == ExamRecordSignStatus.Signed)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ đã ký kết luận, không sửa được");
        }

        if (string.IsNullOrWhiteSpace(record.VariantCode))
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.InvalidState, "Hồ sơ chưa được gán nhóm khám");
        }

        var mapping = await _repo.GetActiveMappingAsync(command.DivisionId, record.VariantCode, ct);
        if (mapping == null)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.NotFound, $"Nhóm khám {record.VariantCode} chưa được cấu hình biểu mẫu hoặc đang bị vô hiệu hoá");
        }

        var targetSection = mapping.Sections.SingleOrDefault(s => s.SectionKind == normalizedKind);
        var otherKind = normalizedKind == "HISTORY" ? "EXTRA_INFO" : "HISTORY";
        var otherSection = mapping.Sections.SingleOrDefault(s => s.SectionKind == otherKind);

        if (targetSection == null || otherSection == null)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(
                ApplicationFailureCode.InvalidState, "Cấu hình biểu mẫu thiếu phân đoạn HISTORY hoặc EXTRA_INFO");
        }

        HisFormDefinitionResult hisDef;
        try
        {
            hisDef = await HisDefinitionResolver.GetDefinitionAsync(
                _client, _cache, command.DivisionId, mapping.TemplateCode, command.Credential, command.TraceId, ct);
        }
        catch (HisApplicationException ex)
        {
            return ApplicationResult<RegistrationRecordForm>.Fail(ex.Code, ex.Message, ex.Payload);
        }

        var targetWritableLeaves = new Dictionary<Guid, (string DataType, string ControlType)>();
        var choiceLabels = new Dictionary<Guid, Dictionary<string, string>>();
        using (var layoutDoc = JsonDocument.Parse(hisDef.LayoutJson ?? "[]"))
        {
            if (layoutDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in layoutDoc.RootElement.EnumerateArray())
                {
                    var controlType = RegistrationFormNormalizer.GetString(n, "ControlType") ?? "TXT";
                    var dataType = RegistrationFormNormalizer.GetString(n, "DataType") ?? "S";
                    var readOnly = RegistrationFormNormalizer.GetBool(n, "ReadOnly") ??
                                   RegistrationFormNormalizer.GetBool(n, "IsReadOnly") ?? false;

                    var isLabelOrGroup = string.Equals(controlType, "LBL", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(controlType, "GRP", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(controlType, "LABEL", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(controlType, "GROUP", StringComparison.OrdinalIgnoreCase);

                    var itemIdStr = RegistrationFormNormalizer.GetString(n, "ItemID") ?? RegistrationFormNormalizer.GetString(n, "ItemId") ?? RegistrationFormNormalizer.GetString(n, "NodeID");
                    if (Guid.TryParse(itemIdStr, out var itemId) && itemId != Guid.Empty)
                    {
                        if (!isLabelOrGroup && !readOnly)
                        {
                            var itemGroupId = RegistrationFormNormalizer.GetInt(n, "ItemGroupID");
                            if (itemGroupId == targetSection.ItemGroupID)
                            {
                                targetWritableLeaves[itemId] = (dataType, controlType);
                            }
                        }

                        var hasChoices = (n.TryGetProperty("Choices", out var choicesProp) && choicesProp.ValueKind == JsonValueKind.Array) ||
                                         (n.TryGetProperty("Options", out choicesProp) && choicesProp.ValueKind == JsonValueKind.Array);
                        if (hasChoices)
                        {
                            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var c in choicesProp.EnumerateArray())
                            {
                                var cCode = RegistrationFormNormalizer.GetString(c, "Code") ?? RegistrationFormNormalizer.GetString(c, "Value");
                                var cLabel = RegistrationFormNormalizer.GetString(c, "DisplayName") ??
                                             RegistrationFormNormalizer.GetString(c, "CodeName") ??
                                             RegistrationFormNormalizer.GetString(c, "Label") ??
                                             RegistrationFormNormalizer.GetString(c, "Name");
                                if (!string.IsNullOrWhiteSpace(cCode) && !string.IsNullOrWhiteSpace(cLabel))
                                {
                                    var prefix = cCode + " - ";
                                    if (cLabel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        cLabel = cLabel[prefix.Length..].Trim();
                                    }
                                    labels[cCode] = cLabel;
                                }
                            }
                            if (labels.Count > 0)
                            {
                                choiceLabels[itemId] = labels;
                            }
                        }
                    }
                }
            }
        }

        if (command.Request?.Fields != null)
        {
            foreach (var field in command.Request.Fields)
            {
                if (!targetWritableLeaves.ContainsKey(field.ItemId))
                {
                    return ApplicationResult<RegistrationRecordForm>.Fail(
                        ApplicationFailureCode.BadRequest,
                        $"Trường {field.ItemId} không thuộc phân đoạn {normalizedKind} hoặc là trường chỉ đọc");
                }
            }
        }

        // Ensure admission if needed
        var departmentId = record.Session?.DepartmentID > 0
            ? record.Session.DepartmentID
            : _options.KskDepartmentId.GetValueOrDefault();

        if (!record.AdmissionID.HasValue || record.AdmissionID.Value <= 0)
        {
            var actorIdNum = long.TryParse(command.ActorId, out var parsedActorId) ? parsedActorId : 0L;
            var ensureOp = _ensureAdmission ?? new EnsureHisAdmission(
                new RepoRecordAdapter(record), _client, _options, new SystemClock(), _uow);

            var admRes = await ensureOp.HandleAsync(new EnsureHisAdmissionCommand(
                command.DivisionId,
                command.RecordId,
                new HisCallContext(command.Credential, command.TraceId, command.DivisionId),
                actorIdNum,
                command.ActorKind), ct);

            if (!admRes.IsSuccess)
            {
                return ApplicationResult<RegistrationRecordForm>.Fail(
                    admRes.Failure.Code, admRes.Failure.Message, admRes.Failure.Payload);
            }

            record.AdmissionID = admRes.Value;
        }

        // Read-merge-write
        var existingDetails = new Dictionary<Guid, (string Value, string Text, string DataType, string ControlStyle)>();
        Guid? existingEmrDataId = record.HisEmrDataID;

        if (record.AdmissionID.HasValue && record.AdmissionID.Value > 0)
        {
            try
            {
                var path = $"{HisConstants.RouteREmr}?EMRDataID={record.HisEmrDataID ?? Guid.Empty}&TemplateID={hisDef.TemplateId}&AdmissionID={record.AdmissionID.Value}&IsInherit=false";
                var readRes = await _client.SendAsync(
                    HisOperation.ReadFormData,
                    new HisRequest(path, "GET", command.Credential, TraceId: command.TraceId, DivisionId: command.DivisionId),
                    ct);

                if (readRes.IsSuccess && !string.IsNullOrWhiteSpace(readRes.Value.RawJson))
                {
                    using var readDoc = JsonDocument.Parse(readRes.Value.RawJson);
                    var root = readDoc.RootElement;
                    ExtractDetailsWithMeta(root, existingDetails);

                    string returnedEmrIdStr = RegistrationFormNormalizer.GetString(root, "EMRDataID") ??
                                              RegistrationFormNormalizer.GetString(root, "Id");
                    if (Guid.TryParse(returnedEmrIdStr, out var readEmrId) && readEmrId != Guid.Empty)
                    {
                        existingEmrDataId = readEmrId;
                    }
                }
            }
            catch
            {
                // Non-fatal if reading previous data fails
            }
        }

        if (command.Request?.Fields != null)
        {
            foreach (var field in command.Request.Fields)
            {
                var meta = targetWritableLeaves[field.ItemId];
                var text = field.Text;
                var val = field.Value ?? "";
                if (choiceLabels.TryGetValue(field.ItemId, out var labels) && labels.TryGetValue(val.Trim(), out var choiceText))
                {
                    text = choiceText;
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    text = val;
                }
                existingDetails[field.ItemId] = (val, text, meta.DataType, meta.ControlType);
            }
        }

        foreach (var key in existingDetails.Keys.ToList())
        {
            var item = existingDetails[key];
            var value = item.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value) &&
                choiceLabels.TryGetValue(key, out var labels) &&
                labels.TryGetValue(value, out var choiceText))
            {
                if (string.IsNullOrWhiteSpace(item.Text) ||
                    string.Equals(item.Text.Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    existingDetails[key] = (item.Value, choiceText, item.DataType, item.ControlStyle);
                }
            }
        }

        var detailsToSave = existingDetails.Select(kv => new
        {
            ItemID = kv.Key,
            Value = kv.Value.Value,
            Text = string.IsNullOrWhiteSpace(kv.Value.Text) ? kv.Value.Value : kv.Value.Text,
            DataType = kv.Value.DataType,
            ControlStyle = kv.Value.ControlStyle
        }).ToList();

        var cuemrPayload = new
        {
            EMRDataID = existingEmrDataId ?? Guid.Empty,
            TemplateID = hisDef.TemplateId,
            AdmissionID = record.AdmissionID.Value,
            PatientID = record.Patient?.HisPatientID ?? 0,
            PatientCode = record.Patient?.PatientCode ?? "",
            DepartmentID = departmentId > 0 ? departmentId : 1,
            VoucherDate = DateTime.UtcNow,
            IsDraft = command.Request?.IsDraft ?? true,
            Details = detailsToSave
        };

        var saveRes = await _client.SendAsync(
            HisOperation.SaveFormData,
            new HisRequest(
                HisConstants.RouteCueEmr,
                "POST",
                command.Credential,
                JsonSerializer.Serialize(cuemrPayload),
                TraceId: command.TraceId,
                DivisionId: command.DivisionId),
            ct);

        if (!saveRes.IsSuccess)
        {
            record.HisFormSyncStatus = "Failed";
            var msg = !string.IsNullOrWhiteSpace(saveRes.Message) ? saveRes.Message : "Lưu dữ liệu biểu mẫu lên HIS thất bại";
            record.HisFormSyncError = msg.Length > 1000 ? msg[..1000] : msg;
            await _uow.SaveChangesAsync(CancellationToken.None);

            return HisOutcomeMapper.ToApplicationResult<RegistrationRecordForm>(
                HisClientResult<RegistrationRecordForm>.Fail(saveRes.Outcome, saveRes.Message));
        }

        Guid savedEmrDataId = Guid.Empty;
        var rawGuid = saveRes.Value.RawJson?.Trim('"', ' ', '\r', '\n');
        if (Guid.TryParse(rawGuid, out var directGuid) && directGuid != Guid.Empty)
        {
            savedEmrDataId = directGuid;
        }
        else if (!string.IsNullOrWhiteSpace(saveRes.Value.RawJson))
        {
            try
            {
                using var saveDoc = JsonDocument.Parse(saveRes.Value.RawJson);
                var sRoot = saveDoc.RootElement;
                string guidStr = null;
                if (sRoot.ValueKind == JsonValueKind.String)
                {
                    guidStr = sRoot.GetString();
                }
                else if (sRoot.ValueKind == JsonValueKind.Object)
                {
                    guidStr = RegistrationFormNormalizer.GetString(sRoot, "Id") ??
                              RegistrationFormNormalizer.GetString(sRoot, "EMRDataID") ??
                              RegistrationFormNormalizer.GetString(sRoot, "Data");
                }
                Guid.TryParse(guidStr, out savedEmrDataId);
            }
            catch
            {
            }
        }

        if (savedEmrDataId == Guid.Empty && existingEmrDataId.HasValue && existingEmrDataId.Value != Guid.Empty)
        {
            savedEmrDataId = existingEmrDataId.Value;
        }

        if (savedEmrDataId != Guid.Empty)
        {
            record.HisEmrDataID = savedEmrDataId;
        }
        record.HisFormTemplateID = hisDef.TemplateId;
        record.HisFormSyncStatus = "Synced";
        record.HisFormSyncError = "";
        await _uow.SaveChangesAsync(ct);

        return await _getHandler.HandleAsync(
            new GetRegistrationFormQuery(command.DivisionId, command.RecordId, command.Credential, command.TraceId), ct);
    }

    private sealed class RepoRecordAdapter : IExamRecordRepository
    {
        private readonly ExamRecord _record;
        public RepoRecordAdapter(ExamRecord record) => _record = record;

        public Task<ExamRecord> GetAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
            => Task.FromResult(_record);

        public Task<ExamRecord> GetWithRegistrationAsync(string divisionId, Guid recordId, bool forUpdate = false, CancellationToken ct = default)
            => Task.FromResult(_record);

        public Task<PageResult<ExamRecordResult>> ListAsync(string divisionId, ExamRecordFilter filter, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PageResult<ExamRecordResult>> ListSignedByPatientLineageAsync(string divisionId, Guid profileLineageID, int page, int size, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamRecordResult> GetResultAsync(string divisionId, Guid recordId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsInSessionAsync(string divisionId, Guid sessionId, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsDuplicateAsync(string divisionId, Guid sessionId, string identityNumber, string patientCode, Guid? excludingRecordId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamSession> GetOrCreateDefaultSessionAsync(string divisionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamSessionProgressResult> GetProgressAsync(string divisionId, Guid sessionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamRecordResult> VerifyPortalCredentialsAsync(string divisionId, string patientCode, string identifier, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamRecordResult> VerifyPortalCredentialsAsync(string divisionId, string patientCode, string identityNumber, string insuranceNumber, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamPackage> GetPackageAsync(string divisionId, Guid packageId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(string Code, string Name)?> ResolveMasterDataAsync(string divisionId, string category, string code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MasterDataOption> ResolveMasterDataOptionAsync(string divisionId, string category, string code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(string Code, string Name)?> ResolveWardAsync(string divisionId, string provinceCode, string wardCode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamRecord> ResolveForWebhookAsync(string divisionId, Guid? submissionId, string subjectId, string hostRefId, bool forUpdate = true, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExamRecord> LockRecordAsync(Guid recordId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ReconcileCandidate>> FindReconcileCandidatesAsync(string divisionId = null, Guid? sessionId = null, int batchSize = 100, CancellationToken ct = default) => throw new NotImplementedException();
        public void Add(ExamRecord record) => throw new NotImplementedException();
    }

    private static void ExtractDetailsWithMeta(
        JsonElement root,
        Dictionary<Guid, (string Value, string Text, string DataType, string ControlStyle)> existingDetails)
    {
        IEnumerable<JsonElement> detailElements = null;
        if (root.ValueKind == JsonValueKind.Array)
        {
            detailElements = root.EnumerateArray();
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("Details", out var details) && details.ValueKind == JsonValueKind.Array)
                detailElements = details.EnumerateArray();
            else if (root.TryGetProperty("Data", out var data))
            {
                if (data.ValueKind == JsonValueKind.Array)
                    detailElements = data.EnumerateArray();
                else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("Details", out var innerDetails) && innerDetails.ValueKind == JsonValueKind.Array)
                    detailElements = innerDetails.EnumerateArray();
            }
        }

        if (detailElements == null) return;

        foreach (var d in detailElements)
        {
            var itemIdStr = RegistrationFormNormalizer.GetString(d, "ItemID") ?? RegistrationFormNormalizer.GetString(d, "ItemId");
            if (!string.IsNullOrEmpty(itemIdStr) && Guid.TryParse(itemIdStr, out var itemId) && itemId != Guid.Empty)
            {
                var val = RegistrationFormNormalizer.GetString(d, "Value") ?? "";
                var text = RegistrationFormNormalizer.GetString(d, "Text");
                var dataType = RegistrationFormNormalizer.GetString(d, "DataType") ?? "S";
                var controlStyle = RegistrationFormNormalizer.GetString(d, "ControlStyle") ?? "TXT";
                existingDetails[itemId] = (val, text, dataType, controlStyle);
            }
        }
    }
}
