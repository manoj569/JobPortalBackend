using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class RecommendationMatcherTests
{
    [Fact]
    public void MatchingSkillsRoleAndLocationProduceFactualReasons()
    {
        var result = CandidateService.Score(Job("Senior .NET Backend Developer", "C# SQL ASP.NET", "Bengaluru"),
            ["c#", "sql", ".net"], ["backend developer"], [], ["bengaluru"]);

        Assert.True(result.MatchScore > 0);
        Assert.InRange(result.MatchReasons.Count, 1, 3);
        Assert.Contains(result.MatchReasons, reason => reason.StartsWith("Matches skills:", StringComparison.Ordinal));
        Assert.Contains(result.MatchReasons, reason => reason.Contains("Bengaluru", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MoreMatchingSkillsSortAheadAndZeroMatchCanBeExcluded()
    {
        var strong = CandidateService.Score(Job("Backend Developer", "C# SQL Azure", "Pune"),
            ["c#", "sql", "azure"], ["backend developer"], [], []);
        var weak = CandidateService.Score(Job("Backend Developer", "C#", "Pune"),
            ["c#", "sql", "azure"], ["backend developer"], [], []);
        var zero = CandidateService.Score(Job("Accountant", "Tax and audit", "Mumbai"),
            ["c#", "sql"], ["backend developer"], [], ["pune"]);

        Assert.True(strong.MatchScore > weak.MatchScore);
        Assert.Equal(0, zero.MatchScore);
        Assert.Empty(zero.MatchReasons);
    }

    private static RecommendationJobCandidate Job(string title, string description, string location) =>
        new(Guid.NewGuid(), "JOB-1", title, "job", description, null, null, Guid.NewGuid(),
            "Acme", "acme", null, "Technology", Guid.NewGuid(), "Engineering", location,
            EmploymentType.FullTime, WorkplaceType.Hybrid, ExperienceLevel.Mid, false,
            DateTime.UtcNow, null);
}
