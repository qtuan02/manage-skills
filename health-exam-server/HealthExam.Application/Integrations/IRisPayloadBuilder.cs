using System;
using System.Collections.Generic;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Paraclinical;

namespace HealthExam.Application.Integrations;

public interface IRisPayloadBuilder
{
    bool IsDispatchConfigured();
    bool ServesDivision(string divisionId);
    string GetConfiguredDivisionId();
    string GetDispatchEnvNames();

    bool TryBuildPayload(
        ParaclinicalOrder order,
        IReadOnlyList<ParaclinicalOrderItem> lines,
        ExamRecord record,
        string status,
        DateTime nowUtc,
        out string payload,
        out IReadOnlyList<VendorMissingFieldResult> missing);
}
