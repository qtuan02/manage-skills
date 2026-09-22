#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;

namespace HealthExam.Application.Auth;

public interface ILogoutHandler
{
    Task<ApplicationResult<object>> HandleAsync(LogoutCommand command, CancellationToken ct = default);
}

public sealed class LogoutHandler : ILogoutHandler
{
    private readonly IHisAuthGateway _gateway;

    public LogoutHandler(IHisAuthGateway gateway) => _gateway = gateway;

    public async Task<ApplicationResult<object>> HandleAsync(LogoutCommand command, CancellationToken ct = default)
    {
        if (command.ActorKind != ActorKind.Employee)
            return ApplicationResult<object>.Fail(ApplicationFailureCode.Forbidden, "Thao tác yêu cầu quyền nhân viên");

        return await _gateway.LogoutAsync(command, ct);
    }
}
