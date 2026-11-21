using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class MarketIntelligenceOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public int RefreshMinutes { get; set; } = 5;
    public int DiscoveryMinimumJobs { get; set; } = 3;
    public int DiscoveryMinimumCompanies { get; set; } = 2;
    public bool IsValid() => BatchSize is >= 1 and <= 200 && RefreshMinutes is >= 1 and <= 1440 &&
        DiscoveryMinimumJobs is >= 2 and <= 100 && DiscoveryMinimumCompanies is >= 2 and <= 50;
}

public sealed record MarketJobSnapshot(Guid Id, Guid CompanyId, DateTime CreatedAtUtc,
    string Title, string Description,
    string? Requirements, string? Responsibilities, string? Location, DateTime? PublishedAtUtc,
    DateTime? ExpiresAtUtc, string[] StructuredSkills, string ContentHash);
public sealed record MarketTerm(Guid Id, string Name, string NormalizedName, string[] Aliases, bool IsActive);
public sealed record MarketExtraction(Guid[] SkillIds, Guid[] StructuredSkillIds,
    Guid? PrimaryRoleId, string[] UnknownSkills, string? UnknownRole);
public sealed record MarketDemand(int ActiveJobCount, int DistinctCompanyCount, int NewJobs7Days,
    int NewJobs30Days, decimal? TrendPercent, DateTime CalculatedAtUtc);
public sealed record MarketPathCard(Guid Id, string Name, string Category, string Description,
    MarketDemand Demand, string LearningPathStatus, string JobFilter,
    IReadOnlyList<MarketSkillAssociation> TopAssociations);
public sealed record MarketSkillAssociation(Guid Id, string Name, int JobCount, decimal Percentage);
public sealed record MarketCompanyDemand(Guid Id, string Name, int ActiveJobCount);
public sealed record MarketPathDetail(MarketPathCard Path, IReadOnlyList<MarketSkillAssociation> Associations,
    IReadOnlyList<MarketSkillAssociation> Related, IReadOnlyList<MarketCompanyDemand> Companies,
    IReadOnlyList<string> Aliases);
public sealed record MarketSearchResult(IReadOnlyList<MarketPathCard> CareerPaths,
    IReadOnlyList<MarketPathCard> SkillPaths);
public sealed record MarketDiscoveryItem(Guid Id, string Term, int DistinctJobCount,
    int DistinctCompanyCount, DateTime FirstSeenAtUtc, DateTime LastSeenAtUtc,
    MarketDiscoveryStatus Status, IReadOnlyList<Guid> ExampleJobIds,
    Guid? SuggestedCanonicalSkillId = null);
public sealed record MarketProcessingStatus(Guid? LastJobId, DateTime? LastSuccessfulAtUtc,
    long JobsProcessed, long Failures);
public sealed record MarketBatchResult(int JobsProcessed, int SkillsMapped, int RolesMapped,
    int SkillCandidatesObserved, int RoleCandidatesObserved);
public sealed record MarketApproveSkillRequest(string Category, string Description, Guid? CompetencyId,
    string Reason);
public sealed record MarketApproveRoleRequest(string Family, string Description, Guid? AcceleratorRoleId,
    string Reason);
public sealed record MarketMergeRequest(Guid TargetId, string Reason);
public sealed record MarketAcceleratorRoleMapping(Guid MarketRoleId,string Name,Guid Revision,Guid? AcceleratorRoleId);
public sealed record MarketMapAcceleratorRoleRequest(Guid AcceleratorRoleId,Guid ExpectedRevision,string Reason);
public sealed record MarketReviewRequest(string Reason);
public sealed record MarketAliasRequest(string Text, string Reason);
public sealed record MarketBackfillRequest(int BatchSize);
public sealed record MarketCandidateSignals(Guid? GoalMarketRoleId,
    IReadOnlyDictionary<Guid, int> VerifiedProficiencyByMarketSkill);
public sealed record MarketRecommendation(MarketPathCard Path, decimal Score, string Reason);
public sealed record MarketRoleGap(Guid MarketSkillId, string SkillName, int RequiredJobCount,
    decimal PercentageOfRoleJobs, int? PublishedRoleExpectedProficiency,
    int VerifiedCandidateProficiency, int? Gap);

public interface IMarketIntelligenceStore
{
    Task BootstrapTaxonomyAsync(DateTime nowUtc, CancellationToken ct);
    Task<MarketProcessingStatus> StatusAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketJobSnapshot>> NextJobsAsync(int take, CancellationToken ct);
    Task CompleteBatchAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketTerm>> SkillsAsync(CancellationToken ct);
    Task<IReadOnlyList<MarketTerm>> RolesAsync(CancellationToken ct);
    Task<bool> ApplyExtractionAsync(MarketJobSnapshot job, MarketExtraction extraction,
        DateTime nowUtc, CancellationToken ct);
    Task RecordFailureAsync(Guid jobId, DateTime createdAtUtc, CancellationToken ct);
    Task<IReadOnlyList<MarketPathCard>> PathsAsync(bool roles, string? search, string? category,
        Guid? relatedRoleId, string? location, int? experience, string sort,
        int skip, int take, DateTime nowUtc, CancellationToken ct);
    Task<MarketPathDetail?> DetailAsync(bool role, Guid id, DateTime nowUtc, CancellationToken ct);
    Task<MarketCandidateSignals> CandidateSignalsAsync(Guid candidateId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, int>> PublishedRoleRequirementsAsync(Guid marketRoleId,
        CancellationToken ct);
    Task<IReadOnlyList<MarketDiscoveryItem>> DiscoveriesAsync(bool roles, int skip, int take,
        CancellationToken ct);
    Task<MarketDiscoveryItem?> DiscoveryAsync(bool role, Guid id, CancellationToken ct);
    Task ApproveSkillAsync(Guid id, MarketApproveSkillRequest request, DateTime nowUtc, CancellationToken ct);
    Task ApproveRoleAsync(Guid id, MarketApproveRoleRequest request, DateTime nowUtc, CancellationToken ct);
    Task<MarketAcceleratorRoleMapping?> AcceleratorRoleMappingAsync(Guid id,CancellationToken ct);
    Task<MarketAcceleratorRoleMapping> MapAcceleratorRoleAsync(Guid id,MarketMapAcceleratorRoleRequest request,DateTime nowUtc,CancellationToken ct);
    Task MergeSkillAsync(Guid id, MarketMergeRequest request, DateTime nowUtc, CancellationToken ct);
    Task MergeRoleAsync(Guid id, MarketMergeRequest request, DateTime nowUtc, CancellationToken ct);
    Task MergeCanonicalAsync(bool role, Guid sourceId, Guid targetId, DateTime nowUtc,
        CancellationToken ct);
    Task ReviewAsync(bool role, Guid id, MarketDiscoveryStatus status, string reason, CancellationToken ct);
    Task AddAliasAsync(bool role, Guid id, string alias, string normalized, CancellationToken ct);
}
