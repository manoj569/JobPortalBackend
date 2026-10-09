using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Services;

public sealed class GreenhouseExternalJobProvider(IHttpClientFactory clients, IOptions<JobAggregationOptions>? options = null) : ICompleteExternalJobProvider
{
    public const string HttpClientName = "GreenhouseJobAggregation";
    public AtsType AtsType => AtsType.Greenhouse;

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        var (records, total, detailsComplete, removed) = await ReadJobsAsync(source, cancellationToken, strictSnapshot: true);
        // Missing/mismatched meta.total cannot establish a complete snapshot.
        var snapshot = PublicAtsSnapshot.Validate(source, records, total == records.Count + removed && detailsComplete);
        return snapshot with { Skipped = snapshot.Skipped + removed };
    }

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
        => (await ReadJobsAsync(source, cancellationToken)).Jobs;

    private async Task<(IReadOnlyCollection<RawExternalJob> Jobs, int? Total, bool DetailsComplete, int Removed)> ReadJobsAsync(
        JobSource source, CancellationToken cancellationToken, bool strictSnapshot = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var token = source.AtsIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Greenhouse ATS identifier is required.");
        var client = clients.CreateClient(HttpClientName);
        using var response = await client
            .GetAsync($"v1/boards/{Uri.EscapeDataString(token)}/jobs?content=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var jobs = payload.RootElement.Field("jobs");
        if (jobs.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Greenhouse jobs response.");
        var entries = jobs.EnumerateArray().ToArray();
        if (entries.Length > PublicAtsSnapshot.MaximumRecords) throw new InvalidDataException("public_ats_record_limit");
        var totalValue = payload.RootElement.Field("meta").Field("total");
        int? total = totalValue.ValueKind == JsonValueKind.Number && totalValue.TryGetInt32(out var count) && count >= 0 ? count : null;
        var results = new RawExternalJob[entries.Length];
        var available = new bool[entries.Length];
        var gone = new bool[entries.Length];
        var concurrency = options?.Value.GreenhouseDetailConcurrency ?? 2;
        if (concurrency is < 1 or > 4) throw new InvalidOperationException("Greenhouse detail concurrency must be between 1 and 4.");
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken
        }, async (index, tokenCancellation) =>
        {
            tokenCancellation.ThrowIfCancellationRequested();
            var job = entries[index];
            var externalId = job.NumericId("id");
            // Documented prospect posts have internal_job_id=null: they are not individual vacancies.
            // Keep the legacy reader compatible, but never import these from a strict snapshot.
            if (strictSnapshot && job.Field("internal_job_id").ValueKind == JsonValueKind.Null)
            {
                gone[index] = true;
                return;
            }
            // Greenhouse documents application_deadline on the detail endpoint, not the list.
            // Bounded requests retain the hardened client's retry/timeout policy and input order.
            var detail = externalId is null || string.IsNullOrWhiteSpace(job.Text("title"))
                ? (Deadline: (DateTime?)null, Available: false, Gone: false)
                : await ReadDeadlineAsync(client, token, externalId, tokenCancellation);
            available[index] = detail.Available;
            // An unavailable detail cannot verify expiry/continued availability. Do not import it
            // with an invented null deadline; omit it and keep the strict snapshot incomplete.
            gone[index] = strictSnapshot && !detail.Available;
            results[index] = new RawExternalJob
            {
                Title = job.Text("title")?.Trim() ?? string.Empty,
                CompanyName = source.Company?.Name?.Trim() ?? string.Empty,
                Location = job.Field("location").Text("name")?.Trim(),
                Description = job.Text("content"),
                DescriptionIsHtml = true,
                ApplicationUrl = job.Text("absolute_url")?.Trim(),
                ExternalId = externalId,
                ExpiresAtUtc = detail.Deadline,
                ExternalCategory = job.SingleDepartment()
            };
        });
        var removed = gone.Count(x => x);
        return (removed == 0 ? results : results.Where((_, index) => !gone[index]).ToArray(), total, available.All(x => x), removed);
    }

    private static async Task<(DateTime? Deadline, bool Available, bool Gone)> ReadDeadlineAsync(
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
            return (null, false, false);
        }
    }

    private static async Task<(DateTime? Deadline, bool Available, bool Gone)> ReadDeadlineCoreAsync(
        HttpClient client, string token, string id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"v1/boards/{Uri.EscapeDataString(token)}/jobs/{Uri.EscapeDataString(id)}", cancellationToken);
        // A post may disappear between the list and detail requests. No deadline is assumed.
        if (!response.IsSuccessStatusCode)
            return (null, false, response.StatusCode == System.Net.HttpStatusCode.NotFound);

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var detail = payload.RootElement;
        if (detail.NumericId("id") != id) return (null, false, false);
        var deadline = detail.Field("application_deadline");
        if (deadline.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return (null, true, false);
        if (deadline.ValueKind != JsonValueKind.String) return (null, false, false);
        var text = deadline.GetString()!;
        // JSON's ISO timestamp parser accepts offset-less values too: reject those explicitly.
        var explicitOffset = text.EndsWith('Z') ||
            (text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':');
        return explicitOffset && deadline.TryGetDateTimeOffset(out var value)
            ? (value.UtcDateTime, true, false) : (null, false, false);
    }
}
