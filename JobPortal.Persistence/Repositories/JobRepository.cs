using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class JobRepository(JobPortalDbContext context) : IJobRepository
{
    public async Task<Job?> GetFreshByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tracked = context.ChangeTracker.Entries<Job>().FirstOrDefault(x => x.Entity.Id == id);
        if (tracked is not null) tracked.State = EntityState.Detached;
        return await GetByIdAsync(id, false, cancellationToken);
    }
    public bool SupportsAggregationBatchPreload => true;
    public Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Job> query = context.Jobs;
        if (includeDeleted) query = query.IgnoreQueryFilters();
        return query
            .Include(x => x.Company)
            .Include(x => x.Category)
            .Include(x => x.Referral)
            .Include(x => x.RecruiterContact).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Job> source = context.Jobs.AsNoTracking().IgnoreQueryFilters()
            .Include(x => x.Company).Include(x => x.Category)
            .Include(x => x.Referral);

        if (query.IsDeleted.HasValue) source = source.Where(x => x.IsDeleted == query.IsDeleted.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(x => x.Title.Contains(term) || x.ReferenceNumber.Contains(term) ||
                x.Description.Contains(term) || (x.Location != null && x.Location.Contains(term)) ||
                x.Company.Name.Contains(term) || (x.Category == null ? "Not specified" : x.Category.Name).Contains(term));
        }
        if (query.CompanyId.HasValue) source = source.Where(x => x.CompanyId == query.CompanyId);
        if (query.JobSourceId.HasValue) source = source.Where(x => x.JobSourceId == query.JobSourceId);
        if (query.CategoryId.HasValue) source = source.Where(x => x.CategoryId == query.CategoryId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.EmploymentType.HasValue) source = source.Where(x => x.EmploymentType == query.EmploymentType);
        if (query.WorkplaceType.HasValue) source = source.Where(x => x.WorkplaceType == query.WorkplaceType);
        if (query.ExperienceLevel.HasValue) source = source.Where(x => x.ExperienceLevel == query.ExperienceLevel);
        if (query.IsFeatured.HasValue) source = source.Where(x => x.IsFeatured == query.IsFeatured);
        if (query.IsHidden.HasValue) source = source.Where(x => x.IsHidden == query.IsHidden);
        if (query.PublishedFromUtc.HasValue) source = source.Where(x => x.PublishedAtUtc >= query.PublishedFromUtc);
        if (query.PublishedToUtc.HasValue) source = source.Where(x => x.PublishedAtUtc <= query.PublishedToUtc);
        if (query.ExpiresFromUtc.HasValue) source = source.Where(x => x.ExpiresAtUtc >= query.ExpiresFromUtc);
        if (query.ExpiresToUtc.HasValue) source = source.Where(x => x.ExpiresAtUtc <= query.ExpiresToUtc);

        var totalCount = await source.CountAsync(cancellationToken);
        source = ApplySorting(source, query.SortBy, query.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase));
        var items = await source.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        context.Companies.AnyAsync(x => x.Id == companyId, cancellationToken);
    public Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        context.Categories.AnyAsync(x => x.Id == categoryId, cancellationToken);
    public Task<int> ExpireOverduePublishedAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        context.Jobs
            .Where(x => x.Status == JobStatus.Published &&
                x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc <= utcNow &&
                (x.AcquisitionSource == null || x.AcquisitionSource.ProviderKey != "website-crawler" && x.AcquisitionSource.ProviderKey != "talentd"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, JobStatus.Expired)
                .SetProperty(x => x.IsFeatured, false)
                .SetProperty(x => x.UpdatedAtUtc, utcNow), cancellationToken);
    public Task AddAsync(Job job, CancellationToken cancellationToken = default) =>
        context.Jobs.AddAsync(job, cancellationToken).AsTask();

    public async Task<IReadOnlyCollection<Skill>> GetSkillsByNormalizedNamesAsync(
        IReadOnlyCollection<string> normalizedNames,
        CancellationToken cancellationToken = default)
    {
        if (normalizedNames.Count == 0) return Array.Empty<Skill>();
        // Acquisition chunks stage several jobs before SaveChanges. Reuse skills
        // already staged by an earlier item as well as rows in PostgreSQL.
        var persisted = await context.Skills
            .Where(x => normalizedNames.Contains(x.NormalizedName))
            .ToArrayAsync(cancellationToken);
        return context.Skills.Local.Where(x => normalizedNames.Contains(x.NormalizedName))
            .Concat(persisted).DistinctBy(x => x.NormalizedName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task AddSkillsAsync(
        IReadOnlyCollection<Skill> skills,
        CancellationToken cancellationToken = default)
    {
        if (skills.Count == 0) return;
        await context.Skills.AddRangeAsync(skills, cancellationToken);
    }

    public void Update(Job job) => context.Jobs.Update(job);
    public void Remove(Job job) => context.Jobs.Remove(job);
    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default) =>
        _ = await context.Jobs.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);

    public Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default)
    {
        var hash = JobPortal.Application.Abstractions.Jobs.ApplicationUrlIdentity.Hash(externalUrl);
        if (hash is null) return Task.FromResult<Job?>(null);
        return CanonicalUrlQuery(hash).FirstOrDefaultAsync(cancellationToken);
    }

    internal IQueryable<Job> CanonicalUrlQuery(string hash) => context.Jobs
        .AsNoTracking()
        .Include(x => x.Company)
        .Include(x => x.Category)
        .Where(x => x.CanonicalApplicationUrlHash == hash && !x.IsDeleted);

    public Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default) =>
        context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.FingerprintHash == fingerprintHash && !x.IsDeleted, cancellationToken);

    public async Task<IReadOnlyCollection<Job>?> FindByFingerprintHashesAsync(IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken = default)
    {
        var keys = hashes.Distinct(StringComparer.Ordinal).ToArray();
        return await context.Jobs.AsNoTracking().Include(x => x.Company).Include(x => x.Category)
            .Where(x => !x.IsDeleted && x.FingerprintHash != null && keys.Contains(x.FingerprintHash))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Job>?> FindAggregationReviewJobsAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var keys = ids.Distinct().ToArray();
        return await context.Jobs.AsNoTracking().Include(x => x.Company).Include(x => x.Category)
            .Where(x => keys.Contains(x.Id) && !x.IsDeleted)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(
        Guid companyId, string title, string location, int maxResults, CancellationToken cancellationToken = default)
    {
        return await context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Where(x => x.CompanyId == companyId && x.Status == JobStatus.Published && !x.IsDeleted)
            .OrderByDescending(x => x.PublishedAtUtc)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> TouchAggregationMetadataAsync(
        Guid jobId,
        DateTime seenAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsRelational())
        {
            var job = await context.Jobs.FirstOrDefaultAsync(x => x.Id == jobId && !x.IsDeleted, cancellationToken);
            if (job is null) return 0;
            job.LastSeenAtUtc = seenAtUtc;
            job.FirstSeenAtUtc ??= seenAtUtc;
            return 1;
        }
        return await context.Jobs
            .Where(x => x.Id == jobId && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastSeenAtUtc, seenAtUtc)
                    .SetProperty(x => x.FirstSeenAtUtc, x => x.FirstSeenAtUtc ?? seenAtUtc),
                cancellationToken);
    }

    public Task<Job?> FindBySourceIdentityAsync(Guid jobSourceId, string externalJobId,
        CancellationToken cancellationToken = default) => context.Jobs
            .IgnoreQueryFilters().FirstOrDefaultAsync(x => x.JobSourceId == jobSourceId && x.ExternalJobId == externalJobId,
                cancellationToken);

    public Task<Job?> FindByAcquisitionIdentityAsync(Guid acquisitionSourceId, string externalJobId,
        CancellationToken cancellationToken = default) => context.Jobs.Include(x => x.JobSkills)
            .FirstOrDefaultAsync(x => x.AcquisitionSourceId == acquisitionSourceId && x.AcquisitionExternalId == externalJobId && !x.IsDeleted,
                cancellationToken);

    public async Task<IReadOnlyCollection<Job>?> FindSourceOwnedJobsAsync(Guid sourceId,
        IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken = default)
    {
        var results = new List<Job>();
        foreach (var batch in externalIds.Distinct(StringComparer.Ordinal).Chunk(500))
            results.AddRange(await context.Jobs.AsNoTracking()
                .IgnoreQueryFilters().Where(x => x.JobSourceId == sourceId && batch.Contains(x.ExternalJobId!))
                .ToArrayAsync(cancellationToken));
        return results;
    }

    public Job TrackAggregationJob(Job job)
    {
        var tracked = context.Jobs.Local.FirstOrDefault(x => x.Id == job.Id);
        if (tracked is not null) return tracked;
        if (context.Entry(job).State == EntityState.Detached)
            context.Entry(job).State = EntityState.Unchanged;
        return job;
    }

    public void ReleaseSavedAggregationTracking()
    {
        // Do not Clear(): source bookkeeping, audit, publication and pending metadata remain tracked.
        foreach (var entry in context.ChangeTracker.Entries<Job>().Where(x => x.State == EntityState.Unchanged).ToArray())
            entry.State = EntityState.Detached;
    }

    public async Task ObserveSourceInventoryAsync(Guid sourceId, IReadOnlyCollection<string> externalIds,
        DateTime seenAtUtc, CancellationToken cancellationToken = default)
    {
        // Positive presence precedes geography selection, even for partial scans.
        var ids = externalIds.Distinct(StringComparer.Ordinal).ToArray();
        var existing = await context.Set<JobSourceVacancy>()
            .Where(x => x.JobSourceId == sourceId && ids.Contains(x.ExternalJobId)).ToListAsync(cancellationToken);
        foreach (var member in existing)
        {
            if (seenAtUtc < member.LastSeenAtUtc) continue;
            member.LastSeenAtUtc = seenAtUtc; member.MissingCompleteSnapshots = 0; member.IsCurrent = true;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ObserveSourceVacancyAsync(Guid sourceId, string externalId, Guid jobId,
        DateTime seenAtUtc, CancellationToken cancellationToken = default)
    {
        var row = context.Set<JobSourceVacancy>().Local.FirstOrDefault(x => x.JobSourceId == sourceId && x.ExternalJobId == externalId)
            ?? await context.Set<JobSourceVacancy>().SingleOrDefaultAsync(x => x.JobSourceId == sourceId && x.ExternalJobId == externalId, cancellationToken);
        if (row is null)
        {
            row = new() { JobSourceId = sourceId, ExternalJobId = externalId, JobId = jobId };
            context.Set<JobSourceVacancy>().Add(row);
        }
        if (row.JobId != jobId) throw new InvalidOperationException("Source vacancy identity changed jobs.");
        if (seenAtUtc < row.LastSeenAtUtc) return;
        row.LastSeenAtUtc = seenAtUtc; row.MissingCompleteSnapshots = 0; row.IsCurrent = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ObserveSourceVacanciesAsync(Guid sourceId,
        IReadOnlyCollection<(string ExternalId, Guid JobId)> vacancies, DateTime seenAtUtc,
        CancellationToken cancellationToken = default)
    {
        var unique = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (externalId, jobId) in vacancies)
        {
            if (string.IsNullOrWhiteSpace(externalId) || jobId == Guid.Empty) continue;
            if (unique.TryGetValue(externalId, out var existingJobId) && existingJobId != jobId)
                throw new InvalidOperationException("Source vacancy identity changed jobs within one observation batch.");
            unique[externalId] = jobId;
        }
        if (unique.Count == 0) return;
        var ids = unique.Keys.ToArray();
        var members = await context.Set<JobSourceVacancy>()
            .Where(x => x.JobSourceId == sourceId && ids.Contains(x.ExternalJobId))
            .ToDictionaryAsync(x => x.ExternalJobId, StringComparer.Ordinal, cancellationToken);
        foreach (var (externalId, jobId) in unique)
        {
            if (!members.TryGetValue(externalId, out var member))
            {
                context.Set<JobSourceVacancy>().Add(new JobSourceVacancy
                {
                    JobSourceId = sourceId, ExternalJobId = externalId, JobId = jobId,
                    LastSeenAtUtc = seenAtUtc, MissingCompleteSnapshots = 0, IsCurrent = true
                });
                continue;
            }
            if (member.JobId != jobId)
                throw new InvalidOperationException("Source vacancy identity changed jobs.");
            if (seenAtUtc < member.LastSeenAtUtc) continue;
            member.LastSeenAtUtc = seenAtUtc;
            member.MissingCompleteSnapshots = 0;
            member.IsCurrent = true;
        }
        await context.SaveChangesAsync(cancellationToken);
    }
    public async Task<int> ReconcileSourceSnapshotAsync(Guid sourceId, IReadOnlyCollection<string> externalIds,
        Guid snapshotId, DateTime observedAtUtc, int missingThreshold, CancellationToken cancellationToken = default)
    {
        if (missingThreshold < 2 || snapshotId == Guid.Empty) throw new ArgumentException("Invalid snapshot policy.");
        var source = await context.JobSources.SingleAsync(x => x.Id == sourceId, cancellationToken);
        if (source.LastReconciledSnapshotId == snapshotId || source.LastReconciledSnapshotAtUtc >= observedAtUtc) return 0;
        var active = externalIds.ToHashSet(StringComparer.Ordinal);
        if (active.Count != externalIds.Count) throw new InvalidOperationException("Duplicate census identities.");
        if (source.LastCompleteSnapshotCount is >= 5 && active.Count < source.LastCompleteSnapshotCount.Value * 0.5)
            throw new InvalidOperationException("Suspicious source inventory drop.");
        // Bootstrap source-owned legacy rows without inferring additional ownership.
        var owned = await context.Jobs.Where(x => x.JobSourceId == sourceId && !x.IsDeleted && x.ExternalJobId != null).ToArrayAsync(cancellationToken);
        var members = await context.Set<JobSourceVacancy>().Where(x => x.JobSourceId == sourceId).ToListAsync(cancellationToken);
        foreach (var job in owned)
            if (!members.Any(x => x.ExternalJobId == job.ExternalJobId))
            {
                var member = new JobSourceVacancy { JobSourceId = sourceId, ExternalJobId = job.ExternalJobId!, JobId = job.Id,
                    LastSeenAtUtc = job.LastSeenAtUtc ?? observedAtUtc };
                members.Add(member); context.Set<JobSourceVacancy>().Add(member);
            }
        var closed = 0;
        foreach (var member in members)
        {
            if (member.LastSeenAtUtc > observedAtUtc) continue;
            member.LastSnapshotId = snapshotId;
            if (active.Contains(member.ExternalJobId))
            { member.LastSeenAtUtc = observedAtUtc; member.MissingCompleteSnapshots = 0; member.IsCurrent = true; continue; }
            member.MissingCompleteSnapshots++;
            member.IsCurrent = member.MissingCompleteSnapshots < missingThreshold;
            if (member.IsCurrent) continue;
            var anotherCurrent = await context.Set<JobSourceVacancy>().AnyAsync(x => x.JobId == member.JobId && x.JobSourceId != sourceId && x.IsCurrent && context.JobSources.Any(s => s.Id == x.JobSourceId && s.IsActive && !s.IsDeleted), cancellationToken);
            if (anotherCurrent) continue;
            var job = owned.FirstOrDefault(x => x.Id == member.JobId) ?? await context.Jobs.SingleOrDefaultAsync(x => x.Id == member.JobId, cancellationToken);
            if (job is null || job.IsDeleted || job.IsHidden || !job.SourceOwnsPublication || job.Status is not (JobStatus.Published or JobStatus.Draft)) continue;
            // Only the owning source may close a shared job; secondary evidence only protects it.
            if (job.JobSourceId != sourceId) continue;
            job.Status = JobStatus.Closed; job.IsFeatured = false; job.SourceClosedBySnapshotId = snapshotId;
            job.UpdatedAtUtc = observedAtUtc; closed++;
        }
        source.LastReconciledSnapshotId = snapshotId; source.LastReconciledSnapshotAtUtc = observedAtUtc; source.LastCompleteSnapshotCount = active.Count;
        return closed; // Existing runner's final Save commits counters and closures together.
    }

    public Task<int> CloseSourceJobsMissingFromSnapshotAsync(Guid jobSourceId,
        IReadOnlyCollection<string> activeExternalJobIds, DateTime closedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var activeIds = activeExternalJobIds.Distinct(StringComparer.Ordinal).ToArray();
        return CloseMissingAsync();

        async Task<int> CloseMissingAsync()
        {
            var missing = await context.Jobs
                .Where(x => x.JobSourceId == jobSourceId && !x.IsDeleted && x.Status != JobStatus.Closed &&
                    (activeIds.Length == 0 || !activeIds.Contains(x.ExternalJobId!)))
                .ToListAsync(cancellationToken);
            foreach (var job in missing)
            {
                job.Status = JobStatus.Closed;
                job.IsFeatured = false;
                job.UpdatedAtUtc = closedAtUtc;
            }
            return missing.Count;
        }
    }


    public async Task<IReadOnlyDictionary<string, Job>> FindByCanonicalUrlHashesAsync(
        IReadOnlyCollection<string> hashes,
        CancellationToken cancellationToken = default)
    {
        if (hashes.Count == 0)
            return new Dictionary<string, Job>(StringComparer.Ordinal);

        var distinctHashes = hashes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (distinctHashes.Length == 0)
            return new Dictionary<string, Job>(StringComparer.Ordinal);

        var matches = await context.Jobs
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Category)
            .Where(x => !x.IsDeleted &&
                        x.CanonicalApplicationUrlHash != null &&
                        distinctHashes.Contains(x.CanonicalApplicationUrlHash))
            .ToArrayAsync(cancellationToken);

        return matches
            .Where(x => x.CanonicalApplicationUrlHash is not null)
            .GroupBy(x => x.CanonicalApplicationUrlHash!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    }

    public async Task<int> TouchAggregationMetadataAsync(
        IReadOnlyCollection<Guid> jobIds,
        DateTime seenAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
            return 0;

        var distinctIds = jobIds.Distinct().ToArray();

        if (!context.Database.IsRelational())
        {
            var tracked = await context.Jobs.Where(x => distinctIds.Contains(x.Id) && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            foreach (var job in tracked)
            {
                job.LastSeenAtUtc = seenAtUtc;
                job.FirstSeenAtUtc ??= seenAtUtc;
            }
            return tracked.Count;
        }

        return await context.Jobs
            .Where(x => distinctIds.Contains(x.Id) && !x.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastSeenAtUtc, seenAtUtc)
                    .SetProperty(x => x.FirstSeenAtUtc, x => x.FirstSeenAtUtc ?? seenAtUtc),
                cancellationToken);
    }

    private static IQueryable<Job> ApplySorting(IQueryable<Job> source, string sortBy, bool descending) =>
        (sortBy.ToLowerInvariant(), descending) switch
        {
            ("title", false) => source.OrderBy(x => x.Title).ThenBy(x => x.Id),
            ("title", true) => source.OrderByDescending(x => x.Title).ThenBy(x => x.Id),
            ("updatedat", false) => source.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("updatedat", true) => source.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("publishedat", false) => source.OrderBy(x => x.PublishedAtUtc).ThenBy(x => x.Id),
            ("publishedat", true) => source.OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id),
            ("minimumsalary", false) => source.OrderBy(x => x.MinimumSalary).ThenBy(x => x.Id),
            ("minimumsalary", true) => source.OrderByDescending(x => x.MinimumSalary).ThenBy(x => x.Id),
            ("maximumsalary", false) => source.OrderBy(x => x.MaximumSalary).ThenBy(x => x.Id),
            ("maximumsalary", true) => source.OrderByDescending(x => x.MaximumSalary).ThenBy(x => x.Id),
            ("expiresat", false) => source.OrderBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id),
            ("expiresat", true) => source.OrderByDescending(x => x.ExpiresAtUtc).ThenBy(x => x.Id),
            ("status", false) => source.OrderBy(x => x.Status).ThenBy(x => x.Id),
            ("status", true) => source.OrderByDescending(x => x.Status).ThenBy(x => x.Id),
            (_, false) => source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            _ => source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
}
