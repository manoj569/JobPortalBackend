namespace JobPortal.Application.Features.HiringOutreach;

public sealed class HiringOutreachOptions
{
    public const string SectionName = "HiringOutreach";
    public bool Enabled { get; set; }
    public int CacheDays { get; set; } = 7;
    public int MaximumContacts { get; set; } = 5;
    public int PromotionalLifetimeDiscoveries { get; set; } = 1;
    public Dictionary<string,int> PlanMonthlyDiscoveries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int DailyDiscoveries { get; set; } = 3;
    public int DailySends { get; set; } = 3;
    public int MonthlySends { get; set; } = 10;
    public int MaximumAttachmentBytes { get; set; } = 5*1024*1024;
    public bool AllowUnverifiedContacts { get; set; }
    public int MinimumUnverifiedConfidence { get; set; } = 90;
    public bool AiDraftingEnabled { get; set; }
    public HiringSharedContactOptions SharedContacts { get; set; } = new();
    public bool IsValid() => CacheDays is >=1 and <=30 && MaximumContacts is >=1 and <=5 &&
        PromotionalLifetimeDiscoveries is >=0 and <=10 && DailyDiscoveries is >=1 and <=20 &&
        DailySends is >=1 and <=20 && MonthlySends is >=1 and <=100 &&
        MaximumAttachmentBytes is >=1024 and <=10*1024*1024 && MinimumUnverifiedConfidence is >=85 and <=100 &&
        SharedContacts.IsValid() && PlanMonthlyDiscoveries.All(x=>x.Key.Length is >0 and <=80 && x.Value is >=0 and <=100);
}
public sealed class HiringSharedContactOptions
{
    // Enable only after confirming the provider contract permits persisted cross-candidate reuse.
    public bool Enabled { get; set; }
    public int CacheDays { get; set; } = 30;
    public int MinimumContacts { get; set; } = 2;
    public int MinimumScore { get; set; } = 70;
    public bool IsValid()=>CacheDays is >=1 and <=90 && MinimumContacts is >=1 and <=4 && MinimumScore is >=50 and <=150;
}
public sealed record DiscoverHiringTeamRequest(Guid JobId,Guid RequestKey);
public sealed record HiringContact(Guid Id,string Name,string Title,string Department,string Type,string WorkEmail,string VerificationStatus,int Confidence,string? LinkedInUrl,bool Recommended,
    string Location="",string Source="",int MatchScore=0,string MatchReason="",string ContactType="");
public sealed record HiringMailbox(bool Connected,string Provider,string? Email);
public sealed record HiringResume(Guid Id,string FileName,bool IsDefault);
public sealed record HiringUsage(bool Charged,int Remaining,int? ProviderAttemptsRemaining=null);
public sealed record HiringSession(Guid SessionId,string State,Guid JobId,string JobTitle,string CompanyName,string CompanyDomain,
    IReadOnlyList<HiringContact> Contacts,HiringMailbox Mailbox,HiringUsage Usage,string? FailureCode,string Source="");
public sealed record HiringDraftRequest(IReadOnlyList<Guid> RecipientIds,Guid ResumeId);
public sealed record HiringDraft(Guid Id,Guid SessionId,HiringMailbox From,IReadOnlyList<HiringContact> Recipients,string Subject,string Body,HiringResume Resume,bool Editable,string Source);
public sealed record HiringSendRequest(Guid DraftId,IReadOnlyList<Guid> RecipientIds,string Subject,string Body,Guid ResumeId,Guid RequestKey);
public sealed record HiringSendResult(Guid ContactId,string State,string? ProviderMessageId,string? FailureCode);
public sealed record HiringSendResponse(string State,IReadOnlyList<HiringSendResult> Recipients);
public sealed record HiringSupportSession(Guid SessionId,Guid CandidateId,Guid JobId,string State,string Provider,string UsageState,
    int ContactCount,int SendCount,int SentCount,DateTime CreatedAtUtc,string? FailureCode);
public sealed record HiringOAuthStart(string AuthorizationUrl,string BrowserBinding);
public sealed record HiringOAuthGrant(string AccountId,string Email,string RefreshToken,string Scopes);
public sealed record HiringOAuthChallenge(string Url,string Verifier);
public sealed record HiringProviderContact(string FullName,string FirstName,string Email,string Title,string Department,
    string Verification,int Confidence,string? LinkedInUrl,string Reference,string Location="",string Source="DomainSearch",string Provider="Hunter");
