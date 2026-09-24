using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default);
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<int> ExpireOverduePublishedAsync(
        DateTime utcNow, CancellationToken cancellationToken = default);
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

    // Job Aggregation & Deduplication (Phase 1)
    Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default);
    Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(Guid companyId, string title, string location, int maxResults, CancellationToken cancellationToken = default);
}

public interface IJobSourceRepository
{
    Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(
        DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default);

    Task<JobSource?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    void Update(JobSource source);
}
