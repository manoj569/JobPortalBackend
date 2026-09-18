using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.Jobs;

public static class JobMappings
{
    public static JobResponse ToResponse(this Job job) => new(
        job.Id, job.ReferenceNumber, job.Title, job.Slug, job.Description,
        job.Responsibilities, job.Requirements, job.Benefits, job.ApplicationUrl,
        job.Company.Name, job.CompanyId, job.Category.Name, job.CategoryId,
        job.Location, job.MinimumSalary, job.MaximumSalary, job.CurrencyCode,
        job.EmploymentType, job.WorkplaceType, job.ExperienceLevel, job.Status,
        job.IsFeatured, job.IsHidden, job.PublishedAtUtc, job.ExpiresAtUtc,
        job.CreatedAtUtc, job.UpdatedAtUtc, job.IsDeleted, job.DeletedAtUtc,
        job.MinimumExperienceYears, job.MaximumExperienceYears,
        job.InternshipDurationMonths, job.IsFlexibleDuration, job.Department,
        job.RoleCategory, job.EducationRequirement, job.PostedByType,
        job.Referral is not null, job.Referral?.Id, job.Referral?.ApprovalStatus, job.Referral?.ReviewedAtUtc);

    public static void Apply(this Job job, UpdateJobRequest request)
    {
        job.Title = request.Title.Trim();
        job.Description = request.Description.Trim();
        job.ApplicationUrl = request.ApplicationUrl.Trim();
        job.CompanyId = request.CompanyId;
        job.CategoryId = request.CategoryId;
        job.Responsibilities = TextNormalizer.TrimOrNull(request.Responsibilities);
        job.Requirements = TextNormalizer.TrimOrNull(request.Requirements);
        job.Benefits = TextNormalizer.TrimOrNull(request.Benefits);
        job.Location = TextNormalizer.TrimOrNull(request.Location);
        job.MinimumSalary = request.MinimumSalary;
        job.MaximumSalary = request.MaximumSalary;
        job.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
        job.EmploymentType = request.EmploymentType;
        job.WorkplaceType = request.WorkplaceType;
        job.ExperienceLevel = request.ExperienceLevel;
        job.MinimumExperienceYears = request.MinimumExperienceYears;
        job.MaximumExperienceYears = request.MaximumExperienceYears;
        job.InternshipDurationMonths = request.InternshipDurationMonths;
        job.IsFlexibleDuration = request.IsFlexibleDuration;
        job.Department = TextNormalizer.TrimOrNull(request.Department);
        job.RoleCategory = TextNormalizer.TrimOrNull(request.RoleCategory);
        job.EducationRequirement = TextNormalizer.TrimOrNull(request.EducationRequirement);
        job.PostedByType = request.PostedByType;
        job.ExpiresAtUtc = request.ExpiresAtUtc;
    }

}
