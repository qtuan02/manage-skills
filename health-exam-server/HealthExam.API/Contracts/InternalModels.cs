namespace HealthExam.API.Contracts;

/// <summary>
/// Body của <c>POST /v1/internal/verify-portal-credentials</c> — iam-server gọi vào khi người
/// bệnh đăng nhập cổng web (UC04).
/// </summary>
public class VerifyPortalCredentialsRequest
{
    /// <summary>Mã người bệnh in trên giấy hẹn. Bắt buộc.</summary>
    public string PatientCode { get; set; } = "";

    /// <summary>Số CCCD/CMND. Bắt buộc CÓ ÍT NHẤT một trong hai: cái này hoặc số thẻ BHYT.</summary>
    public string IdentityNumber { get; set; } = "";

    /// <summary>Số thẻ BHYT.</summary>
    public string InsuranceNumber { get; set; } = "";
}

/// <summary>
/// Kết quả đối chiếu. CHỈ dữ kiện định danh, KHÔNG có token.
/// </summary>
public class VerifyPortalCredentialsResult
{
    public bool IsValid { get; set; }
    public string RecordCode { get; set; } = "";
    public string SessionCode { get; set; } = "";
    public string FullName { get; set; } = "";

    public static VerifyPortalCredentialsResult Invalid() => new() { IsValid = false };
}
