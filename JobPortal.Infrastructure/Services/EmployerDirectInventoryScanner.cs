using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;

namespace JobPortal.Infrastructure.Services;

// Runs inside the existing JobSourceRunWorker, under its source lock, lease and
// concurrency slots. No timer, crawler queue or alternate ingestion path.
public sealed class EmployerDirectInventoryScanner(IHttpClientFactory clients, TimeProvider clock,
    AggregationHttpRetryState cooldowns, IJobSourceExecutionLock? hostLocks = null) : IEmployerDirectInventoryScanner
{
    public const string HttpClientName = "EmployerDirectInventory";
    private const int MaximumPages = 40;
    private const int MaximumJobs = 5000;
    private const int MaximumBytes = 3_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExternalJobSourceSnapshot> ScanAsync(JobSource source, CancellationToken cancellationToken)
    {
        if (source.AtsIdentifier != EmployerDirectInventoryContract.Identifier ||
            !SafeUrl(source.CareerPageUrl, out var root) || root!.Query.Length != 0 || root.Fragment.Length != 0)
            throw new InvalidOperationException("An explicitly configured public inventory-v1 source is required.");
        var observedAt = clock.GetUtcNow().UtcDateTime;
        var hostKey = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("EmployerDirectHost:" + root.Host))[..16]);
        await using var hostLease = hostLocks is null ? null : await hostLocks.TryAcquireAsync(hostKey, cancellationToken);
        if (hostLocks is not null && hostLease is null) return new([], 0, false) { ObservedAtUtc = observedAt };
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(90));
        var ct = budget.Token;
        using var client = clients.CreateClient(HttpClientName);
        var jobs = new List<RawExternalJob>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        EmployerDirectInventoryPage? initial = null;
        byte[]? initialBytes = null;
        var complete = false;
        var reason = "EnumerationIncomplete";
        var requests = 0;
        var totalBytes = 0;

        async Task<byte[]> ReadAsync(Uri uri, int limit)
        {
            if (++requests > MaximumPages + 3) throw new HttpRequestException("Inventory request budget exceeded.");
            var key = "EmployerDirect:" + root.Host;
            if (cooldowns.Remaining(key, clock.GetUtcNow()) > TimeSpan.Zero)
                throw new HttpRequestException("Employer host is cooling down.");
            if (requests > 1) await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("CareerHarborEmployerIndexer/1.0");
            request.Headers.CacheControl = new() { NoCache = true };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var now = clock.GetUtcNow();
                var until = response.Headers.RetryAfter?.Date ?? now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
                cooldowns.Defer(key, until);
            }
            if (uri.AbsolutePath == "/robots.txt" && response.StatusCode == HttpStatusCode.NotFound) return [];
            response.EnsureSuccessStatusCode(); // Includes redirects: handler never follows them.
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("A fresh full response is required.");
            if (response.Content.Headers.ContentLength > limit) throw new HttpRequestException("Inventory response too large.");
            if (uri.AbsolutePath != "/robots.txt" && response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/ld+json"))
                throw new HttpRequestException("Inventory must be explicit JSON.");
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await body.ReadAsync(buffer, timeout.Token)) != 0)
            {
                totalBytes += count;
                if (memory.Length + count > limit || totalBytes > 10_000_000)
                    throw new HttpRequestException("Inventory byte budget exceeded.");
                memory.Write(buffer, 0, count);
            }
            return memory.ToArray();
        }

        try
        {
            var robots = Encoding.UTF8.GetString(await ReadAsync(new Uri(root, "/robots.txt"), 64_000));
            if (!RobotsAllows(robots, root.AbsolutePath)) throw new HttpRequestException("Robots disallows this inventory.");
            var url = root;
            for (var pageNumber = 0; pageNumber < MaximumPages; pageNumber++)
            {
                var bytes = await ReadAsync(url, MaximumBytes);
                var page = JsonSerializer.Deserialize<EmployerDirectInventoryPage>(bytes, JsonOptions)
                    ?? throw new JsonException("Empty inventory response.");
                if (page.Contract != EmployerDirectInventoryContract.Version || page.Source != source.CareerPageUrl ||
                    string.IsNullOrWhiteSpace(page.SnapshotVersion) || page.SnapshotVersion.Length > 255 ||
                    page.ObservedAtUtc.Offset != TimeSpan.Zero || page.ObservedAtUtc > clock.GetUtcNow().AddMinutes(5) ||
                    page.ObservedAtUtc < clock.GetUtcNow().AddHours(-24) || page.Total is < 0 or > MaximumJobs || page.Jobs is null)
                    throw new JsonException("Invalid inventory manifest.");
                initial ??= page;
                initialBytes ??= bytes;
                if (page.SnapshotVersion != initial.SnapshotVersion || page.Total != initial.Total || page.ObservedAtUtc != initial.ObservedAtUtc)
                    throw new JsonException("Inventory changed during enumeration.");
                foreach (var job in page.Jobs)
                {
                    if (job is null || string.IsNullOrWhiteSpace(job.ExternalId) || job.ExternalId.Length > 255 ||
                        !seenIds.Add(job.ExternalId) || string.IsNullOrWhiteSpace(job.Title) || string.IsNullOrWhiteSpace(job.Description) ||
                        !SafeUrl(job.DiscoveredUrl, out var detail) || detail!.Host != root.Host ||
                        !SafeUrl(job.ApplicationUrl, out var apply) || apply!.Host != root.Host ||
                        job.AcquisitionSourceId.HasValue || job.AcquisitionProviderKey is not null ||
                        job.JobSourceId.HasValue || job.CompanyId.HasValue)
                        throw new JsonException("Invalid, duplicate or non-first-party inventory detail.");
                    jobs.Add(job);
                    if (jobs.Count > page.Total) throw new JsonException("Inventory count exceeded its manifest.");
                }
                if (page.NextCursor is null)
                {
                    if (jobs.Count != page.Total) throw new JsonException("Inventory count mismatch.");
                    // Re-fetch the first page: identical manifest, identities and content
                    // are required, including for an explicitly empty inventory.
                    var finalBytes = await ReadAsync(root, MaximumBytes);
                    if (!SHA256.HashData(initialBytes).AsSpan().SequenceEqual(SHA256.HashData(finalBytes)))
                        throw new JsonException("Inventory changed before commit.");
                    complete = true; reason = "AuthoritativeInventoryV1"; break;
                }
                if (page.Jobs.Count == 0 || string.IsNullOrWhiteSpace(page.NextCursor) || page.NextCursor.Length > 512 || !cursors.Add(page.NextCursor))
                    throw new JsonException("Non-progressing inventory cursor.");
                url = new UriBuilder(root) { Query = "cursor=" + Uri.EscapeDataString(page.NextCursor) }.Uri;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Retain independently valid positives; never turn partial failure into absence.
            reason = "InventoryIncomplete";
        }
        return new(jobs, 0, complete)
        {
            ObservedAtUtc = observedAt,
            InventoryAtUtc = initial?.ObservedAtUtc.UtcDateTime,
            InventoryVersion = initial?.SnapshotVersion,
            InventoryDigest = complete ? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                jobs.OrderBy(j => j.ExternalId, StringComparer.Ordinal), JsonOptions))) : null,
            ReportedListingTotal = initial?.Total,
            ProviderReasonCounts = new Dictionary<string, int> { [reason] = 1 }
        };
    }

    internal static bool SafeUrl(string? value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.HostNameType == UriHostNameType.Dns && CompanyLogoHttpTransport.IsSafeUrl(uri);

    internal static bool RobotsAllows(string text, string path)
    {
        // Conservative prefix interpretation: unsupported wildcard restrictions
        // block rather than permit. Specific agent rules override wildcard groups.
        var groups = new List<(List<string> Agents, List<(string Path, bool Allow)> Rules)>();
        List<string> agents = []; List<(string Path, bool Allow)> rules = [];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim(); var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var key = line[..separator].Trim(); var value = line[(separator + 1)..].Trim();
            if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (rules.Count > 0) { groups.Add((agents, rules)); agents = []; rules = []; }
                agents.Add(value);
            }
            else if (agents.Count > 0 && (key.Equals("allow", StringComparison.OrdinalIgnoreCase) || key.Equals("disallow", StringComparison.OrdinalIgnoreCase)) && value.Length > 0)
                rules.Add((value, key.Equals("allow", StringComparison.OrdinalIgnoreCase)));
        }
        groups.Add((agents, rules));
        var specific = groups.Where(g => g.Agents.Any(a => a.Equals("CareerHarborEmployerIndexer", StringComparison.OrdinalIgnoreCase))).ToArray();
        var applicable = specific.Length > 0 ? specific : groups.Where(g => g.Agents.Contains("*")).ToArray();
        var matches = applicable.SelectMany(g => g.Rules).Where(r => (!r.Allow || !r.Path.Contains('*') && !r.Path.Contains('$')) && path.StartsWith(r.Path.Split('*', '$')[0], StringComparison.Ordinal))
            .OrderByDescending(r => r.Path.Split('*', '$')[0].Length).ThenBy(r => r.Allow).ToArray();
        return matches.Length == 0 || matches[0].Allow;
    }
}
