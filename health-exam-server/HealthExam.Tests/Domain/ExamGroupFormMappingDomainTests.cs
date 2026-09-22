using System;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamForms;
using HealthExam.Domain.ExamRecords;
using Xunit;

namespace HealthExam.Tests.Domain;

public class ExamGroupFormMappingDomainTests
{
    [Fact]
    public void ExamGroupFormMapping_initializes_with_expected_defaults()
    {
        var mapping = new ExamGroupFormMapping
        {
            DivisionID = "DEV",
            VariantCode = "DTK_03",
            TemplateCode = "KSK-TREN18TUOI"
        };

        Assert.Equal(Guid.Empty, mapping.MappingID);
        Assert.True(mapping.IsActive);
        Assert.Equal("DEV", mapping.DivisionID);
        Assert.Equal("DTK_03", mapping.VariantCode);
        Assert.Equal("KSK-TREN18TUOI", mapping.TemplateCode);
        Assert.NotNull(mapping.Sections);
        Assert.Empty(mapping.Sections);
    }

    [Fact]
    public void ExamRecord_contains_his_registration_form_state_fields()
    {
        var record = new ExamRecord();
        Assert.Null(record.HisEmrDataID);
        Assert.Null(record.HisFormTemplateID);
        Assert.Equal("Pending", record.HisFormSyncStatus);
        Assert.Equal("", record.HisFormSyncError);
    }
}
