using System.Globalization;
using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Services;

public sealed class SmartRecruitersOptions
{
    public int MaximumPages { get; set; } = 400;
    public int MaximumRequests { get; set; } = 25000;
    public int MaximumRecords { get; set; } = 20000;
    public int MaximumResponseBytes { get; set; } = 4 * 1024 * 1024;
    public long MaximumScanBytes { get; set; } = 64 * 1024 * 1024;
    public int ScanTimeoutSeconds { get; set; } = 900;
    public int RequestTimeoutSeconds { get; set; } = 130;
    public int RequestIntervalMilliseconds { get; set; } = 250;
    public bool IsValid() => MaximumPages is >= 1 and <= 1000 && MaximumRequests is >= 1 and <= 50000 &&
        MaximumRecords is >= 1 and <= 20000 && MaximumResponseBytes is >= 1024 and <= 16 * 1024 * 1024 &&
        MaximumScanBytes >= MaximumResponseBytes && MaximumScanBytes <= 512 * 1024 * 1024 &&
        ScanTimeoutSeconds is >= 1 and <= 3600 && RequestTimeoutSeconds is >= 1 and <= 180 &&
        RequestIntervalMilliseconds is >= 0 and <= 5000;
}

// Only the public Posting API is used. Enumerate inventory before any enrichment;
// all transport attempts/bytes, including retries, consume the same scan budget.
public sealed class SmartRecruitersJobSourceProvider(IHttpClientFactory clients,
    IOptions<SmartRecruitersOptions>? options = null, TimeProvider? clock = null,
    ILogger<SmartRecruitersJobSourceProvider>? logger = null, AggregationHttpRetryState? retryState = null) : ICompleteExternalJobProvider
{
    public const string HttpClientName = "SmartRecruitersJobAggregation";
    public AtsType AtsType => AtsType.SmartRecruiters;
    private static readonly Action<ILogger, Guid, int, long, int, bool, string, Exception?> Summary =
        LoggerMessage.Define<Guid, int, long, int, bool, string>(LogLevel.Information, new EventId(4450, "SmartRecruitersScan"),
            "SmartRecruiters SourceId={SourceId} Requests={Requests} Bytes={Bytes} Jobs={Jobs} Complete={Complete} Reason={Reason}");

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
        (await FetchSnapshotAsync(source, cancellationToken)).Jobs;

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        if (source.AtsType != AtsType) throw new InvalidOperationException("SmartRecruiters source type required.");
        var configuration = JobSourceConfiguration.Require(AtsType, source.CareerPageUrl, source.AtsIdentifier);
        var settings = options?.Value ?? new();
        if (!settings.IsValid()) throw new InvalidOperationException("Invalid SmartRecruiters budgets.");
        var time = clock ?? TimeProvider.System;
        using var scanTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.ScanTimeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scanTimeout.Token);
        using var client = clients.CreateClient(HttpClientName);
        var scan = new Scan(client, settings, time, retryState ?? new(), linked.Token);
        var jobs = new List<RawExternalJob>();
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var inventory = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        int? total = null;
        var complete = false;
        var reason = "SMARTRECRUITERS_INCOMPLETE";
        var skipped = 0;
        try
        {
            inventory = await scan.InventoryAsync(configuration.Identifier!);
            total = inventory.Count;
            foreach (var (identity, listing) in inventory)
            {
                try
                {
                    var detail = HasContent(listing) ? listing : await scan.ReadAsync(
                        $"v1/companies/{configuration.Identifier}/postings/{identity}", true);
                    var job = Map(source, configuration.Identifier!, listing, detail);
                    if (ExternalJobStorageContract.Validate(new ExternalJobNormalizer().Normalize(job)) is not null)
                        throw new InvalidDataException("SMARTRECRUITERS_FIELD_LENGTH_INVALID");
                    jobs.Add(job);
                }
                catch (Exception error) when (error is InvalidDataException data && !data.Message.Contains("_BUDGET", StringComparison.Ordinal) || error is JsonException ||
                    error is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
                { skipped++; Add(reasons, "SMARTRECRUITERS_DETAIL_INVALID"); }
                catch (OperationCanceledException) when (!linked.IsCancellationRequested)
                { skipped++; Add(reasons, "SMARTRECRUITERS_DETAIL_TIMEOUT"); }
            }
            // A second full ID census catches changes during enrichment, including
            // same-count substitutions. Total alone is not an absence contract.
            var verification = await scan.InventoryAsync(configuration.Identifier!);
            complete = inventory.Count > 0 && skipped == 0 && inventory.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(verification.Keys);
            reason = complete ? "SMARTRECRUITERS_PUBLIC_CENSUS" : inventory.Count == 0
                ? "SMARTRECRUITERS_EMPTY_COMPANY_UNPROVEN" : "SMARTRECRUITERS_SNAPSHOT_CHANGED_OR_DETAIL_INVALID";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException)
        {
            reason = error is InvalidDataException data ? data.Message :
                error is OperationCanceledException ? "SMARTRECRUITERS_TIMEOUT" : JobSourceFailure.Code(error);
            skipped++;
        }
        Add(reasons, reason);
        reasons["SmartRecruitersRequests"] = scan.Requests;
        reasons["SmartRecruitersBytes"] = checked((int)scan.Bytes);
        reasons["SmartRecruitersListingPages"] = scan.Pages;
        reasons["SmartRecruitersDetailRequests"] = scan.Details;
        if (logger is not null) Summary(logger, source.Id, scan.Requests, scan.Bytes, jobs.Count, complete, reason, null);
        return new(jobs, skipped, complete) { ReportedListingTotal = total ?? scan.AdvertisedTotal,
            ObservedAtUtc = time.GetUtcNow().UtcDateTime, ProviderReasonCounts = reasons };
    }

    private static void Add(Dictionary<string, int> reasons, string reason) => reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
    private static bool HasContent(JsonElement job) => job.Field("jobAd").Field("sections").Field("jobDescription").Text("text") is { Length: > 0 } &&
        job.Text("applyUrl") is { Length: > 0 } && job.Field("active").ValueKind == JsonValueKind.True;
    private static string Identity(JsonElement job) => Guid.TryParseExact(job.Text("uuid"), "D", out var id) && id != Guid.Empty
        ? id.ToString("D") : throw new InvalidDataException("SMARTRECRUITERS_IDENTITY_INVALID");
    private static RawExternalJob Map(JobSource source, string company, JsonElement listing, JsonElement detail)
    {
        var identity = Identity(listing);
        var numericId = listing.Text("id");
        if (Identity(detail) != identity || string.IsNullOrWhiteSpace(numericId) || numericId.Length > 32 || !numericId.All(char.IsAsciiDigit) || numericId != detail.Text("id") ||
            !string.Equals(detail.Field("company").Text("identifier"), company, StringComparison.OrdinalIgnoreCase) ||
            detail.Field("active").ValueKind != JsonValueKind.True)
            throw new InvalidDataException("SMARTRECRUITERS_DETAIL_BINDING_INVALID");
        var apply = detail.Text("applyUrl");
        var canonical = string.IsNullOrWhiteSpace(detail.Text("postingUrl")) ? apply : detail.Text("postingUrl");
        if (!CandidateUrl(canonical, company, numericId) || !CandidateUrl(apply, company, numericId))
            throw new InvalidDataException("SMARTRECRUITERS_APPLICATION_URL_UNVERIFIED");
        var sections = detail.Field("jobAd").Field("sections");
        static string? Plain(string? value) => new ExternalJobNormalizer().Normalize(new RawExternalJob
            { Description = value, DescriptionIsHtml = true }).Description;
        string? Text(string name) => Plain(sections.Field(name).Text("text"));
        var description = Text("jobDescription");
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(detail.Text("name")))
            throw new InvalidDataException("SMARTRECRUITERS_DESCRIPTION_MISSING");
        // Additional information is not necessarily benefits. Preserve it and the
        // company introduction under explicit headings, not in invented sections.
        foreach (var section in new[] { "companyDescription", "additionalInformation" })
            if (Text(section) is { Length: > 0 } text) description += "\n\n" + (Plain(sections.Field(section).Text("title")) ?? section) + "\n" + text;
        var location = detail.Field("location");
        var country = location.Text("country");
        var timestamp = detail.Text("releasedDate") ?? listing.Text("releasedDate");
        var dated = timestamp is not null && (timestamp.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(timestamp, @"[+-]\d{2}:\d{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) &&
            DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) ? instant.UtcDateTime : (DateTime?)null;
        return new() { ExternalId = identity, Title = detail.Text("name")!, CompanyId = source.CompanyId,
            CompanyName = source.Company?.Name ?? detail.Field("company").Text("name") ?? "",
            Description = description, Requirements = Text("qualifications"),
            Location = string.Join(", ", new[] { location.Text("city"), location.Text("region"), country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
            CountryCodes = country is null ? [] : [country], ApplicationUrl = apply, CanonicalJobUrl = canonical,
            DiscoveredUrl = canonical, SourcePostedAtUtc = dated, EmploymentTypeText = detail.Field("typeOfEmployment").Text("label"),
            WorkplaceType = location.Field("hybrid").ValueKind == JsonValueKind.True ? WorkplaceType.Hybrid :
                location.Field("remote").ValueKind == JsonValueKind.True ? WorkplaceType.Remote : null,
            ExperienceLevel = detail.Field("experienceLevel").Text("id") switch
            { "entry_level" => ExperienceLevel.Entry, "associate" => ExperienceLevel.Junior,
                "director" or "executive" => ExperienceLevel.Executive, _ => null },
            ExternalCategory = detail.Field("department").Text("label") ?? detail.Field("function").Text("label"),
            SourceMetadataJson = detail.GetRawText() };
    }
    private static bool CandidateUrl(string? value, string company, string numericId)
    {
        if (!PublicAtsSnapshot.SafeHttps(value, out var uri) || uri!.Host is not ("jobs.smartrecruiters.com" or "www.smartrecruiters.com") ||
            value!.Contains('\\') || value.Any(char.IsControl)) return false;
        var decodedPath = Uri.UnescapeDataString(uri.AbsolutePath);
        if (decodedPath.Contains('\\') || decodedPath.Any(char.IsControl)) return false;
        var parts = decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals(company, StringComparison.OrdinalIgnoreCase) &&
            (parts[1] == numericId || parts[1].StartsWith(numericId + "-", StringComparison.Ordinal));
    }

    private sealed class Scan(HttpClient client, SmartRecruitersOptions settings, TimeProvider clock, AggregationHttpRetryState retryState, CancellationToken token)
    {
        public int Requests { get; private set; }
        public int Details { get; private set; }
        public int Pages { get; private set; }
        public long Bytes { get; private set; }
        public int? AdvertisedTotal { get; private set; }
        private static bool Number(JsonElement page, string name, out int value)
        {
            value = 0;
            return page.Field(name).ValueKind == JsonValueKind.Number && page.Field(name).TryGetInt32(out value);
        }
        public async Task<Dictionary<string, JsonElement>> InventoryAsync(string company)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            int? total = null;
            for (var offset = 0; ; offset += 100)
            {
                if (Pages >= settings.MaximumPages) throw new InvalidDataException("SMARTRECRUITERS_PAGE_BUDGET");
                Pages++;
                var page = await ReadAsync($"v1/companies/{company}/postings?destination=PUBLIC&limit=100&offset={offset}", false);
                if (!Number(page, "totalFound", out var advertised) || advertised < 0 || advertised > settings.MaximumRecords ||
                    !Number(page, "offset", out var returnedOffset) || returnedOffset != offset ||
                    !Number(page, "limit", out var limit) || limit != 100 || page.Field("content").ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("SMARTRECRUITERS_PAGINATION_INVALID");
                AdvertisedTotal ??= advertised;
                total ??= advertised;
                if (total != advertised) throw new InvalidDataException("SMARTRECRUITERS_TOTAL_CHANGED");
                var content = page.Field("content");
                if (content.GetArrayLength() != Math.Min(100, advertised - offset))
                    throw new InvalidDataException("SMARTRECRUITERS_PAGE_INCOMPLETE");
                foreach (var item in content.EnumerateArray())
                    if (!string.Equals(item.Field("company").Text("identifier"), company, StringComparison.OrdinalIgnoreCase) || !result.TryAdd(Identity(item), item.Clone()))
                        throw new InvalidDataException("SMARTRECRUITERS_DUPLICATE_OR_UNBOUND_IDENTITY");
                if (result.Count == advertised) return result;
            }
        }
        public async Task<JsonElement> ReadAsync(string path, bool detail)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (retryState.Remaining("SmartRecruiters", clock.GetUtcNow()) > TimeSpan.Zero)
                    throw new HttpRequestException("SmartRecruiters rate limited.", null, HttpStatusCode.TooManyRequests);
                if (Requests >= settings.MaximumRequests) throw new InvalidDataException("SMARTRECRUITERS_REQUEST_BUDGET");
                if (settings.RequestIntervalMilliseconds > 0) await Task.Delay(TimeSpan.FromMilliseconds(settings.RequestIntervalMilliseconds), clock, token);
                Requests++; if (detail) Details++;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds), clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.smartrecruiters.com/" + path);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("SMARTRECRUITERS_REDIRECT_REJECTED");
                await using var body = await response.Content.ReadAsStreamAsync(linked.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = await body.ReadAsync(chunk, linked.Token)) > 0)
                {
                    Bytes += count;
                    if (Bytes > settings.MaximumScanBytes || buffer.Length + count > settings.MaximumResponseBytes)
                        throw new InvalidDataException("SMARTRECRUITERS_BYTE_BUDGET");
                    buffer.Write(chunk, 0, count);
                }
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : TimeSpan.FromSeconds(attempt));
                if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is not null && delay > TimeSpan.Zero)
                {
                    var now = clock.GetUtcNow();
                    retryState.Defer("SmartRecruiters", now + (delay > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue - now : delay));
                }
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode is 408 or 500 or 502 or 503 or 504) && attempt < 3)
                {
                    if (delay > TimeSpan.FromSeconds(30)) response.EnsureSuccessStatusCode();
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, token);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(buffer.ToArray());
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("SMARTRECRUITERS_RESPONSE_INVALID");
                return json.RootElement.Clone();
            }
            throw new InvalidDataException("SMARTRECRUITERS_RETRY_EXHAUSTED");
        }
    }
}
