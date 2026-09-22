using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.Catalogs;
using HealthExam.Domain.Patients;

namespace HealthExam.Domain.ExamRecords;

/// <summary>
/// HEX_ExamRecord ★ — hồ sơ KSK của TỪNG NGƯỜI trong MỘT đợt (01-db-model §4.2).
/// Trục chính của module. Phiếu bên form-server neo vào mã ĐỢT (HostRefID) và mang RecordCode
/// ở SubjectID — xem chú thích RecordCode bên dưới.
///
/// Vì sao tên là Record chứ không phải Person: một người khám nhiều đợt (định kỳ hằng năm,
/// đổi hạng GPLX…), nên hàng dữ liệu ở đây là HỒ SƠ KHÁM CỦA MỘT NGƯỜI TRONG MỘT ĐỢT.
/// Đặt tên Person sẽ khiến người đọc sau đi tìm khoá UNIQUE(IdentityNumber) không có thật.
/// </summary>
public class ExamRecord : AuditableEntity
{
    public Guid RecordID { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid SessionID { get; set; }

    /// <summary>
    /// ★ Mã hồ sơ KSK, duy nhất theo (DivisionID, RecordCode). Số thứ tự cấp theo TỪNG ĐỢT.
    ///
    /// ⚠️ KHÔNG phải HostRefID gửi sang form-server — HostRefID là SessionCode (mã ĐỢT) và
    /// ĐÃ CHỐT giữ nguyên như vậy. RecordCode đi vào form-server ở ô KHÁC:
    /// FRM_Submission.SubjectID, và thành claim `sub` của token cổng người bệnh. Đó là chỗ
    /// cách ly người bệnh này với người bệnh kia trong CÙNG một đợt.
    /// Chốt của cụm 236 (24/08, đo trên form-server thật):
    /// medviet-ai/docs/handoff/20260824-236-chot-h3b-va-subjectid.md §3.
    /// Xem ExamFormDraftService.HostRefIdOf — chỗ duy nhất quyết định HostRefID.
    /// </summary>
    public string RecordCode { get; set; } = "";

    // --- Con trỏ HIS M02_Admission; không có foreign key vì khác service/database.
    public long? AdmissionID { get; set; }

    // --- Nghiệp vụ khám
    /// <summary>★ Nhóm khám: DTK_01..DTK_10 — xem HealthExam.Domain.ExamRecords.ExamGroups.</summary>
    public string VariantCode { get; set; } = "";
    public Guid? PackageID { get; set; }
    public string PackageName { get; set; } = "";

    // --- Địa chỉ hành chính tại thời điểm khám
    public string ProvinceCode { get; set; } = "";
    public string ProvinceName { get; set; } = "";
    public string WardCode { get; set; } = "";
    public string WardName { get; set; } = "";

    // --- Lý do khám & nguồn thanh toán khác
    public string ExamReason { get; set; } = "";
    public string PaymentSourceOther { get; set; } = "";

    // --- Con trỏ sang form-server: KHÔNG FK, KHÔNG join (DB khác)
    public Guid? FormID { get; set; }
    public string FormCode { get; set; } = "";
    public Guid? SubmissionID { get; set; }

    // --- Biểu mẫu đăng ký KSK trên HIS EMR
    public Guid? HisEmrDataID { get; set; }
    public Guid? HisFormTemplateID { get; set; }
    public string HisFormSyncStatus { get; set; } = "Pending";
    public string HisFormSyncError { get; set; } = "";
    public string SignedFilePath { get; set; }
    public string SignStatus { get; set; } = ExamRecordSignStatus.New;
    public long? HisSignedByEmployeeID { get; set; }
    public DateTime? HisSignedAt { get; set; }

    // --- Danh sách các bước ký kết luận HIS (01-db-model)
    public List<ExamRecordSignStep> SignSteps { get; set; } = new();

    // --- Máy trạng thái (01-db-model §2.3)
    public ExamRecordState State { get; set; } = ExamRecordState.NotRegistered;
    public DateTime? RegisteredAt { get; set; }
    public long RegisteredBy { get; set; }
    public DateTime? ExamStartedAt { get; set; }
    public DateTime? ExamFinishedAt { get; set; }

    /// <summary>
    /// Mốc <see cref="ExamStartedAt"/> / <see cref="ExamFinishedAt"/> hiện tại là do JOB ĐỐI
    /// SOÁT ƯỚC LƯỢNG (lấy giờ chạy job), không phải giờ thật mang trong sự kiện.
    ///
    /// ★ Vì sao cần hai cờ này (§4.7, review lần 2 MR !5): job đối soát chữa được trạng thái
    /// nhưng KHÔNG biết giờ khám thật — sự kiện mang giờ đó đã mất. Nó đặt giờ chạy job để
    /// báo cáo không đọc mốc trống thành "chưa khám". Nhưng khi gói THẬT tới sau (đến muộn,
    /// hoặc được nạp lại bằng webhook-requeue), nhánh bổ sung chỉ ghi khi mốc còn TRỐNG — mà
    /// job đã điền rồi. Kết quả: hồ sơ giữ GIỜ CHẠY JOB vĩnh viễn, sai một mốc nghiệp vụ,
    /// không chữa lại được, và không chỗ nào báo lỗi.
    ///
    /// Có cờ thì sự kiện thật được phép ĐÈ lên mốc ước lượng — và chỉ lên mốc ước lượng. Mốc
    /// do một sự kiện thật ghi vẫn bất khả xâm phạm như cũ.
    /// </summary>
    public bool ExamStartedAtEstimated { get; set; }

    /// <inheritdoc cref="ExamStartedAtEstimated"/>
    public bool ExamFinishedAtEstimated { get; set; }
    public DateTime? CancelledAt { get; set; }
    public long CancelledBy { get; set; }
    public string CancelReason { get; set; } = "";

    /// <summary>
    /// Cache hiển thị tiến độ {x}/17 trên màn danh sách. KHÔNG dùng làm điều kiện ký:
    /// số này do webhook form-server cập nhật nên có độ trễ, tin nó để chặn ký là chặn nhầm.
    /// </summary>
    public short ProgressDone { get; set; }
    public short ProgressTotal { get; set; }
    public DateTime? ProgressSyncedAt { get; set; }

    /// <summary>Loại I..V — webhook chép về để lọc/thống kê.</summary>
    public string HealthClassCode { get; set; } = "";

    /// <summary>
    /// OccurredAt của sự kiện form-server GẦN NHẤT ĐÃ ÁP lên hồ sơ này — chốt chặn "đến muộn"
    /// của 02-api-spec §5.3 lớp 2.
    ///
    /// ⚠️ BỔ SUNG so với 01-db-model §4.2 (DDL ở đó chưa có cột này, nhưng §5.3 lại bắt "so
    /// OccurredAt với LastEventAt của hồ sơ" — không có chỗ lưu thì không so được).
    ///
    /// Chỉ ghi khi sự kiện ĐƯỢC ÁP, không ghi khi bỏ qua: ghi cả lượt bỏ qua thì một gói lạc
    /// mang mốc thời gian tương lai sẽ khoá chết mọi sự kiện thật đến sau nó.
    /// </summary>
    public DateTime? LastEventAt { get; set; }

    public Guid? ImportBatchID { get; set; }
    public string Note { get; set; }

    public ExamSession Session { get; set; }
    public ExamPackage Package { get; set; }

    // --- 3NF Normalized References
    public Guid? PatientRefID { get; set; }
    public Guid? InsuranceRefID { get; set; }
    public Guid? EmploymentRefID { get; set; }
    public Guid? RelativeRefID { get; set; }
    public Guid? PatientTypeOptionID { get; set; }
    public Guid? PatientSubjectOptionID { get; set; }
    public Guid? PaymentSourceOptionID { get; set; }
    public Guid? ExamLocationOptionID { get; set; }

    public Patient Patient { get; set; }
    public PatientInsurance Insurance { get; set; }
    public PatientEmployment Employment { get; set; }
    public PatientRelative Relative { get; set; }
    public MasterDataOption PatientTypeOption { get; set; }
    public MasterDataOption PatientSubjectOption { get; set; }
    public MasterDataOption PaymentSourceOption { get; set; }
    public MasterDataOption ExamLocationOption { get; set; }

    public DomainResult Confirm()
    {
        if (State != ExamRecordState.NotRegistered)
            return DomainResult.Reject(DomainFailureCode.InvalidTransition, "Chỉ hồ sơ ở trạng thái Chưa đăng ký mới chốt đăng ký được");

        State = ExamRecordState.Waiting;
        return DomainResult.Apply();
    }

    public DomainResult BeginExam(DateTime utcNow)
    {
        if (State == ExamRecordState.Waiting)
        {
            State = ExamRecordState.InProgress;
            ExamStartedAt ??= utcNow;
            return DomainResult.Apply();
        }

        if (State == ExamRecordState.InProgress)
        {
            ExamStartedAt ??= utcNow;
            return DomainResult.NoOp();
        }

        return DomainResult.Reject(
            DomainFailureCode.InvalidTransition,
            $"Hồ sơ đang ở trạng thái \"{StateNames.Of(State)}\", không thể chuyển sang Đang khám");
    }

    public DomainResult Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return DomainResult.Reject(DomainFailureCode.RequiredValue, "Hủy hồ sơ phải có lý do");

        if (State == ExamRecordState.NotRegistered)
        {
            State = ExamRecordState.RegistrationCancelled;
            return DomainResult.Apply();
        }

        if (State is ExamRecordState.Waiting or ExamRecordState.InProgress)
        {
            State = ExamRecordState.ExamCancelled;
            return DomainResult.Apply();
        }

        return DomainResult.Reject(
            DomainFailureCode.InvalidTransition,
            $"Hồ sơ đang ở trạng thái \"{StateNames.Of(State)}\", không thể hủy");
    }
}

public static class ExamRecordSignStatus
{
    public const string New = "New";
    public const string Signed = "Signed";
    public const string Failed = "Failed";
}

file static class StateNames
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
}
