using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Patients;

namespace HealthExam.Application.Patients;

/// <summary>
/// Tiêu chí tìm hồ sơ active: một keyword áp lên một hoặc nhiều trường (OR).
/// <see cref="Latest"/> (không keyword) nghĩa là lấy các hồ sơ mới cập nhật nhất.
/// </summary>
public sealed record PatientSearchCriteria(
    string Keyword,
    IReadOnlyList<PatientSearchField> Fields)
{
    public static readonly PatientSearchCriteria Latest = new(null, Array.Empty<PatientSearchField>());

    public bool HasKeyword => !string.IsNullOrWhiteSpace(Keyword) && Fields != null && Fields.Count > 0;
}

/// <summary>
/// Truy vấn tìm người bệnh. <paramref name="Type"/> là chuỗi thô từ query string:
/// identity | code | name | phone | auto; null/rỗng = auto. <paramref name="Keyword"/>
/// null/rỗng = lấy các hồ sơ mới cập nhật nhất, bỏ qua <paramref name="Type"/>.
/// </summary>
public sealed record SearchPatientsQuery(
    string DivisionId,
    string Type,
    string Keyword);

/// <summary>Dòng tóm tắt để hiển thị danh sách gợi ý; lấy chi tiết bằng GetPatientProfile.</summary>
public sealed record PatientSearchItem(
    Guid PatientRefID,
    string PatientCode,
    string FullName,
    short? BirthYear,
    short GenderID,
    string IdentityNumber);

public interface ISearchPatientsHandler
{
    Task<ApplicationResult<IReadOnlyList<PatientSearchItem>>> HandleAsync(
        SearchPatientsQuery query, CancellationToken ct = default);
}

public sealed class SearchPatientsHandler : ISearchPatientsHandler
{
    public const int MaxResults = 20;

    private static readonly IReadOnlyDictionary<string, PatientSearchField> ExplicitTypes =
        new Dictionary<string, PatientSearchField>(StringComparer.OrdinalIgnoreCase)
        {
            ["identity"] = PatientSearchField.Identity,
            ["code"] = PatientSearchField.Code,
            ["name"] = PatientSearchField.Name,
            ["phone"] = PatientSearchField.Phone
        };

    private readonly IPatientRepository _patientRepository;

    public SearchPatientsHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<ApplicationResult<IReadOnlyList<PatientSearchItem>>> HandleAsync(
        SearchPatientsQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Keyword))
        {
            var latest = await _patientRepository.SearchActiveAsync(
                query.DivisionId, PatientSearchCriteria.Latest, MaxResults, ct);
            return ApplicationResult<IReadOnlyList<PatientSearchItem>>.Success(
                latest.Select(ToItem).ToList());
        }

        var keyword = query.Keyword.Trim();
        var type = string.IsNullOrWhiteSpace(query.Type) ? "auto" : query.Type.Trim();

        IReadOnlyList<PatientSearchField> fields;
        if (type.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            fields = new[]
            {
                PatientSearchField.Identity,
                PatientSearchField.Phone,
                PatientSearchField.Code,
                PatientSearchField.Name
            };
        }
        else if (ExplicitTypes.TryGetValue(type, out var field))
        {
            fields = new[] { field };
        }
        else
        {
            return ApplicationResult<IReadOnlyList<PatientSearchItem>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Loại tìm kiếm không hợp lệ. Chấp nhận: identity, code, name, phone, auto.");
        }

        var patients = await _patientRepository.SearchActiveAsync(
            query.DivisionId, new PatientSearchCriteria(keyword, fields), MaxResults, ct);

        IReadOnlyList<PatientSearchItem> items = patients.Select(ToItem).ToList();
        return ApplicationResult<IReadOnlyList<PatientSearchItem>>.Success(items);
    }

    private static PatientSearchItem ToItem(Patient patient) => new(
        PatientRefID: patient.PatientRefID,
        PatientCode: patient.PatientCode,
        FullName: patient.FullName,
        BirthYear: patient.BirthYear ?? (patient.Dob.HasValue ? (short)patient.Dob.Value.Year : null),
        GenderID: patient.GenderID,
        IdentityNumber: patient.IdentityNumber);
}
