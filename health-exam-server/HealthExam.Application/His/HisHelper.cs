using System;
using System.Collections.Generic;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;
using HealthExam.Domain.Common;

namespace HealthExam.Application.His;

public static class HisConstants
{
    public const string TargetTemplateCode = "KSK-TREN18TUOI";

    public const string RouteGetListTemplate = "api/M03F00030/GetListTemplateByEMR";
    public const string RouteGetTemplate = "api/M03F00030/GetTemplateByID";
    public const string RouteGetTreeTemplate = "api/M03F00030/GetTreeTemplateByID";
    public const string RouteGetTreeByFileDocType = "api/M03F00030/GetTreeByFileDocTypeID";

    public const string RouteRMedicalProcess = "api/M02F01500/RMedicalProcess";
    public const string RouteRMedicalProcessById = "api/M02F01500/RMedicalProcessByID";
    public const string RouteCMedicalProcess = "api/M02F01500/CMedicalProcess";

    public const string RouteREmr = "api/M03F10010/REMR";
    public const string RouteCueEmr = "api/M03F10010/CUEMR";
    public const string RouteVEmr = "api/M03F10010/VEMR";
    public const string RouteVEmrs = "api/M02F01500/VEMRs";
    public const string RouteCUAdmission = "api/M02F00000/GetAdmissionInfo";
}

public sealed class HisApplicationException : Exception
{
    public ApplicationFailureCode Code { get; }
    public object Payload { get; }

    public HisApplicationException(ApplicationFailureCode code, string message, object payload = null)
        : base(message)
    {
        Code = code;
        Payload = payload;
    }
}

public static class HisOutcomeMapper
{
    public static ApplicationResult<T> ToApplicationResult<T>(HisClientResult<T> result)
    {
        if (result.IsSuccess)
            return ApplicationResult<T>.Success(result.Value);

        return result.Outcome switch
        {
            HisClientOutcome.Unauthorized => ApplicationResult<T>.Fail(ApplicationFailureCode.Unauthorized, result.Message, result.Payload),
            HisClientOutcome.Forbidden => ApplicationResult<T>.Fail(ApplicationFailureCode.Forbidden, result.Message, result.Payload),
            HisClientOutcome.NotFound => ApplicationResult<T>.Fail(ApplicationFailureCode.NotFound, result.Message, result.Payload),
            HisClientOutcome.SignPrecondition => ApplicationResult<T>.Fail(ApplicationFailureCode.SignPrecondition, result.Message, result.Payload),
            HisClientOutcome.Timeout => ApplicationResult<T>.Fail(ApplicationFailureCode.HisTimeout, result.Message, result.Payload),
            HisClientOutcome.BadGateway => ApplicationResult<T>.Fail(ApplicationFailureCode.HisBadGateway, result.Message, result.Payload),
            HisClientOutcome.VendorNotConfigured => ApplicationResult<T>.Fail(ApplicationFailureCode.VendorNotConfigured, result.Message, result.Payload),
            _ => ApplicationResult<T>.Fail(ApplicationFailureCode.HisBadGateway, result.Message, result.Payload)
        };
    }
}
