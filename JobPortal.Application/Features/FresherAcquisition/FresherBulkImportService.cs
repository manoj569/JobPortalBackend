using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Enums;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.FresherAcquisition;

public interface IFresherBulkImportService
{
    Task<FresherBulkImportResponse> ProcessStagedAsync(FresherBulkImportRequest request, CancellationToken cancellationToken);
    Task<FresherBulkImportResponse> ImportAsync(FresherBulkImportRequest request, CancellationToken cancellationToken = default);
}

public sealed partial class FresherBulkImportService(
    IJobIngestionService ingestion,
    ILogger<FresherBulkImportService> logger,
    IFresherAcquisitionStore? acquisitionStore = null,
    IJobAutoPublishService? autoPublish = null,
    TimeProvider? clock = null,
    ICategoryManagementRepository? categories = null,
    IOptions<FresherAcquisitionOptions>? options = null) : IFresherBulkImportService
{
    public async Task<FresherBulkImportResponse> ImportAsync(FresherBulkImportRequest request, CancellationToken cancellationToken = default)
        => await ExecuteAsync(request, false, cancellationToken);

    public Task<FresherBulkImportResponse> ProcessStagedAsync(FresherBulkImportRequest request, CancellationToken cancellationToken)
        => ExecuteAsync(request, true, cancellationToken);

    private async Task<FresherBulkImportResponse> ExecuteAsync(FresherBulkImportRequest request, bool runLockOwned, CancellationToken cancellationToken)
    {
        var diagnostic = new ImportDiagnostic();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = diagnostic.CorrelationId,
            ["ImportId"] = request.ImportId, ["SourceType"] = request.SourceType, ["JobCount"] = request.Jobs.Count
        });
        try
        {
            return acquisitionStore is null
                ? await ImportCoreAsync(request, diagnostic, runLockOwned, cancellationToken)
                : await acquisitionStore.ExecuteImportAsync(token => ImportCoreAsync(request, diagnostic, runLockOwned, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not ImportIdPayloadConflictException and not ImportAlreadyRunningException)
        {
            var provider = exception;
            while (provider.InnerException is not null && provider is not System.Data.Common.DbException)
                provider = provider.InnerException;
            // Do not log exception messages, provider Detail, payloads or headers.
            ImportFailed(logger, diagnostic.CorrelationId, request.ImportId, request.SourceType, request.Jobs.Count,
                diagnostic.Stage, diagnostic.ExternalId, exception.GetType().Name,
                (provider as System.Data.Common.DbException)?.SqlState);
            throw;
        }
        finally { PhaseDuration(logger, request.ImportId, "BulkImportTotal", elapsed.ElapsedMilliseconds); }
    }

    private sealed class PhaseTiming(ILogger logger, string importId, string phase) : IDisposable
    {
        private readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        public void Dispose() => PhaseDuration(logger, importId, phase, elapsed.ElapsedMilliseconds);
    }

    private sealed class ImportDiagnostic
    {
        public string CorrelationId { get; } = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        public string Stage { get; set; } = "SourceResolution";
        public string? ExternalId { get; set; }
    }

    private async Task<FresherBulkImportResponse> ImportCoreAsync(FresherBulkImportRequest request,
        ImportDiagnostic diagnostic, bool runLockOwned, CancellationToken cancellationToken)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        JobPortal.Domain.Entities.AcquisitionSource? source = null;
        JobPortal.Domain.Entities.AcquisitionImportRun? run = null;
        if (acquisitionStore is not null)
        {
            source = await acquisitionStore.GetEnabledSourceAsync(request.SourceType.Trim().ToLowerInvariant(), cancellationToken);
            if (source is null)
                return new(request.ImportId, request.Jobs.Count, 0, 0, 0, 0, 0, 0, 0, 0, request.Jobs.Count,
                    Enumerable.Range(0, request.Jobs.Count).Select(i => new FresherImportItemOutcome(i, "Failed", "ACQUISITION_SOURCE_DISABLED")).ToArray());
        }
        var provider = (source?.ProviderKey ?? request.SourceType.Trim()).ToLowerInvariant();
        var talentd = provider == "talentd";
        var website = provider is "website-crawler" or "talentd";
        var requirePublication = talentd || website && request.Jobs.Any(candidate => MetadataBoolean(candidate, "autoPublishRequired"));
        if (requirePublication && (source is null || !source.AutoPublishEnabled || autoPublish is null))
        {
            var reason = source is not null && !source.AutoPublishEnabled ? "ACQUISITION_AUTOPUBLISH_DISABLED" : "ACQUISITION_PUBLICATION_UNAVAILABLE";
            return new(request.ImportId, request.Jobs.Count, 0, 0, 0, 0, 0, 0, 0, 0, request.Jobs.Count,
                Enumerable.Range(0, request.Jobs.Count).Select(i => new FresherImportItemOutcome(i, "Failed", reason)).ToArray());
        }
        // Preserve the original request/fingerprint. Website fallback IDs are mapping values only.
        var identityKeys = request.Jobs.Select(x => FresherCandidateIdentity.Create(source?.Id ?? Guid.Empty, x, website)).ToArray();
        var requestFingerprint = FresherCandidateIdentity.Fingerprint(identityKeys);
        IReadOnlyDictionary<string, FresherImportReceiptSnapshot> priorReceipts = new Dictionary<string, FresherImportReceiptSnapshot>();
        await using var runLock = new AsyncDisposableHolder();
        if (acquisitionStore is not null)
        {
            diagnostic.Stage = "RunLock";
            if (!runLockOwned) runLock.Set(await acquisitionStore.AcquireRunLockAsync(source!.Id, request.ImportId, cancellationToken));
            diagnostic.Stage = "StartRun";
            var start = await acquisitionStore.StartPayloadRunAsync(source!.Id, request.ImportId, diagnostic.CorrelationId,
                FresherCandidateIdentity.PayloadFingerprint(request), requestFingerprint,
                identityKeys.Distinct(StringComparer.Ordinal).Count(), cancellationToken);
            run = start.Run;
            if (start.IsReplay && run.Status == "Completed")
            {
                var completed = await acquisitionStore.GetReplayResponseAsync(run.Id, request.ImportId, cancellationToken)
                    ?? throw new InvalidOperationException("Completed acquisition run has no replay response.");
                var completedItems = await acquisitionStore.GetReceiptsAsync(run.Id, cancellationToken);
                return completed with { Items = MapCurrentRequestItems(identityKeys, completedItems) };
            }
            if (start.IsReplay) priorReceipts = await acquisitionStore.GetReceiptsAsync(run.Id, cancellationToken);
        }
        var categoryCache = new Dictionary<string, JobPortal.Domain.Entities.Category?>(StringComparer.OrdinalIgnoreCase);
        async Task<JobPortal.Domain.Entities.Category?> FindCategory(string name)
        {
            if (!categoryCache.TryGetValue(name, out var category))
            {
                category = categories is null ? null : await categories.FindByNameOrSlugAsync(name, SlugGenerator.Generate(name), cancellationToken);
                categoryCache[name] = category;
            }
            return category;
        }
        var outcomes = new List<FresherImportItemOutcome>(request.Jobs.Count);
        var geoRejected = 0; var experienceRejected = 0; var invalidData = 0; var invalidUrl = 0;
        var created = 0; var updated = 0; var existing = 0; var failed = 0; var accepted = 0; var published = 0;
        var pendingPublication = false;
        var seenInRequest = new HashSet<string>(StringComparer.Ordinal);
        var chunkSize = website ? 1 : acquisitionStore is null ? request.Jobs.Count : Math.Clamp(options?.Value.ChunkSize ?? 50, 1, 200);
        for (var chunkStart = 0; chunkStart < request.Jobs.Count; chunkStart += chunkSize)
        {
            // A completed website receipt needs no empty transaction or source lookup.
            if (website && priorReceipts.TryGetValue(identityKeys[chunkStart], out var completedReceipt) &&
                completedReceipt.PublicationStatus is not ("Pending" or "Failed"))
            {
                outcomes.Add(completedReceipt.Outcome with { Index = chunkStart });
                seenInRequest.Add(identityKeys[chunkStart]);
                continue;
            }
            var committed = false;
            var beforeCount = outcomes.Count;
            var beforeCounts = (geoRejected, experienceRejected, invalidData, invalidUrl, created, updated, existing, failed, accepted, published);
            try
            {
            diagnostic.Stage = "BeginChunk";
            diagnostic.ExternalId = null;
            await using var chunk = run is not null && acquisitionStore is not null
                ? await acquisitionStore.BeginChunkAsync(request.Jobs.Skip(chunkStart).Take(chunkSize)
                    .Where(x => !string.IsNullOrWhiteSpace(x?.ExternalJobId))
                    .Select(x => $"acquisition-job:{source!.Id:N}:{FresherCandidateIdentity.Normalize(x!.ExternalJobId)}")
                    .Concat(request.Jobs.Skip(chunkStart).Take(chunkSize).Where(x => x is not null)
                        .SelectMany(x => FresherCompanyIdentity.Locks(x!))).ToArray(), cancellationToken) : null;
            var chunkReceipts = new List<FresherReceiptInput>();
            var publicationQueue = new List<(int Index, string Identity, Guid JobId, JobIngestionOutcome Outcome)>();
            var chunkEnd = Math.Min(chunkStart + chunkSize, request.Jobs.Count);
            void RecordReceipt(int index, FresherImportCandidate candidate)
            {
                chunkReceipts.Add(new(index, candidate, identityKeys[index], talentd ? "ContentNeutral" : SafeEligibility(candidate), outcomes[^1], now));
            }
            for (var index = chunkStart; index < chunkEnd; index++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = request.Jobs[index];
            using var itemTiming = new PhaseTiming(logger, request.ImportId, "BulkImportItem");
            diagnostic.Stage = "CandidateValidation";
            diagnostic.ExternalId = candidate?.ExternalJobId;
            var identityKey = identityKeys[index];
            if (acquisitionStore is not null && !seenInRequest.Add(identityKey))
            {
                outcomes.Add(new(index, "Duplicate", "DUPLICATE_IN_REQUEST"));
                continue;
            }
            if (priorReceipts.TryGetValue(identityKey, out var prior))
            {
                outcomes.Add(prior.Outcome with { Index = index });
                if (prior.PublicationStatus is "Pending" or "Failed" && prior.Outcome.JobId is { } retryJobId &&
                    autoPublish is not null && source?.AutoPublishEnabled == true)
                    publicationQueue.Add((index, identityKey, retryJobId, JobIngestionOutcome.Unchanged));
                continue;
            }
            if (candidate is null)
            {
                invalidData++;
                outcomes.Add(new(index, "RejectedInvalidData", "CANDIDATE_REQUIRED"));
                chunkReceipts.Add(new(index, null, identityKey, "Unknown", outcomes[^1], now));
                continue;
            }
            var dataError = ValidateCandidate(candidate, website, talentd);
            if (dataError is null && talentd && (string.IsNullOrWhiteSpace(candidate.ExternalJobId) || !FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(candidate.DiscoveredUrl)))
                dataError = "TALENTD_IDENTITY_REQUIRED";
            if (dataError is null && talentd && new[] { candidate.ApplyUrl, candidate.CanonicalJobUrl }
                .Any(url => url is not null && !FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(url)))
                dataError = "TALENTD_UNSAFE_URL";
            if (dataError is null && request.SourceType.Trim().Equals("website-crawler", StringComparison.OrdinalIgnoreCase))
                dataError = ValidateWebsiteCandidate(candidate, now);
            if (dataError is not null)
            {
                invalidData++;
                outcomes.Add(new(index, "RejectedInvalidData", dataError));
                RecordReceipt(index, candidate);
                continue;
            }
            var decision = FresherAcquisitionEligibilityPolicy.Evaluate(ToPolicyCandidate(candidate));
            if (!website && decision.Geography is not (FresherGeographyDecision.IndiaEligible or FresherGeographyDecision.GlobalRemoteEligible))
            {
                geoRejected++;
                outcomes.Add(new(index, "RejectedGeography", decision.GeographyReasonCode));
                RecordReceipt(index, candidate);
                continue;
            }
            if (!website && decision.Experience != FresherExperienceDecision.Eligible)
            {
                experienceRejected++;
                outcomes.Add(new(index, "RejectedExperience", decision.ExperienceReasonCode));
                RecordReceipt(index, candidate);
                continue;
            }
            var selectedUrl = SelectApplyUrl(candidate);
            if (selectedUrl is null)
            {
                invalidUrl++;
                outcomes.Add(new(index, "RejectedInvalidUrl", "NO_SAFE_APPLICATION_URL"));
                RecordReceipt(index, candidate);
                continue;
            }
            if (!website && (string.IsNullOrWhiteSpace(candidate.CompanyName) || string.IsNullOrWhiteSpace(candidate.Title) ||
                string.IsNullOrWhiteSpace(candidate.Description)))
            {
                failed++;
                outcomes.Add(new(index, "RejectedInvalidData", "REQUIRED_FIELD_MISSING"));
                RecordReceipt(index, candidate);
                continue;
            }

            diagnostic.Stage = "CategoryResolution";
            Guid? categoryId = candidate.CategoryId;
            if (categoryId.HasValue && (categories is null || await categories.GetByIdAsync(categoryId.Value, cancellationToken) is null))
            {
                if (website) categoryId = null;
                else
                {
                    invalidData++;
                    outcomes.Add(new(index, "RejectedInvalidData", "CATEGORY_RESOLUTION_FAILED"));
                    RecordReceipt(index, candidate);
                    continue;
                }
            }
            if (!categoryId.HasValue && !string.IsNullOrWhiteSpace(candidate.Category))
            {
                var categoryName = candidate.Category.Trim();
                var matchedCategory = await FindCategory(categoryName);
                if (matchedCategory is null && !website)
                {
                    invalidData++;
                    outcomes.Add(new(index, "RejectedInvalidData", "CATEGORY_RESOLUTION_FAILED"));
                    RecordReceipt(index, candidate);
                    continue;
                }
                categoryId = matchedCategory?.Id;
            }

            if (website && categoryId is null && categories is not null)
            {
                var categoryName = WebsiteCategory(candidate.Title);
                if (categoryName is not null)
                    categoryId = (await FindCategory(categoryName))?.Id;
            }
            try
            {
                diagnostic.Stage = "CompanyResolution";
                var companyId = candidate.CompanyId;
                if (acquisitionStore is not null)
                {
                    using var companyTiming = new PhaseTiming(logger, request.ImportId, "CompanyResolution");
                    var company = website ? await acquisitionStore.ResolveWebsiteCompanyAsync(candidate, cancellationToken)
                        : await acquisitionStore.ResolveCompanyAsync(candidate, cancellationToken);
                    if (company.CompanyId is null)
                    {
                        if (website && company.ReasonCode is "COMPANY_OWNER_UNAVAILABLE" or "COMPANY_CREATION_CONFLICT")
                        {
                            // Do not freeze a recoverable infrastructure failure
                            // into an immutable terminal candidate receipt.
                            failed++;
                            outcomes.Add(new(index, "Failed", "ITEM_NOT_COMMITTED"));
                            CompanyResolutionDeferred(logger, request.ImportId, index, company.ReasonCode);
                            continue;
                        }
                        failed++;
                        outcomes.Add(new(index, "Failed", company.ReasonCode ?? "COMPANY_NOT_RESOLVED"));
                        RecordReceipt(index, candidate);
                        continue;
                    }
                    companyId = company.CompanyId;
                    if (talentd && (candidate.CompanyLogoUrl ?? TalentdFields.Text(candidate, "logo")) is { } logo)
                        await acquisitionStore.PopulateMissingLogoAsync(companyId.Value, logo, cancellationToken);
                }
                var countryCodes = website && candidate.Country?.Trim() is { Length: 2 } suppliedCountry && suppliedCountry.All(char.IsAsciiLetter)
                    ? new[] { suppliedCountry.ToUpperInvariant() }
                    : !website && decision.Geography == FresherGeographyDecision.GlobalRemoteEligible
                    ? new[] { "IN" }
                    : string.Equals(candidate.Country, "India", StringComparison.OrdinalIgnoreCase) ? new[] { "IN" } : Array.Empty<string>();
                var metadataValues = new Dictionary<string, object?>
                {
                    ["importId"] = request.ImportId,
                    ["sourceType"] = source?.ProviderKey ?? request.SourceType,
                    ["sourceName"] = source?.Name ?? request.SourceName,
                    ["eligibility"] = new { decision.Geography, decision.GeographyReasonCode, decision.Experience, decision.ExperienceReasonCode },
                    ["countryCodes"] = countryCodes,
                    ["remoteEligibility"] = candidate.RemoteEligibility,
                    ["workplaceType"] = candidate.WorkplaceType
                };
                if (request.SourceType.Trim().Equals("website-crawler", StringComparison.OrdinalIgnoreCase))
                    metadataValues["discovery"] = new
                    {
                        provider = "website-crawler",
                        sourceDomain = new Uri(candidate.DiscoveredUrl!).Host.ToLowerInvariant(),
                        extractionConfidence = MetadataText(candidate, "extractionConfidence"),
                        applyClassification = MetadataText(candidate, "applyClassification"),
                        destinationVerified = candidate.SourceMetadata is { ValueKind: JsonValueKind.Object } md && md.TryGetProperty("destinationVerified", out var dv) && dv.ValueKind == JsonValueKind.True,
                        extracted = candidate.SourceMetadata
                    };
                if (talentd)
                    metadataValues["discovery"] = new { provider = "talentd", extracted = candidate.SourceMetadata };
                var metadata = JsonSerializer.Serialize(metadataValues);
                var raw = new RawExternalJob
                {
                    Title = candidate.Title,
                    CompanyName = candidate.CompanyName ?? string.Empty,
                    CompanyId = companyId,
                    CompanyWebsiteUrl = candidate.CompanyWebsiteUrl,
                    Location = ComposeLocation(candidate, website),
                    CountryCodes = countryCodes,
                    RemoteEligibilityText = candidate.RemoteEligibility ??
                        (!talentd && decision.Geography == FresherGeographyDecision.GlobalRemoteEligible ? "Remote Worldwide; India eligible" : null),
                    Description = candidate.Description,
                    DescriptionIsHtml = talentd,
                    ApplicationUrl = selectedUrl,
                    // Keep employer ATS identity in its own field; acquisition IDs have a separate Job column.
                    ExternalId = source is null ? candidate.ExternalJobId : null,
                    AcquisitionSourceId = source?.Id,
                    AcquisitionProviderKey = source?.ProviderKey,
                    PreviousAcquisitionExternalId = website ? MetadataText(candidate, "legacyExternalId") : null,
                    AcquisitionExternalId = candidate.ExternalJobId is null ? (website ? FresherCandidateIdentity.WebsiteFallback(candidate) : null) : FresherCandidateIdentity.Normalize(candidate.ExternalJobId),
                    DiscoveredUrl = SafeUrlOrNull(candidate.DiscoveredUrl),
                    CanonicalJobUrl = SafeUrlOrNull(candidate.CanonicalJobUrl),
                    LastVerifiedAtUtc = candidate.SourceLastSeenAt?.ToUniversalTime(),
                    SourcePostedAtUtc = candidate.PostedAt?.ToUniversalTime(),
                    ExpiresAtUtc = candidate.ExpiresAt?.ToUniversalTime(),
                    EmploymentType = MapEmployment(candidate.EmploymentType),
                    WorkplaceType = MapWorkplace(candidate.WorkplaceType),
                    WorkplaceTypeText = candidate.WorkplaceType,
                    ExperienceLevel = website ? ExperienceLevel.Unknown : ExperienceLevel.Entry,
                    MinimumExperienceYears = website ? candidate.MinimumExperienceYears : candidate.MinimumExperienceYears ?? 0,
                    MaximumExperienceYears = candidate.MaximumExperienceYears,
                    EducationRequirement = talentd ? TalentdFields.Education(candidate) : website ? null : candidate.ExperienceClassification,
                    ExternalCategory = candidate.Category,
                    CategoryId = categoryId,
                    Skills = candidate.Skills.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Responsibilities = talentd ? TalentdFields.Text(candidate, "responsibilities") : null,
                    Requirements = talentd ? TalentdFields.Text(candidate, "qualifications") : null,
                    Benefits = talentd ? TalentdFields.Text(candidate, "benefits") : null,
                    SalaryMin = talentd ? TalentdFields.Salary(candidate, "minValue") : null,
                    SalaryMax = talentd ? TalentdFields.Salary(candidate, "maxValue") : null,
                    CurrencyCode = talentd ? TalentdFields.Currency(candidate) : null,
                    SalaryPeriod = talentd ? TalentdFields.Period(candidate) : null,
                    SourceMetadataJson = metadata
                };
                diagnostic.Stage = "JobIngestion";
                using var persistenceTiming = new PhaseTiming(logger, request.ImportId, "Ingestion");
                var result = run is not null
                    ? await ingestion.IngestWithoutSaveAsync(raw, cancellationToken)
                    : await ingestion.IngestAsync(raw, cancellationToken);
                var publicationRequested = result.JobId.HasValue && source?.AutoPublishEnabled == true && autoPublish is not null &&
                    result.Outcome is JobIngestionOutcome.Created or JobIngestionOutcome.Updated or JobIngestionOutcome.Unchanged;
                if (publicationRequested && result.JobId is { } persistedJobId)
                    publicationQueue.Add((index, identityKey, persistedJobId, result.Outcome));
                switch (result.Outcome)
                {
                    case JobIngestionOutcome.Created: created++; accepted++; outcomes.Add(new(index, "Created", "CREATED", result.JobId)); break;
                    case JobIngestionOutcome.Updated: updated++; accepted++; outcomes.Add(new(index, "Updated", "UPDATED", result.JobId)); break;
                    case JobIngestionOutcome.Unchanged: existing++; accepted++; outcomes.Add(new(index, "Existing", "UNCHANGED", result.JobId)); break;
                    case JobIngestionOutcome.MatchedByUrl:
                    case JobIngestionOutcome.MatchedByFingerprint:
                    case JobIngestionOutcome.MatchedByFuzzy: existing++; accepted++; outcomes.Add(new(index, "Duplicate", result.ReasonCode.ToString(), result.JobId)); break;
                    case JobIngestionOutcome.CompanyNotFound:
                        failed++; outcomes.Add(new(index, "Failed", "COMPANY_NOT_RESOLVED")); break;
                    default:
                        failed++; outcomes.Add(new(index, "Failed", result.ReasonCode.ToString())); break;
                }
                chunkReceipts.Add(new(index, candidate, identityKey, talentd ? "ContentNeutral" : SafeEligibility(candidate), outcomes[^1], now,
                    publicationRequested));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                ItemImportFailed(logger, request.ImportId, index, exception.GetType().Name);
                throw;
            }
            }
            diagnostic.Stage = "ChunkCommit";
            diagnostic.ExternalId = null;
            if (chunk is not null && run is not null)
            {
                using var commitTiming = new PhaseTiming(logger, request.ImportId, "PersistenceCommit");
                await chunk.CommitAsync(run.Id, chunkReceipts, cancellationToken);
            }
            committed = chunk is not null;

            // Network/publication side effects happen only after the job+receipt commit.
            foreach (var queued in publicationQueue)
            {
                try
                {
                    using var publicationTiming = new PhaseTiming(logger, request.ImportId, "Publication");
                    diagnostic.Stage = "Publication";
                    JobAutoPublishResult result;
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            result = await autoPublish!.TryPublishAsync(queued.JobId, cancellationToken);
                            break;
                        }
                        catch (Exception) when (website && attempt < 2 && !cancellationToken.IsCancellationRequested)
                        {
                            // Retry publication of this committed job, never ingestion.
                            await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)), cancellationToken);
                        }
                    }
                    var status = result.Outcome is JobAutoPublishOutcome.Published or JobAutoPublishOutcome.AlreadyPublished ? "Published" : "NotPublished";
                    if (requirePublication && (result.Outcome == JobAutoPublishOutcome.Disabled || talentd && status != "Published"))
                    {
                        status = "Pending";
                        pendingPublication = true;
                    }
                    await acquisitionStore!.UpdatePublicationAsync(run!.Id, queued.Identity, status, null, cancellationToken);
                    if (status == "Published") published++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    ItemImportFailed(logger, request.ImportId, queued.Index, exception.GetType().Name);
                    pendingPublication |= requirePublication;
                    await acquisitionStore!.UpdatePublicationAsync(run!.Id, queued.Identity, "Failed", exception.GetType().Name, cancellationToken);
                }
            }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (website)
            {
                if (committed)
                {
                    // The durable job/receipt must not be relabeled uncommitted
                    // when a later publication-status write fails.
                    pendingPublication |= requirePublication;
                    ItemImportFailed(logger, request.ImportId, chunkStart, exception.GetType().Name);
                    continue;
                }
                // Each website item has its own transaction. Rollback/disposal has
                // cleared tracking; successful earlier items remain committed.
                (geoRejected, experienceRejected, invalidData, invalidUrl, created, updated, existing, failed, accepted, published) = beforeCounts;
                outcomes.RemoveRange(beforeCount, outcomes.Count - beforeCount);
                outcomes.Add(new(chunkStart, "Failed", "ITEM_NOT_COMMITTED"));
                failed++;
                seenInRequest.Remove(identityKeys[chunkStart]);
                ItemImportFailed(logger, request.ImportId, chunkStart, exception.GetType().Name);
            }
        }
        ImportSummary(logger, request.ImportId, request.Jobs.Count, accepted, geoRejected, experienceRejected, invalidData, invalidUrl, created, updated, existing, failed);
        var response = new FresherBulkImportResponse(request.ImportId, request.Jobs.Count, accepted, geoRejected, experienceRejected, invalidData, invalidUrl, created, updated, existing, failed, outcomes)
        { RunId = run?.Id, Published = published, PublicationPending = pendingPublication };
        if (run is not null && acquisitionStore is not null && !outcomes.Any(x => x.ReasonCode == "ITEM_NOT_COMMITTED"))
        {
            diagnostic.Stage = "CompleteRun";
            diagnostic.ExternalId = null;
            await acquisitionStore.CompleteRunAsync(run.Id, response, cancellationToken);
            var durable = await acquisitionStore.GetReplayResponseAsync(run.Id, request.ImportId, cancellationToken);
            if (durable is not null)
            {
                var receipts = await acquisitionStore.GetReceiptsAsync(run.Id, cancellationToken);
                return durable with { Items = MapCurrentRequestItems(identityKeys, receipts),
                    PublicationPending = requirePublication && receipts.Values.Any(x => x.PublicationStatus is "Pending" or "Failed") };
            }
        }
        return response;
    }

    private static string SafeEligibility(FresherImportCandidate? candidate)
    {
        if (candidate is null) return "Unknown";
        var evaluated = FresherAcquisitionEligibilityPolicy.Evaluate(ToPolicyCandidate(candidate));
        return $"{evaluated.Geography}:{evaluated.Experience}";
    }

    private static FresherImportItemOutcome[] MapCurrentRequestItems(IReadOnlyList<string> identities,
        IReadOnlyDictionary<string, FresherImportReceiptSnapshot> receipts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return identities.Select((identity, index) =>
        {
            if (!seen.Add(identity)) return new FresherImportItemOutcome(index, "Duplicate", "DUPLICATE_IN_REQUEST");
            return receipts.TryGetValue(identity, out var snapshot)
                ? snapshot.Outcome with { Index = index }
                : new FresherImportItemOutcome(index, "Failed", "ITEM_NOT_COMMITTED");
        }).ToArray();
    }

    private static FresherCandidate ToPolicyCandidate(FresherImportCandidate candidate) => new()
    {
        Title = candidate.Title, Description = candidate.Description, ExperienceText = candidate.ExperienceText,
        MinimumExperienceYears = candidate.MinimumExperienceYears, MaximumExperienceYears = candidate.MaximumExperienceYears,
        EmploymentType = candidate.EmploymentType, LocationText = candidate.LocationText, City = candidate.City,
        State = candidate.State, Country = candidate.Country, WorkplaceType = candidate.WorkplaceType,
        RemoteEligibility = candidate.RemoteEligibility
    };

    private static string? SelectApplyUrl(FresherImportCandidate candidate)
    {
        foreach (var url in new[] { candidate.ApplyUrl, candidate.CanonicalJobUrl, candidate.DiscoveredUrl })
            if (FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(url)) return url!.Trim();
        return null;
    }

    private static string? SafeUrlOrNull(string? url) => FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(url) ? url!.Trim() : null;

    private static string? WebsiteCategory(string title)
    {
        static bool Has(string title, string pattern) => System.Text.RegularExpressions.Regex.IsMatch(title, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (Has(title, @"\b(?:data (?:scientist|analyst|engineer)|analytics)\b")) return "Data Science & Analytics";
        if (Has(title, @"\b(?:machine learning|artificial intelligence)\b")) return "AI & Machine Learning";
        if (Has(title, @"\b(?:devops|cloud engineer)\b")) return "DevOps & Cloud Engineering";
        if (Has(title, @"\b(?:software|developer|programmer)\b")) return "Software Engineering";
        if (Has(title, @"\b(?:accountant|finance|financial)\b")) return "Finance & Accounting";
        if (Has(title, @"\bmarketing\b")) return "Marketing";
        if (Has(title, @"\b(?:sales|business development)\b")) return "Sales & Business Development";
        if (Has(title, @"\b(?:human resources|recruiter)\b")) return "Human Resources & Recruitment";
        if (Has(title, @"\boperations\b")) return "Operations";
        if (Has(title, @"\b(?:customer support|customer success)\b")) return "Customer Success & Support";
        if (Has(title, @"\b(?:ui/ux|user experience|ux designer)\b")) return "UI/UX Design";
        return null;
    }

    private static bool MetadataBoolean(FresherImportCandidate? candidate, string key) =>
        candidate?.SourceMetadata is { ValueKind: JsonValueKind.Object } metadata && metadata.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? MetadataText(FresherImportCandidate candidate, string key) =>
        candidate.SourceMetadata is { ValueKind: JsonValueKind.Object } metadata &&
        metadata.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static string? ValidateWebsiteCandidate(FresherImportCandidate candidate, DateTime now)
    {
        _ = now;
        if (!FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(candidate.DiscoveredUrl))
            return "WEBSITE_SAFE_URLS_REQUIRED";
        if (!string.Equals(MetadataText(candidate, "sourceDomain"), new Uri(candidate.DiscoveredUrl!).Host, StringComparison.OrdinalIgnoreCase))
            return "WEBSITE_PROVENANCE_INVALID";
        return null;
    }

    private static string? ValidateCandidate(FresherImportCandidate candidate, bool website = false, bool talentd = false)
    {
        static bool TooLong(string? value, int max) => value?.Length > max;
        if (string.IsNullOrWhiteSpace(candidate.Title) || TooLong(candidate.Title, 250) ||
            (!website && string.IsNullOrWhiteSpace(candidate.CompanyName)) || TooLong(candidate.CompanyName, 200) ||
            (!website && string.IsNullOrWhiteSpace(candidate.Description)) || (!talentd && TooLong(candidate.Description, 16_000))) return "REQUIRED_OR_OVERSIZED_FIELD";
        if (TooLong(candidate.ExternalJobId, 255) || TooLong(candidate.CompanyWebsiteUrl, 2048) ||
            TooLong(candidate.CompanyLogoUrl, 2048) || TooLong(candidate.LocationText, 250) ||
            TooLong(candidate.City, 100) || TooLong(candidate.State, 100) || TooLong(candidate.Country, 100) ||
            TooLong(candidate.WorkplaceType, 50) || TooLong(candidate.RemoteEligibility, 500) ||
            TooLong(candidate.ExperienceText, 500) || TooLong(candidate.ExperienceClassification, 60) ||
            TooLong(candidate.EmploymentType, 60) || TooLong(candidate.Category, 120)) return "FIELD_LENGTH_EXCEEDED";
        if (candidate.MinimumExperienceYears is < 0 or > 80 || candidate.MaximumExperienceYears is < 0 or > 80 ||
            (candidate.MinimumExperienceYears.HasValue && candidate.MaximumExperienceYears.HasValue &&
             candidate.MinimumExperienceYears > candidate.MaximumExperienceYears)) return "EXPERIENCE_RANGE_INVALID";
        if (candidate.Skills is null || candidate.Skills.Count > 50 || candidate.Skills.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
            return "SKILLS_INVALID";
        if (string.Join(", ", new[] { candidate.LocationText, candidate.City, candidate.State, candidate.Country,
                candidate.RemoteEligibility, candidate.WorkplaceType }.Where(value => !string.IsNullOrWhiteSpace(value))).Length > 250)
            return "LOCATION_LENGTH_EXCEEDED";
        return null;
    }

    private static string? ComposeLocation(FresherImportCandidate candidate, bool websiteCrawler)
    {
        var country = candidate.Country;
        // Website descriptions can already include the country in prose. Preserve
        // structured country evidence above; only avoid duplicating its display text.
        if (websiteCrawler && !string.IsNullOrWhiteSpace(country) &&
            !string.IsNullOrWhiteSpace(candidate.LocationText) &&
            System.Text.RegularExpressions.Regex.IsMatch(candidate.LocationText,
                @"(?<!\w)" + System.Text.RegularExpressions.Regex.Escape(country.Trim()) + @"(?!\w)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
            country = null;
        return string.Join(", ", new[] { candidate.LocationText, candidate.City, candidate.State, country,
            candidate.RemoteEligibility, candidate.WorkplaceType }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static EmploymentType? MapEmployment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "intern" or "internship" => EmploymentType.Internship,
        "full-time" or "full time" or "fulltime" or "full_time" => EmploymentType.FullTime,
        "part-time" or "part time" or "parttime" or "part_time" => EmploymentType.PartTime,
        "contract" => EmploymentType.Contract,
        "temporary" => EmploymentType.Temporary,
        "freelance" => EmploymentType.Freelance,
        _ => null
    };
    private static WorkplaceType? MapWorkplace(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "remote" => WorkplaceType.Remote, "hybrid" => WorkplaceType.Hybrid,
        "on-site" or "onsite" => WorkplaceType.OnSite, _ => null
    };

    [LoggerMessage(EventId = 8581, Level = LogLevel.Information,
        Message = "BulkImportPhaseDuration ImportId={ImportId} Phase={Phase} DurationMs={DurationMs}")]
    private static partial void PhaseDuration(ILogger logger, string importId, string phase, long durationMs);

    [LoggerMessage(EventId = 8580, Level = LogLevel.Warning,
        Message = "Website company resolution deferred ImportId={ImportId} Index={Index} ReasonCode={ReasonCode}.")]
    private static partial void CompanyResolutionDeferred(ILogger logger, string importId, int index, string reasonCode);

    [LoggerMessage(EventId = 8512, Level = LogLevel.Error,
        Message = "Bulk import failed CorrelationId={CorrelationId} ImportId={ImportId} SourceType={SourceType} JobCount={JobCount} Stage={Stage} ExternalId={ExternalId} ExceptionType={ExceptionType} SqlState={SqlState}")]
    private static partial void ImportFailed(ILogger logger, string correlationId, string importId, string sourceType,
        int jobCount, string stage, string? externalId, string exceptionType, string? sqlState);

    [LoggerMessage(EventId = 8510, Level = LogLevel.Error,
        Message = "Fresher acquisition item failed. ImportId={ImportId} ItemIndex={ItemIndex} ErrorType={ErrorType}")]
    private static partial void ItemImportFailed(ILogger logger, string importId, int itemIndex, string errorType);

    [LoggerMessage(EventId = 8511, Level = LogLevel.Information,
        Message = "BulkImportSummary ImportId={ImportId} Received={Received} Accepted={Accepted} RejectedGeography={RejectedGeography} RejectedExperience={RejectedExperience} RejectedInvalidData={RejectedInvalidData} InvalidUrl={InvalidUrl} Created={Created} Updated={Updated} Existing={Existing} Failed={Failed}")]
    private static partial void ImportSummary(ILogger logger, string importId, int received, int accepted,
        int rejectedGeography, int rejectedExperience, int rejectedInvalidData, int invalidUrl,
        int created, int updated, int existing, int failed);
}
