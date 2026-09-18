using System.Linq.Expressions;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.PublicJobs;

public static class PublicJobProjections
{
    public static readonly Expression<Func<Job, PublicJobSummary>> Summary = job =>
        new PublicJobSummary(
            job.Id, job.ReferenceNumber, job.Title, job.Slug,
            job.CompanyId, job.Company.Name, job.Company.Slug, job.Company.LogoUrl,
            job.CategoryId, job.Category.Name, job.Location,
            job.MinimumSalary, job.MaximumSalary, job.CurrencyCode,
            job.EmploymentType, job.WorkplaceType, job.ExperienceLevel,
            job.IsFeatured, job.PublishedAtUtc!.Value, job.ExpiresAtUtc,
            job.MinimumExperienceYears, job.MaximumExperienceYears,
            job.InternshipDurationMonths, job.IsFlexibleDuration,
            job.Department, job.RoleCategory, job.EducationRequirement,
            job.PostedByType, job.Company.CompanyType, job.Company.Industry,
            job.Referral != null,
            job.Referral == null ? null : job.Referral.Id,
            job.Referral == null ? null : (job.Referral.ReferrerUser.FirstName + " " + job.Referral.ReferrerUser.LastName).Trim(),
            job.Referral == null ? null : job.Referral.ReviewedAtUtc,
            null, null, null);
}
