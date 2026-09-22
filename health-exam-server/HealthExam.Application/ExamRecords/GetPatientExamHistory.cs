using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Patients;

namespace HealthExam.Application.ExamRecords;

/// <summary>
/// "Đợt khám trước" — các hồ sơ KSK ĐÃ KÝ KẾT LUẬN của người bệnh đang mở.
///
/// Size mặc định 5 chứ không phải 20 như GET /v1/exam-records: khối này là chú thích dưới
/// bảng Danh sách hồ sơ khám trong một cột hẹp, không phải màn hình chính.
/// </summary>
public sealed record GetPatientExamHistoryQuery(
    string DivisionId,
    Guid PatientRefID,
    int Page = 1,
    int Size = 0);

public interface IGetPatientExamHistoryHandler
{
    Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        GetPatientExamHistoryQuery query, CancellationToken ct = default);
}

public sealed class GetPatientExamHistoryHandler : IGetPatientExamHistoryHandler
{
    public const int DefaultSize = 5;
    public const int MaxSize = 200;

    private readonly IPatientRepository _patientRepository;
    private readonly IExamRecordRepository _examRecordRepository;

    public GetPatientExamHistoryHandler(
        IPatientRepository patientRepository,
        IExamRecordRepository examRecordRepository)
    {
        _patientRepository = patientRepository;
        _examRecordRepository = examRecordRepository;
    }

    public async Task<ApplicationResult<PageResult<ExamRecordResult>>> HandleAsync(
        GetPatientExamHistoryQuery query, CancellationToken ct = default)
    {
        if (query.PatientRefID == Guid.Empty)
        {
            return ApplicationResult<PageResult<ExamRecordResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "PatientRefID không hợp lệ.");
        }

        // Một người khám nhiều đợt có thể mang nhiều PatientRefID (mỗi lần sửa thông tin cá
        // nhân sinh một phiên bản). Dòng hồ sơ mới là danh tính của NGƯỜI.
        var lineageID = await _patientRepository.FindProfileLineageIdAsync(
            query.DivisionId, query.PatientRefID, ct);

        if (lineageID == null)
        {
            return ApplicationResult<PageResult<ExamRecordResult>>.Fail(
                ApplicationFailureCode.NotFound,
                "Không tìm thấy hồ sơ người bệnh.");
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.Size < 1 ? DefaultSize : (query.Size > MaxSize ? MaxSize : query.Size);

        var result = await _examRecordRepository.ListSignedByPatientLineageAsync(
            query.DivisionId, lineageID.Value, page, size, ct);

        return ApplicationResult<PageResult<ExamRecordResult>>.Success(result);
    }
}
