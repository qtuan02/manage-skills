using System;
using HealthExam.API.Middlewares;
using HealthExam.Application.Common;
using HealthExam.API.Contracts;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HealthExam.Tests;

/// <summary>
/// Bảng mã lỗi 02-api-spec §2. FE phân nhánh theo ErrorCode còn gateway/log lọc theo HTTP
/// status, nên hai thứ phải ánh xạ đúng nhau.
/// </summary>
public class ErrorCodeTests
{
    [Theory]
    [InlineData(ErrorCodes.Success, 200)]
    [InlineData(ErrorCodes.BadRequest, 400)]
    [InlineData(ErrorCodes.Unauthorized, 401)]
    [InlineData(ErrorCodes.Forbidden, 403)]
    [InlineData(ErrorCodes.NotOwner, 403)]
    [InlineData(ErrorCodes.NotFound, 404)]
    [InlineData(ErrorCodes.InvalidState, 409)]
    [InlineData(ErrorCodes.SessionClosed, 409)]
    [InlineData(ErrorCodes.NotInSession, 409)]
    [InlineData(ErrorCodes.DuplicateInSession, 409)]
    [InlineData(ErrorCodes.SignPrecondition, 422)]
    [InlineData(ErrorCodes.InternalError, 500)]
    public void Ma_loi_anh_xa_dung_http_status(int errorCode, int expected)
        => Assert.Equal(expected, ErrorCodes.ToHttpStatus(errorCode));

    [Fact]
    public void Ma_rieng_cua_KSK_co_thong_bao_noi_duoc_viec_can_lam()
    {
        Assert.Equal("Đợt khám đã đóng", ErrorCodes.DefaultMessage(ErrorCodes.SessionClosed));
        Assert.Equal("Người bệnh đã có hồ sơ trong đợt khám này",
            ErrorCodes.DefaultMessage(ErrorCodes.DuplicateInSession));
    }

    [Fact]
    public void Envelope_loi_giu_TraceID()
    {
        var result = ResultData<object>.Fail(ErrorCodes.InvalidState, traceId: "T1");

        Assert.Equal(ErrorCodes.InvalidState, result.ErrorCode);
        Assert.Equal("T1", result.TraceID);
        Assert.Equal("Trạng thái không cho phép thao tác này", result.Message);
    }

    [Theory]
    [InlineData(ApplicationFailureCode.BadRequest, ErrorCodes.BadRequest, 400)]
    [InlineData(ApplicationFailureCode.Unauthorized, ErrorCodes.Unauthorized, 401)]
    [InlineData(ApplicationFailureCode.Forbidden, ErrorCodes.Forbidden, 403)]
    [InlineData(ApplicationFailureCode.NotOwner, ErrorCodes.NotOwner, 403)]
    [InlineData(ApplicationFailureCode.NotFound, ErrorCodes.NotFound, 404)]
    [InlineData(ApplicationFailureCode.InvalidState, ErrorCodes.InvalidState, 409)]
    [InlineData(ApplicationFailureCode.FileInvalid, ErrorCodes.FileInvalid, 400)]
    [InlineData(ApplicationFailureCode.SessionClosed, ErrorCodes.SessionClosed, 409)]
    [InlineData(ApplicationFailureCode.NotInSession, ErrorCodes.NotInSession, 409)]
    [InlineData(ApplicationFailureCode.DuplicateInSession, ErrorCodes.DuplicateInSession, 409)]
    [InlineData(ApplicationFailureCode.ParaclinicalResultExists, ErrorCodes.ParaclinicalResultExists, 409)]
    [InlineData(ApplicationFailureCode.VendorPayloadIncomplete, ErrorCodes.VendorPayloadIncomplete, 422)]
    [InlineData(ApplicationFailureCode.VendorNotConfigured, ErrorCodes.VendorNotConfigured, 503)]
    [InlineData(ApplicationFailureCode.DependencyUnavailable, ErrorCodes.DependencyUnavailable, 503)]
    [InlineData(ApplicationFailureCode.HisTimeout, ErrorCodes.HisTimeout, 504)]
    [InlineData(ApplicationFailureCode.HisBadGateway, ErrorCodes.HisBadGateway, 502)]
    [InlineData(ApplicationFailureCode.SignPrecondition, ErrorCodes.SignPrecondition, 422)]
    public void ApplicationFailureCode_anh_xa_dung_ErrorCode_va_HttpStatus(
        ApplicationFailureCode code, int expectedErrorCode, int expectedHttpStatus)
    {
        var actualErrorCode = ApplicationResultMapper.ToErrorCode(code);
        Assert.Equal(expectedErrorCode, actualErrorCode);
        Assert.Equal(expectedHttpStatus, ErrorCodes.ToHttpStatus(actualErrorCode));
        Assert.Equal(expectedHttpStatus, ApplicationResultMapper.ToHttpStatus(code));
    }

    [Fact]
    public void All_ApplicationFailureCodes_map_to_valid_non_zero_ErrorCode()
    {
        foreach (ApplicationFailureCode code in Enum.GetValues(typeof(ApplicationFailureCode)))
        {
            var errorCode = ApplicationResultMapper.ToErrorCode(code);
            Assert.NotEqual(0, errorCode);
            Assert.NotEqual(ErrorCodes.InternalError, errorCode);
        }
    }

    [Fact]
    public void ApplicationResultMapper_ToActionResult_success()
    {
        var result = ApplicationResult<string>.Success("data");
        var actionResult = ApplicationResultMapper.ToActionResult(result, "trace-123");
        var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(200, objectResult.StatusCode);
        var envelope = Assert.IsType<ResultData<string>>(objectResult.Value);
        Assert.Equal(ErrorCodes.Success, envelope.ErrorCode);
        Assert.Equal("data", envelope.Data);
        Assert.Equal("trace-123", envelope.TraceID);
    }

    [Fact]
    public void ApplicationResultMapper_ToActionResult_failure()
    {
        var result = ApplicationResult<string>.Fail(ApplicationFailureCode.SessionClosed, "Session closed");
        var actionResult = ApplicationResultMapper.ToActionResult(result, "trace-456");
        var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(409, objectResult.StatusCode);
        var envelope = Assert.IsType<ResultData<string>>(objectResult.Value);
        Assert.Equal(ErrorCodes.SessionClosed, envelope.ErrorCode);
        Assert.Equal("Session closed", envelope.Message);
        Assert.Equal("trace-456", envelope.TraceID);
    }

    [Fact]
    public void ApplicationResultMapper_ToActionResult_failure_with_payload()
    {
        var errors = new ValidationErrors { { "ProvinceCode", "Mã tỉnh/thành phố không được để trống" } };
        var result = ApplicationResult<string>.Fail(ApplicationFailureCode.BadRequest, "Validation failed", errors);
        var actionResult = ApplicationResultMapper.ToActionResult(result, "trace-789");
        var objectResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(400, objectResult.StatusCode);
        var envelope = Assert.IsType<ResultData<object>>(objectResult.Value);
        Assert.Equal(ErrorCodes.BadRequest, envelope.ErrorCode);
        Assert.Equal("Validation failed", envelope.Message);
        Assert.Equal("trace-789", envelope.TraceID);
        var data = Assert.IsType<ValidationErrors>(envelope.Data);
        Assert.Equal("Mã tỉnh/thành phố không được để trống", data["ProvinceCode"]);
    }
}
