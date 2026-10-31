using System.Globalization;
using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobPortal.Infrastructure.Services;

// Public candidate application v738 contract verified against LTM. No absence reconciliation:
// offset pagination over a live board does not establish an authoritative inventory.
public sealed class RippleHireJobSourceProvider(IHttpClientFactory clients, ILogger<RippleHireJobSourceProvider>? logger = null) : IBatchedExternalJobProvider
{
    private static readonly Action<ILogger, Guid, int, int, Exception?> PageFetched = LoggerMessage.Define<Guid, int, int>(
        LogLevel.Information, new EventId(4380, "RippleHirePageFetched"),
        "RippleHire listing SourceId={SourceId} PagesProcessed={PagesProcessed} JobsFetched={JobsFetched}.");
    public const string HttpClientName = "RippleHireJobAggregation";
    private const int PageSize = 10;
    private const int MaximumJobs = 2000;
    public AtsType AtsType => AtsType.RippleHire;

    public const int ProcessingBatchSize = 50;

    // Compatibility callers explicitly requesting a collection still receive one.
    // Production runner consumes FetchBatchesAsync with backpressure.
    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        var jobs = new List<RawExternalJob>();
        await foreach (var batch in FetchBatchesAsync(source, cancellationToken)) jobs.AddRange(batch.Jobs);
        return jobs;
    }

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
        => new(await FetchJobsAsync(source, cancellationToken), 0, false);

    public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var config = JobSourceConfiguration.Require(AtsType, source.CareerPageUrl, source.AtsIdentifier);
        var board = new Uri(source.CareerPageUrl);
        var client = clients.CreateClient(HttpClientName);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMinutes(20));
        var token = budget.Token;
        using var first = await SearchAsync(client, board, config.Identifier!, 0, token);
        var root = first.RootElement;
        ValidateListing(root);
        var total = root.GetProperty("totalJobCount").GetInt32();
        var jobs = new List<RawExternalJob>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page * PageSize < total; page++)
        {
            using var next = page == 0 ? null : await SearchAsync(client, board, config.Identifier!, page, token);
            var entries = page == 0 ? root.GetProperty("jobVoList") : next!.RootElement;
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() != Math.Min(PageSize, total - page * PageSize))
                throw Unsupported("Listing pagination changed or returned an incomplete page.");
            if (logger is not null) PageFetched(logger, source.Id, page + 1, page * PageSize + entries.GetArrayLength(), null);
            foreach (var entry in entries.EnumerateArray())
            {
                var id = entry.Text("jobSeq");
                if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiDigit) || !seen.Add(id))
                    throw Unsupported("Listing contains an invalid or repeated job identity; retry the source.");
                await Task.Delay(TimeSpan.FromMilliseconds(250), token);
                var detailUrl = new Uri(board, $"candidatejobdetail?token={config.Identifier}&jobSeq={id}&lang=en{SourceQuery(board)}");
                using var detail = await RequestAsync(client, detailUrl, null, token);
                var envelope = detail.RootElement;
                var job = envelope.Field("jobVO");
                if (envelope.Field("linkDeactivated").ValueKind == JsonValueKind.True ||
                    envelope.Field("nonErLinkDeactivated").ValueKind == JsonValueKind.True)
                    throw Unsupported("Public career link is deactivated.");
                if (job.ValueKind != JsonValueKind.Object || job.Text("jobSeq") != id)
                    throw Unsupported("Public job detail contract could not be verified.");
                if (job.Text("allowCareerSite") != "Y" || job.Text("jobStatus") != "ACTIVE") continue;
                if (string.IsNullOrWhiteSpace(job.Text("jobTitle")) || string.IsNullOrWhiteSpace(job.Text("jobDesc")))
                    throw Unsupported("Public job detail is missing its title or description.");
                // A source date without a time cannot be promoted to an invented UTC instant.
                var published = job.Field("publishDetails").Text("CAREER_SITE");
                DateTime? posted = published is not null && published.EndsWith('Z') &&
                    DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
                    ? instant.UtcDateTime : null;
                jobs.Add(new RawExternalJob
                {
                    ExternalId = id, Title = job.Text("jobTitle")!, CompanyName = source.Company?.Name ?? string.Empty,
                    CompanyId = source.CompanyId, Description = job.Text("jobDesc"), DescriptionIsHtml = true,
                    Location = job.Text("jobLocation"), AdditionalLocations = new[] { entry.Text("jobLocation") }.OfType<string>().ToArray(),
                    Requirements = job.Text("jobSkills"), EmploymentTypeText = job.Text("jobTypeCustom3"),
                    SourcePostedAtUtc = posted,
                    ApplicationUrl = source.CareerPageUrl + "#detail/job/" + id,
                    CanonicalJobUrl = source.CareerPageUrl + "#detail/job/" + id,
                    DiscoveredUrl = source.CareerPageUrl
                });
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            if ((page + 1) % (ProcessingBatchSize / PageSize) == 0 || (page + 1) * PageSize >= total)
            {
                // Listing pages contain at most ten jobs; yield after five pages.
                // Never certify absence reconciliation for this live offset feed.
                yield return new ExternalJobSourceSnapshot(jobs, 0, false)
                {
                    ReportedListingTotal = total,
                    AcquisitionSucceeded = (page + 1) * PageSize >= total
                };
                jobs = new List<RawExternalJob>();
            }
        }
    }

    internal static void ValidateListing(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.Text("errorStatus") != "success" ||
            !root.Field("totalJobCount").TryGetInt32(out var total) || total < 0 || total > MaximumJobs ||
            root.Field("jobVoList").ValueKind != JsonValueKind.Array ||
            root.Field("jobVoList").GetArrayLength() != Math.Min(PageSize, total))
            throw Unsupported("Unverified RippleHire listing response or source exceeds the 2,000-job safety limit.");
        // An invalid token can also return an empty successful response. Never certify it as a healthy board.
        if (total == 0) throw Unsupported("Empty source cannot be distinguished from an invalid public career token.");
    }

    internal static Task<JsonDocument> SearchAsync(HttpClient client, Uri board, string publicToken, int page, CancellationToken token)
    {
        var parameters = JsonSerializer.Serialize(new { token = publicToken, page, pagesize = PageSize, search = "*:*", source = Source(board) });
        return RequestAsync(client, new Uri(board, page == 0 ? "candidatejobsearch" : "candidatejobsearchpage"),
            parameters, token);
    }

    private static string? Source(Uri board) => board.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
        .Where(x => x.Length == 2 && x[0] == "source").Select(x => x[1]).SingleOrDefault();
    private static string SourceQuery(Uri board) => Source(board) is { } source ? "&source=" + source : "";

    private static async Task<JsonDocument> RequestAsync(HttpClient client, Uri url, string? parameters, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(parameters is null ? HttpMethod.Get : HttpMethod.Post, url);
            if (parameters is not null) request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["careerSiteUrlParams"] = parameters, ["lang"] = "en" });
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode is >= 500 and <= 599;
            if (transient && attempt < 3)
            {
                var delay = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt));
                if (delay > TimeSpan.FromSeconds(30)) response.EnsureSuccessStatusCode();
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, token);
                continue;
            }
            if ((int)response.StatusCode is >= 300 and <= 399) throw Unsupported("Redirects are unsupported; configure the final public tenant URL.");
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, timeout.Token);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
    }

    private static InvalidOperationException Unsupported(string message)
    {
        var error = new InvalidOperationException(message);
        error.Data["JobSourceReasonCode"] = "RIPPLEHIRE_SOURCE_UNSUPPORTED";
        return error;
    }
}
