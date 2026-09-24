using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Referrals;

/// <summary>Referrer submits a new job referral. JobDetails reuses the existing Compose flow
/// (create-or-link Company/Category) so a referrer never needs Administrator access to post.</summary>
public sealed record SubmitJobReferralRequest(
    ComposeJobRequest JobDetails,
    string? SourceUrl,
    bool ShowLinkedIn,
    bool ShowEmail,
    bool ShowPhone);

public sealed record JobReferralResponse(
    Guid Id,
    Guid JobId,
    string JobTitle,
    string CompanyName,
    string? Location,
    Guid ReferrerUserId,
    string ReferrerName,
    string? SourceUrl,
    bool ShowLinkedIn,
    bool ShowEmail,
    bool ShowPhone,
    JobReferralApprovalStatus ApprovalStatus,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc,
    DateTime? ApprovedAtUtc = null);
public sealed record ReviewJobReferralRequest(
    JobReferralApprovalStatus Decision,
    string? RejectionReason);

/// <summary>What a paying job seeker sees after unlocking — only the fields the referrer
/// chose to expose for this specific job are populated.</summary>
public sealed record ReferrerContactDetailsResponse(
    string ReferrerName,
    string? LinkedInUrl,
    string? Email,
    string? PhoneNumber);

public sealed record PublicReferralJobResponse(
    Guid JobId,
    string JobTitle,
    string CompanyName,
    string? Location,
    string ReferrerName)
{
    public Guid ReferralId { get; init; }
    public Guid CompanyId { get; init; }
    public string? CompanyLogoUrl { get; init; }
    public bool CompanyIsVerified { get; init; }
    public string JobSlug { get; init; } = "";
    public string ApplicationUrl { get; init; } = "";
    public EmploymentType EmploymentType { get; init; }
    public WorkplaceType WorkplaceType { get; init; }
    public int? MinimumExperienceYears { get; init; }
    public int? MaximumExperienceYears { get; init; }
    public IReadOnlyCollection<string> Skills { get; init; } = Array.Empty<string>();
    public JobReferralApprovalStatus ApprovalStatus { get; init; }
    public bool ReferralAvailable { get; init; }
    public string? ReferrerCurrentRole { get; init; }
    public string? ReferrerCompanyName { get; init; }
    public DateOnly? ReferrerCompanyStartDate { get; init; }
    public int? ReferrerCompletedYearsAtCompany { get; init; }
    public string? ReferrerProfileImageUrl { get; init; }
    public ReferralUnlockStatus ContactAccessStatus { get; init; } = ReferralUnlockStatus.LoginRequired;
    public bool CanViewReferrerContact => ContactAccessStatus == ReferralUnlockStatus.Granted;
    public bool IsContactLocked => !CanViewReferrerContact;
}

public enum ReferralUnlockStatus { Granted = 1, LoginRequired, MembershipRequired }

public sealed record ExtractFromUrlRequest(string Url);

public sealed record ReferralUnlockResponse(
    ReferralUnlockStatus Status,
    string Message,
    ReferrerContactDetailsResponse? Contact = null);
