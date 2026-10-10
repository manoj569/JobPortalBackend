using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

public sealed class JobIngestionService(
    IJobRepository jobs,
    ICompanyManagementRepository companies,
    ICategoryManagementRepository categories,
    IJobDeduplicationService deduplicationService,
    IJobFingerprintService fingerprintService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IExternalJobCreationLock creationLock,
    IUrlCanonicalizer canonicalizer) : IJobIngestionService, IBulkJobIngestionService, IBatchedJobIngestionService
{
    // JobSourceRunner processes a source sequentially inside one DI scope.
    // Cache successful company resolutions so a source containing many jobs
    // from the same company does not repeat the same remote database lookup.
    // Misses are intentionally not cached.
    private readonly Dictionary<string, Company> _companyCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Job> _canonicalUrlDuplicateCache =
        new(StringComparer.Ordinal);

    private readonly HashSet<Guid> _bulkTouchedJobIds = [];
    private readonly Dictionary<(Guid Source, string ExternalId), Job> _ownedJobs = [];
    private readonly HashSet<Guid> _preloadedSources = [];
    private readonly HashSet<Guid> _validCategories = [];
    private IExternalJobCreationLockRun? _creationRun;
    private bool _runPrepared;
    private bool _batchMode;
    private bool _exactBatchPrepared;
    private readonly Dictionary<string, Job> _fingerprintMatches = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _stagedMetadataRepairs = [];
    private int _saveCalls;
    private double _saveMilliseconds;
    private int _preloadedOwnedCount;
    private double _preloadMilliseconds;
    public JobIngestionRunMetrics RunMetrics => new(_saveCalls, _saveMilliseconds, _preloadedOwnedCount, _preloadMilliseconds);

    private async Task PersistAsync(CancellationToken token)
    {
        if (_batchMode) return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _saveCalls++;
        try { await unitOfWork.SaveChangesAsync(token); }
        finally { _saveMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    public async Task<IReadOnlyList<JobIngestionResult>> IngestBatchAsync(IReadOnlyList<RawExternalJob> rawJobs,
        CancellationToken cancellationToken = default)
    {
        // Publishing within a batch changes fuzzy eligibility. Keep the old item
        // lifecycle for any potentially publishable job and unsupported lock adapters.
        if (rawJobs.Count > 200) throw new ArgumentOutOfRangeException(nameof(rawJobs));
        if (rawJobs.Any(x => x.ExpiresAtUtc.HasValue || !x.JobSourceId.HasValue || string.IsNullOrWhiteSpace(x.ExternalId)) ||
            !jobs.SupportsAggregationBatchPreload || creationLock is not IExternalJobCreationBatchLock batchLocks)
        {
            await PrepareRunAsync(rawJobs.ToArray(), cancellationToken);
            return Array.Empty<JobIngestionResult>();
        }

        var identities = new List<(string? Url, string Fingerprint)>(rawJobs.Count);
        foreach (var raw in rawJobs)
        {
            var name = TextNormalizer.TrimOrNull(raw.CompanyName) ?? string.Empty;
            var company = await ResolveCompanyAsync(name, SlugGenerator.Generate(name), raw.CompanyId, cancellationToken);
            identities.Add((raw.ApplicationUrl is null ? null : canonicalizer.Canonicalize(raw.ApplicationUrl),
                fingerprintService.GenerateFingerprint(raw.Title ?? string.Empty, company?.Name ?? name, raw.Location)));
        }
        var lease = await batchLocks.AcquireBatchAsync(identities, cancellationToken);
        _batchMode = true;
        try
        {
            // Fresh reads AFTER all creation locks are held; absent exact matches
            // are authoritative only until this lease is released.
            var preloadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            await PrepareRunAsync(rawJobs.ToArray(), cancellationToken);
            if (_ownedJobs.Values.Any(x => x.Status == JobStatus.Published))
            {
                // Updating published fuzzy candidates changes later-item matching.
                // Release the batch lease before the original item lifecycle.
                return Array.Empty<JobIngestionResult>();
            }
            var matches = await jobs.FindByFingerprintHashesAsync(identities.Select(x => x.Fingerprint).ToArray(), cancellationToken);
            _preloadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(preloadStart).TotalMilliseconds;
            if (matches is null || deduplicationService is not IFuzzyJobDeduplicationService ||
                matches.GroupBy(x => x.FingerprintHash).Any(x => x.Count() > 1))
            {
                // Fingerprints are deliberately non-unique. Preserve fresh selection
                // when an owned update could invalidate one of several stored matches.
                // Reuse the original item path for ambiguous stored matches.
                _batchMode = false;
                await PrepareRunAsync(rawJobs.ToArray(), cancellationToken);
                return Array.Empty<JobIngestionResult>();
            }
            foreach (var job in matches) _fingerprintMatches.TryAdd(job.FingerprintHash!, job);
            _exactBatchPrepared = true;
            var results = new List<JobIngestionResult>(rawJobs.Count);
            foreach (var raw in rawJobs) results.Add(await IngestAsync(raw, cancellationToken));
            _batchMode = false;
            await PersistAsync(cancellationToken);
            jobs.ReleaseSavedAggregationTracking();
            return results;
        }
        catch
        {
            // EF SaveChanges is atomic for this bounded batch. No outcome is reported
            // before commit, and earlier batches remain durable on cancellation/failure.
            unitOfWork.ResetAfterFailure();
            ResetRunAfterFailure();
            throw;
        }
        finally
        {
            _batchMode = false;
            _exactBatchPrepared = false;
            _fingerprintMatches.Clear();
            _stagedMetadataRepairs.Clear();
            await lease.DisposeAsync();
        }
    }

    public async Task PrepareRunAsync(
        IReadOnlyCollection<RawExternalJob> rawJobs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cachedCompanies = _batchMode ? _companyCache.ToArray() : [];
        var cachedCategories = _batchMode ? _validCategories.ToArray() : [];
        await CompleteRunAsync();
        _saveCalls = 0;
        _saveMilliseconds = 0;
        _preloadedOwnedCount = 0;
        _preloadMilliseconds = 0;
        _companyCache.Clear();
        foreach (var entry in cachedCompanies) _companyCache[entry.Key] = entry.Value;
        foreach (var category in cachedCategories) _validCategories.Add(category);
        foreach (var source in rawJobs.Where(x => x.JobSourceId.HasValue && !string.IsNullOrWhiteSpace(x.ExternalId))
            .GroupBy(x => x.JobSourceId!.Value))
        {
            var owned = await jobs.FindSourceOwnedJobsAsync(source.Key,
                source.Select(x => x.ExternalId!.Trim()).Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
            if (owned is null) continue;
            _preloadedSources.Add(source.Key);
            foreach (var job in owned) _ownedJobs[(source.Key, job.ExternalJobId!)] = job;
        }
        _creationRun = (creationLock as IExternalJobCreationLockRunFactory)?.CreateRun();
        _preloadedOwnedCount = _ownedJobs.Count;
        _runPrepared = true;

        var canonicalByHash = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawJob in rawJobs)
        {
            var applicationUrl = TextNormalizer.TrimOrNull(rawJob.ApplicationUrl);
            if (applicationUrl is null)
                continue;

            if (!Uri.TryCreate(applicationUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
                continue;

            var canonicalUrl = canonicalizer.Canonicalize(applicationUrl);
            if (string.IsNullOrWhiteSpace(canonicalUrl))
                continue;

            var hash = ApplicationUrlIdentity.Hash(canonicalUrl);
            if (hash is not null)
                canonicalByHash[hash] = canonicalUrl;
        }

        if (canonicalByHash.Count == 0)
            return;

        var matches = await jobs.FindByCanonicalUrlHashesAsync(
            canonicalByHash.Keys.ToArray(),
            cancellationToken);

        if (matches.Count == 0)
            return;

        foreach (var (hash, job) in matches)
        {
            if (canonicalByHash.TryGetValue(hash, out var canonicalUrl))
                _canonicalUrlDuplicateCache[canonicalUrl] = job;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ownedIds = _ownedJobs.Values.Select(x => x.Id).ToHashSet();
        var ids = matches.Values.Select(x => x.Id).Where(x => !ownedIds.Contains(x)).Distinct().ToArray();
        var touched = await jobs.TouchAggregationMetadataAsync(ids, now, cancellationToken);

        if (touched == ids.Length)
        {
            foreach (var id in ids)
                _bulkTouchedJobIds.Add(id);
        }
    }

    public void ResetRunAfterFailure()
    {
        _ownedJobs.Clear();
        _preloadedSources.Clear();
        _canonicalUrlDuplicateCache.Clear();
        _bulkTouchedJobIds.Clear();
        _companyCache.Clear();
        _validCategories.Clear();
        _fingerprintMatches.Clear();
        _stagedMetadataRepairs.Clear();
        _exactBatchPrepared = false;
    }

    public async Task CompleteRunAsync()
    {
        try { if (_creationRun is not null) await _creationRun.DisposeAsync(); }
        finally { _creationRun = null; _runPrepared = false; ResetRunAfterFailure(); }
    }

    public async Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawJob);
        cancellationToken.ThrowIfCancellationRequested();

        var title = TextNormalizer.TrimOrNull(rawJob.Title);
        var companyName = TextNormalizer.TrimOrNull(rawJob.CompanyName);

        if (title is null) return Invalid("Job title is required.");
        if (title.Length > 250) return Invalid("Job title cannot exceed 250 characters.");
        if (companyName is null) return Invalid("Company name is required.");

        if (rawJob.SalaryMin < 0 || rawJob.SalaryMax < 0 || rawJob.SalaryMin > rawJob.SalaryMax)
            return Invalid("Salary range is invalid.");

        if (rawJob.MinimumExperienceYears < 0 ||
            rawJob.MaximumExperienceYears < 0 ||
            rawJob.MinimumExperienceYears > rawJob.MaximumExperienceYears)
            return Invalid("Experience range is invalid.");

        var location = TextNormalizer.TrimOrNull(rawJob.Location);
        var applicationUrl = TextNormalizer.TrimOrNull(rawJob.ApplicationUrl);

        if (applicationUrl is not null &&
            (!Uri.TryCreate(applicationUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https")))
            return Invalid("ApplicationUrl must be an absolute HTTP or HTTPS URL.");

        var companySlug = SlugGenerator.Generate(companyName);
        var company = await ResolveCompanyAsync(
            companyName,
            companySlug,
            rawJob.CompanyId,
            cancellationToken);

        if (company is null)
        {
            return new JobIngestionResult
            {
                Outcome = JobIngestionOutcome.CompanyNotFound,
                Message = $"Company '{companyName}' was not found.",
                ExplicitReasonCode = JobIngestionReasonCode.CompanyNotFound
            };
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Source identity takes precedence over general deduplication so a provider
        // can update only records it owns; URL/fingerprint matches remain untouched.
        var sourceExternalId = TextNormalizer.TrimOrNull(rawJob.ExternalId);
        if (rawJob.JobSourceId.HasValue && sourceExternalId is not null)
        {
            var owned = _ownedJobs.GetValueOrDefault((rawJob.JobSourceId.Value, sourceExternalId));
            if (owned is null && !_preloadedSources.Contains(rawJob.JobSourceId.Value))
                owned = await jobs.FindBySourceIdentityAsync(rawJob.JobSourceId.Value, sourceExternalId, cancellationToken);
            if (owned is not null)
            {
                owned = jobs.TrackAggregationJob(owned);
                var oldUrl = owned.ApplicationUrl;
                var changed = ApplySourceFields(owned, rawJob, title, company, location, applicationUrl);
                owned.LastSeenAtUtc = now;
                owned.FirstSeenAtUtc ??= now;
                if (changed) owned.UpdatedAtUtc = now;
                await PersistAsync(cancellationToken);
                if (_runPrepared)
                {
                    RememberSavedOwned(owned, oldUrl);
                    if (!_batchMode) jobs.ReleaseSavedAggregationTracking();
                }
                return new JobIngestionResult
                {
                    Outcome = changed ? JobIngestionOutcome.Updated : JobIngestionOutcome.Unchanged,
                    JobId = owned.Id,
                    Message = changed ? "Source-owned job updated." : "Source-owned job is unchanged.",
                    ExplicitReasonCode = JobIngestionReasonCode.None
                };
            }
        }

        var duplicate = await FindDuplicateAsync(
            title, company, location, applicationUrl, cancellationToken);

        await using var creationLease =
            duplicate.IsDuplicate || _batchMode
                ? null
                : await (_creationRun ?? creationLock).AcquireAsync(
                    applicationUrl is null ? null : canonicalizer.Canonicalize(applicationUrl),
                    fingerprintService.GenerateFingerprint(title, company.Name, location),
                    cancellationToken);

        if (creationLease is not null)
        {
            // Negative preload results are not authoritative across writers. Recheck under
            // the same creation locks before dedup/save; never trust cached absence here.
            if (rawJob.JobSourceId.HasValue && sourceExternalId is not null &&
                await jobs.FindBySourceIdentityAsync(rawJob.JobSourceId.Value, sourceExternalId, cancellationToken) is { } concurrentOwned)
            {
                var oldUrl = concurrentOwned.ApplicationUrl;
                var changed = ApplySourceFields(concurrentOwned, rawJob, title, company, location, applicationUrl);
                concurrentOwned.LastSeenAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                concurrentOwned.FirstSeenAtUtc ??= concurrentOwned.LastSeenAtUtc;
                await PersistAsync(cancellationToken);
                if (_runPrepared) RememberSavedOwned(concurrentOwned, oldUrl);
                if (_runPrepared && !_batchMode) jobs.ReleaseSavedAggregationTracking();
                return new JobIngestionResult { Outcome = changed ? JobIngestionOutcome.Updated : JobIngestionOutcome.Unchanged,
                    JobId = concurrentOwned.Id, ExplicitReasonCode = JobIngestionReasonCode.None };
            }
            duplicate = await deduplicationService.FindDuplicateAsync(
                title, company.Name, location, applicationUrl, company.Id, cancellationToken);
            now = timeProvider.GetUtcNow().UtcDateTime;
        }

        if (duplicate.IsDuplicate && duplicate.MatchedJob is not null)
        {
            var matchedSnapshot = duplicate.MatchedJob;

            // Normal fast path: established duplicates already have a canonical
            // fingerprint. Touch only aggregation timestamps directly in SQL.
            // No curated job fields are overwritten.
            if (!string.IsNullOrWhiteSpace(matchedSnapshot.FingerprintHash) &&
                (!_batchMode || (!_ownedJobs.Values.Any(x => x.Id == matchedSnapshot.Id) &&
                    !_stagedMetadataRepairs.Contains(matchedSnapshot.Id))))
            {
                if (_bulkTouchedJobIds.Contains(matchedSnapshot.Id))
                    return DuplicateResult(duplicate, matchedSnapshot.Id);

                var touched = await jobs.TouchAggregationMetadataAsync(
                    matchedSnapshot.Id, now, cancellationToken);

                if (touched > 0)
                    return DuplicateResult(duplicate, matchedSnapshot.Id);

                if (touched == 0)
                    return Invalid("Matched job is no longer available.");

                // touched == -1: repository/test fake does not implement the
                // optimized path. Continue through the original safe fallback.
            }

            // Legacy/fallback path. A missing fingerprint must be calculated from
            // the stored canonical job, not from potentially different fuzzy input.
            var matchedJob = _batchMode && !string.IsNullOrWhiteSpace(matchedSnapshot.FingerprintHash)
                ? jobs.TrackAggregationJob(matchedSnapshot) : await jobs.GetByIdAsync(
                matchedSnapshot.Id, cancellationToken: cancellationToken);

            if (matchedJob is null)
                return Invalid("Matched job is no longer available.");

            matchedJob.LastSeenAtUtc = now;

            if (matchedJob.FirstSeenAtUtc is null)
                matchedJob.FirstSeenAtUtc = now;

            if (string.IsNullOrWhiteSpace(matchedJob.FingerprintHash))
            {
                matchedJob.FingerprintHash = fingerprintService.GenerateFingerprint(
                    matchedJob.Title, matchedJob.Company.Name, matchedJob.Location);
                if (_batchMode) _stagedMetadataRepairs.Add(matchedJob.Id);
            }

            await PersistAsync(cancellationToken);
            if (_batchMode && matchedJob.FingerprintHash is not null)
                _fingerprintMatches.TryAdd(matchedJob.FingerprintHash, matchedJob);
            if (_runPrepared && !_batchMode) jobs.ReleaseSavedAggregationTracking();
            return DuplicateResult(duplicate, matchedJob.Id);
        }

        if (!rawJob.CategoryId.HasValue || rawJob.CategoryId.Value == Guid.Empty)
            return Invalid("CategoryId is required when creating a new external job.");

        if (!_validCategories.Contains(rawJob.CategoryId.Value) && !await categories.ExistsAsync(rawJob.CategoryId.Value, cancellationToken))
            return Invalid($"Category '{rawJob.CategoryId.Value}' does not exist.");
        if (_runPrepared) _validCategories.Add(rawJob.CategoryId.Value);

        var id = Guid.NewGuid();

        var job = new Job
        {
            Id = id,
            ReferenceNumber = $"JOB-{now:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            Title = title,
            Slug = $"{SlugGenerator.Generate(title, 240)}-{id.ToString("N")[..8]}",
            Description = TextNormalizer.TrimOrNull(rawJob.Description) ?? string.Empty,
            Responsibilities = TextNormalizer.TrimOrNull(rawJob.Responsibilities),
            Requirements = TextNormalizer.TrimOrNull(rawJob.Requirements),
            Benefits = TextNormalizer.TrimOrNull(rawJob.Benefits),
            ApplicationUrl = applicationUrl ?? string.Empty,
            CanonicalApplicationUrlHash = ApplicationUrlIdentity.Hash(applicationUrl),
            Location = location,
            ExpiresAtUtc = rawJob.ExpiresAtUtc is { Kind: DateTimeKind.Utc } ? rawJob.ExpiresAtUtc : null,
            MinimumSalary = rawJob.SalaryMin,
            MaximumSalary = rawJob.SalaryMax,
            EmploymentType = rawJob.EmploymentType ?? default,
            WorkplaceType = rawJob.WorkplaceType ?? default,
            ExperienceLevel = rawJob.ExperienceLevel ?? default,
            MinimumExperienceYears = rawJob.MinimumExperienceYears,
            MaximumExperienceYears = rawJob.MaximumExperienceYears,
            EducationRequirement = TextNormalizer.TrimOrNull(rawJob.EducationRequirement),
            CompanyId = company.Id,
            CategoryId = rawJob.CategoryId.Value,
            Status = JobStatus.Draft,
            FingerprintHash = fingerprintService.GenerateFingerprint(title, company.Name, location),
            FirstSeenAtUtc = now,
            LastSeenAtUtc = now,
            JobSourceId = rawJob.JobSourceId,
            ExternalJobId = sourceExternalId,
            SourcePostedAtUtc = rawJob.SourcePostedAtUtc is { Kind: DateTimeKind.Utc } posted ? posted : null
        };

        await jobs.AddAsync(job, cancellationToken);
        if (_batchMode) _fingerprintMatches.TryAdd(job.FingerprintHash!, job);
        await PersistAsync(cancellationToken);
        if (_runPrepared && job.JobSourceId.HasValue && sourceExternalId is not null)
            RememberSavedOwned(job, null);
        if (_runPrepared && !_batchMode) jobs.ReleaseSavedAggregationTracking();

        return new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Created,
            JobId = job.Id,
            Message = "External job created as Draft.",
            ExplicitReasonCode = JobIngestionReasonCode.None
        };
    }

    private bool ApplySourceFields(Job job, RawExternalJob raw, string title, Company company,
        string? location, string? applicationUrl)
    {
        var description = TextNormalizer.TrimOrNull(raw.Description) ?? string.Empty;
        var responsibilities = TextNormalizer.TrimOrNull(raw.Responsibilities);
        var requirements = TextNormalizer.TrimOrNull(raw.Requirements);
        var benefits = TextNormalizer.TrimOrNull(raw.Benefits);
        // Missing provider metadata is not evidence that an existing deadline was withdrawn.
        // Do not erase reviewed expiry or make an expired published job visible again.
        DateTime? expiry = raw.ExpiresAtUtc is { Kind: DateTimeKind.Utc } expires ? expires : job.ExpiresAtUtc;
        DateTime? sourcePosted = raw.SourcePostedAtUtc is { Kind: DateTimeKind.Utc } posted ? posted : null;
        var education = TextNormalizer.TrimOrNull(raw.EducationRequirement);
        var canonicalUrlHash = ApplicationUrlIdentity.Hash(applicationUrl);
        var fingerprint = fingerprintService.GenerateFingerprint(title, company.Name, location);
        var slug = $"{SlugGenerator.Generate(title, 240)}-{job.Id.ToString("N")[..8]}";
        var changed = job.Title != title || job.Description != description || job.Responsibilities != responsibilities ||
            job.Requirements != requirements || job.Benefits != benefits || job.ApplicationUrl != (applicationUrl ?? string.Empty) ||
            job.CanonicalApplicationUrlHash != canonicalUrlHash || job.FingerprintHash != fingerprint || job.Slug != slug ||
            job.Location != location || job.ExpiresAtUtc != expiry || job.MinimumSalary != raw.SalaryMin ||
            job.MaximumSalary != raw.SalaryMax || job.EmploymentType != (raw.EmploymentType ?? default) ||
            job.WorkplaceType != (raw.WorkplaceType ?? default) || job.ExperienceLevel != (raw.ExperienceLevel ?? default) ||
            job.MinimumExperienceYears != raw.MinimumExperienceYears || job.MaximumExperienceYears != raw.MaximumExperienceYears ||
            job.EducationRequirement != education || (raw.CategoryId.HasValue && raw.CategoryId.Value != Guid.Empty && job.CategoryId != raw.CategoryId.Value) ||
            job.SourcePostedAtUtc != sourcePosted || job.CompanyId != company.Id;

        job.Title = title;
        job.Slug = slug;
        job.Description = description;
        job.Responsibilities = responsibilities;
        job.Requirements = requirements;
        job.Benefits = benefits;
        job.ApplicationUrl = applicationUrl ?? string.Empty;
        job.CanonicalApplicationUrlHash = canonicalUrlHash;
        job.FingerprintHash = fingerprint;
        job.Location = location;
        job.ExpiresAtUtc = expiry;
        job.MinimumSalary = raw.SalaryMin;
        job.MaximumSalary = raw.SalaryMax;
        job.EmploymentType = raw.EmploymentType ?? default;
        job.WorkplaceType = raw.WorkplaceType ?? default;
        job.ExperienceLevel = raw.ExperienceLevel ?? default;
        job.MinimumExperienceYears = raw.MinimumExperienceYears;
        job.MaximumExperienceYears = raw.MaximumExperienceYears;
        job.EducationRequirement = education;
        job.SourcePostedAtUtc = sourcePosted;
        if (raw.CategoryId.HasValue && raw.CategoryId.Value != Guid.Empty)
            job.CategoryId = raw.CategoryId.Value;
        job.CompanyId = company.Id;
        return changed;
    }

    private void RememberSavedOwned(Job job, string? oldUrl)
    {
        if (_batchMode)
            foreach (var oldFingerprint in _fingerprintMatches.Where(x => x.Value.Id == job.Id && x.Key != job.FingerprintHash)
                .Select(x => x.Key).ToArray()) _fingerprintMatches.Remove(oldFingerprint);
        if (_batchMode && job.FingerprintHash is not null) _fingerprintMatches.TryAdd(job.FingerprintHash, job);
        _ownedJobs[(job.JobSourceId!.Value, job.ExternalJobId!)] = job;
        var oldKey = oldUrl is null ? null : canonicalizer.Canonicalize(oldUrl);
        var newKey = canonicalizer.Canonicalize(job.ApplicationUrl);
        if (oldKey is not null && oldKey != newKey &&
            _canonicalUrlDuplicateCache.TryGetValue(oldKey, out var oldMatch) && oldMatch.Id == job.Id)
            _canonicalUrlDuplicateCache.Remove(oldKey);
        if (!string.IsNullOrWhiteSpace(newKey)) _canonicalUrlDuplicateCache[newKey] = job;
    }

    private async Task<DeduplicationResult> FindDuplicateAsync(
        string title,
        Company company,
        string? location,
        string? applicationUrl,
        CancellationToken cancellationToken)
    {
        if (applicationUrl is not null)
        {
            var canonicalUrl = canonicalizer.Canonicalize(applicationUrl);

            if (!string.IsNullOrWhiteSpace(canonicalUrl) &&
                _canonicalUrlDuplicateCache.TryGetValue(canonicalUrl, out var cachedJob))
            {
                return DeduplicationResult.SourceUrlMatch(cachedJob);
            }
        }

        if (_exactBatchPrepared)
        {
            var fingerprint = fingerprintService.GenerateFingerprint(title, company.Name, location);
            if (_fingerprintMatches.TryGetValue(fingerprint, out var matched))
                return DeduplicationResult.FingerprintMatch(matched);
            return await ((IFuzzyJobDeduplicationService)deduplicationService)
                .FindFuzzyDuplicateAsync(title, company.Name, location, company.Id, cancellationToken);
        }
        if (_batchMode)
            return await deduplicationService.FindDuplicateAsync(title, company.Name, location, applicationUrl, company.Id, cancellationToken);

        // Bulk URL hits are safe positives. For misses, skip the redundant unlocked
        // three-query pass: the full fresh dedup check still runs while holding the lock.
        if (_runPrepared) return DeduplicationResult.NoMatch();

        var duplicate = await deduplicationService.FindDuplicateAsync(
            title,
            company.Name,
            location,
            applicationUrl,
            company.Id,
            cancellationToken);

        if (duplicate.IsDuplicate &&
            duplicate.MatchedJob is not null &&
            duplicate.MatchTypeEnum == JobPortal.Application.Abstractions.Jobs.MatchType.SourceUrl &&
            applicationUrl is not null)
        {
            var canonicalUrl = canonicalizer.Canonicalize(applicationUrl);

            if (!string.IsNullOrWhiteSpace(canonicalUrl))
                _canonicalUrlDuplicateCache[canonicalUrl] = duplicate.MatchedJob;
        }

        return duplicate;
    }

    private async Task<Company?> ResolveCompanyAsync(
        string companyName,
        string companySlug,
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{companyId}\u001F{companyName}\u001F{companySlug}";

        if (_companyCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var company = companyId.HasValue
            ? await companies.GetByIdAsync(companyId.Value, cancellationToken)
            : await companies.FindByNameOrSlugAsync(companyName, companySlug, cancellationToken);

        if (company is not null)
            _companyCache[cacheKey] = company;

        return company;
    }

    private static JobIngestionResult DuplicateResult(DeduplicationResult duplicate, Guid jobId) =>
        new()
        {
            ExplicitReasonCode = ToReasonCode(duplicate.MatchTypeEnum),
            Outcome = ToOutcome(duplicate.MatchTypeEnum),
            JobId = jobId,
            Message = duplicate.MatchTypeEnum == JobPortal.Application.Abstractions.Jobs.MatchType.Fuzzy && duplicate.SimilarityScore.HasValue
                ? $"Matched existing job by fuzzy similarity ({duplicate.SimilarityScore.Value:F3})."
                : $"Matched existing job by {duplicate.MatchTypeEnum}."
        };

    private static JobIngestionResult Invalid(string message) =>
        new()
        {
            Outcome = JobIngestionOutcome.Invalid,
            Message = message,
            ExplicitReasonCode = JobIngestionReasonCodes.FromOutcome(JobIngestionOutcome.Invalid, message)
        };

    private static JobIngestionReasonCode ToReasonCode(JobPortal.Application.Abstractions.Jobs.MatchType matchType) =>
        matchType switch
        {
            JobPortal.Application.Abstractions.Jobs.MatchType.SourceUrl => JobIngestionReasonCode.DuplicateCanonicalUrl,
            JobPortal.Application.Abstractions.Jobs.MatchType.Fingerprint => JobIngestionReasonCode.DuplicateFingerprint,
            JobPortal.Application.Abstractions.Jobs.MatchType.Fuzzy => JobIngestionReasonCode.DuplicateFuzzyMatch,
            _ => JobIngestionReasonCode.Unknown
        };

    private static JobIngestionOutcome ToOutcome(JobPortal.Application.Abstractions.Jobs.MatchType matchType) =>
        matchType switch
        {
            JobPortal.Application.Abstractions.Jobs.MatchType.SourceUrl => JobIngestionOutcome.MatchedByUrl,
            JobPortal.Application.Abstractions.Jobs.MatchType.Fingerprint => JobIngestionOutcome.MatchedByFingerprint,
            JobPortal.Application.Abstractions.Jobs.MatchType.Fuzzy => JobIngestionOutcome.MatchedByFuzzy,
            _ => JobIngestionOutcome.Failed
        };
}
