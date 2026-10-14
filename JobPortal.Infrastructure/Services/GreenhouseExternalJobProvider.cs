using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
namespace JobPortal.Infrastructure.Services;

public sealed partial class GreenhouseExternalJobProvider(IHttpClientFactory clients, IOptions<JobAggregationOptions>? options = null,
    ILogger<GreenhouseExternalJobProvider>? logger = null) : IBatchedExternalJobProvider
{
    public const string HttpClientName = "GreenhouseJobAggregation";
    private const int BatchSize = 25;
    public AtsType AtsType => AtsType.Greenhouse;
    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        var records = new List<RawExternalJob>(); var skipped = 0; var complete = false; int? total = null;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        await foreach (var batch in FetchBatchesAsync(source, cancellationToken))
        {
            records.AddRange(batch.Jobs); skipped += batch.Skipped; complete = batch.IsComplete; total = batch.ReportedListingTotal;
            foreach (var (reason, count) in batch.ProviderReasonCounts ?? new Dictionary<string, int>())
                reasons[reason] = reasons.GetValueOrDefault(reason) + count;
        }
        return new(records, skipped, complete) { ReportedListingTotal = total, ProviderReasonCounts = reasons };
    }
    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        var records = new List<RawExternalJob>();
        await foreach (var batch in ReadBatchesAsync(source, false, cancellationToken)) records.AddRange(batch.Jobs);
        return records;
    }
    public IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source, CancellationToken cancellationToken = default) =>
        ReadBatchesAsync(source, true, cancellationToken);
    private async IAsyncEnumerable<ExternalJobSourceSnapshot> ReadBatchesAsync(JobSource source, bool strict,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var token = JobSourceConfiguration.Require(AtsType.Greenhouse, source.CareerPageUrl, source.AtsIdentifier).Identifier!;
        var concurrency = options?.Value.GreenhouseDetailConcurrency ?? 2;
        if (concurrency is < 1 or > 4)
        {
            var error = new InvalidOperationException("Greenhouse detail concurrency must be between 1 and 4.");
            error.Data["JobSourceReasonCode"] = "CONFIGURATION_INVALID"; throw error;
        }
        var client = clients.CreateClient(HttpClientName);
        using var response = await client.GetAsync($"v1/boards/{Uri.EscapeDataString(token)}/jobs?content=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var jobs = payload.RootElement.Field("jobs");
        if (jobs.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Greenhouse jobs response.");
        var entries = jobs.EnumerateArray().ToArray();
        if (entries.Length > PublicAtsSnapshot.MaximumRecords) throw new InvalidDataException("public_ats_record_limit");
        var totalValue = payload.RootElement.Field("meta").Field("total");
        int? total = totalValue.ValueKind == JsonValueKind.Number && totalValue.TryGetInt32(out var count) && count >= 0 ? count : null;
        var identities = new HashSet<string>(StringComparer.Ordinal); var healthy = total == entries.Length;
        if (logger is not null) LogListing(logger, source.Id, entries.Length);
        if (entries.Length == 0) { yield return new([], 0, total == 0) { ReportedListingTotal = total ?? 0,
            ProviderReasonCounts = total == 0 ? null : new Dictionary<string,int> { ["PROVIDER_RESPONSE_INVALID"] = 1 } }; yield break; }
        for (var start = 0; start < entries.Length; start += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = Math.Min(BatchSize, entries.Length - start); var batchNumber = start / BatchSize + 1;
            var results = new RawExternalJob[size]; var available = new bool[size]; var attempted = new bool[size]; var reasons = new string?[size];
            if (logger is not null) LogBatchStarted(logger, source.Id, batchNumber, size);
            await Parallel.ForEachAsync(Enumerable.Range(0, size), new ParallelOptions
                { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken }, async (index, ct) =>
            {
                var job = entries[start + index]; var id = job.NumericId("id");
                if (strict && job.Field("internal_job_id").ValueKind == JsonValueKind.Null)
                { reasons[index] = "GREENHOUSE_PROSPECT_POST"; return; }
                attempted[index] = id is not null && !string.IsNullOrWhiteSpace(job.Text("title"));
                var detail = id is null || string.IsNullOrWhiteSpace(job.Text("title"))
                    ? (Deadline: (DateTime?)null, Available: false, Reason: "PROVIDER_RESPONSE_INVALID")
                    : await ReadDeadlineAsync(client, token, id, ct);
                available[index] = detail.Available; reasons[index] = detail.Reason;
                results[index] = new() { Title = job.Text("title")?.Trim() ?? "", CompanyName = source.Company?.Name?.Trim() ?? "",
                    Location = job.Field("location").Text("name")?.Trim(), Description = job.Text("content"), DescriptionIsHtml = true,
                    ApplicationUrl = job.Text("absolute_url")?.Trim(), ExternalId = id, ExpiresAtUtc = detail.Deadline, ExternalCategory = job.SingleDepartment() };
            });
            var raw = strict ? results.Where((item, index) => available[index] && item is not null).ToArray() : results.Where(item => item is not null).ToArray();
            var records = new List<RawExternalJob>(); var removed = strict ? size - raw.Length : 0;
            foreach (var job in raw) { if (!strict || job.ExternalId is { } id && identities.Add(id)) records.Add(job); else removed++; }
            var selected = strict ? PublicAtsSnapshot.Validate(source, records, true) : new ExternalJobSourceSnapshot(records, 0, false);
            var skipped = selected.Skipped + removed; healthy &= available.All(value => value) && skipped == 0;
            var reasonCounts = reasons.OfType<string>().GroupBy(reason => reason, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var (reason,reasonCount) in selected.ProviderReasonCounts ?? new Dictionary<string,int>())
                reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason) + reasonCount;
            if (skipped > 0 && reasonCounts.Count == 0) reasonCounts["PROVIDER_PARSE_PARTIAL"] = skipped;
            if (start + size >= entries.Length && total != entries.Length) reasonCounts["PROVIDER_RESPONSE_INVALID"] = 1;
            if (strict && healthy && start + size >= entries.Length)
            {
                // Recheck identities before absence reconciliation. A changed board
                // may still ingest safe batches, but cannot certify missing jobs.
                using var verification = await client.GetAsync($"v1/boards/{Uri.EscapeDataString(token)}/jobs?content=false", cancellationToken);
                verification.EnsureSuccessStatusCode();
                using var verificationBody = JsonDocument.Parse(await verification.Content.ReadAsStringAsync(cancellationToken));
                var current = verificationBody.RootElement.Field("jobs");
                var currentTotal = verificationBody.RootElement.Field("meta").Field("total");
                var ids = entries.Select(entry => entry.NumericId("id")).Order(StringComparer.Ordinal).ToArray();
                healthy = current.ValueKind == JsonValueKind.Array && current.GetArrayLength() == entries.Length &&
                    currentTotal.ValueKind == JsonValueKind.Number && currentTotal.TryGetInt32(out var finalTotal) && finalTotal == total &&
                    ids.SequenceEqual(current.EnumerateArray().Select(entry => entry.NumericId("id")).Order(StringComparer.Ordinal), StringComparer.Ordinal);
                if (!healthy) reasonCounts["PROVIDER_SNAPSHOT_CHANGED"] = 1;
            }
            if (logger is not null) { var succeeded = available.Where((value, index) => value && attempted[index]).Count();
                LogBatchCompleted(logger, source.Id, batchNumber, succeeded, attempted.Count(value => value) - succeeded);
                LogReady(logger, source.Id, batchNumber, selected.Jobs.Count, skipped); }
            // Iterator backpressure: the runner commits this batch before fetching the next.
            yield return selected with { Skipped = skipped, IsComplete = strict && healthy && start + size >= entries.Length,
                ReportedListingTotal = total ?? entries.Length, ProviderReasonCounts = reasonCounts };
        }
    }
    [LoggerMessage(EventId = 4400, Level = LogLevel.Information, Message = "GreenhouseListingFetched SourceId={SourceId} ListingTotal={ListingTotal}.")]
    private static partial void LogListing(ILogger logger, Guid sourceId, int listingTotal);
    [LoggerMessage(EventId = 4401, Level = LogLevel.Information, Message = "GreenhouseDetailBatchStarted SourceId={SourceId} BatchNumber={BatchNumber} BatchSize={BatchSize}.")]
    private static partial void LogBatchStarted(ILogger logger, Guid sourceId, int batchNumber, int batchSize);
    [LoggerMessage(EventId = 4402, Level = LogLevel.Information, Message = "GreenhouseDetailBatchCompleted SourceId={SourceId} BatchNumber={BatchNumber} Succeeded={Succeeded} Failed={Failed}.")]
    private static partial void LogBatchCompleted(ILogger logger, Guid sourceId, int batchNumber, int succeeded, int failed);
    [LoggerMessage(EventId = 4403, Level = LogLevel.Information, Message = "GreenhouseParsedBatchReady SourceId={SourceId} BatchNumber={BatchNumber} Parsed={Parsed} Skipped={Skipped}.")]
    private static partial void LogReady(ILogger logger, Guid sourceId, int batchNumber, int parsed, int skipped);
    private static async Task<(DateTime? Deadline, bool Available, string? Reason)> ReadDeadlineAsync(
        HttpClient client, string token, string id, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadDeadlineCoreAsync(client, token, id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            // Optional enrichment is fail-soft, including exhausted transport/timeouts.
            // Never log raw exceptions/response bodies or turn missing data into a deadline.
            return (null, false, JobSourceFailure.Code(exception));
        }
    }

    private static async Task<(DateTime? Deadline, bool Available, string? Reason)> ReadDeadlineCoreAsync(
        HttpClient client, string token, string id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"v1/boards/{Uri.EscapeDataString(token)}/jobs/{Uri.EscapeDataString(id)}", cancellationToken);
        // A post may disappear between the list and detail requests. No deadline is assumed.
        if (!response.IsSuccessStatusCode)
            return (null, false, JobSourceFailure.Code(new HttpRequestException("Provider detail failed.", null, response.StatusCode)));

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var detail = payload.RootElement;
        if (detail.NumericId("id") != id) return (null, false, "PROVIDER_RESPONSE_INVALID");
        var deadline = detail.Field("application_deadline");
        if (deadline.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return (null, true, null);
        if (deadline.ValueKind != JsonValueKind.String) return (null, false, "PROVIDER_RESPONSE_INVALID");
        var text = deadline.GetString()!;
        // JSON's ISO timestamp parser accepts offset-less values too: reject those explicitly.
        var explicitOffset = text.EndsWith('Z') ||
            (text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':');
        return explicitOffset && deadline.TryGetDateTimeOffset(out var value)
            ? (value.UtcDateTime, true, null) : (null, false, "PROVIDER_RESPONSE_INVALID");
    }
}
