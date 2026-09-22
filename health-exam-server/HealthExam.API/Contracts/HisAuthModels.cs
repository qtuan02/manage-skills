#nullable enable

using System.Collections.Generic;

namespace HealthExam.API.Contracts;

public sealed class HisLoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool ConfirmLogin { get; set; }
}

public sealed class HisLoginResult
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
}

/// <summary>Đúng 10 field HIS api/Auth/Info trả. Không có FullName/Title (HIS không có).</summary>
public sealed class HisCurrentUser
{
    public long? AccountID { get; set; }
    public long? EmployeeID { get; set; }
    public string? EmployeeCode { get; set; }
    public string? DivisionID { get; set; }
    public string? UserName { get; set; }
    public string? PhoneNumber { get; set; }
    public int? DepartmentID { get; set; }
    public string? DepartmentName { get; set; }
    public bool IsChangePassword { get; set; }
    public IReadOnlyDictionary<string, int>? Permissions { get; set; }
}
