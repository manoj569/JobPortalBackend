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
        var identityKeys = request.Jobs.Select(x => FresherCandidateIdentity.Create(source?.Id ?? Guid.Empty, x)).ToArray();
        var requestFingerprint = FresherCandidateIdentity.Fingerprint(identityKeys);
        IReadOnlyDictionary<string, FresherImportReceiptSnapshot> priorReceipts = new Dictionary<string, FresherImportReceiptSnapshot>();
        await using var runLock = new AsyncDisposableHolder();
        if (acquisitionStore is not null)
        {
            runLock.Set(await acquisitionStore.AcquireRunLockAsync(source!.Id, request.ImportId, cancellationToken));
            var start = await acquisitionStore.StartRunAsync(source.Id, request.ImportId, Guid.NewGuid().ToString("N"), requestFingerprint, identityKeys.Distinct(StringComparer.Ordinal).Count(), cancellationToken);
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
        var outcomes = new List<FresherImportItemOutcome>(request.Jobs.Count);
        var geoRejected = 0; var experienceRejected = 0; var invalidData = 0; var invalidUrl = 0;
        var created = 0; var updated = 0; var existing = 0; var failed = 0; var accepted = 0; var published = 0;
        var seenInRequest = new HashSet<string>(StringComparer.Ordinal);
        var chunkSize = acquisitionStore is null ? request.Jobs.Count : Math.Clamp(options?.Value.ChunkSize ?? 50, 1, 200);
        for (var chunkStart = 0; chunkStart < request.Jobs.Count; chunkStart += chunkSize)
        {
            await using var chunk = run is not null && acquisitionStore is not null
                ? await acquisitionStore.BeginChunkAsync(request.Jobs.Skip(chunkStart).Take(chunkSize)
                    .Where(x => !string.IsNullOrWhiteSpace(x?.ExternalJobId))
                    .Select(x => $"acquisition-job:{source!.Id:N}:{FresherCandidateIdentity.Normalize(x!.ExternalJobId)}").ToArray(), cancellationToken) : null;
            var chunkReceipts = new List<FresherReceiptInput>();
            var publicationQueue = new List<(int Index, string Identity, Guid JobId, JobIngestionOutcome Outcome)>();
            var chunkEnd = Math.Min(chunkStart + chunkSize, request.Jobs.Count);
            void RecordReceipt(int index, FresherImportCandidate candidate)
            {
                chunkReceipts.Add(new(index, candidate, identityKeys[index], SafeEligibility(candidate), outcomes[^1], now));
            }
            for (var index = chunkStart; index < chunkEnd; index++)
            {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = request.Jobs[index];
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
            var dataError = ValidateCandidate(candidate);
            if (dataError is not null)
            {
                invalidData++;
                outcomes.Add(new(index, "RejectedInvalidData", dataError));
                RecordReceipt(index, candidate);
                continue;
            }
            var decision = FresherAcquisitionEligibilityPolicy.Evaluate(ToPolicyCandidate(candidate));
            if (decision.Geography is not (FresherGeographyDecision.IndiaEligible or FresherGeographyDecision.GlobalRemoteEligible))
            {
                geoRejected++;
                outcomes.Add(new(index, "RejectedGeography", decision.GeographyReasonCode));
                RecordReceipt(index, candidate);
                continue;
            }
            if (decision.Experience != FresherExperienceDecision.Eligible)
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
            if (string.IsNullOrWhiteSpace(candidate.CompanyName) || string.IsNullOrWhiteSpace(candidate.Title) ||
                string.IsNullOrWhiteSpace(candidate.Description))
            {
                failed++;
                outcomes.Add(new(index, "RejectedInvalidData", "REQUIRED_FIELD_MISSING"));
                RecordReceipt(index, candidate);
                continue;
            }

            Guid? categoryId = candidate.CategoryId;
            if (categoryId.HasValue && (categories is null || await categories.GetByIdAsync(categoryId.Value, cancellationToken) is null))
            {
                invalidData++;
                outcomes.Add(new(index, "RejectedInvalidData", "CATEGORY_RESOLUTION_FAILED"));
                RecordReceipt(index, candidate);
                continue;
            }
            if (!categoryId.HasValue && !string.IsNullOrWhiteSpace(candidate.Category))
            {
                var categoryName = candidate.Category.Trim();
                var matchedCategory = categories is null ? null : await categories.FindByNameOrSlugAsync(categoryName, SlugGenerator.Generate(categoryName), cancellationToken);
                if (matchedCategory is null)
                {
                    invalidData++;
                    outcomes.Add(new(index, "RejectedInvalidData", "CATEGORY_RESOLUTION_FAILED"));
                    RecordReceipt(index, candidate);
                    continue;
                }
                categoryId = matchedCategory.Id;
            }

            try
            {
                var countryCodes = decision.Geography == FresherGeographyDecision.GlobalRemoteEligible
                    ? new[] { "IN" }
                    : string.Equals(candidate.Country, "India", StringComparison.OrdinalIgnoreCase) ? new[] { "IN" } : Array.Empty<string>();
                var metadata = JsonSerializer.Serialize(new
                {
                    importId = request.ImportId,
                    sourceType = source?.ProviderKey ?? request.SourceType,
                    sourceName = source?.Name ?? request.SourceName,
                    eligibility = new { decision.Geography, decision.GeographyReasonCode, decision.Experience, decision.ExperienceReasonCode },
                    countryCodes,
                    remoteEligibility = candidate.RemoteEligibility,
                    workplaceType = candidate.WorkplaceType
                });
                var raw = new RawExternalJob
                {
                    Title = candidate.Title,
                    CompanyName = candidate.CompanyName,
                    CompanyId = candidate.CompanyId,
                    CompanyWebsiteUrl = candidate.CompanyWebsiteUrl,
                    Location = ComposeLocation(candidate),
                    CountryCodes = countryCodes,
                    RemoteEligibilityText = candidate.RemoteEligibility ??
                        (decision.Geography == FresherGeographyDecision.GlobalRemoteEligible ? "Remote Worldwide; India eligible" : null),
                    Description = candidate.Description,
                    ApplicationUrl = selectedUrl,
                    // Keep employer ATS identity in its own field; acquisition IDs have a separate Job column.
                    ExternalId = source is null ? candidate.ExternalJobId : null,
                    AcquisitionSourceId = source?.Id,
                    AcquisitionExternalId = candidate.ExternalJobId is null ? null : FresherCandidateIdentity.Normalize(candidate.ExternalJobId),
                    DiscoveredUrl = SafeUrlOrNull(candidate.DiscoveredUrl),
                    CanonicalJobUrl = SafeUrlOrNull(candidate.CanonicalJobUrl),
                    LastVerifiedAtUtc = candidate.SourceLastSeenAt?.ToUniversalTime(),
                    SourcePostedAtUtc = candidate.PostedAt?.ToUniversalTime(),
                    ExpiresAtUtc = candidate.ExpiresAt?.ToUniversalTime(),
                    EmploymentType = MapEmployment(candidate.EmploymentType),
                    WorkplaceType = MapWorkplace(candidate.WorkplaceType),
                    WorkplaceTypeText = candidate.WorkplaceType,
                    ExperienceLevel = ExperienceLevel.Entry,
                    MinimumExperienceYears = candidate.MinimumExperienceYears ?? 0,
                    MaximumExperienceYears = candidate.MaximumExperienceYears,
                    EducationRequirement = candidate.ExperienceClassification,
                    ExternalCategory = candidate.Category,
                    CategoryId = categoryId,
                    Skills = candidate.Skills,
                    SourceMetadataJson = metadata
                };
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
                chunkReceipts.Add(new(index, candidate, identityKey, SafeEligibility(candidate), outcomes[^1], now,
                    publicationRequested));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                ItemImportFailed(logger, request.ImportId, index, exception.GetType().Name);
                throw;
            }
            }
            if (chunk is not null && run is not null)
                await chunk.CommitAsync(run.Id, chunkReceipts, cancellationToken);

            // Network/publication side effects happen only after the job+receipt commit.
            foreach (var queued in publicationQueue)
            {
                try
                {
                    var result = await autoPublish!.TryPublishAsync(queued.JobId, cancellationToken);
                    var status = result.Outcome == JobAutoPublishOutcome.Published ? "Published" : "NotPublished";
                    await acquisitionStore!.UpdatePublicationAsync(run!.Id, queued.Identity, status, null, cancellationToken);
                    if (status == "Published") published++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    ItemImportFailed(logger, request.ImportId, queued.Index, exception.GetType().Name);
                    await acquisitionStore!.UpdatePublicationAsync(run!.Id, queued.Identity, "Failed", exception.GetType().Name, cancellationToken);
                }
            }
        }
        ImportSummary(logger, request.ImportId, request.Jobs.Count, accepted, geoRejected, experienceRejected, invalidData, invalidUrl, created, updated, existing, failed);
        var response = new FresherBulkImportResponse(request.ImportId, request.Jobs.Count, accepted, geoRejected, experienceRejected, invalidData, invalidUrl, created, updated, existing, failed, outcomes)
        { RunId = run?.Id, Published = published };
        if (run is not null && acquisitionStore is not null)
        {
            await acquisitionStore.CompleteRunAsync(run.Id, response, cancellationToken);
            var durable = await acquisitionStore.GetReplayResponseAsync(run.Id, request.ImportId, cancellationToken);
            if (durable is not null) return durable with { Items = MapCurrentRequestItems(identityKeys,
                await acquisitionStore.GetReceiptsAsync(run.Id, cancellationToken)) };
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

    private static string? ValidateCandidate(FresherImportCandidate candidate)
    {
        static bool TooLong(string? value, int max) => value?.Length > max;
        if (string.IsNullOrWhiteSpace(candidate.Title) || TooLong(candidate.Title, 250) ||
            string.IsNullOrWhiteSpace(candidate.CompanyName) || TooLong(candidate.CompanyName, 200) ||
            string.IsNullOrWhiteSpace(candidate.Description) || TooLong(candidate.Description, 16_000)) return "REQUIRED_OR_OVERSIZED_FIELD";
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

    private static string? ComposeLocation(FresherImportCandidate candidate) => string.Join(", ",
        new[] { candidate.LocationText, candidate.City, candidate.State, candidate.Country,
            candidate.RemoteEligibility, candidate.WorkplaceType }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static EmploymentType? MapEmployment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "intern" or "internship" => EmploymentType.Internship,
        "full-time" or "full time" => EmploymentType.FullTime,
        "part-time" or "part time" => EmploymentType.PartTime,
        "contract" => EmploymentType.Contract,
        _ => null
    };
    private static WorkplaceType? MapWorkplace(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "remote" => WorkplaceType.Remote, "hybrid" => WorkplaceType.Hybrid,
        "on-site" or "onsite" => WorkplaceType.OnSite, _ => null
    };

    [LoggerMessage(EventId = 8510, Level = LogLevel.Error,
        Message = "Fresher acquisition item failed. ImportId={ImportId} ItemIndex={ItemIndex} ErrorType={ErrorType}")]
    private static partial void ItemImportFailed(ILogger logger, string importId, int itemIndex, string errorType);

    [LoggerMessage(EventId = 8511, Level = LogLevel.Information,
        Message = "BulkImportSummary ImportId={ImportId} Received={Received} Accepted={Accepted} RejectedGeography={RejectedGeography} RejectedExperience={RejectedExperience} RejectedInvalidData={RejectedInvalidData} InvalidUrl={InvalidUrl} Created={Created} Updated={Updated} Existing={Existing} Failed={Failed}")]
    private static partial void ImportSummary(ILogger logger, string importId, int received, int accepted,
        int rejectedGeography, int rejectedExperience, int rejectedInvalidData, int invalidUrl,
        int created, int updated, int existing, int failed);
}
