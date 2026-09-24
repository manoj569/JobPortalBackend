using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Referrals;

public sealed class JobReferralService(
    IJobReferralRepository referrals,
    IJobRepository jobs,
    IJobService jobService,
    IMembershipRepository memberships,
    IAuditWriter auditWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IDashboardRepository? dashboard = null) : IJobReferralService
{
    private const string ReferralContactPlanCode = "ReferralContactAccess";

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<JobReferralResponse> SubmitAsync(
        Guid referrerUserId,
        SubmitJobReferralRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ShowLinkedIn && !request.ShowEmail && !request.ShowPhone)
        {
            throw new BadRequestException(
                "Select at least one contact field to share with job seekers.",
                "validation_error");
        }

        // Reuses the existing compose flow (create-or-link Company/Category) so the referrer
        // never needs Administrator rights just to post — the job is created in Draft status
        // and only becomes visible once an admin approves the referral below.
        var composed = await jobService.ComposeAsync(
            referrerUserId,
            request.JobDetails,
            cancellationToken);

        var referral = new JobReferral
        {
            JobId = composed.Id,
            ReferrerUserId = referrerUserId,
            SourceUrl = request.SourceUrl,
            ShowLinkedIn = request.ShowLinkedIn,
            ShowEmail = request.ShowEmail,
            ShowPhone = request.ShowPhone,
            ApprovalStatus = JobReferralApprovalStatus.Pending,
            CreatedAtUtc = UtcNow,
        };

        await referrals.AddAsync(referral, cancellationToken);

        await auditWriter.AppendAsync(
            new(
                AuditAction.Submit,
                "JobReferral",
                referral.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["jobId"] = composed.Id.ToString(),
                    ["referrerUserId"] = referrerUserId.ToString(),
                }),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await referrals.GetByIdAsync(referral.Id, cancellationToken)
            ?? referral;

        var job = await jobs.GetByIdAsync(
            composed.Id,
            false,
            cancellationToken)
            ?? throw new NotFoundException("Job was not found.");

        return await ToResponseAsync(saved, job, cancellationToken);
    }

    public async Task<PagedResponse<JobReferralResponse>> GetPendingForAdminAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await referrals.GetPendingAsync(
            pageNumber,
            pageSize,
            cancellationToken);

        var responses = new List<JobReferralResponse>(items.Count);

        foreach (var referral in items)
        {
            responses.Add(
                await ToResponseAsync(
                    referral,
                    referral.Job,
                    cancellationToken));
        }

        return new PagedResponse<JobReferralResponse>(
            responses,
            pageNumber,
            pageSize,
            totalCount);
    }

    public async Task<JobReferralResponse> ReviewAsync(
        Guid jobReferralId,
        Guid adminUserId,
        ReviewJobReferralRequest request,
        CancellationToken cancellationToken = default)
    {
        var referral = await referrals.GetByIdAsync(
            jobReferralId,
            cancellationToken)
            ?? throw new NotFoundException("Referral was not found.");

        if (referral.ApprovalStatus != JobReferralApprovalStatus.Pending)
        {
            throw new ConflictException(
                "This referral has already been reviewed.");
        }

        if (request.Decision == JobReferralApprovalStatus.Rejected &&
            string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            throw new BadRequestException(
                "A rejection reason is required.",
                "validation_error");
        }

        referral.ApprovalStatus = request.Decision;
        referral.ReviewedByUserId = adminUserId;
        referral.ReviewedAtUtc = UtcNow;
        referral.RejectionReason =
            request.Decision == JobReferralApprovalStatus.Rejected
                ? request.RejectionReason
                : null;

        var job = await jobs.GetByIdAsync(
            referral.JobId,
            false,
            cancellationToken)
            ?? throw new NotFoundException("Job was not found.");

        if (request.Decision == JobReferralApprovalStatus.Approved)
        {
            job.Status = JobStatus.Published;
            job.PublishedAtUtc = UtcNow;

            jobs.Update(job);

            if (dashboard is not null &&
                !await dashboard.NotificationExistsAsync(
                    referral.ReferrerUserId,
                    NotificationType.ReferralApproved,
                    referral.Id,
                    cancellationToken))
            {
                await dashboard.AddNotificationAsync(
                    new Notification
                    {
                        UserId = referral.ReferrerUserId,
                        Type = NotificationType.ReferralApproved,
                        ReferralId = referral.Id,
                        JobId = referral.JobId,
                        Title = "Referred job approved",
                        Message = $"Your referred job \"{job.Title}\" was approved successfully.",
                        ActionUrl = "/dashboard/referrals",
                        IsRead = false,
                        CreatedAtUtc = UtcNow
                    },
                    cancellationToken);
            }
        }

        await auditWriter.AppendAsync(
            new(
                request.Decision == JobReferralApprovalStatus.Approved
                    ? AuditAction.Publish
                    : AuditAction.Update,
                "JobReferral",
                referral.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["decision"] = request.Decision.ToString(),
                    ["adminUserId"] = adminUserId.ToString(),
                    ["rejectionReason"] = referral.RejectionReason,
                }),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ToResponseAsync(
            referral,
            job,
            cancellationToken);
    }

    public async Task<PagedResponse<JobReferralResponse>> GetMySubmissionsAsync(
    Guid referrerUserId,
    string? search,
    JobReferralApprovalStatus? status,
    int pageNumber,
    int pageSize,
    CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var (items, totalCount) = await referrals.GetByReferrerAsync(
            referrerUserId,
            search,
            status,
            pageNumber,
            pageSize,
            cancellationToken);

        var responses = new List<JobReferralResponse>(items.Count);

        foreach (var referral in items)
        {
            responses.Add(
                await ToResponseAsync(
                    referral,
                    referral.Job,
                    cancellationToken));
        }

        return new PagedResponse<JobReferralResponse>(
            responses,
            pageNumber,
            pageSize,
            totalCount);
    }

    public async Task<PagedResponse<PublicReferralJobResponse>> GetApprovedPublicAsync(
     int pageNumber,
     int pageSize,
     Guid? seekerUserId = null,
     CancellationToken cancellationToken = default)
    {
        if (pageNumber is < 1 or > 1000000 || pageSize is < 1 or > 100)
            throw new BadRequestException("Invalid pagination.");
        var (items, totalCount) = await referrals.GetApprovedAsync(
            pageNumber,
            pageSize,
            cancellationToken);

        var access = await ContactAccessAsync(seekerUserId, cancellationToken);
        var responses = items.Select(referral => ToPublicResponse(referral, access)).ToList();

        return new PagedResponse<PublicReferralJobResponse>(
            responses,
            pageNumber,
            pageSize,
            totalCount);
    }

    public async Task<ReferralUnlockResponse> UnlockContactAsync(
        Guid? seekerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var referral = await referrals.GetByJobIdAsync(
            jobId,
            cancellationToken)
            ?? throw new NotFoundException(
                "This job has no referral attached.");

        if (referral.ApprovalStatus != JobReferralApprovalStatus.Approved || referral.Job.Status != JobStatus.Published ||
            referral.Job.IsDeleted || referral.Job.IsHidden || referral.Job.ExpiresAtUtc <= UtcNow ||
            referral.ReferrerUser.IsDeleted || referral.ReferrerUser.Status != UserStatus.Active)
        {
            throw new NotFoundException(
                "This job has no referral attached.");
        }

        var access = await ContactAccessAsync(seekerUserId, cancellationToken);
        if (access == ReferralUnlockStatus.LoginRequired)
        {
            return new ReferralUnlockResponse(
                ReferralUnlockStatus.LoginRequired,
                "Please log in to continue.");
        }

        if (access != ReferralUnlockStatus.Granted)
        {
            return new ReferralUnlockResponse(
                ReferralUnlockStatus.MembershipRequired,
                "Subscribe for ₹299 / 30 days to unlock referrer contact details.");
        }

        await referrals.RecordUnlockAsync(
            referral.Id,
            seekerUserId!.Value,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var referrerName =
            $"{referral.ReferrerUser.FirstName} {referral.ReferrerUser.LastName}".Trim();

        var contact = new ReferrerContactDetailsResponse(
            referrerName,
            referral.ShowLinkedIn
                ? referral.ReferrerUser.LinkedInUrl
                : null,
            referral.ShowEmail
                ? referral.ReferrerUser.Email
                : null,
            referral.ShowPhone
                ? referral.ReferrerUser.PhoneNumber
                : null);

        return new ReferralUnlockResponse(
            ReferralUnlockStatus.Granted,
            "Contact details unlocked.",
            contact);
    }

    private async Task<ReferralUnlockStatus> ContactAccessAsync(Guid? seekerUserId, CancellationToken ct)
    {
        if (!seekerUserId.HasValue) return ReferralUnlockStatus.LoginRequired;
        var membership = await memberships.GetActiveForUserAsync(seekerUserId.Value, ReferralContactPlanCode, ct);
        return membership is not null && !membership.IsDeleted && membership.UserId == seekerUserId &&
            membership.PlanCode == ReferralContactPlanCode && membership.Status == MembershipStatus.Active &&
            membership.StartsAtUtc <= UtcNow && (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > UtcNow)
            ? ReferralUnlockStatus.Granted : ReferralUnlockStatus.MembershipRequired;
    }

    private PublicReferralJobResponse ToPublicResponse(JobReferral referral, ReferralUnlockStatus access)
    {
        var job = referral.Job;
        var user = referral.ReferrerUser;
        var portfolio = user.CandidatePortfolio;
        var published = !user.IsDeleted && user.Status == UserStatus.Active &&
            portfolio is { IsDeleted: false, Status: CandidatePortfolioStatus.Published };
        var today = DateOnly.FromDateTime(UtcNow);
        // Work history is private unless the existing public portfolio explicitly exposes it.
        var experience = published && portfolio!.SectionSettings.Any(x => !x.IsDeleted && x.SectionType == PortfolioSectionType.Experience && x.IsVisible)
            ? user.CandidateExperiences.Where(x => !x.IsDeleted && x.IsCurrent && x.EndDate == null && x.StartDate <= today)
                .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id).FirstOrDefault() : null;
        int? years = null;
        if (experience is not null)
        {
            years = today.Year - experience.StartDate.Year;
            if (experience.StartDate.AddYears(years.Value) > today) years--;
        }
        return new(job.Id, job.Title, job.Company.Name, job.Location, $"{user.FirstName} {user.LastName}".Trim())
        {
            ReferralId = referral.Id, CompanyId = job.CompanyId, CompanyLogoUrl = job.Company.LogoUrl,
            CompanyIsVerified = job.Company.IsVerified, JobSlug = job.Slug, ApplicationUrl = job.ApplicationUrl,
            EmploymentType = job.EmploymentType, WorkplaceType = job.WorkplaceType,
            MinimumExperienceYears = job.MinimumExperienceYears, MaximumExperienceYears = job.MaximumExperienceYears,
            Skills = job.JobSkills.Where(x => !x.IsDeleted && !x.Skill.IsDeleted).Select(x => x.Skill.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            ApprovalStatus = referral.ApprovalStatus, ReferralAvailable = referral.ApprovalStatus == JobReferralApprovalStatus.Approved,
            ReferrerCurrentRole = experience?.JobTitle, ReferrerCompanyName = experience?.CompanyName,
            ReferrerCompanyStartDate = experience?.StartDate, ReferrerCompletedYearsAtCompany = years,
            ReferrerProfileImageUrl = published ? user.ProfileImageUrl : null,
            ContactAccessStatus = access
        };
    }

    private static Task<JobReferralResponse> ToResponseAsync(
     JobReferral referral,
     Job job,
     CancellationToken cancellationToken)
    {
        var referrerName =
            $"{referral.ReferrerUser.FirstName} {referral.ReferrerUser.LastName}".Trim();

        return Task.FromResult(
            new JobReferralResponse(
                referral.Id,
                referral.JobId,
                job.Title,
                job.Company.Name,
                job.Location,
                referral.ReferrerUserId,
                referrerName,
                referral.SourceUrl,
                referral.ShowLinkedIn,
                referral.ShowEmail,
                referral.ShowPhone,
                referral.ApprovalStatus,
                referral.RejectionReason,
                referral.CreatedAtUtc,
                referral.ReviewedAtUtc,
                referral.ApprovalStatus == JobReferralApprovalStatus.Approved
                    ? referral.ReviewedAtUtc
                    : null));
    }
}
