using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Services;

public sealed class JobQualityGate : IJobQualityGate
{
    public JobQualityResult Evaluate(
        Job job,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(job);

        var rejected = new List<JobQualityReasonCode>();
        var review = new List<JobQualityReasonCode>();

        // ---------------------------------------------------------
        // HARD FAILURES
        // These jobs must never become eligible for auto-publish.
        // ---------------------------------------------------------

        if (string.IsNullOrWhiteSpace(job.Title))
        {
            rejected.Add(
                JobQualityReasonCode.MissingTitle);
        }

        if (string.IsNullOrWhiteSpace(job.Description))
        {
            rejected.Add(
                JobQualityReasonCode.MissingDescription);
        }

        if (job.CompanyId == Guid.Empty)
        {
            rejected.Add(
                JobQualityReasonCode.MissingCompany);
        }

        if (job.CategoryId == Guid.Empty)
        {
            rejected.Add(
                JobQualityReasonCode.MissingCategory);
        }

        if (string.IsNullOrWhiteSpace(job.ApplicationUrl))
        {
            rejected.Add(
                JobQualityReasonCode.MissingApplicationUrl);
        }
        else if (!Uri.TryCreate(
                     job.ApplicationUrl,
                     UriKind.Absolute,
                     out var applicationUri) ||
                 applicationUri.Scheme is not ("http" or "https"))
        {
            rejected.Add(
                JobQualityReasonCode.InvalidApplicationUrl);
        }

        // An explicitly expired job must never be auto-published.
        if (job.ExpiresAtUtc.HasValue &&
            job.ExpiresAtUtc.Value <= utcNow)
        {
            rejected.Add(
                JobQualityReasonCode.Expired);
        }

        // Validate salary only when salary information exists.
        // Missing salary is allowed.
        if (job.MinimumSalary < 0 ||
            job.MaximumSalary < 0 ||
            (job.MinimumSalary.HasValue &&
             job.MaximumSalary.HasValue &&
             job.MinimumSalary.Value >
             job.MaximumSalary.Value))
        {
            rejected.Add(
                JobQualityReasonCode.InvalidSalaryRange);
        }

        // Validate experience only when experience information exists.
        // Missing experience is allowed.
        if (job.MinimumExperienceYears < 0 ||
            job.MaximumExperienceYears < 0 ||
            (job.MinimumExperienceYears.HasValue &&
             job.MaximumExperienceYears.HasValue &&
             job.MinimumExperienceYears.Value >
             job.MaximumExperienceYears.Value))
        {
            rejected.Add(
                JobQualityReasonCode.InvalidExperienceRange);
        }

        // Hard failures always take priority over review warnings.
        if (rejected.Count > 0)
        {
            return new JobQualityResult
            {
                Decision = JobQualityDecision.Rejected,

                Reasons = rejected
                    .Distinct()
                    .ToArray()
            };
        }

        // ---------------------------------------------------------
        // REVIEW CONDITIONS
        //
        // These do not mean the job is invalid.
        // They mean the job should remain Draft for admin review
        // instead of being eligible for unattended publishing.
        // ---------------------------------------------------------

        // JobService.PublishAsync requires a future expiry date.
        // Missing expiry therefore requires admin review.
        if (!job.ExpiresAtUtc.HasValue)
        {
            review.Add(
                JobQualityReasonCode.MissingExpiry);
        }

        if (string.IsNullOrWhiteSpace(job.Location))
        {
            review.Add(
                JobQualityReasonCode.MissingLocation);
        }

        // Enum default value 0 represents missing/unreliable
        // structured source information.
        if ((int)job.WorkplaceType == 0)
        {
            review.Add(
                JobQualityReasonCode.MissingWorkplaceType);
        }

        if ((int)job.EmploymentType == 0)
        {
            review.Add(
                JobQualityReasonCode.MissingEmploymentType);
        }

        if (review.Count > 0)
        {
            return new JobQualityResult
            {
                Decision = JobQualityDecision.NeedsReview,

                Reasons = review
                    .Distinct()
                    .ToArray()
            };
        }

        // Salary, education and experience are intentionally not
        // mandatory. Many legitimate job listings omit these fields.
        //
        // Missing values must never be fabricated just to make a job
        // eligible for automatic publishing.

        return new JobQualityResult
        {
            Decision = JobQualityDecision.Eligible,

            Reasons =
                Array.Empty<JobQualityReasonCode>()
        };
    }
}
