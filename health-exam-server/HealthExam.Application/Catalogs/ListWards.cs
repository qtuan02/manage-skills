using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;

namespace HealthExam.Application.Catalogs;

public sealed record ListWardsQuery(
    string DivisionId,
    string ProvinceCode,
    string Keyword = null);

public interface IListWardsHandler
{
    Task<ApplicationResult<IReadOnlyList<MasterDataOptionResult>>> HandleAsync(
        ListWardsQuery query, CancellationToken ct = default);
}

public sealed class ListWardsHandler : IListWardsHandler
{
    private readonly ICatalogRepository _repository;

    public ListWardsHandler(ICatalogRepository repository)
    {
        _repository = repository;
    }

    public async Task<ApplicationResult<IReadOnlyList<MasterDataOptionResult>>> HandleAsync(
        ListWardsQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.ProvinceCode))
        {
            return ApplicationResult<IReadOnlyList<MasterDataOptionResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Mã tỉnh/thành phố không được để trống",
                new ValidationErrors { { "ProvinceCode", "Mã tỉnh/thành phố không được để trống" } });
        }

        var result = await _repository.ListWardsAsync(
            query.DivisionId, query.ProvinceCode, query.Keyword, ct);

        if (result == null)
        {
            return ApplicationResult<IReadOnlyList<MasterDataOptionResult>>.Fail(
                ApplicationFailureCode.BadRequest,
                "Tỉnh/thành phố không hợp lệ",
                new ValidationErrors { { "ProvinceCode", "Tỉnh/thành phố không hợp lệ" } });
        }

        return ApplicationResult<IReadOnlyList<MasterDataOptionResult>>.Success(result);
    }
}
