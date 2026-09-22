#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Domain.Common;

namespace HealthExam.Application.Auth;

public interface IGetCurrentUserHandler
{
    Task<ApplicationResult<CurrentUserResult>> HandleAsync(GetCurrentUserQuery query, CancellationToken ct = default);
}

public sealed class GetCurrentUserHandler : IGetCurrentUserHandler
{
    private readonly IHisAuthGateway _gateway;

    public GetCurrentUserHandler(IHisAuthGateway gateway) => _gateway = gateway;

    public async Task<ApplicationResult<CurrentUserResult>> HandleAsync(GetCurrentUserQuery query, CancellationToken ct = default)
    {
        if (query.ActorKind != ActorKind.Employee)
            return ApplicationResult<CurrentUserResult>.Fail(ApplicationFailureCode.Forbidden, "Thao tác yêu cầu quyền nhân viên");

        return await _gateway.GetCurrentUserAsync(query, ct);
    }
}
