using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
    AggregationHttpRetryState? retryState = null) : IBatchedExternalJobProvider, IDisposable
{
    public const string HttpClientName = "WorkdayJobAggregation";

    // Workday's public CXS listing endpoint is capped at 20 items per request.
    private const int PageSize = 20;
    private const int MaxJobs = 10_000;
    // Some Workday searches advertise exactly 2,000 despite larger facet counts.
    // Treat that boundary as capped, never as proof of full inventory.
    private const int SuspectedSearchCap = 2_000;
    private const int MaxJsonBytes = 4 * 1024 * 1024;

    // Accenture currently exposes India jobs through this Workday country facet.
    // Keep this provider-specific mapping fail-closed: other Workday tenants are
    // left unfiltered until their country facet is explicitly verified.
    private const string AccentureTenant = "accenture";
    private const string AccentureSite = "AccentureCareers";
    private const string IndiaCountryFacetId = "c4f78be1a8f14da0ab49ce1162348a5e";

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
        await foreach (var batch in FetchBatchesAsync(source, cancellationToken))
        {
            jobs.AddRange(batch.Jobs);
            skipped += batch.Skipped;
            complete = batch.IsComplete;
        }
        return new(jobs, skipped, complete);
    }

    public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(
        JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Settings.IsValid()) throw new InvalidOperationException("Invalid bounded Workday fetch settings.");
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
            catch (Exception exception) { channel.Writer.TryComplete(exception); }
        }
    }

    private async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchSequentialBatchesAsync(
        JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = ValidateSource(source);
        var settings = Settings;
        if (!settings.IsValid()) throw new InvalidOperationException("Invalid bounded Workday fetch settings.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RunBudgetSeconds), Clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
        var stopwatch = Stopwatch.StartNew();
        if (logger is not null) Started(logger, source.Id, null);
        var succeeded = 0;
        var failed = 0;
        var attempted = 0;
        var complete = false;
        try
        {
            var client = clients.CreateClient(HttpClientName);
            var listingTimer = Stopwatch.StartNew();
            var postings = new List<ListingEntry>();
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            var rawEntriesRead = 0;
            var duplicatePaths = 0;
            var placeholderRows = 0;
            int? expectedTotal = null;
            var listingChanged = false;
            var pageSignatures = new HashSet<string>(StringComparer.Ordinal);

            for (var offset = 0; ; offset += PageSize)
            {
                token.ThrowIfCancellationRequested();

                var page = await FetchListingPageAsync(
                    client,
                    target,
                    source.Id,
                    offset,
                    token);

                if (expectedTotal.HasValue && page.Total != expectedTotal.Value) listingChanged = true;
                var signature = string.Join("|", page.Postings.Select(x => x.ExternalPath));
                if (page.Postings.Count > 0 && !pageSignatures.Add(signature))
                {
                    listingChanged = true;
                    if (logger is not null) LogSourceFailure(logger, source.Id, "listing_snapshot", offset, "RepeatedPage", page.StatusCode);
                    break;
                }
                rawEntriesRead += page.RawEntryCount;
                placeholderRows += page.RawEntryCount - page.Postings.Count;

                if (expectedTotal is null)
                {
                    expectedTotal = page.Total;

                    if (expectedTotal < 0 || expectedTotal > MaxJobs)
                        throw LoggedInvalidDocument(
                            source.Id,
                            "listing_parse",
                            offset,
                            page.StatusCode,
                            expectedTotal > MaxJobs
                                ? FailureReason.ListingRecordLimitExceeded
                                : FailureReason.ListingTotalInvalid,
                            "The Workday listing total is invalid or exceeds the configured safe limit.");
                }

                if (expectedTotal == 0)
                {
                    if (offset != 0 || page.RawEntryCount != 0 || page.Postings.Count != 0)
                        throw LoggedInvalidDocument(
                            source.Id,
                            "listing_snapshot",
                            offset,
                            page.StatusCode,
                            FailureReason.ListingSnapshotChanged,
                            "The Workday listing changed while reading the snapshot.");

                    break;
                }

                if (page.RawEntryCount == 0)
                    throw LoggedInvalidDocument(
                        source.Id,
                        "listing_snapshot",
                        offset,
                        page.StatusCode,
                        FailureReason.ListingEndedEarly,
                        "The Workday listing ended before the first-page total was reached.");

                foreach (var posting in page.Postings)
                {
                    if (!seenPaths.Add(posting.ExternalPath))
                    {
                        // Workday can repeat a posting across pages. Count raw slots
                        // for pagination but fetch each unique job only once.
                        duplicatePaths++;
                        continue;
                    }

                    postings.Add(posting);
                }

                if (rawEntriesRead >= expectedTotal)
                    break;

                if (page.RawEntryCount < PageSize)
                    throw LoggedInvalidDocument(
                        source.Id,
                        "listing_snapshot",
                        offset,
                        page.StatusCode,
                        FailureReason.ListingEndedEarly,
                        "The Workday listing returned a short raw page before the first-page total was reached.");

                await Task.Delay(RequestSpacing, token);
            }

            if (!listingChanged && (expectedTotal is null || rawEntriesRead < expectedTotal))
                throw LoggedInvalidDocument(
                    source.Id,
                    "listing_snapshot",
                    rawEntriesRead,
                    null,
                    FailureReason.ListingSnapshotCountMismatch,
                    "The Workday listing did not produce the complete advertised raw snapshot.");


            if (logger is not null) LogStage(logger, source.Id, "Listing", rawEntriesRead,
                listingTimer.Elapsed.TotalMilliseconds, 1);
            if (logger is not null)
                LogListingSummary(logger, source.Id, rawEntriesRead, postings.Count, duplicatePaths, placeholderRows, expectedTotal, false);
            var externalIds = new HashSet<string>(StringComparer.Ordinal);
            var duplicateIds = false;
            foreach (var chunk in postings.Chunk(settings.BatchSize))
            {
                var detailTimer = Stopwatch.StartNew();
                token.ThrowIfCancellationRequested();
                var parsed = new ConcurrentDictionary<int, RawExternalJob>();
                var batchFailed = 0;
                await Parallel.ForEachAsync(chunk.Select((entry, index) => (entry, index)),
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Min(settings.DetailConcurrency, Volatile.Read(ref adaptiveConcurrency)), CancellationToken = token },
                    async (item, itemToken) =>
                    {
                        Interlocked.Increment(ref attempted);
                        try
                        {
                            var detail = await FetchDetailAsync(client, target, source, item.entry, source.Id, item.index, itemToken);
                            if (detail is null)
                            {
                                Interlocked.Increment(ref batchFailed);
                            }
                            else parsed[item.index] = detail;
                        }
                        catch (OperationCanceledException) when (itemToken.IsCancellationRequested) { throw; }
                        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidDataException)
                        {
                            Interlocked.Increment(ref batchFailed);
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
                yield return new(jobs, batchFailed, false);
            }
            token.ThrowIfCancellationRequested();
            complete = !listingChanged && duplicatePaths == 0 && placeholderRows == 0 &&
                expectedTotal != SuspectedSearchCap && failed == 0 && !duplicateIds &&
                succeeded == postings.Count && rawEntriesRead == expectedTotal;
            if (!complete && logger is not null)
                LogSourceFailure(logger, source.Id, "snapshot", rawEntriesRead,
                    listingChanged ? "ListingSnapshotChanged" :
                    duplicatePaths > 0 ? "DuplicateListingPaths" :
                    placeholderRows > 0 ? "ListingPlaceholders" :
                    expectedTotal == SuspectedSearchCap ? "SuspectedSearchCap" :
                    failed > 0 ? "FailedDetails" : duplicateIds ? "DuplicateExternalIds" : "SnapshotCountMismatch", null);
            if (logger is not null)
                LogListingSummary(logger, source.Id, rawEntriesRead, postings.Count, duplicatePaths, placeholderRows, expectedTotal, complete);
            yield return new(Array.Empty<RawExternalJob>(), 0, complete);
        }
        finally
        {
            if (logger is not null)
            {
                Completed(logger, source.Id, attempted, succeeded, failed, stopwatch.Elapsed.TotalMilliseconds, null);
                LogRunEnded(logger, source.Id,
                    cancellationToken.IsCancellationRequested ? "CallerCancelled" :
                    budget.IsCancellationRequested ? "RunBudgetExceeded" :
                    complete ? "Complete" : "Incomplete", attempted, stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }

    internal static WorkdayTarget ValidateSource(JobSource source)
    {
        if (source.AtsType != AtsType.Workday ||
            string.IsNullOrWhiteSpace(source.AtsIdentifier) ||
            !Uri.TryCreate(source.CareerPageUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.HostNameType != UriHostNameType.Dns ||
            uri.IsLoopback)
        {
            throw new InvalidOperationException(
                "Workday source must use a public HTTPS myworkdayjobs.com career-site URL and an ATS identifier in tenant/site format.");
        }

        var hostMatch = WorkdayHostRegex().Match(uri.IdnHost);
        if (!hostMatch.Success)
            throw new InvalidOperationException(
                "Workday source host must match {tenant}.wd{number}.myworkdayjobs.com.");

        var tenant = hostMatch.Groups["tenant"].Value;
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        string site;
        if (segments.Length == 1)
        {
            site = segments[0];
        }
        else if (segments.Length == 2 && LocaleRegex().IsMatch(segments[0]))
        {
            site = segments[1];
        }
        else
        {
            throw new InvalidOperationException(
                "Workday CareerPageUrl must point to the Workday site root, for example https://tenant.wdN.myworkdayjobs.com/SiteName.");
        }

        if (!SiteRegex().IsMatch(site))
            throw new InvalidOperationException("Workday career-site name contains unsupported characters.");

        var expectedIdentifier = $"{tenant}/{site}";
        if (!string.Equals(
                source.AtsIdentifier.Trim(),
                expectedIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workday ATS identifier must exactly match '{expectedIdentifier}' for this CareerPageUrl.");
        }

        var origin = new UriBuilder(Uri.UriSchemeHttps, uri.IdnHost).Uri;
        var apiRoot = new Uri(
            origin,
            $"wday/cxs/{Uri.EscapeDataString(tenant)}/{Uri.EscapeDataString(site)}");

        return new WorkdayTarget(
            origin,
            apiRoot,
            tenant,
            site);
    }

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

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("total", out var totalElement) ||
                totalElement.ValueKind != JsonValueKind.Number ||
                !totalElement.TryGetInt32(out var total) ||
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
                    throw InvalidDocument(
                        FailureReason.ListingSchemaInvalid,
                        "Workday listing contained a malformed posting.");

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
                    throw InvalidDocument(
                        FailureReason.ListingEntryInvalid,
                        "Workday listing contained a partially malformed posting.");
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
                postings);
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
            var description = GetString(info, "jobDescription");
            var externalId =
                GetString(info, "jobReqId")?.Trim() ??
                GetString(info, "jobPostingId")?.Trim() ??
                listing.BulletId?.Trim() ??
                listing.ExternalPath;

            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(externalId))
            {
                reason = string.IsNullOrWhiteSpace(title) ? "DetailTitleMissing" :
                    string.IsNullOrWhiteSpace(description) ? "DetailDescriptionMissing" : "DetailIdentityMissing";
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
                if (string.Equals(
                        descriptor,
                        "India",
                        StringComparison.OrdinalIgnoreCase))
                {
                    countryCodes.Add("IN");
                }
            }

            if (info.TryGetProperty("country", out var directCountry) &&
                directCountry.ValueKind == JsonValueKind.Object)
            {
                var alpha2 = GetString(directCountry, "alpha2Code")?.Trim();
                if (!string.IsNullOrWhiteSpace(alpha2))
                    countryCodes.Add(alpha2);

                var descriptor = GetString(directCountry, "descriptor")?.Trim();
                if (string.Equals(
                        descriptor,
                        "India",
                        StringComparison.OrdinalIgnoreCase))
                {
                    countryCodes.Add("IN");
                }
            }

            if (IsAccentureIndiaSource(target))
            {
                // The verified listing facet proves India eligibility when detail
                // geography is absent. Explicit contradictory geography must fail closed.
                if (countryCodes.Any(code => code.Trim().ToUpperInvariant() is not ("IN" or "IND" or "INDIA")))
                {
                    reason = "DetailGeographyContradictsIndiaFacet";
                    return null;
                }
                countryCodes.Add("IN");
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
        if (IsAccentureIndiaSource(target))
        {
            return new Dictionary<string, string[]>
            {
                ["locationCountry"] = [IndiaCountryFacetId]
            };
        }

        return [];
    }

    private static bool IsAccentureIndiaSource(WorkdayTarget target) =>
        string.Equals(
            target.Tenant,
            AccentureTenant,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            target.Site,
            AccentureSite,
            StringComparison.OrdinalIgnoreCase);

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
            response.StatusCode);
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
                HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
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

    [GeneratedRegex(
        @"^(?<tenant>[a-z0-9][a-z0-9-]{0,62})\.wd[0-9]+\.myworkdayjobs\.com$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorkdayHostRegex();

    [GeneratedRegex(
        @"^[a-z]{2}-[a-z]{2}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocaleRegex();

    [GeneratedRegex(
        @"^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SiteRegex();

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
        IReadOnlyCollection<ListingEntry> Postings);

    private sealed record ListingPageWithStatus(
        int Total,
        int RawEntryCount,
        IReadOnlyCollection<ListingEntry> Postings,
        int StatusCode);

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
