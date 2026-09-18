using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

/// <summary>
/// Records that a job seeker viewed a referral's contact details. Access control itself is
/// driven by the user's active Membership at request time (see MembershipService) — this
/// table is a history/analytics log, not the source of truth for "can they see it right now".
/// </summary>
public sealed class ReferralUnlock : BaseEntity
{
    public Guid JobReferralId { get; set; }

    public JobReferral JobReferral { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTime UnlockedAtUtc { get; set; }
}
