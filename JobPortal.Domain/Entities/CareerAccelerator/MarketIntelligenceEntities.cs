using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum MarketConceptStatus { Active = 1, Deprecated, Merged }
public enum MarketDiscoveryStatus { CollectingEvidence = 0, PendingReview = 1, Approved, Merged, Rejected, Ignored }
public enum MarketMappingSource { StructuredSource = 1, Alias, PhraseRule, Administrator }

// Approved market concepts link to the existing Skill universe. A learning path is a
// separate editorial decision represented by the optional Accelerator competency.
public sealed class MarketSkill : BaseEntity
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public Guid? CompetencyId { get; set; }
    public AcceleratorCompetency? Competency { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MarketConceptStatus Status { get; set; } = MarketConceptStatus.Active;
    public Guid? MergedIntoId { get; set; }
    public ICollection<MarketSkillAlias> Aliases { get; set; } = [];
}

public sealed class MarketSkillAlias : BaseEntity
{
    public Guid MarketSkillId { get; set; }
    public MarketSkill MarketSkill { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
}

public sealed class MarketRole : BaseEntity
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid? AcceleratorRoleId { get; set; }
    public AcceleratorCareerRole? AcceleratorRole { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MarketConceptStatus Status { get; set; } = MarketConceptStatus.Active;
    public Guid? MergedIntoId { get; set; }
    public ICollection<MarketRoleAlias> Aliases { get; set; } = [];
}

public sealed class MarketRoleAlias : BaseEntity
{
    public Guid MarketRoleId { get; set; }
    public MarketRole MarketRole { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
}

public sealed class MarketJobSkill : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public Guid MarketSkillId { get; set; }
    public MarketSkill MarketSkill { get; set; } = null!;
    public MarketMappingSource Source { get; set; }
    public decimal Confidence { get; set; }
    public DateTime FirstObservedAtUtc { get; set; }
    public DateTime LastObservedAtUtc { get; set; }
}

public sealed class MarketJobRole : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public Guid MarketRoleId { get; set; }
    public MarketRole MarketRole { get; set; } = null!;
    public MarketMappingSource Source { get; set; }
    public decimal Confidence { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class MarketSkillDiscovery : BaseEntity
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid? SuggestedSkillId { get; set; }
    public string RawTerm { get; set; } = string.Empty;
    public string NormalizedTerm { get; set; } = string.Empty;
    public string SuggestedCategory { get; set; } = string.Empty;
    public MarketDiscoveryStatus Status { get; set; } = MarketDiscoveryStatus.CollectingEvidence;
    public Guid? ResolvedMarketSkillId { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public string? ReviewReason { get; set; }
}

public sealed class MarketRoleDiscovery : BaseEntity
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string RawTitle { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public MarketDiscoveryStatus Status { get; set; } = MarketDiscoveryStatus.CollectingEvidence;
    public Guid? ResolvedMarketRoleId { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public string? ReviewReason { get; set; }
}

// One observation per job/term makes repeated processing idempotent and permits
// distinct-company evidence without inflating counts on retries.
public sealed class MarketDiscoveryObservation : BaseEntity
{
    public Guid JobId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? SkillDiscoveryId { get; set; }
    public Guid? RoleDiscoveryId { get; set; }
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class MarketJobProcessingState : BaseEntity
{
    public Guid JobId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public long TaxonomyRevision { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

public sealed class MarketProcessingCheckpoint : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid? LastJobId { get; set; }
    public DateTime? LastJobCreatedAtUtc { get; set; }
    public DateTime? LastSuccessfulAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    public long JobsProcessed { get; set; }
    public long Failures { get; set; }
    public long TaxonomyRevision { get; set; }
}
