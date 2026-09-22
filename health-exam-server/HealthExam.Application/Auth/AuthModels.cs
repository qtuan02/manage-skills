#nullable enable

using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Application.Auth;

public sealed record LoginCommand(
    string DivisionId,
    string TraceId,
    string Username,
    string Password,
    bool ConfirmLogin = false);

public sealed record LoginResult(
    string AccessToken,
    string TokenType = "Bearer");

public sealed record GetCurrentUserQuery(
    string DivisionId,
    string TraceId,
    ActorKind ActorKind,
    long? ActorId,
    string? ActorName,
    string? AuthorizationHeader);

/// <summary>
/// Đúng shape HIS <c>api/Auth/Info</c> trả (<c>UserInfo</c> bên his-server): không thêm,
/// không bớt, không đổi tên — FE đọc field theo tên HIS. HIS không có FullName/Title;
/// <c>UserName</c> là tên hiển thị của tài khoản (ví dụ "Bác sĩ EMR"), không phải login.
/// </summary>
public sealed record CurrentUserResult(
    long? AccountID,
    long? EmployeeID,
    string? EmployeeCode,
    string? DivisionID,
    string? UserName,
    string? PhoneNumber,
    int? DepartmentID,
    string? DepartmentName,
    bool IsChangePassword,
    IReadOnlyDictionary<string, int>? Permissions);

public sealed record LogoutCommand(
    string DivisionId,
    string TraceId,
    ActorKind ActorKind,
    string? AuthorizationHeader);
