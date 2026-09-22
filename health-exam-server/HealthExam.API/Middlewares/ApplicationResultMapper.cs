using HealthExam.Application.Common;
using HealthExam.API.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HealthExam.API.Middlewares;

public static class ApplicationResultMapper
{
    public static int ToErrorCode(ApplicationFailureCode code) => code switch
    {
        ApplicationFailureCode.BadRequest => ErrorCodes.BadRequest,
        ApplicationFailureCode.Unauthorized => ErrorCodes.Unauthorized,
        ApplicationFailureCode.Forbidden => ErrorCodes.Forbidden,
        ApplicationFailureCode.NotOwner => ErrorCodes.NotOwner,
        ApplicationFailureCode.NotFound => ErrorCodes.NotFound,
        ApplicationFailureCode.InvalidState => ErrorCodes.InvalidState,
        ApplicationFailureCode.FileInvalid => ErrorCodes.FileInvalid,
        ApplicationFailureCode.SessionClosed => ErrorCodes.SessionClosed,
        ApplicationFailureCode.NotInSession => ErrorCodes.NotInSession,
        ApplicationFailureCode.DuplicateInSession => ErrorCodes.DuplicateInSession,
        ApplicationFailureCode.ParaclinicalResultExists => ErrorCodes.ParaclinicalResultExists,
        ApplicationFailureCode.VendorPayloadIncomplete => ErrorCodes.VendorPayloadIncomplete,
        ApplicationFailureCode.VendorNotConfigured => ErrorCodes.VendorNotConfigured,
        ApplicationFailureCode.DependencyUnavailable => ErrorCodes.DependencyUnavailable,
        ApplicationFailureCode.HisTimeout => ErrorCodes.HisTimeout,
        ApplicationFailureCode.HisBadGateway => ErrorCodes.HisBadGateway,
        ApplicationFailureCode.SignPrecondition => ErrorCodes.SignPrecondition,
        ApplicationFailureCode.PatientProfileChanged => ErrorCodes.PatientProfileChanged,
        _ => ErrorCodes.InternalError
    };

    public static int ToHttpStatus(ApplicationFailureCode code)
        => ErrorCodes.ToHttpStatus(ToErrorCode(code));

    public static ActionResult<ResultData<T>> ToActionResult<T>(ApplicationResult<T> result, string traceId = "")
    {
        if (result.IsSuccess)
        {
            return new ObjectResult(ResultData<T>.Ok(result.Value, traceId))
            {
                StatusCode = StatusCodes.Status200OK
            };
        }

        var errorCode = ToErrorCode(result.Failure.Code);
        var httpStatus = ErrorCodes.ToHttpStatus(errorCode);

        if (result.Failure.Payload != null)
        {
            if (result.Failure.Payload is T typedPayload)
            {
                return new ObjectResult(ResultData<T>.Fail(errorCode, result.Failure.Message, typedPayload, traceId))
                {
                    StatusCode = httpStatus
                };
            }

            return new ObjectResult(ResultData<object>.Fail(errorCode, result.Failure.Message, result.Failure.Payload, traceId))
            {
                StatusCode = httpStatus
            };
        }

        return new ObjectResult(ResultData<T>.Fail(errorCode, result.Failure.Message, default, traceId))
        {
            StatusCode = httpStatus
        };
    }
}
