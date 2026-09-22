#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Auth;

public interface ILoginHandler
{
    Task<ApplicationResult<LoginResult>> HandleAsync(LoginCommand command, CancellationToken ct = default);
}

public sealed class LoginHandler : ILoginHandler
{
    private readonly IHisAuthGateway _gateway;

    public LoginHandler(IHisAuthGateway gateway) => _gateway = gateway;

    public async Task<ApplicationResult<LoginResult>> HandleAsync(LoginCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Username))
            return ApplicationResult<LoginResult>.Fail(ApplicationFailureCode.BadRequest, "Tên đăng nhập là bắt buộc");

        if (string.IsNullOrWhiteSpace(command.Password))
            return ApplicationResult<LoginResult>.Fail(ApplicationFailureCode.BadRequest, "Mật khẩu là bắt buộc");

        return await _gateway.LoginAsync(command, ct);
    }
}
