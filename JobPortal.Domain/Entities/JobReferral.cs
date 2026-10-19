using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

/// <summary>
/// Referral metadata attached to a job that a user (the referrer) submitted for free.
/// Holds private, offer-specific contact values and which methods the referrer has
/// chosen to expose for THIS job, and the admin approval workflow state.
/// </summary>
public sealed class JobReferral : BaseEntity
{
    public int ReferralSlots { get; set; } = 1;
    public Guid JobId { get; set; }

    public Job Job { get; set; } = null!;

    public Guid ReferrerUserId { get; set; }

    public User ReferrerUser { get; set; } = null!;

    /// <summary>The job URL the referrer originally pasted in, if any (used for AI auto-extraction).</summary>
    public string? SourceUrl { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public bool ShowLinkedIn { get; set; }

    public bool ShowEmail { get; set; }

    public bool ShowPhone { get; set; }

    public JobReferralApprovalStatus ApprovalStatus { get; set; } = JobReferralApprovalStatus.Pending;

    public Guid? ReviewedByUserId { get; set; }

    public User? ReviewedByUser { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public string? RejectionReason { get; set; }
}
