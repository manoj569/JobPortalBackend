using System.Text.Json;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.Portfolios;
using JobPortal.Application.Common.Text;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateProfileValidationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("O'Connor", true)]
    [InlineData("Anne-Marie", true)]
    [InlineData("राम कुमार", true)]
    [InlineData("!!!", false)]
    [InlineData("<b>John</b>", false)]
    [InlineData("-John", false)]
    public void NamesPermitUnicodeAndPersonalPunctuation(string name, bool valid) =>
        Assert.Equal(valid, PersonalName.IsValid(name));
    [Theory]
    [InlineData(".NET 8 Developer", "3M", ".NET")]
    [InlineData("UI/UX Designer", "AT&T", "C#")]
    [InlineData("L2 Support Engineer", "TCS iON", "C++")]
    public void ProfessionalPunctuationIsValid(string title, string company, string skill)
    {
        var request = new ExperienceRequest(title, company, null, null,
            new(2020, 1, 1), new(2022, 1, 1), false, "Built APIs in C#.", 0,
            1300000, [skill]);
        Assert.True(new ExperienceRequestValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("!!!")]
    [InlineData("\u0001Bad")]
    public void UnsafeProfessionalLabelsAreRejected(string value)
    {
        var request = new ExperienceRequest(value, "3M", null, null,
            new(2020, 1, 1), new(2022, 1, 1), false, null, 0);
        Assert.False(new ExperienceRequestValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1000000000, true)]
    [InlineData(-1, false)]
    [InlineData(1000000001, false)]
    [InlineData(1.5, false)]
    public void SalaryHasSafeWholeNumberRange(decimal amount, bool valid)
    {
        var request = new UpdateCandidateCareerPreferencesRequest([], [], amount, [], [], []);
        Assert.Equal(valid, new UpdateCandidateCareerPreferencesRequestValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData("1e6")]
    [InlineData("1.0")]
    [InlineData("-1")]
    [InlineData("\"1000000\"")]
    [InlineData("1000000001")]
    public void SalaryJsonRejectsUnsupportedFormats(string token)
    {
        var json = "{\"preferredJobRoles\":[],\"preferredCities\":[],\"expectedAnnualSalary\":" +
            token + ",\"jobTypes\":[],\"employmentTypes\":[],\"preferredShifts\":[]}";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateCandidateCareerPreferencesRequest>(json, WebJson));
    }

    [Theory]
    [InlineData("Percentage", "100", true)]
    [InlineData("Percentage", "101", false)]
    [InlineData("Cgpa10", "10", true)]
    [InlineData("Cgpa10", "11", false)]
    [InlineData("Gpa4", "4", true)]
    [InlineData("Gpa4", "5", false)]
    [InlineData("PassFail", "Pass", true)]
    [InlineData("PassFail", "Maybe", false)]
    public void GradesFollowGradingSystem(string system, string score, bool valid)
    {
        var request = new EducationRequest("B.Tech", "K. K. Wagh", "Mechanical Engineering",
            2015, 2020, score, null, 0, null, false, system);
        Assert.Equal(valid, new EducationRequestValidator(TimeProvider.System).Validate(request).IsValid);
    }

    [Fact]
    public void CurrentlyStudyingIgnoresStaleEndYear()
    {
        var request = new EducationRequest("M.Sc", "K. K. Wagh", null,
            2020, 2010, null, null, 0, null, true);
        Assert.True(new EducationRequestValidator(TimeProvider.System).Validate(request).IsValid);
    }
}
