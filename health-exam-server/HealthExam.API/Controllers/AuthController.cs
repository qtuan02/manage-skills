#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.API.Contracts;
using HealthExam.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace HealthExam.API.Controllers;

[Route("v1/auth")]
public sealed class AuthController : HealthExamControllerBase
{
    private readonly ILoginHandler _loginHandler;
    private readonly IGetCurrentUserHandler _getCurrentUserHandler;
    private readonly ILogoutHandler _logoutHandler;

    public AuthController(
        ILoginHandler loginHandler,
        IGetCurrentUserHandler getCurrentUserHandler,
        ILogoutHandler logoutHandler)
    {
        _loginHandler = loginHandler;
        _getCurrentUserHandler = getCurrentUserHandler;
        _logoutHandler = logoutHandler;
    }

    [HttpPost("login")]
    [SwaggerOperation(
        Summary = "Đăng nhập tài khoản nhân viên qua HIS",
        Description = "Chuyển tiếp thông tin đăng nhập sang HIS EMR. Yêu cầu header X-Division-Id. Trả về access token JWT của HIS nếu thành công; trả về 4090 nếu tài khoản đang đăng nhập nơi khác và cần xác nhận ConfirmLogin.")]
    public async Task<ActionResult<ResultData<HisLoginResult>>> Login(
        [FromBody] HisLoginRequest request,
        CancellationToken ct = default)
    {
        var cmd = new LoginCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.TraceId,
            request.Username,
            request.Password,
            request.ConfirmLogin);

        var res = await _loginHandler.HandleAsync(cmd, ct);
        return ToActionResult(res, r => new HisLoginResult
        {
            AccessToken = r.AccessToken,
            TokenType = r.TokenType
        });
    }

    [HttpGet("me")]
    [SwaggerOperation(
        Summary = "Lấy thông tin nhân viên đang đăng nhập",
        Description = "Chuyển tiếp token Bearer của nhân viên sang HIS api/Auth/Info và trả nguyên 10 field HIS trả (AccountID, EmployeeID, EmployeeCode, DivisionID, UserName, PhoneNumber, DepartmentID, DepartmentName, IsChangePassword, Permissions). Yêu cầu token hợp lệ của nhân viên.")]
    public async Task<ActionResult<ResultData<HisCurrentUser>>> Me(CancellationToken ct = default)
    {
        var authHeader = HealthExamContext.GetRequestHeader("Authorization");
        var query = new GetCurrentUserQuery(
            HealthExamContext.DivisionId,
            HealthExamContext.TraceId,
            HealthExamContext.ActorKind,
            HealthExamContext.ActorId,
            HealthExamContext.ActorName,
            authHeader);

        var res = await _getCurrentUserHandler.HandleAsync(query, ct);
        return ToActionResult(res, r => new HisCurrentUser
        {
            AccountID = r.AccountID,
            EmployeeID = r.EmployeeID,
            EmployeeCode = r.EmployeeCode,
            DivisionID = r.DivisionID,
            UserName = r.UserName,
            PhoneNumber = r.PhoneNumber,
            DepartmentID = r.DepartmentID,
            DepartmentName = r.DepartmentName,
            IsChangePassword = r.IsChangePassword,
            Permissions = r.Permissions
        });
    }

    [HttpPost("logout")]
    [SwaggerOperation(
        Summary = "Đăng xuất tài khoản nhân viên",
        Description = "Chuyển tiếp yêu cầu đăng xuất sang HIS để thu hồi/hủy phiên làm việc. Yêu cầu token hợp lệ của nhân viên.")]
    public async Task<ActionResult<ResultData<object>>> Logout(CancellationToken ct = default)
    {
        var authHeader = HealthExamContext.GetRequestHeader("Authorization");
        var cmd = new LogoutCommand(
            HealthExamContext.DivisionId,
            HealthExamContext.TraceId,
            HealthExamContext.ActorKind,
            authHeader);

        var res = await _logoutHandler.HandleAsync(cmd, ct);
        return ToActionResult(res);
    }
}
