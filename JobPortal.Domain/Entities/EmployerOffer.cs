using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class EmployerOffer : BaseEntity
{
    public Guid JobApplicationId { get; set; }

    public JobApplication JobApplication { get; set; } = null!;

    public Guid PipelineId { get; set; }

    public EmployerHiringPipeline Pipeline { get; set; } = null!;

    public EmployerOfferStatus Status { get; set; }
        = EmployerOfferStatus.Draft;

    public string JobTitle { get; set; } = string.Empty;

    public decimal? AnnualSalary { get; set; }

    public string CurrencyCode { get; set; } = "INR";

    public DateTime? JoiningDateUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public string? WorkLocation { get; set; }

    public string? Notes { get; set; }

    public string? OfferLetterUrl { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public DateTime? DeclinedAtUtc { get; set; }

    public DateTime? WithdrawnAtUtc { get; set; }

    public DateTime? ExpiredAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;
}
