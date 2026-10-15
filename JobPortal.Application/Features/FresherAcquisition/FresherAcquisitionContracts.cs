using System.ComponentModel.DataAnnotations;

namespace JobPortal.Application.Features.FresherAcquisition;

public sealed class FresherBulkImportRequest
{
    [Required, StringLength(100)] public string ImportId { get; init; } = string.Empty;
    [Required, StringLength(80)] public string SourceType { get; init; } = string.Empty;
    [Required, StringLength(160)] public string SourceName { get; init; } = string.Empty;
    [Required, MinLength(1)] public IReadOnlyList<FresherImportCandidate> Jobs { get; init; } = [];
}

public sealed class FresherImportCandidate
{
    public Guid? CompanyId { get; init; }
    public string? ExternalJobId { get; init; }
    public string? CompanyName { get; init; }
    public string? CompanyWebsiteUrl { get; init; }
    public string? CompanyLogoUrl { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? LocationText { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? WorkplaceType { get; init; }
    public string? RemoteEligibility { get; init; }
    public string? ExperienceText { get; init; }
    [Range(0, 80)] public int? MinimumExperienceYears { get; init; }
    [Range(0, 80)] public int? MaximumExperienceYears { get; init; }
    public string? ExperienceClassification { get; init; }
    public string? EmploymentType { get; init; }
    public string? Category { get; init; }
    public Guid? CategoryId { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
    public string? DiscoveredUrl { get; init; }
    public string? CanonicalJobUrl { get; init; }
    public string? ApplyUrl { get; init; }
    public DateTime? PostedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? SourceLastSeenAt { get; init; }
    public System.Text.Json.JsonElement? SourceMetadata { get; init; }
}

public sealed class FresherAcquisitionOptions
{
    public const string SectionName = "FresherAcquisition";
    public int MaximumBatchSize { get; set; } = 500;
    public int MaximumRequestBytes { get; set; } = 3_000_000;
    public int ChunkSize { get; set; } = 50;
}

public sealed record FresherImportItemOutcome(int Index, string Outcome, string ReasonCode, Guid? JobId = null);
public sealed record FresherBulkImportResponse(
    string ImportId, int Received, int Accepted, int RejectedGeography, int RejectedExperience, int RejectedInvalidData,
    int InvalidUrl, int Created, int Updated, int Existing, int Failed,
    IReadOnlyList<FresherImportItemOutcome> Items)
{
    public Guid? RunId { get; init; }
    public int Published { get; init; }
}
