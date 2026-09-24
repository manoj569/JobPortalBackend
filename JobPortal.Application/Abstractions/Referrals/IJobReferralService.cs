using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Referrals;

public interface IJobReferralService
{
    /// <summary>
    /// Referrer submits/attaches a referral to a job they've created.
    /// Job stays in Draft (not publicly visible) until admin approves.
    /// </summary>
    Task<JobReferralResponse> SubmitAsync(
        Guid referrerUserId,
        SubmitJobReferralRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<JobReferralResponse>> GetPendingForAdminAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin approves or rejects.
    /// Approving also publishes the underlying job.
    /// </summary>
    Task<JobReferralResponse> ReviewAsync(
        Guid jobReferralId,
        Guid adminUserId,
        ReviewJobReferralRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<JobReferralResponse>> GetMySubmissionsAsync(
        Guid referrerUserId,
        string? search,
        JobReferralApprovalStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Public, anonymous-friendly listing of approved referral jobs for the
    /// Browse Jobs "Referral jobs" tab.
    /// Never includes protected contact details.
    /// See UnlockContactAsync for protected referrer contact information.
    /// </summary>
    Task<PagedResponse<PublicReferralJobResponse>> GetApprovedPublicAsync(
        int pageNumber,
        int pageSize,
        Guid? seekerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns protected referrer contact details only when the seeker has
    /// the required ReferralContactAccess membership.
    /// </summary>
    Task<ReferralUnlockResponse> UnlockContactAsync(
        Guid? seekerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}

public interface IJobReferralRepository
{
    Task AddAsync(
        JobReferral referral,
        CancellationToken cancellationToken = default);

    Task<JobReferral?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<JobReferral?> GetByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetPendingAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetApprovedAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetByReferrerAsync(
        Guid referrerUserId,
        string? search,
        JobReferralApprovalStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task RecordUnlockAsync(
        Guid jobReferralId,
        Guid seekerUserId,
        CancellationToken cancellationToken = default);
}
