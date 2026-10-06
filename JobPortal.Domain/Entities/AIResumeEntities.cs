using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public enum AIResumeSessionStatus { Created = 1, Analyzing, Analyzed, Generating, Generated, Failed }
public enum AIResumeGenerationStatus { Reserved = 1, Consumed, Released }
public enum AIResumeCreditKind { Purchase = 1, Reservation, Consumption, Release }

public sealed class AIResumeSession : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid SourceResumeId { get; set; }
    public Guid? JobId { get; set; }
    public string SourceType { get; set; } = "EXTERNAL_JD";
    public string JobTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string JobDescription { get; set; } = string.Empty;
    public string SourceJson { get; set; } = "{}";
    public string EvidenceJson { get; set; } = "[]";
    public string? AnalysisJson { get; set; }
    public string? AnalysisModel { get; set; }
    public int AnalysisInputTokens { get; set; }
    public int AnalysisOutputTokens { get; set; }
    public AIResumeSessionStatus Status { get; set; }
    public Guid? AnalysisOwner { get; set; }
    public DateTime? AnalysisLeaseUntilUtc { get; set; }
}
public sealed class AIResumeCreditWallet : BaseEntity
{
    public Guid UserId { get; set; }
    public int Balance { get; set; }
    public int Reserved { get; set; }
    public int LifetimePurchased { get; set; }
    public int LifetimeConsumed { get; set; }
}
public sealed class AIResumePurchase : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid? SessionId { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public int Credits { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
    public string MerchantOrderId { get; set; } = string.Empty;
    public string? ProviderPaymentId { get; set; }
    public string? RedirectUrl { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public DateTime? PaidAtUtc { get; set; }
    public Guid? CheckoutOwner { get; set; }
    public DateTime? CheckoutLeaseUntilUtc { get; set; }
}
public sealed class AIResumeGeneration : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid Owner { get; set; }
    public DateTime LeaseUntilUtc { get; set; }
    public AIResumeGenerationStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public int Attempt { get; set; }
}
public sealed class TailoredResume : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid GenerationId { get; set; }
    public int Version { get; set; }
    public int EditRevision { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string TemplateCode { get; set; } = "Professional";
    public string GenerationModel { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}
public sealed class TailoredResumeEdit : BaseEntity
{
    public Guid ResumeId { get; set; }
    public int Revision { get; set; }
    public string ContentJson { get; set; } = "{}";
}
public sealed class AIResumeCreditTransaction : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? PurchaseId { get; set; }
    public Guid? GenerationId { get; set; }
    public AIResumeCreditKind Kind { get; set; }
    public int AvailableDelta { get; set; }
    public int ReservedDelta { get; set; }
    public int BalanceAfter { get; set; }
    public int ReservedAfter { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
