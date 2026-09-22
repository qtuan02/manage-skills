using HealthExam.API.Contracts;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;

namespace HealthExam.Server.Service;

/// <summary>
/// Tên trạng thái tiếng Việt trả kèm mỗi bản ghi. Server trả tên thay vì để FE tự map:
/// bảng số này sẽ còn thay đổi (ExamSessionState mới là "chốt tạm" — 01-db-model §2.2
/// Q-DB-01), và một bảng map nằm ở FE nữa là hai chỗ phải sửa cùng lúc.
/// </summary>
public static class StateNames
{
    public static string Of(ExamRecordState state) => state switch
    {
        ExamRecordState.NotRegistered => "Chưa đăng ký",
        ExamRecordState.Waiting => "Chờ khám",
        ExamRecordState.InProgress => "Đang khám",
        ExamRecordState.Completed => "Đã khám",
        ExamRecordState.RegistrationCancelled => "Hủy đăng ký",
        ExamRecordState.ExamCancelled => "Hủy khám",
        _ => ""
    };

    public static string Of(ExamSessionState state) => state switch
    {
        ExamSessionState.Draft => "Nháp",
        ExamSessionState.Open => "Đang mở",
        ExamSessionState.InProgress => "Đang khám",
        ExamSessionState.Closed => "Đã đóng",
        ExamSessionState.Cancelled => "Hủy",
        _ => ""
    };

    /// <summary>Vòng đời một dòng dịch vụ CLS — 01-db-model §2.4.</summary>
    public static string Of(ParaclinicalItemState state) => state switch
    {
        ParaclinicalItemState.Ordered => "Chờ chỉ định",
        ParaclinicalItemState.Waiting => "Chờ thực hiện",
        ParaclinicalItemState.InProgress => "Đang thực hiện",
        ParaclinicalItemState.Done => "Đã trả KQ",
        ParaclinicalItemState.Cancelled => "Hủy",
        _ => ""
    };

    public static string Of(ImportBatchState state) => state switch
    {
        ImportBatchState.Pending => "Đang xử lý",
        ImportBatchState.Completed => "Hoàn tất",
        ImportBatchState.Failed => "Lỗi",
        ImportBatchState.Discarded => "Đã hủy",
        ImportBatchState.Committing => "Đang ghi hồ sơ",
        _ => ""
    };
}
