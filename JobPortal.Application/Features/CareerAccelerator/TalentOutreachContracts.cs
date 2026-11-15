namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class TalentOutreachOptions
{
    public bool EmployerOutreachEnabled { get; set; }
    public bool ContactDiscoveryEnabled { get; set; }
    public bool GoogleMailboxEnabled { get; set; }
    public bool MicrosoftMailboxEnabled { get; set; }
    public bool RegisteredEmployerTalentInboxEnabled { get; set; } = true;
    public bool OutreachAiDraftingEnabled { get; set; }
    public bool OutreachReconciliationEnabled { get; set; }
    public int DispatchRecoveryMinutes { get; set; } = 10;
    public int MaxRecommendedContacts { get; set; } = 2;
    public int MaxRecipientsPerDraft { get; set; } = 2;
    public int MaxSendsPerDay { get; set; } = 5;
    public int MaxSendsPerMonth { get; set; } = 30;
    public int RecipientCooldownDays { get; set; } = 30;
}

public sealed record DiscoveredEmployerContact(string FullName, string Title, string Department,
    string ProfessionalEmail, string Source);

// Implementations must use a lawful source and supply provenance. No live implementation is
// registered until its permissions, retention and verification policy are configured.
public interface IEmployerContactProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<IReadOnlyList<DiscoveredEmployerContact>> DiscoverAsync(Guid companyId, Guid jobId,
        CancellationToken cancellationToken);
}

public enum MailboxDispatchOutcome { Sent = 1, ConfirmedNonChargeableFailure, Unknown }
public sealed record MailboxDispatchResult(MailboxDispatchOutcome Outcome, string? ProviderMessageId,
    string? FailureEvidenceReference = null);

public interface ICandidateMailboxProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<MailboxDispatchResult> SendAsync(string tokenReference, string idempotencyKey,
        IReadOnlyList<string> recipients, string subject, string body, CancellationToken cancellationToken);
}

public static class TalentContactRanker
{
    public static int Score(string jobTitle, string department, string contactTitle)
    {
        var role = contactTitle.ToUpperInvariant();
        var job = jobTitle.ToUpperInvariant();
        var dept = department.ToUpperInvariant();
        var score = role.Contains("RECRUIT", StringComparison.Ordinal) ? 40 :
            role.Contains("TALENT", StringComparison.Ordinal) ? 35 :
            role.Contains("HIRING MANAGER", StringComparison.Ordinal) ? 30 : 0;
        if (job.Contains("ENGINEER", StringComparison.Ordinal) &&
            (role.Contains("TECHNICAL", StringComparison.Ordinal) ||
             role.Contains("ENGINEER", StringComparison.Ordinal) ||
             dept.Contains("ENGINEER", StringComparison.Ordinal))) score += 25;
        if (job.Contains("PRODUCT", StringComparison.Ordinal) &&
            (role.Contains("PRODUCT", StringComparison.Ordinal) ||
             dept.Contains("PRODUCT", StringComparison.Ordinal))) score += 25;
        return score;
    }
}
