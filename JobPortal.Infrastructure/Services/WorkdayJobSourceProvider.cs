using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

/// <summary>
/// Reads public Workday Candidate Experience (CXS) career sites hosted on
/// *.wdN.myworkdayjobs.com. Only public listing/detail endpoints are called.
/// Application endpoints, candidate login, cookies and form submission are never used.
/// </summary>
public sealed partial class WorkdayJobSourceProvider(
    IHttpClientFactory clients,
    ILogger<WorkdayJobSourceProvider>? logger = null) : ICompleteExternalJobProvider
{
    public const string HttpClientName = "WorkdayJobAggregation";

    // Workday's public CXS listing endpoint is capped at 20 items per request.
    private const int PageSize = 20;
    private const int MaxJobs = 10_000;
    private const int MaxJsonBytes = 4 * 1024 * 1024;
    private const int DetailConcurrency = 4;
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

    private const string ReasonDataKey = "WorkdayFailureReason";

    public AtsType AtsType => AtsType.Workday;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(
        JobSource source,
        CancellationToken cancellationToken = default) =>
        (await FetchSnapshotAsync(source, cancellationToken)).Jobs;

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(
        JobSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var target = ValidateSource(source);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        if (logger is not null)
            Started(logger, source.Id, null);

        var client = clients.CreateClient(HttpClientName);
        var postings = new List<ListingEntry>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        int? expectedTotal = null;

        for (var offset = 0; ; offset += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await FetchListingPageAsync(
                client,
                target,
                source.Id,
                offset,
                cancellationToken);

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
                if (offset != 0 || page.Postings.Count != 0)
                    throw LoggedInvalidDocument(
                        source.Id,
                        "listing_snapshot",
                        offset,
                        page.StatusCode,
                        FailureReason.ListingSnapshotChanged,
                        "The Workday listing changed while reading the snapshot.");

                break;
            }

            if (page.Postings.Count == 0)
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
                    throw LoggedInvalidDocument(
                        source.Id,
                        "listing_snapshot",
                        offset,
                        page.StatusCode,
                        FailureReason.ListingDuplicatePaths,
                        "The Workday listing contained duplicate external paths.");

                postings.Add(posting);

                if (postings.Count > expectedTotal)
                    throw LoggedInvalidDocument(
                        source.Id,
                        "listing_snapshot",
                        offset,
                        page.StatusCode,
                        FailureReason.ListingSnapshotChanged,
                        "The Workday listing grew beyond the first-page total during pagination.");
            }

            if (postings.Count == expectedTotal)
                break;

            if (page.Postings.Count < PageSize)
                throw LoggedInvalidDocument(
                    source.Id,
                    "listing_snapshot",
                    offset,
                    page.StatusCode,
                    FailureReason.ListingEndedEarly,
                    "The Workday listing returned a short page before the first-page total was reached.");

            await Task.Delay(RequestSpacing, cancellationToken);
        }

        if (expectedTotal is null || postings.Count != expectedTotal)
            throw LoggedInvalidDocument(
                source.Id,
                "listing_snapshot",
                postings.Count,
                null,
                FailureReason.ListingSnapshotCountMismatch,
                "The Workday listing did not produce the complete advertised snapshot.");

        var parsed = new ConcurrentDictionary<int, RawExternalJob>();
        var skipped = 0;
        var processed = 0;

        using var concurrency = new SemaphoreSlim(DetailConcurrency, DetailConcurrency);
        using var pace = new SemaphoreSlim(1, 1);
        var lastRequestAt = DateTimeOffset.MinValue;

        await Parallel.ForEachAsync(
            postings.Select((entry, index) => (entry, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = DetailConcurrency,
                CancellationToken = cancellationToken
            },
            async (item, token) =>
            {
                await concurrency.WaitAsync(token);

                try
                {
                    await pace.WaitAsync(token);
                    try
                    {
                        var remaining = lastRequestAt + RequestSpacing - DateTimeOffset.UtcNow;
                        if (remaining > TimeSpan.Zero)
                            await Task.Delay(remaining, token);

                        lastRequestAt = DateTimeOffset.UtcNow;
                    }
                    finally
                    {
                        pace.Release();
                    }

                    var detail = await FetchDetailAsync(
                        client,
                        target,
                        source,
                        item.entry,
                        source.Id,
                        item.index,
                        token);

                    if (detail is null)
                        Interlocked.Increment(ref skipped);
                    else
                        parsed[item.index] = detail;

                    Interlocked.Increment(ref processed);
                }
                finally
                {
                    concurrency.Release();
                }
            });

        var ordered = parsed
            .OrderBy(x => x.Key)
            .Select(x => x.Value)
            .ToArray();

        var uniqueExternalIds = ordered
            .Select(x => x.ExternalId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var isComplete =
            skipped == 0 &&
            processed == postings.Count &&
            ordered.Length == expectedTotal &&
            uniqueExternalIds == expectedTotal;

        if (logger is not null)
            Completed(
                logger,
                source.Id,
                postings.Count,
                ordered.Length,
                skipped,
                stopwatch.Elapsed.TotalMilliseconds,
                null);

        return new ExternalJobSourceSnapshot(
            ordered,
            skipped,
            isComplete);
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
                !totalElement.TryGetInt32(out var total) ||
                !root.TryGetProperty("jobPostings", out var postingsElement) ||
                postingsElement.ValueKind != JsonValueKind.Array)
            {
                throw InvalidDocument(
                    FailureReason.ListingSchemaInvalid,
                    "Workday listing response did not contain the expected total/jobPostings schema.");
            }

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

                if (string.IsNullOrWhiteSpace(title) ||
                    !TryValidateExternalPath(externalPath, out var normalizedPath))
                {
                    throw InvalidDocument(
                        FailureReason.ListingEntryInvalid,
                        "Workday listing contained a posting without a valid title/externalPath.");
                }

                postings.Add(new ListingEntry(
                    title,
                    normalizedPath,
                    locationsText,
                    postedOn,
                    FirstString(item, "bulletFields")));
            }

            return new ListingPage(
                total,
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
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("jobPostingInfo", out var info) ||
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
            return null;
        }
    }

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
                appliedFacets = new Dictionary<string, string[]>(),
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

        return ParseDetail(
            response.Body,
            target,
            source,
            listing);
    }

    private async Task<JsonResponse> SendAsync(
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
        IReadOnlyCollection<ListingEntry> Postings);

    private sealed record ListingPageWithStatus(
        int Total,
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
