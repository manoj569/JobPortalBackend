using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

public sealed class EmployerAcquisitionOptions
{
    public string? Endpoint { get; set; }
    public string? Token { get; set; }
}

// Transport to the fixed, operator-configured acquisition worker, not an arbitrary
// admin URL. Public employer requests are vetted/pinned by the worker's SafeWeb.
public sealed partial class GenericEmployerScanner(IHttpClientFactory clients, IOptions<EmployerAcquisitionOptions> options,
    IJobSourceRunCheckpoint? checkpoint = null, EmployerDirectInventoryScanner? inventory = null,
    IServiceProvider? services = null, ILogger<GenericEmployerScanner>? logger = null) : IEmployerDirectInventoryScanner
{
    [LoggerMessage(EventId = 4398, Level = LogLevel.Warning, Message = "Rebuilding corrupt generic acquisition strategy for source {SourceId}")]
    private static partial void StrategyRecovery(ILogger logger, Guid sourceId);

    [LoggerMessage(EventId = 4405, Level = LogLevel.Information,
        Message = "Event=EightfoldAcquisition SourceId={SourceId} Host={Host} Strategy={Strategy} Generation={Generation} AdvertisedInventoryCount={AdvertisedInventoryCount} BatchStart={BatchStart} BatchEnd={BatchEnd} ReturnedCount={ReturnedCount} NextPosition={NextPosition} Anchor={Anchor} HasMore={HasMore} CycleCompleted={CycleCompleted} IsComplete={IsComplete} ReconciliationSafe={ReconciliationSafe} IdentityDigest={IdentityDigest}")]
    private static partial void EightfoldAcquisition(ILogger logger, Guid sourceId, string host, string strategy,
        string generation, int advertisedInventoryCount, int batchStart, int batchEnd, int returnedCount,
        int nextPosition, string anchor, bool hasMore, bool cycleCompleted, bool isComplete, bool reconciliationSafe, string identityDigest);

    [LoggerMessage(EventId = 4406, Level = LogLevel.Information,
        Message = "Event=EightfoldContinuationLoaded SourceId={SourceId} Strategy={Strategy} Generation={Generation} Position={Position} Anchor={Anchor} CycleCompleted={CycleCompleted} InventoryCount={InventoryCount}")]
    private static partial void EightfoldContinuationLoaded(ILogger logger, Guid sourceId, string strategy,
        string generation, int position, string anchor, bool cycleCompleted, int inventoryCount);

    public const string Identifier = GenericEmployerContract.Identifier;
    public const string HttpClientName = "EmployerAcquisitionWorker";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExternalJobSourceSnapshot> ScanAsync(JobSource source, CancellationToken cancellationToken)
    {
        if (source.AtsType == AtsType.Custom && source.AtsIdentifier == EmployerDirectInventoryContract.Identifier)
            return await (inventory ?? throw new InvalidOperationException("Inventory scanner is not registered.")).ScanAsync(source, cancellationToken);
        if (source.AtsType != AtsType.Custom || !GenericEmployerContract.IsGeneric(source.AtsIdentifier) || checkpoint?.RunId is not { } runId)
            throw new InvalidOperationException("Generic acquisition requires a configured source and durable run.");
        var settings = options.Value;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback) ||
            endpoint.UserInfo.Length != 0 || settings.Token?.Length < 32 || string.IsNullOrEmpty(settings.Token))
            throw new InvalidOperationException("EMPLOYER_ACQUISITION_WORKER_UNCONFIGURED");
        using var client = clients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/employer/scan"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        JsonElement? strategy = null;
        var recoveringStrategy = false;
        try
        {
            strategy = string.IsNullOrEmpty(source.EmployerStrategyJson) ? null : JsonSerializer.Deserialize<JsonElement>(source.EmployerStrategyJson);
            if (strategy is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
                throw new JsonException("Invalid acquisition memory shape.");
        }
        catch (JsonException)
        {
            // Only provider-owned acquisition memory is reset, never source config.
            // Do not log the corrupt value, which might contain sensitive text.
            recoveringStrategy = true;
            strategy = null;
            if (logger is not null) StrategyRecovery(logger, source.Id);
        }
        var priorState = default(JsonElement);
        var priorEightfold = strategy is { ValueKind: JsonValueKind.Object } && TryGetEightfoldContinuation(strategy.Value, out priorState);
        if (priorEightfold && logger is not null)
            EightfoldContinuationLoaded(logger, source.Id, StrategyName(strategy!.Value),
                FingerprintPrefix(priorState), ReadInt(priorState, "cursor"), ReadString(priorState, "anchorId"),
                ReadBool(priorState, "cycleCompleted"), ReadInt(priorState, "count"));
        request.Content = JsonContent.Create(new { runId, source = new { source.Id, source.CompanyId,
            CompanyName = source.Company?.Name, source.CareerPageUrl, source.AutoPublishEnabled, source.ScanIntervalMinutes, strategy } });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 12_000_000)
            throw new HttpRequestException("Acquisition response too large.");
        await response.Content.LoadIntoBufferAsync(12_000_000, cancellationToken);
        var observed = await response.Content.ReadFromJsonAsync<WorkerObservation>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Acquisition response is empty.");
        if (observed.RunId != runId || observed.SourceId != source.Id || observed.CompanyId != source.CompanyId ||
            observed.CareerPageUrl != source.CareerPageUrl || observed.ObservedAtUtc.Offset != TimeSpan.Zero ||
            observed.Jobs is null || observed.Jobs.Count > 5000)
            throw new InvalidOperationException("Acquisition response source binding failed.");
        if (observed.ObservedExternalIds is not null)
        {
            if (!observed.IsComplete || observed.CompletionReason != ExternalJobInventoryCensus.CompletionReason || observed.ProviderRoutes.Count != 0 ||
                observed.Errors.ValueKind != JsonValueKind.Array || observed.Errors.GetArrayLength() != 0)
                throw new InvalidOperationException("Unproven identity census.");
            ExternalJobInventoryCensus.Validate(observed.ObservedExternalIds, observed.InventoryEvidence, observed.Jobs);
        }
        else if (observed.InventoryEvidence is not null)
            throw new InvalidOperationException("Missing census identities.");
        var metadata = JsonSerializer.Serialize(new { observed.Strategy, observed.Metrics, observed.Errors,
            observed.CompletionReason, observed.IsComplete, observed.Status }, JsonOptions);
        if (metadata.Length > 128_000) throw new InvalidOperationException("Acquisition strategy evidence too large.");
        // Strategy memory is source configuration evidence, saved by the existing
        // runner/UnitOfWork. No alternate Job or import record is created.
        source.EmployerStrategyJson = JsonSerializer.Serialize(observed.Strategy, JsonOptions);
        source.EmployerDiagnosticsJson = metadata;
        var isEightfold = IsEightfold(observed.Strategy);
        var pendingContinuation = IsContinuationPending(observed);
        if (isEightfold && logger is not null)
        {
            var sitemapState = TryGetSitemapContinuation(observed.Strategy, out var currentState) ? currentState : default;
            var ids = observed.Jobs.Select(job => job.ExternalId).Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!.Trim()).Order(StringComparer.Ordinal).ToArray();
            var batchDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ids))))[..16];
            var host = Uri.TryCreate(source.CareerPageUrl, UriKind.Absolute, out var career) ? career.Host : "invalid";
            var start = ReadInt(observed.Metrics, "inventory_batch_start", ReadInt(sitemapState, "cursor"));
            var end = ReadInt(observed.Metrics, "inventory_batch_end", ReadInt(sitemapState, "cursor"));
            var count = ReadInt(observed.Metrics, "inventory_expected_count", ReadInt(sitemapState, "count"));
            var anchor = ReadString(observed.Metrics, "inventory_anchor_id", ReadString(sitemapState, "anchorId"));
            EightfoldAcquisition(logger, source.Id, host, StrategyName(observed.Strategy),
                FingerprintPrefix(sitemapState), count, start, end, ids.Length, end, anchor, pendingContinuation,
                ReadBool(observed.Metrics, "inventory_cycle_completed"), observed.IsComplete,
                observed.IsComplete && observed.ObservedExternalIds is not null, batchDigest);
        }
        if (observed.ProviderRoutes.Count > 0)
        {
            var jobs = observed.Jobs.ToList();
            var authoritative = observed.Jobs.Count == 0 && observed.ProviderRoutes.Count == 1 &&
                new Uri(source.CareerPageUrl).Host == new Uri(observed.ProviderRoutes[0].ProviderCareerUrl).Host;
            foreach (var route in observed.ProviderRoutes)
            {
                if (!route.ProviderRoutable || !Enum.TryParse<AtsType>(route.Provider, out var type) || type == AtsType.Custom)
                    throw new InvalidOperationException("Unsupported discovered ATS route.");
                var provider = (services ?? throw new InvalidOperationException("Provider resolver is not registered.")).GetServices<IExternalJobProvider>().SingleOrDefault(p => p.AtsType == type)
                    ?? throw new InvalidOperationException("Detected ATS provider is not executable.");
                var routed = new JobSource { Id = source.Id, CompanyId = source.CompanyId, Company = source.Company ?? throw new InvalidOperationException("Detected ATS route requires its source company."),
                    AtsType = type, AtsIdentifier = route.AtsIdentifier, CareerPageUrl = route.ProviderCareerUrl,
                    IsActive = source.IsActive, AutoPublishEnabled = source.AutoPublishEnabled,
                    ScanIntervalMinutes = source.ScanIntervalMinutes };
                var snapshot = provider is ICompleteExternalJobProvider complete
                    ? await complete.FetchSnapshotAsync(routed, cancellationToken)
                    : new ExternalJobSourceSnapshot(await provider.FetchJobsAsync(routed, cancellationToken), 0, false);
                jobs.AddRange(snapshot.Jobs);
                authoritative &= snapshot.IsComplete && snapshot.Skipped == 0 && type != AtsType.Workday;
            }
            return new(jobs, 0, authoritative) { ObservedAtUtc = observed.ObservedAtUtc.UtcDateTime,
                ProviderReasonCounts = new Dictionary<string, int> { ["GenericAtsHandoff"] = 1 } };
        }
        if (observed.IsComplete && observed.CompletionReason != "AUTHORITATIVE_API_CENSUS" &&
            !(observed.ObservedExternalIds is not null && observed.CompletionReason == ExternalJobInventoryCensus.CompletionReason))
            throw new InvalidOperationException("Unrecognized generic enumeration contract.");
        var generation = observed.InventoryEvidence?.Generation.ToString("D");
        var repeatedGeneration = generation is not null && source.LastInventoryVersion == generation && source.LastObservedSnapshotId != runId;
        var reason = recoveringStrategy ? "AcquisitionStrategyRecovery" : repeatedGeneration ? "CensusGenerationAlreadyObserved" : observed.CompletionReason;
        return new(observed.Jobs, 0, observed.IsComplete && !recoveringStrategy && !repeatedGeneration)
        {
            ObservedAtUtc = observed.ObservedAtUtc.UtcDateTime,
            ObservedExternalIds = observed.ObservedExternalIds,
            InventoryEvidence = observed.InventoryEvidence,
            InventoryVersion = generation,
            InventoryDigest = observed.InventoryEvidence?.Digest,
            InventoryAtUtc = generation is null ? null : observed.ObservedAtUtc.UtcDateTime,
            ProviderReasonCounts = new Dictionary<string, int> { [reason] = 1 },
            AcquisitionSucceeded = observed.Status == "OBSERVED" && observed.ProviderRoutes.Count == 0,
            ContinuationPending = pendingContinuation
        };
    }

    private static bool IsContinuationPending(WorkerObservation observation) => !observation.IsComplete &&
        (observation.Status == "INVENTORY_PENDING_ENRICHMENT" || ReadInt(observation.Metrics, "inventory_pending_details") > 0);

    private static bool IsEightfold(JsonElement strategy)
    {
        if (strategy.ValueKind != JsonValueKind.Object) return false;
        foreach (var name in new[] { "provider", "detectedStrategy", "providerProtocol", "paginationStrategy" })
            if (strategy.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                (value.GetString()!.Contains("EIGHTFOLD", StringComparison.OrdinalIgnoreCase) ||
                 value.GetString()!.Equals("PCS_LEGACY", StringComparison.OrdinalIgnoreCase))) return true;
        return strategy.TryGetProperty("eightfoldSitemapContinuation", out _) || strategy.TryGetProperty("eightfoldContinuation", out _);
    }

    private static string StrategyName(JsonElement strategy)
    {
        foreach (var name in new[] { "paginationStrategy", "providerProtocol", "detectedStrategy", "provider" })
            if (strategy.ValueKind == JsonValueKind.Object && strategy.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!;
        return "EIGHTFOLD";
    }

    private static bool TryGetEightfoldContinuation(JsonElement strategy, out JsonElement continuation)
    {
        continuation = default;
        return strategy.ValueKind == JsonValueKind.Object &&
            (strategy.TryGetProperty("eightfoldSitemapContinuation", out continuation) ||
             strategy.TryGetProperty("eightfoldContinuation", out continuation));
    }

    private static bool TryGetSitemapContinuation(JsonElement strategy, out JsonElement continuation)
    {
        continuation = default;
        return strategy.ValueKind == JsonValueKind.Object && strategy.TryGetProperty("eightfoldSitemapContinuation", out continuation);
    }

    private static int ReadInt(JsonElement value, string name, int fallback = 0) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.TryGetInt32(out var number) ? number : fallback;

    private static string ReadString(JsonElement value, string name, string fallback = "") =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() ?? fallback : fallback;

    private static bool ReadBool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static string FingerprintPrefix(JsonElement continuation)
    {
        foreach (var name in new[] { "inventoryDigest", "binding" })
            if (continuation.ValueKind == JsonValueKind.Object && continuation.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() is { Length: >= 12 } fingerprint)
                return fingerprint[..12];
        return "none";
    }

    private sealed record WorkerObservation(Guid RunId, Guid SourceId, Guid CompanyId, string CareerPageUrl,
        DateTimeOffset ObservedAtUtc, bool IsComplete, string CompletionReason, string Status,
        IReadOnlyCollection<RawExternalJob> Jobs, IReadOnlyList<WorkerRoute> ProviderRoutes,
        JsonElement Strategy, JsonElement Metrics, JsonElement Errors,
        IReadOnlyCollection<string>? ObservedExternalIds = null, ExternalJobInventoryEvidence? InventoryEvidence = null);

    private sealed record WorkerRoute(string Provider, [property: JsonPropertyName("ats_identifier")] string AtsIdentifier,
        [property: JsonPropertyName("provider_career_url")] string ProviderCareerUrl,
        [property: JsonPropertyName("provider_routable")] bool ProviderRoutable);
}
