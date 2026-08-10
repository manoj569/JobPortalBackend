using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CandidateRepository(
    JobPortalDbContext context,
    TimeProvider timeProvider) : ICandidateRepository
{
    public Task<User?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.SingleOrDefaultAsync(x => x.Id == userId &&
            x.RoleId == SystemRoleIds.Candidate &&
            x.Status == UserStatus.Active, cancellationToken);

    public async Task<CandidateResumeProfile?> GetResumeProfileAsync(Guid userId, bool tracking, CancellationToken cancellationToken = default)
    {
        var query = context.CandidateResumeProfiles.Where(x => x.UserId == userId);
        return await (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public Task AddResumeProfileAsync(CandidateResumeProfile profile, CancellationToken cancellationToken = default) =>
        context.CandidateResumeProfiles.AddAsync(profile, cancellationToken).AsTask();

    public async Task<IReadOnlyCollection<RecommendationJobCandidate>> GetRecommendationCandidatesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await context.Jobs.AsNoTracking().Where(x => x.Status == JobStatus.Published &&
                !x.IsHidden && !x.IsDeleted && x.PublishedAtUtc.HasValue &&
                (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > now) &&
                !x.Applications.Any(a => a.UserId == userId && !a.IsDeleted))
            .OrderByDescending(x => x.PublishedAtUtc).Take(2000)
            .Select(x => new RecommendationJobCandidate(x.Id, x.ReferenceNumber, x.Title, x.Slug,
                x.Description, x.Requirements, x.Responsibilities, x.CompanyId, x.Company.Name,
                x.Company.Slug, x.Company.LogoUrl, x.Company.Industry, x.CategoryId, x.Category.Name,
                x.Location, x.EmploymentType, x.WorkplaceType, x.ExperienceLevel, x.IsFeatured,
                x.PublishedAtUtc!.Value, x.ExpiresAtUtc)).ToArrayAsync(cancellationToken);
    }

    public async Task<(IReadOnlyCollection<RecommendationJobCandidate> Items, int TotalCount)> GetCandidateBrowseJobsAsync(
        Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var source = context.Jobs.AsNoTracking().Where(x => x.Status == JobStatus.Published &&
            !x.IsHidden && !x.IsDeleted && x.PublishedAtUtc.HasValue &&
            (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > now) &&
            !x.Applications.Any(a => a.UserId == userId && !a.IsDeleted));
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new RecommendationJobCandidate(x.Id, x.ReferenceNumber, x.Title, x.Slug,
                x.Description, x.Requirements, x.Responsibilities, x.CompanyId, x.Company.Name,
                x.Company.Slug, x.Company.LogoUrl, x.Company.Industry, x.CategoryId, x.Category.Name,
                x.Location, x.EmploymentType, x.WorkplaceType, x.ExperienceLevel, x.IsFeatured,
                x.PublishedAtUtc!.Value, x.ExpiresAtUtc)).ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public Task<CandidateJob?> GetAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return context.Jobs.AsNoTracking().Where(x => x.Id == jobId &&
                x.Status == JobStatus.Published && !x.IsHidden && !x.IsDeleted &&
                x.PublishedAtUtc.HasValue &&
                (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > now))
            .Select(x => new CandidateJob(x.Id, x.Title, x.Slug, x.Company.Name, x.ApplicationUrl))
            .SingleOrDefaultAsync(cancellationToken);
    }
    public Task<CandidateRecruiterContact?> GetApprovedRecruiterContactForAvailableJobAsync(
    Guid jobId,
    CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        return context.Jobs.AsNoTracking()
            .Where(job =>
                job.Id == jobId &&
                job.Status == JobStatus.Published &&
                !job.IsHidden &&
                !job.IsDeleted &&
                job.PublishedAtUtc.HasValue &&
                (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc > now) &&
                job.RecruiterContact != null &&
                job.RecruiterContact.IsSharingApproved)
            .Select(job => new CandidateRecruiterContact(
                job.Id,
                job.Title,
                job.Slug,
                job.Company.Name,
                job.RecruiterContact!.ContactName,
                job.RecruiterContact.ContactRole,
                job.RecruiterContact.Email,
                job.RecruiterContact.PhoneNumber))
            .SingleOrDefaultAsync(cancellationToken);
    }
    public Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return context.Memberships.AsNoTracking().AnyAsync(x => x.UserId == userId &&
            x.Status == MembershipStatus.Active &&
            x.StartsAtUtc <= now &&
            (!x.EndsAtUtc.HasValue || x.EndsAtUtc > now), cancellationToken);
    }

    public Task<bool> IsResumeReferencedAsync(
        string storageKey, CancellationToken cancellationToken = default) =>
        context.JobApplications.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.ResumeStorageKey == storageKey, cancellationToken);

    public Task<JobApplication?> GetApplicationAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default) =>
        context.JobApplications.Include(x => x.Job).ThenInclude(x => x.Company)
            .SingleOrDefaultAsync(x => x.Id == applicationId && x.UserId == userId, cancellationToken);

    public Task<bool> HasApplicationAsync(
        Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
        context.JobApplications.IgnoreQueryFilters()
            .AnyAsync(x => x.UserId == userId && x.JobId == jobId, cancellationToken);
    public Task<JobApplication?> GetApplicationByJobAsync(
        Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
        context.JobApplications.Include(x => x.Job).ThenInclude(x => x.Company)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.JobId == jobId, cancellationToken);
    public Task<ApplicationQuotaUsage?> GetQuotaUsageAsync(
    Guid userId,
    ApplicationQuotaPeriod period,
    DateTime periodStartsAtUtc,
    CancellationToken cancellationToken = default) =>
    context.ApplicationQuotaUsages
        .SingleOrDefaultAsync(
            usage => usage.UserId == userId &&
                     usage.Period == period &&
                     usage.PeriodStartsAtUtc == periodStartsAtUtc,
            cancellationToken);

    public Task AddQuotaUsageAsync(
        ApplicationQuotaUsage quotaUsage,
        CancellationToken cancellationToken = default) =>
        context.ApplicationQuotaUsages.AddAsync(quotaUsage, cancellationToken).AsTask();
    public Task AddApplicationAsync(JobApplication application, CancellationToken cancellationToken = default) =>
        context.JobApplications.AddAsync(application, cancellationToken).AsTask();

    public async Task<(IReadOnlyCollection<JobApplicationResponse> Items, int TotalCount)> GetApplicationsAsync(
        Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.JobApplications.AsNoTracking().Where(x => x.UserId == userId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new JobApplicationResponse(
                x.Id, x.JobId, x.Job.Title, x.Job.Slug, x.Job.Company.Name, x.Status,
                x.CoverLetter, x.ResumeFileName, x.SubmittedAtUtc, x.WithdrawnAtUtc,
                x.ApplicationMethod, x.Job.Category.Name, x.Job.Location, x.Job.EmploymentType,
                x.Job.WorkplaceType, x.Job.ExperienceLevel, x.Job.ApplicationUrl, x.Job.ExpiresAtUtc))
            .ToArrayAsync(cancellationToken);
        return (items, count);
    }
}
