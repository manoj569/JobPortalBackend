using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

/// <summary>
/// Referral metadata attached to a job that a user (the referrer) submitted for free.
/// Holds which of the referrer's own contact fields (email/phone/LinkedIn) they've
/// chosen to expose for THIS job, and the admin approval workflow state.
/// </summary>
public sealed class JobReferral : BaseEntity
{
    public Guid JobId { get; set; }

    public Job Job { get; set; } = null!;

    public Guid ReferrerUserId { get; set; }

    public User ReferrerUser { get; set; } = null!;

    /// <summary>The job URL the referrer originally pasted in, if any (used for AI auto-extraction).</summary>
    public string? SourceUrl { get; set; }

    public bool ShowLinkedIn { get; set; }

    public bool ShowEmail { get; set; }

    public bool ShowPhone { get; set; }

    public JobReferralApprovalStatus ApprovalStatus { get; set; } = JobReferralApprovalStatus.Pending;

    public Guid? ReviewedByUserId { get; set; }

    public User? ReviewedByUser { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? RejectionReason { get; set; }
}
