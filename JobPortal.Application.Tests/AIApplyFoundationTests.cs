using System.Text.Json;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyFoundationTests
{
    [Fact]
    public void MatchingJobReturnsDeterministicSkillScoreWithoutAiCall()
    {
        var preferences = Preferences(skills: ["C#", "ASP.NET Core"], titles: ["Developer"]);
        var job = Job("Senior .NET Developer", "C# ASP.NET Core APIs", 1_500_000, WorkplaceType.Remote);
        var result = new DeterministicAIApplyMatcher().Match(new User(), job, preferences, []);
        Assert.True(result.IsEligible); Assert.Equal(100, result.MatchScore); Assert.Equal(2, result.MatchedSkills.Count);
    }

    [Fact]
    public void SalaryAndLocationPreferencesRejectUnsuitableJob()
    {
        var preferences = Preferences(skills: [], titles: ["Developer"]);
        preferences.MinimumSalary = 2_000_000; preferences.WorkplaceTypesJson = JsonSerializer.Serialize(new[] { WorkplaceType.Remote });
        var result = new DeterministicAIApplyMatcher().Match(new User(), Job("Developer", "Role", 1_000_000, WorkplaceType.OnSite), preferences, []);
        Assert.False(result.IsEligible); Assert.Contains("salary_below_minimum", result.SkipReasons); Assert.Contains("workplace_type_not_allowed", result.SkipReasons);
    }

    [Fact]
    public void RequiredSkillRuleRejectsJob()
    {
        var rule = new AIApplyRule { RuleType = AIApplyRuleType.RequiredSkill, Operator = AIApplyRuleOperator.Contains, Value = "Kubernetes", IsEnabled = true };
        var result = new DeterministicAIApplyMatcher().Match(new User(), Job("Developer", "C#", 2_000_000, WorkplaceType.Remote), Preferences([], ["Developer"]), [rule]);
        Assert.False(result.IsEligible); Assert.Contains("required_skill_missing", result.SkipReasons);
    }

    [Fact]
    public void SubmittedApplicationCannotReturnToProcessing()
    {
        var application = new AIApplyApplication { Status = AIApplyRunStatus.Submitted };
        var error = Assert.Throws<ConflictException>(() => AIApplyStateMachine.Transition(application, AIApplyRunStatus.Processing, DateTime.UtcNow));
        Assert.Equal("invalid_application_transition", error.Code);
    }

    [Theory]
    [InlineData("Given Name", "givenName", "text", "FirstName")]
    [InlineData("CV Upload", "cv", "file", "Resume")]
    [InlineData("Mobile Number", "mobile", "tel", "Phone")]
    public void DynamicLabelsMapToSemanticFields(string label, string name, string type, string expected)
    {
        Assert.Equal(expected, new DeterministicApplicationFieldMapper().Map(label, name, null, type, null));
    }

    [Fact]
    public void QueueModelHasOwnerDuplicateAndClaimIndexes()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options;
        using var context = new JobPortalDbContext(options);
        var indexes = context.Model.FindEntityType(typeof(AIApplyApplication))!.GetIndexes().ToList();
        Assert.Contains(indexes, x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["UserId", "JobId"]));
        Assert.Contains(indexes, x => x.Properties.Select(p => p.Name).SequenceEqual(["Status", "ScheduledAtUtc", "Priority"]));
    }

    private static AIApplyPreference Preferences(IReadOnlyList<string> skills, IReadOnlyList<string> titles) => new() { SkillsJson = JsonSerializer.Serialize(skills), JobTitlesJson = JsonSerializer.Serialize(titles), PreferredLocationsJson = "[]", WorkplaceTypesJson = "[]", EmploymentTypesJson = "[]" };
    private static Job Job(string title, string description, decimal maxSalary, WorkplaceType workplace) => new() { Title = title, Description = description, Requirements = "", MaximumSalary = maxSalary, WorkplaceType = workplace, EmploymentType = EmploymentType.FullTime };
}
