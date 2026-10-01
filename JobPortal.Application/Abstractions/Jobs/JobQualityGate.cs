using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

public enum JobQualityDecision
{
    Eligible = 1,
    NeedsReview = 2,
    Rejected = 3
}

public enum JobQualityReasonCode
{
    None = 0,

    MissingTitle,
    MissingDescription,
    MissingCompany,
    MissingCategory,
    MissingApplicationUrl,
    InvalidApplicationUrl,
    MissingExpiry,
    Expired,

    InvalidSalaryRange,
    InvalidExperienceRange,

    MissingLocation,
    MissingWorkplaceType,
    MissingEmploymentType,
    MissingExperienceLevel
}

public sealed record JobQualityResult
{
    public JobQualityDecision Decision { get; init; }

    public IReadOnlyList<JobQualityReasonCode> Reasons { get; init; } =
        Array.Empty<JobQualityReasonCode>();

    public bool IsEligible =>
        Decision == JobQualityDecision.Eligible;
}

public interface IJobQualityGate
{
    JobQualityResult Evaluate(
        Job job,
        DateTime utcNow);
}
