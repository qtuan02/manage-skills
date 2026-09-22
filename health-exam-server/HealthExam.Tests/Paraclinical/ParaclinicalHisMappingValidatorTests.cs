using System.Collections.Generic;
using HealthExam.Application.Common;
using HealthExam.Application.Paraclinical;
using Xunit;

namespace HealthExam.Tests.Paraclinical;

public class ParaclinicalHisMappingValidatorTests
{
    [Fact]
    public void Validate_returns_success_when_all_identities_and_services_are_valid()
    {
        var identity = new ParaclinicalHisIdentity(
            HisPtId: 1001,
            HisPtCode: "BN001",
            HisAdmissionId: 2001,
            HisAdmissionCode: "HEX-ADM-001",
            HisTreatmentProcessId: 3001,
            PCReqDoctorId: 4001,
            ReqDeptId: 10);

        var items = new List<ParaclinicalServiceMappingItem>
        {
            new("XN_MAU", 501, "Tổng phân tích máu", "XN"),
            new("XQUANG_PHOI", 502, "X-Quang ngực thẳng", "CDHA")
        };

        var result = ParaclinicalSubmitPreconditionValidator.Validate(identity, items);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Validate_fails_when_identity_is_null()
    {
        var items = new List<ParaclinicalServiceMappingItem>
        {
            new("XN_MAU", 501, "Tổng phân tích máu", "XN")
        };

        var result = ParaclinicalSubmitPreconditionValidator.Validate(null, items);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, result.Failure.Code);
        var errors = Assert.IsType<ApplicationValidationErrors>(result.Failure.Payload);
        Assert.Contains(errors.Errors, e => e.Field == "HisIdentity");
    }

    [Theory]
    [InlineData(0, "BN001", 2001, 3001, 4001, 10, "HisPtId")]
    [InlineData(1001, "", 2001, 3001, 4001, 10, "HisPtId")]
    [InlineData(1001, "BN001", 0, 3001, 4001, 10, "HisAdmissionId")]
    [InlineData(1001, "BN001", 2001, 0, 4001, 10, "HisTreatmentProcessId")]
    [InlineData(1001, "BN001", 2001, 3001, 0, 10, "PCReqDoctorId")]
    [InlineData(1001, "BN001", 2001, 3001, 4001, 0, "ReqDeptId")]
    public void Validate_fails_when_any_his_identity_is_missing(
        long ptId, string ptCode, long admId, long tpid, long doctorId, int deptId, string expectedField)
    {
        var identity = new ParaclinicalHisIdentity(ptId, ptCode, admId, "ADM", tpid, doctorId, deptId);
        var items = new List<ParaclinicalServiceMappingItem>
        {
            new("XN_MAU", 501, "Tổng phân tích máu", "XN")
        };

        var result = ParaclinicalSubmitPreconditionValidator.Validate(identity, items);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, result.Failure.Code);
        var errors = Assert.IsType<ApplicationValidationErrors>(result.Failure.Payload);
        Assert.Contains(errors.Errors, e => e.Field == expectedField);
    }

    [Fact]
    public void Validate_fails_when_items_list_is_empty()
    {
        var identity = new ParaclinicalHisIdentity(1001, "BN001", 2001, "ADM", 3001, 4001, 10);
        var items = new List<ParaclinicalServiceMappingItem>();

        var result = ParaclinicalSubmitPreconditionValidator.Validate(identity, items);

        Assert.False(result.IsSuccess);
        var errors = Assert.IsType<ApplicationValidationErrors>(result.Failure.Payload);
        Assert.Contains(errors.Errors, e => e.Field == "Items");
    }

    [Fact]
    public void Validate_fails_when_service_mapping_medserid_is_missing()
    {
        var identity = new ParaclinicalHisIdentity(1001, "BN001", 2001, "ADM", 3001, 4001, 10);
        var items = new List<ParaclinicalServiceMappingItem>
        {
            new("XN_MAU", 501, "Tổng phân tích máu", "XN"),
            new("SA_BUNG", 0, "Siêu âm bụng", "CDHA")
        };

        var result = ParaclinicalSubmitPreconditionValidator.Validate(identity, items);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.SignPrecondition, result.Failure.Code);
        var errors = Assert.IsType<ApplicationValidationErrors>(result.Failure.Payload);
        Assert.Contains(errors.Errors, e => e.Field == "Items[1].HisMedSerId");
    }
}
