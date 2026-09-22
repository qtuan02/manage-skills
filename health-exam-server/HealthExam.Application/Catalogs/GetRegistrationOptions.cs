using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record GetRegistrationOptionsQuery(string DivisionId);

public interface IGetRegistrationOptionsHandler
{
    Task<ApplicationResult<RegistrationOptionsResult>> HandleAsync(
        GetRegistrationOptionsQuery query, CancellationToken ct = default);
}

public sealed class GetRegistrationOptionsHandler : IGetRegistrationOptionsHandler
{
    private readonly ICatalogRepository _repository;

    public GetRegistrationOptionsHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<RegistrationOptionsResult>> HandleAsync(
        GetRegistrationOptionsQuery query, CancellationToken ct = default)
    {
        var result = await _repository.GetRegistrationOptionsAsync(query.DivisionId, ct);
        return ApplicationResult<RegistrationOptionsResult>.Success(result);
    }
}
