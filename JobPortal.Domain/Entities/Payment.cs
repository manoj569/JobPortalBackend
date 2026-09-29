using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class Payment : BaseEntity
{
    public string? PlanCode { get; set; }
    // Null snapshots identify pre-GST transactions; never retroactively invent their tax breakdown.
    public string? PlanName { get; set; }
    public int? DurationDays { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? TaxAmount { get; set; }
    public string? ReturnTo { get; set; }
    // Amount is the persisted final total charged by the provider, including GST.
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public PaymentProvider Provider { get; set; }
    public string? TransactionReference { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string? ProviderOrderId { get; set; }
    public string? ProviderReceipt { get; set; }
    public DateTime? ProviderOrderCreatedAtUtc { get; set; }
    public DateTime? LastReconciledAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }
    public ICollection<PaymentHistory> History { get; set; } = new List<PaymentHistory>();
}
