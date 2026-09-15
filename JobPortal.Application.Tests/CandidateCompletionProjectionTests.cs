using System.Text.Json;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateCompletionProjectionTests
{
    private static readonly string[] Roles = [".NET Developer"];
    private static readonly string[] Cities = ["Pune"];
    [Fact]
    public void EmploymentOnlyIsCountedAndFirstMissingStepIsBasicDetails()
    {
        var user = new User();
        var result = CandidateProfileCompletionProjection.Create(user, false, false, true);
        Assert.True(result.CompletionPercentage > 0);
        Assert.Contains("Employment", result.CompletedSections);
        Assert.DoesNotContain("Employment", result.MissingSections);
        Assert.Equal("BasicDetails", result.NextRecommendedIncompleteStep);
    }

    [Theory]
    [InlineData(CandidateWorkStatus.Fresher, false)]
    [InlineData(CandidateWorkStatus.Experienced, true)]
    public void ApplicableCompleteProfileReachesOneHundredPercent(CandidateWorkStatus status, bool employment)
    {
        var user = new User
        {
            FirstName = "Manoj", LastName = "Shekapure", WorkStatus = status,
            CurrentCountry = "India", CurrentCity = "Pune", Headline = ".NET Developer", Bio = "Developer",
            PreferredJobRolesJson = JsonSerializer.Serialize(Roles),
            PreferredCitiesJson = JsonSerializer.Serialize(Cities),
            ResumeStorageKey = "resume"
        };
        var result = CandidateProfileCompletionProjection.Create(user, true, true, employment);
        Assert.Equal(100, result.CompletionPercentage);
        Assert.Empty(result.MissingSections);
        Assert.Null(result.NextRecommendedIncompleteStep);
        if (status == CandidateWorkStatus.Fresher) Assert.DoesNotContain("Employment", result.CompletedSections);
    }
}
