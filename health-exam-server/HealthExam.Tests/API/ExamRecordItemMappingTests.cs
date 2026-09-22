using System;
using HealthExam.API.Contracts;
using HealthExam.Application.ExamRecords;
using HealthExam.Domain.Common;
using Xunit;

namespace HealthExam.Tests.API;

/// <summary>
/// ExamRecordItem.From is a pure static mapper (HealthExam.API/Contracts/ExamRecordModels.cs) from the
/// Application-layer ExamRecordResult. Regression guard for the HIS sign-status fields: an earlier task
/// added HisSignStatus/HisSignedAt/HisSignedFilePath to ExamRecordResult and to PatientController's
/// exam-history response, but GET /v1/exam-records and GET /v1/exam-records/{id} (which serialize
/// ExamRecordItem) silently dropped them since From() never copied them across.
/// </summary>
public class ExamRecordItemMappingTests
{
    [Fact]
    public void From_maps_HisSignStatus_HisSignedAt_and_HisSignedFilePath()
    {
        var signedAt = new DateTime(2026, 3, 10, 9, 15, 0, DateTimeKind.Utc);

        var result = new ExamRecordResult(
            RecordID: Guid.NewGuid(),
            SessionID: Guid.NewGuid(),
            SessionCode: "S001",
            ExamDate: new DateOnly(2026, 3, 10),
            RecordCode: "KSK-001",
            PatientID: 1,
            AdmissionID: null,
            PatientCode: "BN001",
            FullName: "Nguyen Van A",
            Dob: null,
            BirthYear: null,
            GenderID: 1,
            IdentityNumber: "",
            InsuranceNumber: "",
            PhoneNumber: "",
            Email: "",
            Address: "",
            StaffCode: "",
            OrgDeptName: "",
            JobTitle: "",
            VariantCode: "DTK_01",
            VariantName: "",
            PackageID: null,
            PackageName: "",
            FormID: null,
            FormCode: "",
            SubmissionID: null,
            State: ExamRecordState.Completed,
            StateName: "Đã khám",
            RegisteredAt: null,
            ExamStartedAt: null,
            ExamFinishedAt: null,
            CancelledAt: null,
            CancelReason: "",
            ProgressDone: 0,
            ProgressTotal: 0,
            HealthClassCode: "",
            Note: null,
            EthnicityCode: "",
            EthnicityName: "",
            OccupationCode: "",
            OccupationName: "",
            BloodAboCode: "",
            BloodAboName: "",
            BloodRhCode: "",
            BloodRhName: "",
            ProvinceCode: "",
            ProvinceName: "",
            WardCode: "",
            WardName: "",
            IdentityIssuedDate: null,
            IdentityIssuerCode: "",
            IdentityIssuerName: "",
            RelativeRelationshipCode: "",
            RelativeRelationshipName: "",
            RelativeFullName: "",
            RelativeIdentityNumber: "",
            RelativePhoneNumber: "",
            InsuranceObjectCode: "",
            InsuranceObjectName: "",
            InsuranceValidFrom: null,
            InsuranceValidTo: null,
            ExamReason: "",
            PatientTypeCode: "",
            PatientTypeName: "",
            PaymentSourceCode: "",
            PaymentSourceName: "",
            PaymentSourceOther: "",
            ExamLocationCode: "",
            ExamLocationName: "",
            CreatedDate: DateTime.UtcNow,
            ModifiedDate: DateTime.UtcNow,
            HisSignStatus: "Signed",
            HisSignedAt: signedAt,
            HisSignedFilePath: "/Signed/2026/03/KSK-001.pdf");

        var item = ExamRecordItem.From(result);

        Assert.Equal("Signed", item.HisSignStatus);
        Assert.Equal(signedAt, item.HisSignedAt);
        Assert.Equal("/Signed/2026/03/KSK-001.pdf", item.HisSignedFilePath);
    }
}
