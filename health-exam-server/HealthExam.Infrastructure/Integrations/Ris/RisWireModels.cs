using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HealthExam.Application.Integrations;

namespace HealthExam.Infrastructure.Integrations.Ris;

/// <summary>
/// Cấu hình cầu nối RIS (P3b) — đọc từ biến môi trường, KHÔNG từ appsettings.
/// </summary>
public class RisOptions : IVendorCallbackAuthOptions
{
    string IVendorCallbackAuthOptions.CallbackUserEnv => CallbackUserEnv;
    string IVendorCallbackAuthOptions.CallbackPasswordEnv => CallbackPasswordEnv;
    public const string BaseUrlEnv = "RIS_BASE_URL";
    public const string UsernameEnv = "RIS_USERNAME";
    public const string PasswordEnv = "RIS_PASSWORD";
    public const string VoucherTypeEnv = "RIS_VOUCHER_TYPE";
    public const string StationAeEnv = "RIS_STATION_AE";

    /// <summary>Tài khoản RIS gọi NGƯỢC về ta (Basic). Khác cặp ở trên: đó là ta gọi sang họ.</summary>
    public const string CallbackUserEnv = "RIS_CALLBACK_USER";
    public const string CallbackPasswordEnv = "RIS_CALLBACK_PASSWORD";

    /// <summary>Đường dẫn nhận phiếu của VietRad — cố định trong hợp đồng dây, chỉ phần gốc
    /// là cấu hình được (pacs-connect-server/M07F99020Commands.cs:128).</summary>
    public const string OrderPath = "/his/json/request";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public string BaseUrl { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string VoucherType { get; init; } = "2";
    public string StationAe { get; init; } = "";
    public string CallbackUser { get; init; } = "";
    public string CallbackPassword { get; init; } = "";
    public const string DivisionEnv = "RIS_DIVISION_ID";
    public string DivisionId { get; init; } = "";

    public bool IsDispatchConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(DivisionId);

    public static string DispatchEnvNames =>
        string.Join(", ", BaseUrlEnv, UsernameEnv, PasswordEnv, DivisionEnv);

    public bool ServesDivision(string divisionId) =>
        IsDispatchConfigured &&
        string.Equals(DivisionId, divisionId, StringComparison.Ordinal);

    public bool IsCallbackConfigured =>
        !string.IsNullOrWhiteSpace(CallbackUser) && !string.IsNullOrWhiteSpace(CallbackPassword);

    public static RisOptions FromEnvironment()
    {
        var voucherType = (Environment.GetEnvironmentVariable(VoucherTypeEnv) ?? "").Trim();

        return new RisOptions
        {
            BaseUrl = (Environment.GetEnvironmentVariable(BaseUrlEnv) ?? "").Trim().TrimEnd('/'),
            Username = (Environment.GetEnvironmentVariable(UsernameEnv) ?? "").Trim(),
            Password = Environment.GetEnvironmentVariable(PasswordEnv) ?? "",
            VoucherType = voucherType.Length == 0 ? "2" : voucherType,
            StationAe = (Environment.GetEnvironmentVariable(StationAeEnv) ?? "").Trim(),
            CallbackUser = (Environment.GetEnvironmentVariable(CallbackUserEnv) ?? "").Trim(),
            CallbackPassword = Environment.GetEnvironmentVariable(CallbackPasswordEnv) ?? "",
            DivisionId = (Environment.GetEnvironmentVariable(DivisionEnv) ?? "").Trim()
        };
    }
}

public static class RisOrderStatuses
{
    public const string New = "NEW";
    public const string Cancelled = "CANCELLED";
}

public class RisOrderRequest
{
    [Required] public RisPatient Patient { get; set; }
    [Required] public List<RisOrderLine> Orders { get; set; } = new();
    [Required] public string OrderNumber { get; set; } = "";
    [Required] public VendorClock.Stamp OrderDate { get; set; }
    [Required] public string Status { get; set; } = "";
    [Required] public VendorClock.Stamp MsgDate { get; set; }
    public string EncounterID { get; set; }
    public string StationAE { get; set; }
    public string Comment { get; set; }
    public string VoucherType { get; set; } = "2";
    public string VoucherNo { get; set; }
    public RisClinical Clinical { get; set; }
}

public class RisPatient
{
    [Required] public string ID { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    [Required] public string Sex { get; set; } = "";
    [Required] public string IdentityCardID { get; set; } = "";
    public string BirthYear { get; set; }
    public string Phone { get; set; }
    public string Address { get; set; }
    public RisHealthInsurance HealthInsurance { get; set; }
}

public class RisHealthInsurance
{
    public string HealthInsuranceCardID { get; set; }
}

public class RisOrderLine
{
    [Required] public string Id { get; set; } = "";
    [Required] public string Code { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    [Required] public string Group { get; set; } = "";
    public string PCReqDltVoucherNo { get; set; } = "";
}

public class RisClinical
{
    public string Department { get; set; }
    public string Doctor { get; set; }
    public string Diagnosis { get; set; }
    public string Room { get; set; }
}
