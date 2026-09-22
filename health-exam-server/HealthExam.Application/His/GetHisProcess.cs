using System;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IGetHisProcessHandler
{
    Task<ApplicationResult<HisJsonDocument>> HandleAsync(
        GetHisProcessQuery query, CancellationToken ct = default);
}

public class GetHisProcessHandler : IGetHisProcessHandler
{
    private readonly IHisProcessValidator _validator;

    public GetHisProcessHandler(IHisProcessValidator validator)
    {
        _validator = validator;
    }

    public Task<ApplicationResult<HisJsonDocument>> HandleAsync(
        GetHisProcessQuery query, CancellationToken ct = default)
    {
        return _validator.ValidateAndGetOwnedProcessAsync(
            query.DivisionId,
            query.RecordId,
            query.ProcessId,
            query.Credential,
            query.TraceId,
            ct);
    }
}
