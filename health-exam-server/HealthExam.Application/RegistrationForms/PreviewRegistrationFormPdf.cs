using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.His;
using HealthExam.Application.Integrations;
using HealthExam.Domain.ExamRecords;

namespace HealthExam.Application.RegistrationForms;

public interface IPreviewRegistrationFormPdfHandler
{
    Task<ApplicationResult<byte[]>> HandleAsync(
        PreviewRegistrationFormPdfQuery query, CancellationToken ct = default);
}

public class PreviewRegistrationFormPdfHandler : IPreviewRegistrationFormPdfHandler
{
    private readonly IRegistrationFormRepository _repo;
    private readonly IHisEmrClient _client;
    private readonly IExamFileStore _store;

    public PreviewRegistrationFormPdfHandler(
        IRegistrationFormRepository repo,
        IHisEmrClient client,
        IExamFileStore store)
    {
        _repo = repo;
        _client = client;
        _store = store;
    }

    public async Task<ApplicationResult<byte[]>> HandleAsync(
        PreviewRegistrationFormPdfQuery query, CancellationToken ct = default)
    {
        var record = await _repo.GetRecordAsync(query.DivisionId, query.RecordId, forUpdate: false, ct);
        if (record == null)
        {
            return ApplicationResult<byte[]>.Fail(
                ApplicationFailureCode.NotFound, "Không tìm thấy hồ sơ khám");
        }

        if (record.SignStatus == ExamRecordSignStatus.Signed)
        {
            // Đã Signed thì tuyệt đối không render lại bản nháp — file thiếu là sự cố dữ liệu,
            // phải báo lỗi để người vận hành xử lý, không âm thầm trả bản không chữ ký.
            if (string.IsNullOrWhiteSpace(record.SignedFilePath))
            {
                return ApplicationResult<byte[]>.Fail(
                    ApplicationFailureCode.NotFound,
                    "Hồ sơ đã ký nhưng chưa có đường dẫn file");
            }

            var signed = await _store.DownloadAsync(record.SignedFilePath, ct);
            if (signed == null || signed.Length == 0)
            {
                return ApplicationResult<byte[]>.Fail(
                    ApplicationFailureCode.NotFound,
                    "Không đọc được file đã ký, vui lòng báo quản trị");
            }

            return ApplicationResult<byte[]>.Success(signed);
        }

        var hisRequest = new HisRequest(
            "", "GET", query.Credential,
            TraceId: query.TraceId,
            DivisionId: query.DivisionId);

        if (!record.HisEmrDataID.HasValue || record.HisEmrDataID == Guid.Empty)
        {
            return ApplicationResult<byte[]>.Fail(
                ApplicationFailureCode.InvalidState,
                "Biểu mẫu chưa được lưu lên HIS");
        }

        var draft = await _client.RenderFormPdfAsync(record.HisEmrDataID.Value, hisRequest, ct);
        return HisOutcomeMapper.ToApplicationResult(draft);
    }
}
