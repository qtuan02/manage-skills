#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Auth;

public interface IHisAuthGateway
{
    Task<ApplicationResult<LoginResult>> LoginAsync(LoginCommand command, CancellationToken ct = default);
    Task<ApplicationResult<CurrentUserResult>> GetCurrentUserAsync(GetCurrentUserQuery query, CancellationToken ct = default);
    Task<ApplicationResult<object>> LogoutAsync(LogoutCommand command, CancellationToken ct = default);
}
