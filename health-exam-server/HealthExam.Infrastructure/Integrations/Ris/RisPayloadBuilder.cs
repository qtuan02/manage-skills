using System;
using System.Collections.Generic;
using System.Linq;
using HealthExam.Application.Integrations;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Infrastructure.Integrations.Ris;

public sealed class RisPayloadBuilder : IRisPayloadBuilder
{
    private readonly RisOptions _options;

    public RisPayloadBuilder(RisOptions options)
    {
        _options = options;
    }

    public bool IsDispatchConfigured() => _options.IsDispatchConfigured;

    public bool ServesDivision(string divisionId) => _options.ServesDivision(divisionId);

    public string GetConfiguredDivisionId() => _options.DivisionId;

    public string GetDispatchEnvNames() => RisOptions.DispatchEnvNames;

    public bool TryBuildPayload(
        ParaclinicalOrder order,
        IReadOnlyList<ParaclinicalOrderItem> lines,
        ExamRecord record,
        string status,
        DateTime nowUtc,
        out string payload,
        out IReadOnlyList<VendorMissingFieldResult> missing)
    {
        var built = RisOrderPayloadBuilder.Build(
            order, lines, record, _options, status, nowUtc, out var missingFields);

        if (built == null || (missingFields != null && missingFields.Count > 0))
        {
            payload = null;
            missing = (missingFields ?? Array.Empty<VendorMissingField>())
                .Select(m => new VendorMissingFieldResult(m.Field, m.Reason))
                .ToList();
            return false;
        }

        payload = RisClient.Serialize(built);
        missing = Array.Empty<VendorMissingFieldResult>();
        return true;
    }
}
