using JobPortal.Domain.Entities;
using JobPortal.Application.Features.Payments;

namespace JobPortal.Application.Features.AIResume;

public interface IAIResumeRepository
{
    Task<T> WriteAsync<T>(Guid userId, Func<Task<T>> action, CancellationToken ct);
    Task<User?> CandidateAsync(Guid userId, CancellationToken ct);
    Task<Job?> JobAsync(Guid jobId, DateTime now, CancellationToken ct);
    Task<AIResumeSession?> SessionAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<AIResumeCreditWallet?> WalletAsync(Guid userId, CancellationToken ct);
    Task<AIResumePurchase?> PurchaseAsync(string merchantOrderId, CancellationToken ct);
    Task<AIResumePurchase?> PurchaseByKeyAsync(Guid userId, Guid key, CancellationToken ct);
    Task<AIResumePurchase?> PurchaseForOwnerAsync(Guid userId, string merchantOrderId, CancellationToken ct);
    Task<AIResumeGeneration?> GenerationByKeyAsync(Guid userId, Guid key, CancellationToken ct);
    Task<AIResumeGeneration?> ActiveGenerationAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<TailoredResume?> ResumeAsync(Guid userId, Guid resumeId, CancellationToken ct);
    Task<TailoredResume?> LatestAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<TailoredResume?> ResumeForGenerationAsync(Guid userId, Guid generationId, CancellationToken ct);
    Task<IReadOnlyList<(TailoredResume Resume, AIResumeSession Session)>> HistoryAsync(Guid userId, int skip, int take, CancellationToken ct);
    void Add(object entity);
}
public sealed record CreateAIResumeSessionRequest(Guid SourceResumeId, Guid? JobId, string? ExternalJobDescription,
    string? JobTitle = null, string? CompanyName = null);
public sealed record AIResumeSessionResponse(Guid Id, Guid SourceResumeId, Guid? JobId, string SourceType,
    string JobTitle, string CompanyName, AIResumeSessionStatus Status, ResumeAnalysis? Analysis, Guid? TailoredResumeId)
{
    public ResumeDocumentCapabilities? DocumentCapabilities { get; init; }
}
public sealed record AIResumeCreditResponse(int Balance, int Reserved, int LifetimePurchased, int LifetimeConsumed);
public sealed record AIResumePackage(string Code, string Name, decimal Price, int Credits, bool IsPopular,
    string CurrencyCode = "INR")
{
    public decimal PricePerResume => Price / Credits;
}
public sealed record AIResumeCheckoutRequest(string PackageCode, Guid IdempotencyKey, Guid? SessionId = null);
public sealed record AIResumeCheckout(Guid PaymentId, string MerchantOrderId, string? RedirectUrl, decimal Amount,
    string CurrencyCode, int Credits, string PackageCode, JobPortal.Domain.Enums.PaymentStatus Status, string? ReturnTo);
public sealed record AIResumeGenerationRequest(Guid IdempotencyKey);
public sealed record AIResumeEditRequest(int ExpectedRevision, TailoredResumeContent Content);
public sealed record AIResumeResponse(Guid Id, Guid SessionId, int Version, int EditRevision, string TemplateCode,
    DateTime GeneratedAtUtc, DateTime? UpdatedAtUtc, TailoredResumeContent Content)
{
    public ResumeTailoringReview? Tailoring { get; init; }
    public ResumeDocumentCapabilities? DocumentCapabilities { get; init; }
}
public sealed record AIResumeHistoryItem(Guid Id, string JobTitle, string CompanyName, string SourceType,
    int Version, string TemplateCode, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string[] DownloadFormats);
public sealed record AIResumeDownload(byte[] Content, string ContentType, string FileName);
public interface IAIResumeDocumentRenderer
{
    AIResumeDownload Render(TailoredResumeContent content, string format);
}
public interface IAIResumeSourceParser
{
    Task<TailoredResumeContent> ParseAsync(User candidate, CancellationToken ct);
}
public interface IAIResumeService
{
    IReadOnlyList<AIResumePackage> Packages();
    Task<AIResumeCreditResponse> CreditsAsync(Guid userId, CancellationToken ct);
    Task<AIResumeSessionResponse> CreateSessionAsync(Guid userId, CreateAIResumeSessionRequest request, CancellationToken ct);
    Task<AIResumeSessionResponse> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<AIResumeSessionResponse> AnalyzeAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<AIResumeCheckout> CheckoutAsync(Guid userId, AIResumeCheckoutRequest request, CancellationToken ct);
    Task<AIResumeCheckout?> PendingCheckoutAsync(Guid userId, string merchantOrderId, CancellationToken ct);
    Task<bool> ProcessPhonePeWebhookAsync(PhonePeWebhookRequest request, CancellationToken ct);
    Task<AIResumeCheckout> PhonePeReturnAsync(Guid userId, string merchantOrderId, CancellationToken ct);
    Task<AIResumeResponse> GenerateAsync(Guid userId, Guid sessionId, AIResumeGenerationRequest request, CancellationToken ct);
    Task<AIResumeResponse> GetResumeAsync(Guid userId, Guid resumeId, CancellationToken ct);
    Task<IReadOnlyList<AIResumeHistoryItem>> HistoryAsync(Guid userId, int page, int pageSize, CancellationToken ct);
    Task<AIResumeResponse> EditAsync(Guid userId, Guid resumeId, AIResumeEditRequest request, CancellationToken ct);
    Task<AIResumeResponse> ReviewReplacementAsync(Guid userId, Guid resumeId, string targetId, AIResumeReplacementReviewRequest request, CancellationToken ct);
    Task<AIResumeDownload> DownloadAsync(Guid userId, Guid resumeId, string format, CancellationToken ct);
}
