using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.ExamRecords;
using HealthExam.Domain.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class PatientExamHistoryHandlerTests
{
    private const string Division = "D01";

    private static (GetPatientExamHistoryHandler Sut, FakeExamRecordRepository Records, Patient Patient)
        Build(Guid? lineageID = null)
    {
        var patient = new Patient
        {
            PatientRefID = Guid.NewGuid(),
            DivisionID = Division,
            FullName = "Nguyễn Văn An",
            ProfileLineageID = lineageID ?? Guid.NewGuid(),
            // Phiên bản đã bị thay thế vẫn phải tra được lịch sử khám.
            IsActive = false
        };
        var patients = new FakePatientRepository(patient);
        var records = new FakeExamRecordRepository();
        return (new GetPatientExamHistoryHandler(patients, records), records, patient);
    }

    [Fact]
    public async Task PatientRefID_rong_tra_BadRequest()
    {
        var (sut, _, _) = Build();

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, Guid.Empty));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.BadRequest, result.Failure.Code);
    }

    [Fact]
    public async Task Khong_tim_thay_nguoi_benh_tra_NotFound()
    {
        var (sut, _, _) = Build();

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Ho_so_don_vi_khac_tra_NotFound()
    {
        var (sut, _, patient) = Build();

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery("D99", patient.PatientRefID));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationFailureCode.NotFound, result.Failure.Code);
    }

    [Fact]
    public async Task Tra_cuu_theo_lineage_cua_phien_ban_duoc_truyen()
    {
        var lineageID = Guid.NewGuid();
        var (sut, records, patient) = Build(lineageID);

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID));

        Assert.True(result.IsSuccess);
        Assert.Equal(Division, records.LastHistoryDivisionId);
        Assert.Equal(lineageID, records.LastHistoryLineageID);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Size_khong_hop_le_ve_mac_dinh_5(int size)
    {
        var (sut, records, patient) = Build();

        await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID, Page: 0, Size: size));

        Assert.Equal(1, records.LastHistoryPage);
        Assert.Equal(5, records.LastHistorySize);
    }

    [Fact]
    public async Task Size_vuot_tran_bi_kep_200()
    {
        var (sut, records, patient) = Build();

        await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID, Page: 2, Size: 10000));

        Assert.Equal(2, records.LastHistoryPage);
        Assert.Equal(200, records.LastHistorySize);
    }

    [Fact]
    public async Task Tra_thang_trang_ket_qua_cua_repository()
    {
        var (sut, records, patient) = Build();
        records.HistoryPage = new PageResult<ExamRecordResult>(
            new List<ExamRecordResult>(), 3, 5, 42);

        var result = await sut.HandleAsync(new GetPatientExamHistoryQuery(Division, patient.PatientRefID, Page: 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.Total);
        Assert.Equal(3, result.Value.Page);
    }
}
