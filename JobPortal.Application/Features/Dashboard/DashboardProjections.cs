using System.Linq.Expressions;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.Dashboard;

public static class DashboardProjections
{
    public static readonly Expression<Func<SavedJob, SavedJobResponse>> SavedJob = saved =>
        new SavedJobResponse(saved.Id, saved.CreatedAtUtc,
            new PublicJobSummary(
                saved.Job.Id, saved.Job.ReferenceNumber, saved.Job.Title, saved.Job.Slug,
                saved.Job.CompanyId, saved.Job.Company.Name, saved.Job.Company.Slug, saved.Job.Company.LogoUrl,
                saved.Job.CategoryId, saved.Job.Category.Name, saved.Job.Location,
                saved.Job.MinimumSalary, saved.Job.MaximumSalary, saved.Job.CurrencyCode,
                saved.Job.EmploymentType, saved.Job.WorkplaceType, saved.Job.ExperienceLevel,
                saved.Job.IsFeatured, saved.Job.PublishedAtUtc!.Value, saved.Job.ExpiresAtUtc,
                saved.Job.MinimumExperienceYears, saved.Job.MaximumExperienceYears,
                saved.Job.InternshipDurationMonths, saved.Job.IsFlexibleDuration,
                saved.Job.Department, saved.Job.RoleCategory,
                saved.Job.EducationRequirement, saved.Job.PostedByType,
                saved.Job.Company.CompanyType, saved.Job.Company.Industry,
                false, null, null, null, null, null, null));
}
