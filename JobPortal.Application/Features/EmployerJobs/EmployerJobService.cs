using JobPortal.Application.Abstractions.EmployerJobs;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.EmployerJobs;

public sealed class EmployerJobService(
    IEmployerJobRepository employerJobs,
    IJobService jobs)
    : IEmployerJobService
{
    public async Task<PagedResponse<JobResponse>> SearchAsync(
        Guid employerUserId,
        EmployerJobSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var company =
            await RequiredCompanyAsync(
                employerUserId,
                cancellationToken);

        var adminQuery = new JobSearchQuery(
            Search: query.Search,
            CompanyId: company.Id,
            CategoryId: null,
            Status: query.Status,
            EmploymentType: query.EmploymentType,
            WorkplaceType: query.WorkplaceType,
            ExperienceLevel: query.ExperienceLevel,
            IsFeatured: null,
            IsHidden: null,
PageNumber: query.Page,
PageSize: query.PageSize,
            IsDeleted: false);

        return await jobs.SearchAsync(
            adminQuery,
            cancellationToken);
    }

    public async Task<JobResponse> GetAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken);

        return await jobs.GetByIdAsync(
            jobId,
            cancellationToken);
    }

    public async Task<JobResponse> CreateAsync(
        Guid employerUserId,
        EmployerCreateJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var company =
            await RequiredCompanyAsync(
                employerUserId,
                cancellationToken);

        var createRequest = new CreateJobRequest(
            request.Title,
            request.Description,
            company.Id,
            request.CategoryId,
            request.ApplicationUrl,
            request.Responsibilities,
            request.Requirements,
            request.Benefits,
            request.Location,
            request.MinimumSalary,
            request.MaximumSalary,
            request.CurrencyCode,
            request.EmploymentType,
            request.WorkplaceType,
            request.ExperienceLevel,
            request.ExpiresAtUtc,
            request.MinimumExperienceYears,
            request.MaximumExperienceYears,
            request.InternshipDurationMonths,
            request.IsFlexibleDuration,
            request.Department,
            request.RoleCategory,
            request.EducationRequirement,
            request.PostedByType,
            request.SalaryPeriod,
            request.ManualOverrideFields);

        return await jobs.CreateAsync(
            createRequest,
            cancellationToken);
    }

    public async Task<JobResponse> UpdateAsync(
        Guid employerUserId,
        Guid jobId,
        EmployerUpdateJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var ownedJob =
            await RequiredOwnedJobAsync(
                employerUserId,
                jobId,
                cancellationToken);

        var updateRequest = new UpdateJobRequest(
            request.Title,
            request.Description,

            // Company cannot be changed by Employer.
            ownedJob.CompanyId,

            request.CategoryId,
            request.ApplicationUrl,
            request.Responsibilities,
            request.Requirements,
            request.Benefits,
            request.Location,
            request.MinimumSalary,
            request.MaximumSalary,
            request.CurrencyCode,
            request.EmploymentType,
            request.WorkplaceType,
            request.ExperienceLevel,
            request.ExpiresAtUtc,
            request.MinimumExperienceYears,
            request.MaximumExperienceYears,
            request.InternshipDurationMonths,
            request.IsFlexibleDuration,
            request.Department,
            request.RoleCategory,
            request.EducationRequirement,
            request.PostedByType,
            request.SalaryPeriod,
            request.ManualOverrideFields);

        return await jobs.UpdateAsync(
            jobId,
            updateRequest,
            cancellationToken);
    }

    public async Task<JobResponse> PublishAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken);

        return await jobs.PublishAsync(
            jobId,
            cancellationToken);
    }

    public async Task<JobResponse> PauseAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken);

        /*
         * Existing JobService calls this Unpublish:
         * Published -> Draft.
         *
         * Employer UI can call it "Pause" without
         * duplicating the state-transition implementation.
         */
        return await jobs.UnpublishAsync(
            jobId,
            cancellationToken);
    }

    public async Task<JobResponse> CloseAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken);

        return await jobs.CloseAsync(
            jobId,
            cancellationToken);
    }

    public async Task<JobResponse> ArchiveAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken);

        return await jobs.ArchiveAsync(
            jobId,
            cancellationToken);
    }

    public async Task DeleteAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await RequiredOwnedJobAsync(
            employerUserId,
            jobId,
            cancellationToken);

        /*
         * Employer only gets soft delete.
         * Permanent delete remains Admin-only.
         */
        await jobs.SoftDeleteAsync(
            jobId,
            cancellationToken);
    }

    private async Task<Company> RequiredCompanyAsync(
        Guid employerUserId,
        CancellationToken cancellationToken)
    {
        var company =
            await employerJobs.GetOwnedCompanyAsync(
                employerUserId,
                cancellationToken);

        if (company is null)
        {
            throw new NotFoundException(
                "Complete company setup before managing jobs.");
        }

        return company;
    }

    private async Task<Job> RequiredOwnedJobAsync(
        Guid employerUserId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job =
            await employerJobs.GetOwnedJobAsync(
                employerUserId,
                jobId,
                cancellationToken);

        if (job is null)
        {
            /*
             * Deliberately return NotFound instead of revealing
             * whether another employer owns this JobId.
             */
            throw new NotFoundException(
                "Job was not found.");
        }

        return job;
    }
}
