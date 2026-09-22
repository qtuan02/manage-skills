using HealthExam.Application.Patients;
using Xunit;

namespace HealthExam.Tests.Application;

public class PatientSearchKeywordClassifierTests
{
    [Theory]
    [InlineData("079123456789")]   // 12 số → CCCD
    [InlineData("123456789")]      // 9 số → CMND
    public void Digits_of_identity_length_classify_as_identity_only(string keyword)
    {
        var fields = PatientSearchKeywordClassifier.Classify(keyword);
        Assert.Equal(new[] { PatientSearchField.Identity }, fields);
    }

    [Fact]
    public void Ten_digits_starting_with_zero_classify_as_phone_only()
    {
        var fields = PatientSearchKeywordClassifier.Classify("0901234567");
        Assert.Equal(new[] { PatientSearchField.Phone }, fields);
    }

    [Theory]
    [InlineData("0791")]          // gõ dở
    [InlineData("1234567890")]    // 10 số nhưng không bắt đầu bằng 0
    [InlineData("12345678901")]   // 11 số
    public void Other_all_digit_keywords_search_identity_phone_and_code(string keyword)
    {
        var fields = PatientSearchKeywordClassifier.Classify(keyword);
        Assert.Equal(
            new[] { PatientSearchField.Identity, PatientSearchField.Phone, PatientSearchField.Code },
            fields);
    }

    [Theory]
    [InlineData("Nguyễn Văn An")]
    [InlineData("HEX-KSK2026")]
    [InlineData("BN000125001")]
    public void Keywords_with_letters_search_name_and_code(string keyword)
    {
        var fields = PatientSearchKeywordClassifier.Classify(keyword);
        Assert.Equal(new[] { PatientSearchField.Name, PatientSearchField.Code }, fields);
    }

    [Fact]
    public void Classify_trims_before_measuring_length()
    {
        var fields = PatientSearchKeywordClassifier.Classify("  079123456789  ");
        Assert.Equal(new[] { PatientSearchField.Identity }, fields);
    }
}
