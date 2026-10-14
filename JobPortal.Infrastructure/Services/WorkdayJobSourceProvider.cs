using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using JobPortal.Application.Features.JobAggregation;

namespace JobPortal.Infrastructure.Services;

/// <summary>
/// Reads public Workday Candidate Experience (CXS) career sites hosted on
/// *.wdN.myworkdayjobs.com. Only public listing/detail endpoints are called.
/// Application endpoints, candidate login, cookies and form submission are never used.
/// </summary>
public sealed partial class WorkdayJobSourceProvider(
    IHttpClientFactory clients,
    ILogger<WorkdayJobSourceProvider>? logger = null,
    IOptions<JobAggregationOptions>? options = null,
    TimeProvider? timeProvider = null,
    AggregationHttpRetryState? retryState = null,
    IJobSourceRunCheckpoint? checkpoint = null) : IBatchedExternalJobProvider, IDisposable
{
    public const string HttpClientName = "WorkdayJobAggregation";

    // Workday's public CXS listing endpoint is capped at 20 items per request.
    private const int PageSize = 20;
    private const int MaxJobs = 10_000;
    // Some Workday searches advertise exactly 2,000 despite larger facet counts.
    // Treat that boundary as capped, never as proof of full inventory.
    private const int SuspectedSearchCap = 2_000;
    private const int MaxJsonBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(250);

    private static readonly Action<ILogger, Guid, Exception?> Started =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(4370, nameof(Started)),
            "WorkdaySyncStarted Source={JobSourceId}.");

    private static readonly Action<ILogger, Guid, int, int, int, double, Exception?> Completed =
        LoggerMessage.Define<Guid, int, int, int, double>(
            LogLevel.Information,
            new EventId(4371, nameof(Completed)),
            "WorkdaySyncFetched Source={JobSourceId} Fetched={Fetched} Parsed={Parsed} Skipped={Skipped} DurationMs={DurationMs}.");

    [LoggerMessage(
        EventId = 4372,
        Level = LogLevel.Warning,
        Message = "WorkdaySourceFailure Source={JobSourceId} Stage={Stage} Offset={Offset} ReasonCode={ReasonCode} StatusCode={StatusCode}.")]
    private static partial void LogSourceFailure(
        ILogger logger,
        Guid jobSourceId,
        string stage,
        int offset,
        string reasonCode,
        int? statusCode);

    [LoggerMessage(
        EventId = 4373,
        Level = LogLevel.Information,
        Message = "WorkdayListingSummary Source={JobSourceId} RawSlots={RawSlots} UniquePaths={UniquePaths} Duplicates={Duplicates} Placeholders={Placeholders} AdvertisedTotal={AdvertisedTotal} IsComplete={IsComplete}")]
    private static partial void LogListingSummary(
        ILogger logger,
        Guid jobSourceId,
        int rawSlots,
        int uniquePaths,
        int duplicates,
        int placeholders,
        int? advertisedTotal,
        bool isComplete);

    private const string ReasonDataKey = "WorkdayFailureReason";

    [LoggerMessage(EventId = 4374, Level = LogLevel.Information,
        Message = "WorkdayBatchReady Source={SourceId} DetailAttempted={Attempted} DetailSucceeded={Succeeded} DetailFailed={Failed} DurationMs={DurationMs}.")]
    private static partial void LogBatch(ILogger logger, Guid sourceId, int attempted, int succeeded, int failed, double durationMs);

    [LoggerMessage(EventId = 4375, Level = LogLevel.Information,
        Message = "WorkdayRunEnded Source={SourceId} ReasonCode={ReasonCode} DetailAttempted={Attempted} DurationMs={DurationMs}.")]
    private static partial void LogRunEnded(ILogger logger, Guid sourceId, string reasonCode, int attempted, double durationMs);

    [LoggerMessage(EventId = 4376, Level = LogLevel.Information,
        Message = "WorkdayStage Source={SourceId} Stage={Stage} Items={Items} DurationMs={DurationMs} EffectiveConcurrency={Concurrency}.")]
    private static partial void LogStage(ILogger logger, Guid sourceId, string stage, int items, double durationMs, int concurrency);

    [LoggerMessage(EventId = 4377, Level = LogLevel.Information,
        Message = "WorkdayListingValidation Source={SourceId} Stage={Stage} Offset={Offset} ExpectedTotal={ExpectedTotal} ReportedTotal={ReportedTotal} RawSlots={RawSlots} TotalOmitted={TotalOmitted}.")]
    private static partial void LogListingValidation(ILogger logger, Guid sourceId, string stage, int offset,
        int expectedTotal, int reportedTotal, int rawSlots, bool totalOmitted);

    [LoggerMessage(EventId = 4390, Level = LogLevel.Information,
        Message = "WorkdaySnapshotValidation Source={SourceId} InitialReportedTotal={InitialReportedTotal} FinalReportedTotal={FinalReportedTotal} RawSlots={RawSlots} UniquePaths={UniquePaths} DuplicatePaths={DuplicatePaths} PlaceholderCount={PlaceholderCount} FirstPageChanged={FirstPageChanged} LastOffset={LastOffset} LastPageSize={LastPageSize} SnapshotStable={SnapshotStable} IngestionSafe={IngestionSafe} ReconciliationSafe={ReconciliationSafe} ReasonCode={ReasonCode} RetryRecommended={RetryRecommended}.")]
    private static partial void LogSnapshotValidation(ILogger logger, Guid sourceId, int initialReportedTotal,
        int? finalReportedTotal, int rawSlots, int uniquePaths, int duplicatePaths, int placeholderCount,
        bool firstPageChanged, int lastOffset, int lastPageSize, bool snapshotStable, bool ingestionSafe,
        bool reconciliationSafe, string reasonCode, bool retryRecommended);

    private void LogSnapshot(Guid sourceId, WorkdayListingDiagnostics evidence)
    {
        if (logger is not null) LogSnapshotValidation(logger, sourceId, evidence.InitialReportedTotal,
            evidence.FinalReportedTotal, evidence.RawSlots, evidence.UniquePaths, evidence.DuplicatePaths,
            evidence.PlaceholderCount, evidence.FirstPageChanged, evidence.LastOffset, evidence.LastPageSize,
            evidence.SnapshotStable, evidence.IngestionSafe, evidence.ReconciliationSafe, evidence.ReasonCode, evidence.RetryRecommended);
    }

    public AtsType AtsType => AtsType.Workday;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(
        JobSource source,
        CancellationToken cancellationToken = default) =>
        (await FetchSnapshotAsync(source, cancellationToken)).Jobs;

    private WorkdayFetchOptions Settings => options?.Value.Workday ?? new();
    private TimeProvider Clock => timeProvider ?? TimeProvider.System;
    private readonly AggregationHttpRetryState workdayRetryState = retryState ?? new();
    private int adaptiveConcurrency = 16;
    private readonly SemaphoreSlim requestPace = new(1, 1);
    private DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    public void Dispose()
    {
        requestPace.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task PaceRequestAsync(CancellationToken token)
    {
        await requestPace.WaitAsync(token);
        try
        {
            var remaining = lastRequestAt.AddMilliseconds(Settings.RequestSpacingMilliseconds) - Clock.GetUtcNow();
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, Clock, token);
            lastRequestAt = Clock.GetUtcNow();
        }
        finally { requestPace.Release(); }
    }

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(
        JobSource source, CancellationToken cancellationToken = default)
    {
        var jobs = new List<RawExternalJob>();
        var skipped = 0;
        var complete = false;
        WorkdayListingDiagnostics? diagnostics = null;
        var reasons = new Dictionary<string,int>(StringComparer.Ordinal);
        await foreach (var batch in FetchBatchesAsync(source, cancellationToken))
        {
            jobs.AddRange(batch.Jobs);
            skipped += batch.Skipped;
            complete = batch.IsComplete;
            diagnostics = batch.ListingDiagnostics ?? diagnostics;
            foreach (var (reason,count) in batch.ProviderReasonCounts ?? new Dictionary<string,int>())
                reasons[reason] = reasons.GetValueOrDefault(reason) + count;
        }
        return new(jobs, skipped, complete) { ListingDiagnostics = diagnostics, ProviderReasonCounts = reasons };
    }

    public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(
        JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateFetchSettings(source, Settings);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<ExternalJobSourceSnapshot>(new BoundedChannelOptions(2)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var producer = ProduceAsync();
        try
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(cancellationToken)) yield return batch;
        }
        finally
        {
            await stop.CancelAsync();
            await producer;
        }

        async Task ProduceAsync()
        {
            try
            {
                await foreach (var batch in FetchSequentialBatchesAsync(source, stop.Token))
                    await channel.Writer.WriteAsync(batch, stop.Token);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && checkpoint is { Enabled: true })
            { channel.Writer.TryComplete(new JobSourceContinuationException("ProviderRunBudgetExceeded")); }
            catch (Exception exception) { channel.Writer.TryComplete(exception); }
        }
    }

    private async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchSequentialBatchesAsync(
        JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = InitializeSource(source);
        var settings = Settings;
        ValidateFetchSettings(source, settings);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RunBudgetSeconds), Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
        var stopwatch = Stopwatch.StartNew();
        if (logger is not null) Started(logger, source.Id, null);
        var succeeded = 0;
        var failed = 0;
        var attempted = 0;
        var complete = false;
        var continuation = false;
        try
        {
            var client = clients.CreateClient(HttpClientName);
            var listingTimer = Stopwatch.StartNew();
            var inventory = await ReadListingAsync(client, target, source, token);
            var postings = inventory.Postings;
            var expectedTotal = inventory.Total;
            var rawEntriesRead = inventory.RawSlots;
            var duplicatePaths = inventory.Duplicates;
            var placeholderRows = inventory.Placeholders;
            var durable = checkpoint is { Enabled: true };
            if (durable)
            {
                var selection = options?.Value.SourceApprovals.GetValueOrDefault(source.Id.ToString("D"));
                var policyIdentity = JsonSerializer.Serialize(new
                {
                    IngestionPolicy = "AllTechnicallyValidSourceJobsV2",
                    Limit = selection?.TestImportLimit ?? 0
                });
                var sourceIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"v1|{source.Id:D}|{source.CompanyId:D}|{target.Origin}|{target.Tenant}|{target.Site}|{policyIdentity}")));
                // Changed membership restarts progress through normal deduplication.
                // Existing execution/time limits bound repeated live-board changes.
                var priorRebases = checkpoint!.State?.RebaseCount ?? 0;
                await checkpoint!.PrepareAsync(sourceIdentity, ListingIdentity(inventory), postings.Count, inventory.Pages, token, inventory.Diagnostics);
                if (logger is not null && checkpoint.State?.RebaseCount > priorRebases)
                    LogStage(logger, source.Id, "CheckpointRebased", postings.Count, listingTimer.Elapsed.TotalMilliseconds, 1);
            }
            var initial = durable ? checkpoint!.State : null;
            var infrastructureFailure = initial?.ListingDiagnostics?.InfrastructureFailure == true ? 1 : 0;
            var listingDiagnostics = inventory.Diagnostics with { InfrastructureFailure = infrastructureFailure != 0 };
            var cursor = initial?.Cursor ?? 0;
            succeeded = initial?.ObservedJobs.Count ?? 0;
            failed = initial?.ProviderSkipped ?? 0;
            attempted = cursor;
            if (durable) checkpoint!.Details.Begin(cursor, succeeded, failed);
            if (logger is not null) LogStage(logger, source.Id, "Listing", rawEntriesRead,
                listingTimer.Elapsed.TotalMilliseconds, 1);
            if (logger is not null)
                LogListingSummary(logger, source.Id, rawEntriesRead, postings.Count, duplicatePaths, placeholderRows, expectedTotal, false);
            var externalIds = new HashSet<string>(initial?.ObservedJobs.Select(x => x.ExternalId!) ?? [], StringComparer.Ordinal);
            var duplicateIds = false;
            var pending = postings.Skip(cursor).Take(durable ? settings.JobsPerExecution : postings.Count);
            foreach (var chunk in pending.Chunk(settings.BatchSize))
            {
                var detailTimer = Stopwatch.StartNew();
                token.ThrowIfCancellationRequested();
                var parsed = new ConcurrentDictionary<int, RawExternalJob>();
                var detailReasons = new ConcurrentDictionary<string,int>(StringComparer.Ordinal);
                var batchFailed = 0;
                await Parallel.ForEachAsync(chunk.Select((entry, index) => (entry, index)),
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Min(settings.DetailConcurrency, Volatile.Read(ref adaptiveConcurrency)), CancellationToken = token },
                    async (item, itemToken) =>
                    {
                        Interlocked.Increment(ref attempted);
                        if (durable) checkpoint!.Details.RecordAttempt();
                        try
                        {
                            var detail = await FetchDetailAsync(client, target, source, item.entry, source.Id, item.index, itemToken);
                            if (detail is null)
                            {
                                Interlocked.Increment(ref batchFailed);
                                detailReasons.AddOrUpdate("PROVIDER_RESPONSE_INVALID", 1, (_, count) => count + 1);
                                if (durable) checkpoint!.Details.RecordFailure();
                            }
                            else
                            {
                                parsed[item.index] = durable ? detail with { SourceOrdinal = cursor + item.index } : detail;
                                if (durable) checkpoint!.Details.RecordSuccess();
                            }
                        }
                        catch (OperationCanceledException) when (itemToken.IsCancellationRequested) { throw; }
                        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidDataException)
                        {
                            Interlocked.Increment(ref batchFailed);
                            detailReasons.AddOrUpdate(JobSourceFailure.Code(exception), 1, (_, count) => count + 1);
                            if (exception is HttpRequestException or TimeoutException) Interlocked.Exchange(ref infrastructureFailure, 1);
                            if (durable) checkpoint!.Details.RecordFailure();
                            if (logger is not null) LogSourceFailure(logger, source.Id, "detail_fetch", item.index,
                                exception is TimeoutException ? "RequestTimeoutExhausted" :
                                exception is InvalidDataException ? "InvalidDetailResponse" : "DetailHttpFailure",
                                exception is HttpRequestException http ? (int?)http.StatusCode : null);
                        }
                    });
                var jobs = parsed.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
                foreach (var job in jobs)
                    if (!externalIds.Add(job.ExternalId!)) duplicateIds = true;
                succeeded += jobs.Length;
                failed += batchFailed;
                if (logger is not null) LogBatch(logger, source.Id, attempted, succeeded, failed, stopwatch.Elapsed.TotalMilliseconds);
                if (logger is not null) LogStage(logger, source.Id, "DetailBatch", chunk.Length,
                    detailTimer.Elapsed.TotalMilliseconds, Math.Min(settings.DetailConcurrency, Volatile.Read(ref adaptiveConcurrency)));
                // Bounded producer can fetch the next detail batch while the sole
                // DbContext consumer commits this one. Backpressure bounds memory.
                cursor += chunk.Length;
                listingDiagnostics = listingDiagnostics with { InfrastructureFailure = infrastructureFailure != 0 };
                yield return new(jobs, batchFailed, false)
                { CheckpointCursor = durable ? cursor : null, ListingDiagnostics = listingDiagnostics with { ReconciliationSafe = false },
                    ProviderReasonCounts = detailReasons.IsEmpty ? null : new Dictionary<string,int>(detailReasons) };
            }
            token.ThrowIfCancellationRequested();
            // Processing completion carries no authority to infer that absent jobs closed.
            complete = cursor == postings.Count;
            continuation = durable && !complete;
            var finalDiagnostics = listingDiagnostics with
            {
                ProcessingComplete = complete,
                ReconciliationSafe = false,
                InfrastructureFailure = infrastructureFailure != 0,
                ReasonCode = infrastructureFailure != 0 ? "InfrastructureFailure" :
                    continuation ? "ExecutionSliceComplete" : failed > 0 ? "SkippedDetails" :
                    duplicateIds ? "DuplicateExternalIds" : listingDiagnostics.ReasonCode,
                RetryRecommended = infrastructureFailure != 0
            };
            LogSnapshot(source.Id, finalDiagnostics);
            yield return new(Array.Empty<RawExternalJob>(), 0, false)
            {
                ContinuationPending = durable && cursor < postings.Count,
                CheckpointCursor = durable ? cursor : null,
                ListingDiagnostics = finalDiagnostics
            };
        }
        finally
        {
            if (logger is not null)
            {
                Completed(logger, source.Id, attempted, succeeded, failed, stopwatch.Elapsed.TotalMilliseconds, null);
                LogRunEnded(logger, source.Id,
                    cancellationToken.IsCancellationRequested ? "CallerCancelled" :
                    budget.IsCancellationRequested ? "RunBudgetExceeded" :
                    complete ? "Complete" : continuation ? "ExecutionSliceComplete" : "Incomplete", attempted, stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }

    internal static WorkdayTarget ValidateSource(JobSource source)
    {
        var result = ValidateConfiguration(source);
        if (!result.IsValid) throw ConfigurationError(result);
        var configuration = result.Configuration!;
        return new(configuration.Origin, configuration.ApiRoot, configuration.Tenant, configuration.CareerSite);
    }

    private static WorkdaySourceValidation ValidateConfiguration(JobSource source) => source.AtsType == AtsType.Workday
        ? WorkdaySourceConfiguration.Validate(source.CareerPageUrl, source.AtsIdentifier)
        : new(null, "WORKDAY_CONFIGURATION_INVALID", "Source provider must be Workday.", "", "", "");

    private WorkdayTarget InitializeSource(JobSource source)
    {
        if (logger is not null) WorkdayExecutionDiagnostics.Input(logger, source);
        var result = ValidateConfiguration(source);
        if (logger is not null) WorkdayExecutionDiagnostics.Resolved(logger, source.Id, result);
        if (logger is not null && result.IsValid)
            LogConfiguration(logger, source.Id, source.Company?.Name ?? string.Empty, "Workday", result.Host,
                result.Tenant, result.CareerSite, "ConfigurationValidation", result.IsValid ? "" : nameof(InvalidOperationException),
                result.ReasonCode, result.Message);
        if (!result.IsValid)
        {
            LogConfigurationFailure(source, result, "ConfigurationValidation");
            throw ConfigurationError(result);
        }
        var configuration = result.Configuration!;
        return new(configuration.Origin, configuration.ApiRoot, configuration.Tenant, configuration.CareerSite);
    }

    [LoggerMessage(EventId = 4393, Level = LogLevel.Information,
        Message = "WorkdayProviderConfiguration SourceId={SourceId} SourceName={SourceName} Provider={Provider} Host={Host} Tenant={Tenant} CareerSite={CareerSite} Stage={Stage} ErrorType={ErrorType} ReasonCode={ReasonCode} Message={DiagnosticMessage}.")]
    private static partial void LogConfiguration(ILogger logger, Guid sourceId, string sourceName, string provider,
        string host, string tenant, string careerSite, string stage, string errorType, string reasonCode, string diagnosticMessage);

    private void ValidateFetchSettings(JobSource source, WorkdayFetchOptions settings)
    {
        if (settings.IsValid()) return;
        var configuration = ValidateConfiguration(source);
        LogConfigurationFailure(source, configuration with
            { ReasonCode = "WORKDAY_CONFIGURATION_INVALID", Message = "Invalid bounded Workday fetch settings." }, "FetchSettings");
        throw ConfigurationError(configuration with { ReasonCode = "CONFIGURATION_INVALID", Message = "Invalid bounded Workday fetch settings." });
    }

    private static InvalidOperationException ConfigurationError(WorkdaySourceValidation result)
    {
        var error = new InvalidOperationException(result.Message);
        error.Data["JobSourceReasonCode"] = result.ReasonCode;
        return error;
    }

    private void LogConfigurationFailure(JobSource source, WorkdaySourceValidation result, string stage)
    {
        if (logger is not null) WorkdayExecutionDiagnostics.Rejected(logger, source.Id, result);
        if (logger is not null)
            LogFailure(logger, source.Id, source.Company?.Name ?? string.Empty, "Workday", result.Host,
                result.Tenant, result.CareerSite, stage, nameof(InvalidOperationException), result.ReasonCode, result.Message);
    }

    [LoggerMessage(EventId = 4394, Level = LogLevel.Warning,
        Message = "WorkdayProviderFailure SourceId={SourceId} SourceName={SourceName} Provider={Provider} Host={Host} Tenant={Tenant} CareerSite={CareerSite} Stage={Stage} ErrorType={ErrorType} ReasonCode={ReasonCode} Message={DiagnosticMessage}.")]
    private static partial void LogFailure(ILogger logger, Guid sourceId, string sourceName, string provider,
        string host, string tenant, string careerSite, string stage, string errorType, string reasonCode, string diagnosticMessage);
    internal static ListingPage ParseListingPage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw InvalidDocument(
                FailureReason.ResponseJsonInvalid,
                "Workday returned an empty JSON response.");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var total = 0;
            var hasTotal = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("total", out _);
            if (root.ValueKind != JsonValueKind.Object ||
                (hasTotal && (root.GetProperty("total").ValueKind != JsonValueKind.Number ||
                    !root.GetProperty("total").TryGetInt32(out total) || total < 0)) ||
                !root.TryGetProperty("jobPostings", out var postingsElement) ||
                postingsElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidDocument(
                    FailureReason.ListingSchemaInvalid,
                    "Workday listing response did not contain the expected total/jobPostings schema.");
            }

            var rawEntryCount = postingsElement.GetArrayLength();
            if (rawEntryCount > PageSize)
                throw InvalidDocument(FailureReason.ListingSchemaInvalid, "Workday returned more than the requested page size.");
            var postings = new List<ListingEntry>();

            foreach (var item in postingsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var title = GetString(item, "title")?.Trim();
                var externalPath = GetString(item, "externalPath")?.Trim();
                var locationsText = GetString(item, "locationsText")?.Trim();
                var postedOn = GetString(item, "postedOn")?.Trim();

                var titleMissing = string.IsNullOrWhiteSpace(title);
                var pathMissing = string.IsNullOrWhiteSpace(externalPath);

                // Some Workday tenants return non-job placeholder cards containing
                // only bulletFields. A row with neither a title nor an externalPath
                // cannot be fetched and is ignored without hiding partially corrupt jobs.
                if (titleMissing && pathMissing)
                    continue;

                if (titleMissing ||
                    !TryValidateExternalPath(externalPath, out var normalizedPath))
                {
                    // Unsafe paths are never requested; skip only this row.
                    continue;
                }

                postings.Add(new ListingEntry(
                    title!,
                    normalizedPath,
                    locationsText,
                    postedOn,
                    FirstString(item, "bulletFields")));
            }

            return new ListingPage(
                total,
                rawEntryCount,
                postings)
            { HasReportedTotal = hasTotal };
        }
        catch (JsonException exception)
        {
            throw InvalidDocument(
                FailureReason.ResponseJsonInvalid,
                "Workday returned malformed JSON.",
                exception);
        }
    }

    internal static RawExternalJob? ParseDetail(
        string json,
        WorkdayTarget target,
        JobSource source,
        ListingEntry listing)
        => ParseDetail(json, target, source, listing, out _);

    private static RawExternalJob? ParseDetail(string json, WorkdayTarget target,
        JobSource source, ListingEntry listing, out string reason)
    {
        reason = "DetailSchemaInvalid";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jobPostingInfo", out var info) ||
                info.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var title = GetString(info, "title")?.Trim();
            var description = GetString(info, "jobDescription") ?? string.Empty;
            var externalId =
                GetString(info, "jobReqId")?.Trim() ??
                GetString(info, "jobPostingId")?.Trim() ??
                listing.BulletId?.Trim() ??
                listing.ExternalPath;

            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(externalId))
            {
                reason = string.IsNullOrWhiteSpace(title) ? "DetailTitleMissing" :
                    "DetailIdentityMissing";
                return null;
            }

            var location =
                GetString(info, "location")?.Trim() ??
                listing.LocationsText;

            var additionalLocations = ReadStringArray(
                info,
                "additionalLocations");

            var countryCodes = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            if (info.TryGetProperty("jobRequisitionLocation", out var reqLocation) &&
                reqLocation.ValueKind == JsonValueKind.Object &&
                reqLocation.TryGetProperty("country", out var country) &&
                country.ValueKind == JsonValueKind.Object)
            {
                var alpha2 = GetString(country, "alpha2Code")?.Trim();
                if (!string.IsNullOrWhiteSpace(alpha2))
                    countryCodes.Add(alpha2);

                var descriptor = GetString(country, "descriptor")?.Trim();
                if (string.IsNullOrWhiteSpace(alpha2) && !string.IsNullOrWhiteSpace(descriptor))
                    countryCodes.Add(descriptor);
            }

            if (info.TryGetProperty("country", out var directCountry) &&
                directCountry.ValueKind == JsonValueKind.Object)
            {
                var alpha2 = GetString(directCountry, "alpha2Code")?.Trim();
                if (!string.IsNullOrWhiteSpace(alpha2))
                    countryCodes.Add(alpha2);

                var descriptor = GetString(directCountry, "descriptor")?.Trim();
                if (string.IsNullOrWhiteSpace(alpha2) && !string.IsNullOrWhiteSpace(descriptor))
                    countryCodes.Add(descriptor);
            }

            var timeType = GetString(info, "timeType")?.Trim();
            var remoteType = GetString(info, "remoteType")?.Trim();

            var publicJobUri = new Uri(
                target.Origin,
                $"en-US/{Uri.EscapeDataString(target.Site)}{listing.ExternalPath}");

            return new RawExternalJob
            {
                ExternalId = externalId,
                Title = title,
                CompanyName = source.Company?.Name ?? string.Empty,
                Location = location,
                AdditionalLocations = additionalLocations,
                CountryCodes = countryCodes.ToArray(),
                Description = description,
                DescriptionIsHtml = true,
                ExternalCategory = ReadSpecialism(description),
                ApplicationUrl = publicJobUri.AbsoluteUri,
                SourcePostedAtUtc = ParseIsoDate(
                    GetString(info, "startDate")),
                EmploymentType = ParseEmploymentType(timeType),
                EmploymentTypeText = timeType,
                WorkplaceType = ParseWorkplaceType(remoteType),
                WorkplaceTypeText = remoteType
            };
        }
        catch (JsonException)
        {
            reason = "DetailJsonInvalid";
            return null;
        }
    }

    private static Dictionary<string, string[]> GetAppliedFacets(
        WorkdayTarget target)
    {
        return [];
    }

    private static string? ReadSpecialism(string description)
    {
        try
        {
            var match = Specialism().Match(description);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    [GeneratedRegex(@"<b>\s*Specialism\s*</b>\s*</p>\s*([^<]{1,200})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex Specialism();


    private async Task<ListingInventory> ReadListingAsync(HttpClient client, WorkdayTarget target,
        JobSource source, CancellationToken token)
    {
        var postings = new List<ListingEntry>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var rawSlots = 0;
        var duplicates = 0;
        var placeholders = 0;
        int? initialTotal = null;
        int? finalTotal = null;
        var targetTotal = 0;
        var changed = false;
        var repeated = false;
        var pages = 0;
        var lastOffset = 0;
        var lastSize = 0;
        // The hard request/slot bound is independent of a mutable advertised total.
        for (var offset = 0; offset < MaxJobs; offset += PageSize)
        {
            token.ThrowIfCancellationRequested();
            var page = await FetchListingPageAsync(client, target, source.Id, offset, token);
            pages++;
            lastOffset = offset;
            lastSize = page.RawEntryCount;
            if (initialTotal is null)
            {
                if (!page.HasReportedTotal)
                    throw LoggedInvalidDocument(source.Id, "listing_parse", offset, page.StatusCode,
                        FailureReason.ListingTotalInvalid, "The initial Workday page must report a total.");
                if (page.Total > MaxJobs)
                    throw LoggedInvalidDocument(source.Id, "listing_parse", offset, page.StatusCode,
                        FailureReason.ListingRecordLimitExceeded, "Workday listing exceeds the configured safe limit.");
                initialTotal = page.Total;
            }
            var omitted = offset > 0 && (!page.HasReportedTotal || page.Total == 0);
            if (!omitted)
            {
                finalTotal = page.Total;
                changed |= page.Total != initialTotal;
                targetTotal = Math.Min(MaxJobs, Math.Max(targetTotal, page.Total));
            }
            if (logger is not null) LogListingValidation(logger, source.Id, "page", offset,
initialTotal ?? page.Total, page.Total, page.RawEntryCount, omitted);
            var signature = string.Join("|", page.Postings.Select(x => x.ExternalPath).Order(StringComparer.Ordinal));
            if (page.Postings.Count > 0 && !signatures.Add(signature))
            {
                repeated = true;
                break;
            }
            rawSlots += page.RawEntryCount;
            placeholders += page.RawEntryCount - page.Postings.Count;
            foreach (var posting in page.Postings)
            {
                if (seenPaths.Add(posting.ExternalPath)) postings.Add(posting);
                else duplicates++;
            }
            if (page.RawEntryCount < PageSize || (targetTotal > 0 && rawSlots >= targetTotal)) break;
            await Task.Delay(RequestSpacing, token);
        }
        var reason = repeated ? "RepeatedPage" : duplicates > 0 ? "DuplicateListingPaths" :
            placeholders > 0 ? "ListingPlaceholders" : changed ? "ListingTotalDrift" :
            rawSlots != initialTotal ? "ListingEndedEarly" : initialTotal == SuspectedSearchCap ? "SuspectedSearchCap" : "Complete";
        var diagnostics = new WorkdayListingDiagnostics
        {
            InitialReportedTotal = initialTotal ?? 0, FinalReportedTotal = finalTotal,
            RawSlots = rawSlots, UniquePaths = postings.Count, DuplicatePaths = duplicates,
            PlaceholderCount = placeholders, LastOffset = lastOffset, LastPageSize = lastSize,
            PageCount = pages, SnapshotStable = false, IngestionSafe = true,
            ReconciliationSafe = false, ReasonCode = reason, RetryRecommended = false
        };
        LogSnapshot(source.Id, diagnostics);
        return new(postings, initialTotal ?? 0, rawSlots, duplicates, placeholders, pages, diagnostics);
    }

    private static string ListingIdentity(ListingInventory inventory) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(inventory.Postings.Select(x => new
        { x.ExternalPath, x.Title, x.LocationsText, x.BulletId })))));

    private sealed record ListingInventory(List<ListingEntry> Postings, int Total, int RawSlots, int Duplicates,
        int Placeholders, int Pages, WorkdayListingDiagnostics Diagnostics);

    private async Task<ListingPageWithStatus> FetchListingPageAsync(
        HttpClient client,
        WorkdayTarget target,
        Guid sourceId,
        int offset,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            target.ApiRoot.AbsoluteUri.TrimEnd('/') + "/jobs");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            uri)
        {
            Content = JsonContent.Create(new
            {
                appliedFacets = GetAppliedFacets(target),
                limit = PageSize,
                offset,
                searchText = string.Empty
            })
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.AcceptLanguage.ParseAdd("en-US");

        var response = await SendAsync(
            client,
            request,
            sourceId,
            "listing_response",
            offset,
            cancellationToken);

        ListingPage parsed;
        try
        {
            parsed = ParseListingPage(response.Body);
        }
        catch (InvalidDataException exception)
        {
            LogInvalidDocument(
                sourceId,
                "listing_parse",
                offset,
                response.StatusCode,
                exception);

            throw;
        }

        return new ListingPageWithStatus(
            parsed.Total,
            parsed.RawEntryCount,
            parsed.Postings,
            response.StatusCode)
        { HasReportedTotal = parsed.HasReportedTotal };
    }

    private async Task<RawExternalJob?> FetchDetailAsync(
        HttpClient client,
        WorkdayTarget target,
        JobSource source,
        ListingEntry listing,
        Guid sourceId,
        int index,
        CancellationToken cancellationToken)
    {
        var detailUri = new Uri(
            target.ApiRoot.AbsoluteUri.TrimEnd('/') +
            listing.ExternalPath);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            detailUri);

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.AcceptLanguage.ParseAdd("en-US");

        var response = await SendAsync(
            client,
            request,
            sourceId,
            "detail_response",
            index,
            cancellationToken);

        var detail = ParseDetail(
            response.Body,
            target,
            source,
            listing, out var reason);
        if (detail is null && logger is not null)
            LogSourceFailure(logger, sourceId, "detail_parse", index, reason, (int)response.StatusCode);
        return detail;
    }

    private async Task<JsonResponse> SendAsync(
        HttpClient client, HttpRequestMessage request, Guid sourceId, string stage,
        int offset, CancellationToken cancellationToken)
    {
        // Recreate the public, read-only CXS search POST as well as GET requests.
        // A retry never reuses a previously sent HttpRequestMessage.
        var content = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var rateLimitKey = "Workday:" + request.RequestUri!.Host;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (true)
            {
                var deferred = workdayRetryState.Remaining(rateLimitKey, Clock.GetUtcNow());
                if (deferred > TimeSpan.FromSeconds(30))
                    throw new HttpRequestException("Workday host remains rate limited.", null, HttpStatusCode.TooManyRequests);
                if (deferred > TimeSpan.Zero) await Task.Delay(deferred, Clock, cancellationToken);
                // Every attempt respects spacing; recheck cooldown after waiting
                // because another in-flight request may just have returned 429.
                await PaceRequestAsync(cancellationToken);
                if (workdayRetryState.Remaining(rateLimitKey, Clock.GetUtcNow()) <= TimeSpan.Zero) break;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Settings.RequestTimeoutSeconds), Clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var copy = new HttpRequestMessage(request.Method, request.RequestUri)
            { Version = client.DefaultRequestVersion, VersionPolicy = client.DefaultVersionPolicy };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (content is not null)
            {
                copy.Content = new ByteArrayContent(content);
                foreach (var header in request.Content!.Headers) copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt - 1) * 1000 + Random.Shared.Next(0, 251));
            try
            {
                return await SendAttemptAsync(client, copy, sourceId, stage, offset, linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (logger is not null) LogSourceFailure(logger, sourceId, stage, offset, "CallerOrRunBudgetCancelled", null);
                throw;
            }
            catch (OperationCanceledException)
            {
                if (logger is not null) LogSourceFailure(logger, sourceId, stage, offset, "RequestTimeout", null);
                if (attempt >= Settings.MaximumAttempts) throw new TimeoutException("Workday request attempts timed out.");
            }
            catch (HttpRequestException exception) when (exception.StatusCode is null or HttpStatusCode.TooManyRequests or
                HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                if (exception.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    int observed;
                    int reduced;
                    do
                    {
                        observed = Volatile.Read(ref adaptiveConcurrency);
                        reduced = Math.Max(1, Math.Min(Settings.DetailConcurrency, observed) / 2);
                    } while (Interlocked.CompareExchange(ref adaptiveConcurrency, reduced, observed) != observed);
                }
                if (exception.Data["RetryAfter"] is TimeSpan retryAfter)
                {
                    if (exception.StatusCode == HttpStatusCode.TooManyRequests && retryAfter > TimeSpan.Zero)
                        workdayRetryState.Defer(rateLimitKey, Clock.GetUtcNow() + retryAfter);
                    // Never retry earlier than Retry-After. Long throttles fail this detail closed
                    // instead of tying up a worker; the next cooled-down run can safely replay it.
                    if (retryAfter > TimeSpan.FromSeconds(30)) throw;
                    if (retryAfter > delay) delay = retryAfter;
                }
                if (attempt >= Settings.MaximumAttempts) throw;
            }
            if (logger is not null) LogSourceFailure(logger, sourceId, stage, offset, "TransientRetry", null);
            await Task.Delay(delay, Clock, cancellationToken);
        }
    }

    private async Task<JsonResponse> SendAttemptAsync(
        HttpClient client,
        HttpRequestMessage request,
        Guid sourceId,
        string stage,
        int offset,
        CancellationToken cancellationToken)
    {
        int? status = null;

        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var failure = new HttpRequestException("Workday returned an unsuccessful HTTP status.", null, response.StatusCode);
                var retryAfter = response.Headers.RetryAfter;
                if (retryAfter is not null)
                    failure.Data["RetryAfter"] = retryAfter.Delta ?? (retryAfter.Date!.Value - Clock.GetUtcNow());
                throw failure;
            }
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType) &&
                !mediaType.Contains(
                    "json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidDocument(
                    FailureReason.ResponseContentTypeInvalid,
                    "Workday returned a non-JSON response.");
            }

            if (response.Content.Headers.ContentLength is > MaxJsonBytes)
                throw InvalidDocument(
                    FailureReason.ResponseDeclaredSizeExceeded,
                    "Workday returned an oversized JSON response.");

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using var output = new MemoryStream();

            var buffer = new byte[8192];
            int count;

            while ((count = await stream.ReadAsync(
                       buffer,
                       cancellationToken)) > 0)
            {
                if (output.Length + count > MaxJsonBytes)
                    throw InvalidDocument(
                        FailureReason.ResponseReadSizeExceeded,
                        "Workday returned an oversized JSON response.");

                await output.WriteAsync(
                    buffer.AsMemory(0, count),
                    cancellationToken);
            }

            var charset =
                response.Content.Headers.ContentType?.CharSet?.Trim('"');

            Encoding encoding;

            try
            {
                encoding = string.IsNullOrWhiteSpace(charset)
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }

            return new JsonResponse(
                encoding.GetString(output.ToArray()),
                (int)response.StatusCode);
        }
        catch (InvalidDataException exception)
        {
            LogInvalidDocument(
                sourceId,
                stage,
                offset,
                status,
                exception);

            throw;
        }
        catch (HttpRequestException exception)
        {
            if (logger is not null)
            {
                LogSourceFailure(
                    logger,
                    sourceId,
                    stage,
                    offset,
                    status is >= 300 ||
                    exception.StatusCode is { } errorStatus &&
                    (int)errorStatus >= 300
                        ? "HttpStatusFailure"
                        : status.HasValue
                            ? "ResponseReadTransportFailure"
                            : "HttpTransportFailure",
                    status ??
                    (exception.StatusCode is { } code
                        ? (int)code
                        : null));
            }

            throw;
        }
    }

    private InvalidDataException LoggedInvalidDocument(
        Guid sourceId,
        string stage,
        int offset,
        int? status,
        FailureReason reason,
        string message)
    {
        var exception = InvalidDocument(
            reason,
            message);

        LogInvalidDocument(
            sourceId,
            stage,
            offset,
            status,
            exception);

        return exception;
    }

    private static InvalidDataException InvalidDocument(
        FailureReason reason,
        string message,
        Exception? inner = null)
    {
        var exception = inner is null
            ? new InvalidDataException(message)
            : new InvalidDataException(message, inner);

        exception.Data[ReasonDataKey] = reason;
        return exception;
    }

    private void LogInvalidDocument(
        Guid sourceId,
        string stage,
        int offset,
        int? status,
        InvalidDataException exception)
    {
        var reason =
            exception.Data[ReasonDataKey] is FailureReason known
                ? known
                : FailureReason.ResponseJsonInvalid;

        if (logger is not null)
            LogSourceFailure(
                logger,
                sourceId,
                stage,
                offset,
                reason.ToString(),
                status);
    }

    private static bool TryValidateExternalPath(
        string? value,
        out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(
                "/job/",
                StringComparison.Ordinal) ||
            value.Contains(
                "..",
                StringComparison.Ordinal) ||
            value.Contains(
                '?',
                StringComparison.Ordinal) ||
            value.Contains(
                '#',
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(
                new Uri("https://example.invalid/"),
                value,
                out var parsed) ||
            parsed.IdnHost != "example.invalid" ||
            !parsed.AbsolutePath.StartsWith(
                "/job/",
                StringComparison.Ordinal))
        {
            return false;
        }

        normalized = parsed.AbsolutePath;
        return true;
    }

    private static string? GetString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? FirstString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var text = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string[] ReadStringArray(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTime? ParseIsoDate(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.UtcDateTime;
        }

        return null;
    }

    private static EmploymentType? ParseEmploymentType(
        string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "FULL TIME" or "FULL-TIME" =>
                EmploymentType.FullTime,

            "PART TIME" or "PART-TIME" =>
                EmploymentType.PartTime,

            "CONTRACT" or "CONTRACTOR" =>
                EmploymentType.Contract,

            "INTERN" or "INTERNSHIP" =>
                EmploymentType.Internship,

            "TEMPORARY" =>
                EmploymentType.Temporary,

            _ => null
        };

    private static WorkplaceType? ParseWorkplaceType(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Contains(
                "remote",
                StringComparison.OrdinalIgnoreCase))
            return WorkplaceType.Remote;

        if (value.Contains(
                "hybrid",
                StringComparison.OrdinalIgnoreCase))
            return WorkplaceType.Hybrid;

        if (value.Contains(
                "on-site",
                StringComparison.OrdinalIgnoreCase) ||
            value.Contains(
                "onsite",
                StringComparison.OrdinalIgnoreCase))
            return WorkplaceType.OnSite;

        return null;
    }

    internal sealed record WorkdayTarget(
        Uri Origin,
        Uri ApiRoot,
        string Tenant,
        string Site);

    internal sealed record ListingEntry(
        string Title,
        string ExternalPath,
        string? LocationsText,
        string? PostedOn,
        string? BulletId);

    internal sealed record ListingPage(
        int Total,
        int RawEntryCount,
        IReadOnlyCollection<ListingEntry> Postings)
    {
        public bool HasReportedTotal { get; init; } = true;
    }

    private sealed record ListingPageWithStatus(
        int Total,
        int RawEntryCount,
        IReadOnlyCollection<ListingEntry> Postings,
        int StatusCode)
    {
        public bool HasReportedTotal { get; init; } = true;
    }

    private sealed record JsonResponse(
        string Body,
        int StatusCode);

    private enum FailureReason
    {
        ListingTotalInvalid,
        ListingSchemaInvalid,
        ListingEntryInvalid,
        ListingRecordLimitExceeded,
        ListingEndedEarly,
        ListingDuplicatePaths,
        ListingSnapshotChanged,
        ListingSnapshotCountMismatch,
        ResponseContentTypeInvalid,
        ResponseDeclaredSizeExceeded,
        ResponseReadSizeExceeded,
        ResponseJsonInvalid
    }
}
