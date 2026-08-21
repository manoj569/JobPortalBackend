using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class PendingRegistration : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string NormalizedPhoneNumber { get; set; } = string.Empty;
    public DateTime TermsAndPrivacyAcceptedAtUtc { get; set; }
    public string TermsAndPrivacyVersion { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedUserId { get; set; }
    public User? CompletedUser { get; set; }
    public ICollection<OtpChallenge> OtpChallenges { get; set; } =
        new List<OtpChallenge>();
}

public sealed class OtpChallenge : BaseEntity
{
    public OtpPurpose Purpose { get; set; }
    public string NormalizedPhoneNumber { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttemptCount { get; set; }
    public int SendCount { get; set; } = 1;
    public DateTime LastSentAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ResetChallengeExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public Guid? PendingRegistrationId { get; set; }
    public PendingRegistration? PendingRegistration { get; set; }
}

public sealed class RegistrationEmailRequest : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string VerificationToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}
