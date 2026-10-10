using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default);
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<int> ExpireOverduePublishedAsync(DateTime utcNow, CancellationToken cancellationToken = default);
    Task AddAsync(Job job, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Skill>> GetSkillsByNormalizedNamesAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Skill>>(Array.Empty<Skill>());

    Task AddSkillsAsync(
        IReadOnlyCollection<Skill> skills,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    void Update(Job job);
    void Remove(Job job);
    Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default);
    Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(
        Guid companyId, string title, string location, int maxResults,
        CancellationToken cancellationToken = default);

    Task<int> TouchAggregationMetadataAsync(
        Guid jobId,
        DateTime seenAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(-1);

    Task<IReadOnlyDictionary<string, Job>> FindByCanonicalUrlHashesAsync(
        IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, Job>>(
            new Dictionary<string, Job>(StringComparer.Ordinal));

    Task<int> TouchAggregationMetadataAsync(
        IReadOnlyCollection<Guid> jobIds,
        DateTime seenAtUtc,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(-1);

    Task<Job?> FindBySourceIdentityAsync(Guid jobSourceId, string externalJobId,
        CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);

    // Null means unsupported, not an authoritative empty result (legacy test repositories).
    Task<IReadOnlyCollection<Job>?> FindSourceOwnedJobsAsync(Guid sourceId, IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Job>?>(null);
    Job TrackAggregationJob(Job job) => job;

    // Both URL and fingerprint bulk results must be complete for their requested
    // keys. Legacy adapters cannot accidentally treat unsupported reads as misses.
    bool SupportsAggregationBatchPreload => false;

    Task<IReadOnlyCollection<Job>?> FindAggregationReviewJobsAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Job>?>(null);

    Task<IReadOnlyCollection<Job>?> FindByFingerprintHashesAsync(IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Job>?>(null);
    void ReleaseSavedAggregationTracking() { }

    Task<int> CloseSourceJobsMissingFromSnapshotAsync(Guid jobSourceId, IReadOnlyCollection<string> activeExternalJobIds,
        DateTime closedAtUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

public interface IJobSourceRepository
{
    Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(
        DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default);

    Task<JobSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Update(JobSource source);
}
