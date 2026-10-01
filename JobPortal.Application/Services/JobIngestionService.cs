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
    IUrlCanonicalizer canonicalizer) : IJobIngestionService, IBulkJobIngestionService
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

    public async Task PrepareRunAsync(
        IReadOnlyCollection<RawExternalJob> rawJobs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        var ids = matches.Values.Select(x => x.Id).Distinct().ToArray();
        var touched = await jobs.TouchAggregationMetadataAsync(ids, now, cancellationToken);

        if (touched >= 0)
        {
            foreach (var id in ids)
                _bulkTouchedJobIds.Add(id);
        }
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

        var duplicate = await FindDuplicateAsync(
            title, company, location, applicationUrl, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var creationLease =
            duplicate.IsDuplicate
                ? null
                : await creationLock.AcquireAsync(
                    applicationUrl is null ? null : canonicalizer.Canonicalize(applicationUrl),
                    fingerprintService.GenerateFingerprint(title, company.Name, location),
                    cancellationToken);

        if (creationLease is not null)
        {
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
            if (!string.IsNullOrWhiteSpace(matchedSnapshot.FingerprintHash))
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
            var matchedJob = await jobs.GetByIdAsync(
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
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DuplicateResult(duplicate, matchedJob.Id);
        }

        if (!rawJob.CategoryId.HasValue || rawJob.CategoryId.Value == Guid.Empty)
            return Invalid("CategoryId is required when creating a new external job.");

        if (!await categories.ExistsAsync(rawJob.CategoryId.Value, cancellationToken))
            return Invalid($"Category '{rawJob.CategoryId.Value}' does not exist.");

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
            Company = company,
            CategoryId = rawJob.CategoryId.Value,
            Status = JobStatus.Draft,
            FingerprintHash = fingerprintService.GenerateFingerprint(title, company.Name, location),
            FirstSeenAtUtc = now,
            LastSeenAtUtc = now
        };

        await jobs.AddAsync(job, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Created,
            JobId = job.Id,
            Message = "External job created as Draft.",
            ExplicitReasonCode = JobIngestionReasonCode.None
        };
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
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{companyName}\u001F{companySlug}";

        if (_companyCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var company = await companies.FindByNameOrSlugAsync(
            companyName,
            companySlug,
            cancellationToken);

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
