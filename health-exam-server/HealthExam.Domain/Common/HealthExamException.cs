using System;

namespace HealthExam.Domain.Common;

/// <summary>
/// Lỗi nghiệp vụ mang sẵn mã trong bảng mã lỗi.
/// </summary>
public class HealthExamException : Exception
{
    public int ErrorCode { get; }
    public string CustomMessage { get; }
    public object Payload { get; }

    public HealthExamException(int errorCode, string message = null, object payload = null)
        : base(message)
    {
        ErrorCode = errorCode;
        CustomMessage = message;
        Payload = payload;
    }

    public static HealthExamException BadRequest(string message = null, object payload = null)
        => new(4001, message, payload);

    public static HealthExamException NotFound(string message = null)
        => new(4040, message);

    public static HealthExamException Forbidden(string message = null)
        => new(4030, message);

    public static HealthExamException NotOwner(string message = null)
        => new(4031, message);

    public static HealthExamException InvalidState(string message = null, object payload = null)
        => new(4090, message, payload);

    /// <summary>4002 — cả file Excel không dùng được, khác hẳn "vài dòng hỏng".</summary>
    public static HealthExamException FileInvalid(string message = null, object payload = null)
        => new(4002, message, payload);

    /// <summary>4091 — đợt khám đã đóng, người dùng cần biết đường đi mở lại đợt.</summary>
    public static HealthExamException SessionClosed(string message = null)
        => new(4091, message);

    /// <summary>4092 — bản ghi CÓ tồn tại nhưng không thuộc đợt trong đường dẫn.</summary>
    public static HealthExamException NotInSession(string message = null)
        => new(4092, message);

    /// <summary>4093 — người bệnh đã có hồ sơ trong đợt này.</summary>
    public static HealthExamException DuplicateInSession(string message = null, object payload = null)
        => new(4093, message, payload);

    /// <summary>4094 — chỉ định CLS đã có kết quả nên không huỷ được.</summary>
    public static HealthExamException ParaclinicalResultExists(string message = null, object payload = null)
        => new(4094, message, payload);

    /// <summary>4095 — phiếu thiếu trường BẮT BUỘC CỦA VENDOR nên không gửi đi được.</summary>
    public static HealthExamException VendorPayloadIncomplete(string message = null, object payload = null)
        => new(4095, message, payload);

    /// <summary>5021 — cầu nối vendor chưa được cấu hình.</summary>
    public static HealthExamException VendorNotConfigured(string message = null, object payload = null)
        => new(5021, message, payload);
}
