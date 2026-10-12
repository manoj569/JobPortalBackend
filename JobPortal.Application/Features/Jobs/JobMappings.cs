using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.Jobs;

public static class JobMappings
{
    public static JobResponse ToResponse(this Job job) => new(
        job.Id, job.ReferenceNumber, job.Title, job.Slug, job.Description,
        job.Responsibilities, job.Requirements, job.Benefits, job.ApplicationUrl,
        job.Company.Name, job.CompanyId, (job.Category == null ? "Not specified" : job.Category.Name), job.CategoryId,
        job.Location, job.MinimumSalary, job.MaximumSalary, job.CurrencyCode,
        job.EmploymentType, job.WorkplaceType, job.ExperienceLevel, job.Status,
        job.IsFeatured, job.IsHidden, job.PublishedAtUtc, job.ExpiresAtUtc,
        job.CreatedAtUtc, job.UpdatedAtUtc, job.IsDeleted, job.DeletedAtUtc,
        job.MinimumExperienceYears, job.MaximumExperienceYears,
        job.InternshipDurationMonths, job.IsFlexibleDuration, job.Department,
        job.RoleCategory, job.EducationRequirement, job.PostedByType,
        job.Referral is not null, job.Referral?.Id, job.Referral?.ApprovalStatus, job.Referral?.ReviewedAtUtc, job.SalaryPeriod, job.ClassificationJson, JobPortal.Application.Services.JobClassificationPolicy.Overrides(job));

    public static void Apply(this Job job, UpdateJobRequest request)
    {
        var salaryUnchanged = job.MinimumSalary == request.MinimumSalary && job.MaximumSalary == request.MaximumSalary &&
            string.Equals(job.CurrencyCode, request.CurrencyCode?.Trim(), StringComparison.OrdinalIgnoreCase);
        var salaryPeriod = request.SalaryPeriod ??
            (request.ManualOverrideFields is null && salaryUnchanged ? job.SalaryPeriod : null);
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
        job.CurrencyCode = request.MinimumSalary.HasValue || request.MaximumSalary.HasValue ? TextNormalizer.TrimOrNull(request.CurrencyCode)?.ToUpperInvariant() : null;
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
        job.SalaryPeriod = request.MinimumSalary.HasValue || request.MaximumSalary.HasValue ? salaryPeriod : null;
        // Legacy full PUT values are explicit manual selections. New clients can choose locks.
        job.ManualClassificationOverrides = string.Join(",", request.ManualOverrideFields ??
            JobPortal.Application.Services.JobClassificationPolicy.Fields);
        job.ClassificationJson = null; // An explicit admin PUT establishes field ownership.
        JobPortal.Application.Services.JobClassificationPolicy.Apply(job,
            JobPortal.Application.Services.JobClassificationPolicy.Analyze(job));
    }

}
