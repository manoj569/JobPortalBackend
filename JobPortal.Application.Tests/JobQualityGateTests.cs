using Xunit;

using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Tests;

public sealed class JobQualityGateTests
{
    private static readonly DateTime Now =
        new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly JobQualityGate _gate = new();

    [Fact]
    public void EvaluateCompleteValidJobReturnsEligible()
    {
        var job = CreateValidJob();

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Eligible, result.Decision);
        Assert.True(result.IsEligible);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void EvaluateMissingLocationRequiresReview()
    {
        var job = CreateValidJob();
        job.Location = null;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.NeedsReview, result.Decision);
        Assert.Contains(JobQualityReasonCode.MissingLocation, result.Reasons);
    }

    [Fact]
    public void EvaluateMissingExpiryReturnsEligible()
    {
        var job = CreateValidJob();
        job.ExpiresAtUtc = null;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Eligible, result.Decision);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void EvaluateMissingWorkplaceTypeReturnsEligible()
    {
        var job = CreateValidJob();
        job.WorkplaceType = default;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Eligible, result.Decision);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void EvaluateMissingEmploymentTypeReturnsEligible()
    {
        var job = CreateValidJob();
        job.EmploymentType = default;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Eligible, result.Decision);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void EvaluateMissingOptionalSalaryEducationAndExperienceRemainsEligible()
    {
        var job = CreateValidJob();

        job.MinimumSalary = null;
        job.MaximumSalary = null;

        job.MinimumExperienceYears = null;
        job.MaximumExperienceYears = null;

        job.EducationRequirement = null;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Eligible, result.Decision);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void EvaluateInvalidApplicationUrlReturnsRejected()
    {
        var job = CreateValidJob();
        job.ApplicationUrl = "not-a-valid-url";

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Rejected, result.Decision);
        Assert.False(result.IsEligible);

        Assert.Contains(
            JobQualityReasonCode.InvalidApplicationUrl,
            result.Reasons);
    }

    [Fact]
    public void EvaluateExpiredJobReturnsRejected()
    {
        var job = CreateValidJob();
        job.ExpiresAtUtc = Now.AddMinutes(-1);

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Rejected, result.Decision);

        Assert.Contains(
            JobQualityReasonCode.Expired,
            result.Reasons);
    }

    [Fact]
    public void EvaluateInvalidSalaryRangeReturnsRejected()
    {
        var job = CreateValidJob();

        job.MinimumSalary = 20;
        job.MaximumSalary = 10;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Rejected, result.Decision);

        Assert.Contains(
            JobQualityReasonCode.InvalidSalaryRange,
            result.Reasons);
    }

    [Fact]
    public void EvaluateInvalidExperienceRangeReturnsRejected()
    {
        var job = CreateValidJob();

        job.MinimumExperienceYears = 5;
        job.MaximumExperienceYears = 2;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Rejected, result.Decision);

        Assert.Contains(
            JobQualityReasonCode.InvalidExperienceRange,
            result.Reasons);
    }

    [Fact]
    public void EvaluateMissingCoreDataReturnsRejected()
    {
        var job = CreateValidJob();

        job.Title = "";
        job.Description = "";
        job.CompanyId = Guid.Empty;
        job.CategoryId = Guid.Empty;
        job.ApplicationUrl = "";

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Rejected, result.Decision);

        Assert.Contains(JobQualityReasonCode.MissingTitle, result.Reasons);
        Assert.Contains(JobQualityReasonCode.MissingCompany, result.Reasons);
        Assert.Contains(JobQualityReasonCode.MissingApplicationUrl, result.Reasons);
    }

    [Fact]
    public void EvaluateRejectedReasonTakesPriorityOverReviewReason()
    {
        var job = CreateValidJob();

        job.ApplicationUrl = "invalid-url";
        job.Location = null;

        var result = _gate.Evaluate(job, Now);

        Assert.Equal(JobQualityDecision.Rejected, result.Decision);

        Assert.Contains(
            JobQualityReasonCode.InvalidApplicationUrl,
            result.Reasons);
    }

    private static Job CreateValidJob()
    {
        return new Job
        {
            Id = Guid.NewGuid(),
            ReferenceNumber = "JOB-TEST-001",
            Title = ".NET Developer",
            Slug = "dotnet-developer",
            Description =
                "Develop and maintain ASP.NET Core applications and APIs.",

            ApplicationUrl =
                "https://careers.example.com/jobs/123",

            Location = "Pune, Maharashtra",

            EmploymentType =
                EmploymentType.FullTime,

            WorkplaceType =
                WorkplaceType.Hybrid,

            ExperienceLevel =
                ExperienceLevel.Mid,

            MinimumExperienceYears = 2,
            MaximumExperienceYears = 4,

            CompanyId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),

            Status = JobStatus.Draft,

            ExpiresAtUtc =
                Now.AddDays(30)
        };
    }
}
