using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum ReferralRequestStatus { Requested = 1, Accepted = 2, Rejected = 3, Expired = 4, ReferralSubmitted = 5, CandidateConfirmed = 6 }

/// <summary>One candidate/opportunity connection. Acceptance attribution is permanent, including after expiry of membership.</summary>
public sealed class ReferralRequest : BaseEntity
{
    public Guid JobReferralId { get; set; }
    public JobReferral JobReferral { get; set; } = null!;
    public Guid CandidateUserId { get; set; }
    public User CandidateUser { get; set; } = null!;
    public Guid ReferrerUserId { get; set; }
    public ReferralRequestStatus Status { get; private set; } = ReferralRequestStatus.Requested;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public DateTime? ReferralSubmittedAtUtc { get; private set; }
    public DateTime? CandidateConfirmedAtUtc { get; private set; }
    public DateTime? NotReceivedAtUtc { get; private set; }
    public string? CandidateMessage { get; set; }
    public string? RejectionReason { get; private set; }
    public string? ReferralSubmissionReference { get; private set; }
    public Guid? AcceptedMembershipId { get; private set; }
    public DateTime? QuotaPeriodStartUtc { get; private set; }
    public DateTime? QuotaPeriodEndUtc { get; private set; }

    public ReferralRequestStatus EffectiveStatus(DateTime now) => Status == ReferralRequestStatus.Requested && ExpiresAtUtc <= now
        ? ReferralRequestStatus.Expired : Status;
    public bool IsSuccessful => Status is ReferralRequestStatus.Accepted or ReferralRequestStatus.ReferralSubmitted or ReferralRequestStatus.CandidateConfirmed;
    public bool Accept(DateTime now, Guid membershipId, DateTime start, DateTime end)
    {
        if (IsSuccessful) return false;
        Require(ReferralRequestStatus.Requested, now);
        Status = ReferralRequestStatus.Accepted; AcceptedAtUtc = now;
        AcceptedMembershipId = membershipId; QuotaPeriodStartUtc = start; QuotaPeriodEndUtc = end;
        return true;
    }
    public bool Reject(DateTime now, string? reason)
    {
        if (Status == ReferralRequestStatus.Rejected) return false;
        Require(ReferralRequestStatus.Requested, now);
        Status = ReferralRequestStatus.Rejected; RejectedAtUtc = now; RejectionReason = reason; return true;
    }
    public bool Submit(DateTime now, string? reference)
    {
        if (Status is ReferralRequestStatus.ReferralSubmitted or ReferralRequestStatus.CandidateConfirmed) return false;
        Require(ReferralRequestStatus.Accepted, now);
        Status = ReferralRequestStatus.ReferralSubmitted; ReferralSubmittedAtUtc = now; ReferralSubmissionReference = reference; return true;
    }
    public bool Confirm(DateTime now)
    {
        if (Status == ReferralRequestStatus.CandidateConfirmed) return false;
        Require(ReferralRequestStatus.ReferralSubmitted, now);
        Status = ReferralRequestStatus.CandidateConfirmed; CandidateConfirmedAtUtc = now; return true;
    }
    public bool ReportNotReceived(DateTime now)
    {
        Require(ReferralRequestStatus.ReferralSubmitted, now);
        if (NotReceivedAtUtc.HasValue) return false;
        NotReceivedAtUtc = now; return true;
    }
    private void Require(ReferralRequestStatus expected, DateTime now)
    {
        var actual = EffectiveStatus(now);
        if (actual == ReferralRequestStatus.Expired) throw new ReferralTransitionException("REFERRAL_REQUEST_EXPIRED");
        if (actual != expected) throw new ReferralTransitionException("INVALID_REFERRAL_STATUS");
    }
}

public sealed class ReferralTransitionException(string code) : InvalidOperationException("Referral transition is not permitted.")
{
    public string Code { get; } = code;
}