public sealed record HiringDiscoveryContext(string Company,string Domain,string JobTitle,string Department,string Location="",string Description="",int DesiredStrongContacts=2);
// Rich job-context and attachment contracts are separate from the legacy Accelerator-only adapters.
// They never activate the Accelerator feature or use the transactional email sender.
public interface IHiringContactProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<IReadOnlyList<HiringProviderContact>> DiscoverAsync(HiringDiscoveryContext context,CancellationToken ct);
    async Task<HiringDiscoveryResult> DiscoverResultAsync(HiringDiscoveryContext context,CancellationToken ct)=>new(await DiscoverAsync(context,ct));
}
public sealed record HiringDiscoveryResult(IReadOnlyList<HiringProviderContact> Contacts,bool Uncertain=false,IReadOnlyList<string>? Failures=null);
public interface IHiringFallbackContactProvider : IHiringContactProvider { }
public sealed record HiringPersonIdentity(string Provider,string Reference,string FirstName,string FullName,string Title,string Department,string Domain,string Location,string Stage);
public interface IHiringPersonDiscoveryProvider
{
    bool IsAvailable { get; }
    IAsyncEnumerable<IReadOnlyList<HiringPersonIdentity>> SearchAsync(HiringDiscoveryContext context,CancellationToken ct);
}
public interface IHiringContactEnrichmentProvider
{
    Task<HiringProviderContact?> EnrichAsync(HiringPersonIdentity person,HiringDiscoveryContext context,CancellationToken ct);
}
// One identity provider owns its search and enrichment budgets. Existing contracts remain reusable.
public interface IHiringPersonProvider : IHiringPersonDiscoveryProvider,IHiringContactEnrichmentProvider
{
    string Name { get; }
    bool AllowCandidateDisplay { get; }
    int MaximumSearchRequests { get; }
    int MaximumEnrichments { get; }
}
public interface IHiringProfessionalEmailFinder
{
    bool IsAvailable { get; }
    Task<HiringProviderContact?> FindAsync(HiringPersonIdentity person,CancellationToken ct);
}
public interface IHiringEmailVerificationProvider
{
    bool IsAvailable { get; }
    int MaximumRequests=>2;
    Task<HiringProviderContact?> VerifyAsync(HiringProviderContact contact,CancellationToken ct);
}
public static class HiringProviderFailures
{
    public static bool ConfirmedRejection(string code)=>code is "ContactProviderConfiguration" or "ContactProviderRateLimited" or
        "ApolloConfigurationRequired" or "ApolloRateLimited" or "ApolloQuotaExceeded" or "ApolloInvalidRequest" or
        "HunterConfigurationRequired" or "HunterRateLimited" or "HunterQuotaExceeded" or "HunterInvalidRequest" or
        "CoresignalConfigurationRequired" or "CoresignalRateLimited" or "CoresignalQuotaExceeded" or "CoresignalInvalidRequest" or "CoresignalPrivacyRestricted" or
        "CoresignalEmployeeNotFound" or "CoresignalEmployeeUnavailable" or "CoresignalIdentityMismatch";
}
public sealed record HiringMailMessage(string From,string To,string Subject,string Body,string FileName,string ContentType,byte[] Attachment,string MessageKey);
public sealed record HiringMailResult(string State,string? MessageId=null,string? ThreadId=null,string? FailureCode=null);
public interface IHiringMailboxProvider
{
    bool IsAvailable { get; }
    HiringOAuthChallenge Authorize(string state);
    Task<HiringOAuthGrant> ExchangeAsync(string code,string verifier,CancellationToken ct);
    Task RevokeAsync(string refreshToken,CancellationToken ct);
    Task<HiringMailResult> SendAsync(string refreshToken,HiringMailMessage message,CancellationToken ct);
}
public interface IHiringCredentialProtector
{
    string Protect(Guid owner,string secret);
    string Unprotect(Guid owner,string secret);
}
public interface IHiringSharedContactProtector
{
    string Protect(string domain,string value);
    string Unprotect(string domain,string value);
}
public interface IHiringOutreachService
{
    Task<HiringSession> DiscoverAsync(Guid actor,DiscoverHiringTeamRequest request,CancellationToken ct);
    Task<HiringSession> GetAsync(Guid actor,Guid sessionId,CancellationToken ct);
    Task<IReadOnlyList<HiringSession>> HistoryAsync(Guid actor,int page,int pageSize,CancellationToken ct);
    Task<IReadOnlyList<HiringResume>> ResumesAsync(Guid actor,CancellationToken ct);
    Task<HiringDraft> DraftAsync(Guid actor,Guid sessionId,HiringDraftRequest request,CancellationToken ct);
    Task<HiringSendResponse> SendAsync(Guid actor,Guid sessionId,HiringSendRequest request,CancellationToken ct);
    Task<HiringMailbox> MailboxAsync(Guid actor,CancellationToken ct);
    Task<HiringOAuthStart> ConnectAsync(Guid actor,CancellationToken ct);
    Task<HiringMailbox> CallbackAsync(string state,string code,string browserBinding,CancellationToken ct);
    Task DisconnectAsync(Guid actor,CancellationToken ct);
    Task<IReadOnlyList<HiringSupportSession>> SupportAsync(Guid actor,int page,int pageSize,CancellationToken ct);
}
public sealed class HiringProviderException(string code) : Exception("Outreach provider request could not be completed.")
{ public string Code { get; } = code; }
