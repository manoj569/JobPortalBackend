using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

/// <summary>
/// Reads configured public, server-rendered SAP SuccessFactors career boards.
/// Only listing and public job detail pages are fetched; application endpoints are never visited.
/// </summary>
public sealed partial class SuccessFactorsJobSourceProvider(
    IHttpClientFactory clients,
    ILogger<SuccessFactorsJobSourceProvider>? logger = null) : ICompleteExternalJobProvider
{
    public const string HttpClientName = "SuccessFactorsJobAggregation";
    private const int PageSize = 25;
    private const int MaxJobs = 10_000;
    private const int MaxHtmlBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(250);
    private static readonly Action<ILogger, Guid, Exception?> Started = LoggerMessage.Define<Guid>(LogLevel.Information,
        new EventId(4350, nameof(Started)), "SuccessFactorsSyncStarted Source={JobSourceId}.");
    private static readonly Action<ILogger, Guid, int, int, int, double, Exception?> Completed = LoggerMessage.Define<Guid, int, int, int, double>(
        LogLevel.Information, new EventId(4352, nameof(Completed)), "SuccessFactorsSyncFetched Source={JobSourceId} Fetched={Fetched} Parsed={Parsed} Skipped={Skipped} DurationMs={DurationMs}.");

    [LoggerMessage(EventId = 4360, Level = LogLevel.Warning,
        Message = "SuccessFactorsSourceFailure Source={JobSourceId} Stage={Stage} PaginationOffset={PaginationOffset} ReasonCode={ReasonCode} StatusCode={StatusCode}.")]
    private static partial void LogSourceFailure(ILogger logger, Guid jobSourceId, string stage,
        int paginationOffset, string reasonCode, int? statusCode);

    private const string ReasonDataKey = "SuccessFactorsFailureReason";
    private static InvalidDataException InvalidDocument(FailureReason reason, string message)
    {
        var exception = new InvalidDataException(message);
        exception.Data[ReasonDataKey] = reason;
        return exception;
    }
    private void LogInvalidDocument(Guid sourceId, string stage, int offset, int? status, InvalidDataException exception)
    {
        // Only internal enum values reach logs, never exception messages, HTML or URLs.
        var reason = exception.Data[ReasonDataKey] is FailureReason known ? known : FailureReason.ResponseReadInvalidData;
        if (logger is not null) LogSourceFailure(logger, sourceId, stage, offset, reason.ToString(), status);
    }

    public AtsType AtsType => AtsType.SuccessFactors;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
        (await FetchSnapshotAsync(source, cancellationToken)).Jobs;

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var boardUri = ValidateSource(source);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        if (logger is not null) Started(logger, source.Id, null);

        var client = clients.CreateClient(HttpClientName);
        var pages = new List<ListingEntry>();
        var expectedTotal = -1;
        var expectedLastOffset = -1;
        var lastResponseStatus = (int?)null;
        for (var offset = 0; ; offset += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await GetHtmlAsync(client, ListingUri(boardUri, offset), source.Id, "listing_response", offset, cancellationToken);
            lastResponseStatus = response.StatusCode;
            var stage = "listing_parse";
            ListingPage page;
            try
            {
                page = ParseListingPage(response.Html, offset, boardUri);
                stage = "listing_pagination";
                if (expectedTotal < 0)
                {
                    expectedTotal = page.TotalCount;
                    expectedLastOffset = page.LastOffset;
                    var calculatedLast = expectedTotal == 0 ? 0 : ((expectedTotal - 1) / PageSize) * PageSize;
                    if (expectedTotal > MaxJobs || calculatedLast != expectedLastOffset)
                        throw InvalidDocument(expectedTotal > MaxJobs ? FailureReason.ListingRecordLimitExceeded : FailureReason.ListingLastOffsetMismatch,
                        "The SuccessFactors listing pagination did not provide a safe, bounded complete result set.");
                }
                if (page.TotalCount != expectedTotal || page.LastOffset != expectedLastOffset ||
                    page.FirstResult != offset + 1 || page.Results.Count != Math.Min(PageSize, Math.Max(0, expectedTotal - offset)))
                    throw InvalidDocument(page.TotalCount != expectedTotal ? FailureReason.ListingTotalChanged :
                        page.LastOffset != expectedLastOffset ? FailureReason.ListingLastOffsetChanged : FailureReason.ListingSnapshotChanged,
                        "The SuccessFactors listing changed during pagination; refusing an incomplete snapshot.");
            }
            catch (InvalidDataException exception)
            {
                LogInvalidDocument(source.Id, stage, offset, response.StatusCode, exception);
                throw;
            }

            pages.AddRange(page.Results);
            if (offset == expectedLastOffset) break;
            if (page.Results.Count == 0)
            {
                var exception = InvalidDocument(FailureReason.ListingEndedEarly, "The SuccessFactors listing ended before its advertised last page.");
                LogInvalidDocument(source.Id, "listing_snapshot", offset, response.StatusCode, exception);
                throw exception;
            }
            await Task.Delay(RequestSpacing, cancellationToken);
        }

        if (pages.Count != expectedTotal || pages.Select(x => x.DetailUrl.AbsoluteUri).Distinct(StringComparer.Ordinal).Count() != expectedTotal)
        {
            var exception = InvalidDocument(pages.Count != expectedTotal ? FailureReason.ListingSnapshotCountMismatch : FailureReason.ListingDuplicateDetailLinks,
                "The SuccessFactors listing contained missing or duplicate job detail links.");
            LogInvalidDocument(source.Id, "listing_snapshot", expectedLastOffset, lastResponseStatus, exception);
            throw exception;
        }

        var parsed = new ConcurrentDictionary<int, RawExternalJob>();
        var skipped = 0;
        var nextIndex = 0;
        using var concurrency = new SemaphoreSlim(2, 2);
        using var pace = new SemaphoreSlim(1, 1);
        var lastRequestAt = DateTimeOffset.MinValue;
        await Parallel.ForEachAsync(pages.Select((entry, index) => (entry, index)),
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken }, async (item, token) =>
            {
                await concurrency.WaitAsync(token);
                try
                {
                    await pace.WaitAsync(token);
                    try
                    {
                        var remaining = lastRequestAt + RequestSpacing - DateTimeOffset.UtcNow;
                        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
                        lastRequestAt = DateTimeOffset.UtcNow;
                    }
                    finally { pace.Release(); }

                    var detailResponse = await GetHtmlAsync(client, item.entry.DetailUrl, source.Id, "detail_response",
                        (item.index / PageSize) * PageSize, token);
                    var detail = ParseDetail(detailResponse.Html, item.entry, source.Company?.Name ?? string.Empty);
                    if (detail is null) Interlocked.Increment(ref skipped);
                    else parsed[item.index] = detail;
                    Interlocked.Increment(ref nextIndex);
                }
                finally { concurrency.Release(); }
            });

        var ordered = parsed.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
        var uniqueExternalIds = ordered.Select(x => x.ExternalId).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Count();
        var isComplete = skipped == 0 && nextIndex == pages.Count && ordered.Length == expectedTotal && uniqueExternalIds == expectedTotal;
        if (logger is not null) Completed(logger, source.Id, pages.Count, ordered.Length, skipped, stopwatch.Elapsed.TotalMilliseconds, null);
        return new(ordered, skipped, isComplete);
    }

    internal static ListingPage ParseListingPage(string html, int requestedOffset, Uri boardUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        var totalMatch = ListingTotalRegex().Match(html);
        if (!totalMatch.Success ||
            !int.TryParse(totalMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
            !int.TryParse(totalMatch.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var last) ||
            !int.TryParse(totalMatch.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
            throw InvalidDocument(FailureReason.ListingPaginationMetadataInvalid, "The SuccessFactors listing page did not contain valid pagination metadata.");

        var calculatedLastOffset = total == 0 ? 0 : ((total - 1) / PageSize) * PageSize;
        var lastOffset = 0;

        // Single-page SuccessFactors boards may omit paginationItemLast entirely.
        // Multi-page boards must still expose a validated last-page link so snapshot
        // completeness remains fail-closed.
        if (calculatedLastOffset > 0)
        {
            var lastMatch = LastPageRegex().Match(html);
            if (!lastMatch.Success)
                throw InvalidDocument(FailureReason.ListingPaginationMetadataInvalid, "The SuccessFactors listing page did not contain valid last-page pagination metadata.");

            var prefix = boardUri.AbsolutePath.TrimEnd('/') + "/";
            var boardRoot = new Uri(boardUri.AbsoluteUri.TrimEnd('/') + "/");
            if (!Uri.TryCreate(boardRoot, WebUtility.HtmlDecode(lastMatch.Groups[3].Value), out var lastUri) ||
                !SameOrigin(boardUri, lastUri) || !lastUri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(lastUri.AbsolutePath[prefix.Length..].TrimEnd('/'), NumberStyles.None, CultureInfo.InvariantCulture, out lastOffset))
                throw InvalidDocument(FailureReason.ListingLastPageLinkInvalid, "The SuccessFactors listing page did not contain a valid last-page link for this board.");
        }

        if (lastOffset != calculatedLastOffset)
            throw InvalidDocument(FailureReason.ListingLastOffsetMismatch, "The SuccessFactors listing last-page offset did not match the calculated result count.");

        var rows = ListingRowRegex().Matches(html).Select(x => x.Groups[2].Value).ToArray();
        var results = new List<ListingEntry>(rows.Length);
        FailureReason? rowFailure = null;
        foreach (var row in rows)
        {
            var links = JobLinkRegex().Matches(row);
            var link = links.Cast<Match>().Select(x => x.Groups[3].Value).FirstOrDefault();
            if (link is null) { rowFailure ??= FailureReason.ListingRowMissingJobLink; continue; }
            var titleMatch = JobTitleRegex().Match(row);
            var title = titleMatch.Success ? PlainText(titleMatch.Groups[4].Value) : string.Empty;
            var locationMatch = ClassSpanRegex("jobLocation").Match(row);
            var dateMatch = ClassSpanRegex("jobDate").Match(row);
            var location = locationMatch.Success ? PlainText(locationMatch.Groups[2].Value) : string.Empty;
            var postedText = dateMatch.Success ? PlainText(dateMatch.Groups[2].Value) : string.Empty;
            if (title.Length == 0) { rowFailure ??= FailureReason.ListingRowMissingTitle; continue; }
            if (!TryDetailUrl(boardUri, link, out var detailUri)) { rowFailure ??= FailureReason.ListingRowInvalidDetailUrl; continue; }
            if (!ParseListingDate(postedText, out var posted)) { rowFailure ??= FailureReason.ListingRowInvalidDate; continue; }
            results.Add(new(title, location, posted, detailUri));
        }

        var expectedCount = total == 0 ? 0 : Math.Min(PageSize, Math.Max(0, total - requestedOffset));
        if (first != requestedOffset + 1 || last < first || total < 0 || rows.Length != expectedCount || results.Count != expectedCount)
            throw InvalidDocument(first != requestedOffset + 1 || last < first || total < 0 ? FailureReason.ListingRangeInvalid :
                rows.Length != expectedCount ? FailureReason.ListingRowCountMismatch : rowFailure ?? FailureReason.ListingRowsUnparseable,
                "The SuccessFactors listing page contained malformed or incomplete job rows.");
        return new(first, last, total, lastOffset, results);
    }

    internal static RawExternalJob? ParseDetail(string html, ListingEntry listing, string companyName)
    {
        ArgumentNullException.ThrowIfNull(html);
        var titleHtml = ExtractItemPropElement(html, "title");
        var descriptionHtml = ExtractItemPropElement(html, "description");
        var title = PlainText(titleHtml);
        if (title.Length == 0 || descriptionHtml is null) return null;

        var reqMatch = RequisitionIdRegex().Match(PlainText(descriptionHtml));
        var postingId = PostingIdRegex().Match(listing.DetailUrl.AbsolutePath);
        var externalId = reqMatch.Success ? reqMatch.Groups[1].Value : postingId.Success ? postingId.Groups[1].Value : null;
        if (string.IsNullOrWhiteSpace(externalId)) return null;

        var location = MetaContent(html, "streetAddress");
        if (location is not null) location = NormalizeIndiaLocation(location);
        if (string.IsNullOrWhiteSpace(location)) location = listing.Location;

        return new RawExternalJob
        {
            ExternalId = externalId,
            Title = title,
            CompanyName = companyName,
            Location = location,
            Description = descriptionHtml,
            DescriptionIsHtml = true,
            ApplicationUrl = listing.DetailUrl.AbsoluteUri,
            SourcePostedAtUtc = (ParseMetaDate(html, "datePosted") ?? listing.PostedAtUtc).UtcDateTime,
            ExpiresAtUtc = ParseMetaDate(html, "validThrough")?.UtcDateTime,
            // This public HTML template does not expose stable structured skills, salary, experience, or work-mode fields.
        };
    }

    private async Task<HtmlResponse> GetHtmlAsync(HttpClient client, Uri uri, Guid sourceId, string stage, int offset, CancellationToken cancellationToken)
    {
        int? status = null;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            status = (int)response.StatusCode;
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxHtmlBytes)
                throw InvalidDocument(FailureReason.ResponseDeclaredSizeExceeded, "SuccessFactors returned an oversized public page.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + count > MaxHtmlBytes)
                    throw InvalidDocument(FailureReason.ResponseReadSizeExceeded, "SuccessFactors returned an oversized public page.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            Encoding encoding;
            try { encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset); }
            catch (ArgumentException) { encoding = Encoding.UTF8; }
            return new(encoding.GetString(output.ToArray()), (int)response.StatusCode);
        }
        catch (InvalidDataException exception)
        {
            LogInvalidDocument(sourceId, stage, offset, status, exception);
            throw;
        }
        catch (HttpRequestException exception)
        {
            if (logger is not null) LogSourceFailure(logger, sourceId, stage, offset,
                status is >= 300 || exception.StatusCode is { } errorStatus && (int)errorStatus >= 300 ? "HttpStatusFailure" :
                    status.HasValue ? "ResponseReadTransportFailure" : "HttpTransportFailure",
                status ?? (exception.StatusCode is { } code ? (int)code : null));
            throw;
        }
    }

    internal static Uri ValidateSource(JobSource source)
    {
        if (source.AtsType != AtsType.SuccessFactors || string.IsNullOrWhiteSpace(source.AtsIdentifier) ||
            !Uri.TryCreate(source.CareerPageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.HostNameType != UriHostNameType.Dns ||
            !uri.Host.Contains('.') || uri.IsLoopback ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SuccessFactors source must use a public HTTPS /go/{board}/{numeric-id} URL matching its ATS identifier.");
        var board = BoardPathRegex().Match(uri.AbsolutePath);
        if (!board.Success || board.Groups[1].Value != source.AtsIdentifier)
            throw new InvalidOperationException("SuccessFactors source must use a public HTTPS /go/{board}/{numeric-id} URL matching its ATS identifier.");
        return uri;
    }

    private static Uri ListingUri(Uri boardUri, int offset)
    {
        var root = new Uri(boardUri.AbsoluteUri.TrimEnd('/') + "/");
        var path = offset == 0 ? root.AbsolutePath : root.AbsolutePath + offset.ToString(CultureInfo.InvariantCulture) + "/";
        return new UriBuilder(root) { Path = path, Query = "q=&sortColumn=referencedate&sortDirection=desc" }.Uri;
    }

    private static bool SameOrigin(Uri boardUri, Uri candidate) =>
        candidate.Scheme == boardUri.Scheme && candidate.Port == boardUri.Port &&
        string.Equals(candidate.IdnHost, boardUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(candidate.UserInfo) && string.IsNullOrEmpty(candidate.Fragment);

    private static bool TryDetailUrl(Uri boardUri, string href, out Uri uri)
    {
        uri = boardUri;
        if (!Uri.TryCreate(boardUri, WebUtility.HtmlDecode(href), out var candidate) || !SameOrigin(boardUri, candidate) ||
            !candidate.AbsolutePath.StartsWith("/job/", StringComparison.Ordinal)) return false;
        if (!PostingIdRegex().IsMatch(candidate.AbsolutePath)) return false;
        uri = candidate;
        return true;
    }

    private static string? ExtractItemPropElement(string html, string property)
    {
        foreach (Match match in HtmlTagRegex().Matches(html))
        {
            if (match.Groups[1].Value == "/") continue;
            var tag = match.Groups[2].Value;
            if (!string.Equals(tag, "span", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Attribute(match.Groups[3].Value, "itemprop"), property, StringComparison.OrdinalIgnoreCase)) continue;
            var depth = 1;
            foreach (Match nested in HtmlTagRegex().Matches(html, match.Index + match.Length))
            {
                if (!string.Equals(nested.Groups[2].Value, tag, StringComparison.OrdinalIgnoreCase)) continue;
                if (nested.Groups[1].Value == "/") depth--;
                else if (!nested.Value.EndsWith("/>", StringComparison.Ordinal)) depth++;
                if (depth == 0) return html[(match.Index + match.Length)..nested.Index];
            }
            return null;
        }
        return null;
    }

    private static string? MetaContent(string html, string itemProp)
    {
        foreach (Match match in HtmlTagRegex().Matches(html))
            if (string.Equals(match.Groups[2].Value, "meta", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Attribute(match.Groups[3].Value, "itemprop"), itemProp, StringComparison.OrdinalIgnoreCase))
                return WebUtility.HtmlDecode(Attribute(match.Groups[3].Value, "content"));
        return null;
    }

    private static DateTimeOffset? ParseMetaDate(string html, string itemProp)
    {
        var value = MetaContent(html, itemProp);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
    }

    private static bool ParseListingDate(string text, out DateTimeOffset date) =>
        DateTimeOffset.TryParse(text, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

    private static string NormalizeIndiaLocation(string value)
    {
        var normalized = Regex.Replace(WebUtility.HtmlDecode(value).Trim(), @"\s+", " ");
        normalized = Regex.Replace(normalized, @",\s*IN\s*$", ", India", RegexOptions.IgnoreCase);
        return normalized;
    }

    private static string PlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var withoutActive = ActiveContentRegex().Replace(value, " ");
        return Regex.Replace(WebUtility.HtmlDecode(HtmlTagRegex().Replace(withoutActive, " ")), @"\s+", " ").Trim();
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = AttributeRegex(name).Match(attributes);
        return match.Success ? match.Groups[1].Value.Length > 0 ? match.Groups[1].Value :
            match.Groups[2].Value.Length > 0 ? match.Groups[2].Value : match.Groups[3].Value : null;
    }

    private static Regex AttributeRegex(string name) => new($"""\b{Regex.Escape(name)}\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex ClassSpanRegex(string className) => new($"""(?is)<span\b(?=[^>]*\bclass\s*=\s*(['"])[^'"]*\b{Regex.Escape(className)}\b[^'"]*\1)[^>]*>(.*?)</span>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex("""(?is)<tr\b(?=[^>]*\bclass\s*=\s*(['"])[^'"]*\bdata-row\b[^'"]*\1)[^>]*>(.*?)</tr>""", RegexOptions.CultureInvariant)]
    private static partial Regex ListingRowRegex();
    [GeneratedRegex("""(?is)<a\b(?=[^>]*\bclass\s*=\s*(['"])[^'"]*\bjobTitle-link\b[^'"]*\1)[^>]*\bhref\s*=\s*(['"])(.*?)\2[^>]*>""", RegexOptions.CultureInvariant)]
    private static partial Regex JobLinkRegex();
    [GeneratedRegex("""(?is)<a\b(?=[^>]*\bclass\s*=\s*(['"])[^'"]*\bjobTitle-link\b[^'"]*\1)[^>]*\bhref\s*=\s*(['"])(.*?)\2[^>]*>(.*?)</a>""", RegexOptions.CultureInvariant)]
    private static partial Regex JobTitleRegex();
    [GeneratedRegex(@"(?is)Results\s+(\d+)\s+(?:to|[-–—])\s+(\d+)\s+of\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ListingTotalRegex();
    [GeneratedRegex("""(?is)<a\b(?=[^>]*\bclass\s*=\s*(['"])[^'"]*\bpaginationItemLast\b[^'"]*\1)[^>]*\bhref\s*=\s*(['"])(.*?)\2[^>]*>""", RegexOptions.CultureInvariant)]
    private static partial Regex LastPageRegex();
    [GeneratedRegex(@"^/go/[A-Za-z0-9_-]+/([0-9]+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex BoardPathRegex();
    [GeneratedRegex(@"/(\d+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex PostingIdRegex();
    [GeneratedRegex(@"(?is)Job requisition ID\s*(?:</strong>)?\s*:?[\s·•]*([0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex RequisitionIdRegex();
    [GeneratedRegex(@"(?is)<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>", RegexOptions.CultureInvariant)]
    private static partial Regex ActiveContentRegex();
    [GeneratedRegex("""<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9:-]*)\b(?<attrs>(?:[^>"']|"[^"]*"|'[^']*')*)>""", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    internal sealed record ListingEntry(string Title, string Location, DateTimeOffset PostedAtUtc, Uri DetailUrl);
    internal sealed record ListingPage(int FirstResult, int LastResult, int TotalCount, int LastOffset, IReadOnlyList<ListingEntry> Results);
    private sealed record HtmlResponse(string Html, int StatusCode);
    private enum FailureReason
    {
        ListingPaginationMetadataInvalid, ListingLastPageLinkInvalid, ListingRangeInvalid, ListingRowCountMismatch,
        ListingRowMissingJobLink, ListingRowMissingTitle, ListingRowInvalidDetailUrl, ListingRowInvalidDate, ListingRowsUnparseable,
        ListingRecordLimitExceeded, ListingLastOffsetMismatch, ListingTotalChanged, ListingLastOffsetChanged, ListingSnapshotChanged,
        ListingEndedEarly, ListingSnapshotCountMismatch, ListingDuplicateDetailLinks,
        ResponseDeclaredSizeExceeded, ResponseReadSizeExceeded, ResponseReadInvalidData
    }
}
