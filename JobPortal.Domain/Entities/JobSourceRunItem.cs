namespace JobPortal.Domain.Entities;

// Durable outcome receipt; committed in the same SaveChanges as its job mutation.
public sealed class JobSourceRunItem
{
    public Guid RunId { get; set; }
    public int Ordinal { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public Guid? JobId { get; set; }
    public int IngestionOutcome { get; set; }
    public int ReasonCode { get; set; }
    public string? ValidationReason { get; set; }
    public int? PublicationOutcome { get; set; }
    public string? PublicationReasonsJson { get; set; }
    public DateTime CommittedAtUtc { get; set; }
    public DateTime? PublicationAtUtc { get; set; }
}
