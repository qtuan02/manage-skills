#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Auth;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;
using Xunit;

namespace HealthExam.Tests.Application;

public class AuthHandlerTests
{
    [Fact]
    public async Task Login_fails_when_username_or_password_is_empty()
    {
        var gateway = new FakeHisAuthGateway();
        var handler = new LoginHandler(gateway);

        var resEmptyUser = await handler.HandleAsync(new LoginCommand("DEV", "trace", "", "password"));
        Assert.False(resEmptyUser.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, resEmptyUser.Failure.Code);

        var resEmptyPass = await handler.HandleAsync(new LoginCommand("DEV", "trace", "doctor", ""));
        Assert.False(resEmptyPass.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, resEmptyPass.Failure.Code);
    }

    [Fact]
    public async Task Login_forwards_to_gateway_when_valid()
    {
        var gateway = new FakeHisAuthGateway
        {
            LoginOutcome = ApplicationResult<LoginResult>.Success(new LoginResult("token-123"))
        };
        var handler = new LoginHandler(gateway);

        var res = await handler.HandleAsync(new LoginCommand("DEV", "trace", "doctor", "secret", true));

        Assert.True(res.IsSuccess);
        Assert.Equal("token-123", res.Value.AccessToken);
        Assert.Equal("doctor", gateway.LastLoginCommand?.Username);
        Assert.True(gateway.LastLoginCommand?.ConfirmLogin);
    }

    [Fact]
    public async Task GetCurrentUser_requires_employee_actor_kind()
    {
        var gateway = new FakeHisAuthGateway();
        var handler = new GetCurrentUserHandler(gateway);

        var resNonEmployee = await handler.HandleAsync(new GetCurrentUserQuery("DEV", "trace", ActorKind.System, 1, "sys", "Bearer tok"));
        Assert.False(resNonEmployee.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, resNonEmployee.Failure.Code);

        gateway.CurrentUserOutcome = ApplicationResult<CurrentUserResult>.Success(new CurrentUserResult(
            AccountID: 1,
            EmployeeID: 10,
            EmployeeCode: "EMP10",
            DivisionID: "DEV",
            UserName: "doc",
            PhoneNumber: null,
            DepartmentID: null,
            DepartmentName: null,
            IsChangePassword: false,
            Permissions: null));
        var resEmployee = await handler.HandleAsync(new GetCurrentUserQuery("DEV", "trace", ActorKind.Employee, 10, "doc", "Bearer tok"));
        Assert.True(resEmployee.IsSuccess);
        Assert.Equal("EMP10", resEmployee.Value.EmployeeCode);
    }

    [Fact]
    public async Task Logout_requires_employee_actor_kind()
    {
        var gateway = new FakeHisAuthGateway();
        var handler = new LogoutHandler(gateway);

        var resNonEmployee = await handler.HandleAsync(new LogoutCommand("DEV", "trace", ActorKind.Patient, "Bearer tok"));
        Assert.False(resNonEmployee.IsSuccess);
        Assert.Equal(ApplicationFailureCode.Forbidden, resNonEmployee.Failure.Code);

        gateway.LogoutOutcome = ApplicationResult<object>.Success(null!);
        var resEmployee = await handler.HandleAsync(new LogoutCommand("DEV", "trace", ActorKind.Employee, "Bearer tok"));
        Assert.True(resEmployee.IsSuccess);
    }

    private sealed class FakeHisAuthGateway : IHisAuthGateway
    {
        public LoginCommand? LastLoginCommand { get; private set; }
        public ApplicationResult<LoginResult> LoginOutcome { get; set; } =
            ApplicationResult<LoginResult>.Fail(ApplicationFailureCode.Unauthorized, "Unauthorized");

        public ApplicationResult<CurrentUserResult> CurrentUserOutcome { get; set; } =
            ApplicationResult<CurrentUserResult>.Fail(ApplicationFailureCode.Unauthorized, "Unauthorized");

        public ApplicationResult<object> LogoutOutcome { get; set; } =
            ApplicationResult<object>.Success(null!);

        public Task<ApplicationResult<LoginResult>> LoginAsync(LoginCommand command, CancellationToken ct = default)
        {
            LastLoginCommand = command;
            return Task.FromResult(LoginOutcome);
        }

        public Task<ApplicationResult<CurrentUserResult>> GetCurrentUserAsync(GetCurrentUserQuery query, CancellationToken ct = default)
        {
            return Task.FromResult(CurrentUserOutcome);
        }

        public Task<ApplicationResult<object>> LogoutAsync(LogoutCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(LogoutOutcome);
        }
    }
}
