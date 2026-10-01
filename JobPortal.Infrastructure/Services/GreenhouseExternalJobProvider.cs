using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Application.Features.JobAggregation;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Services;

public sealed class GreenhouseExternalJobProvider(IHttpClientFactory clients, IOptions<JobAggregationOptions>? options = null) : IExternalJobProvider
{
    public const string HttpClientName = "GreenhouseJobAggregation";
    public AtsType AtsType => AtsType.Greenhouse;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
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
        var results = new RawExternalJob[entries.Length];
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
            // Greenhouse documents application_deadline on the detail endpoint, not the list.
            // Bounded requests retain the hardened client's retry/timeout policy and input order.
            var expiry = externalId is null || string.IsNullOrWhiteSpace(job.Text("title"))
                ? null
                : await ReadDeadlineAsync(client, token, externalId, tokenCancellation);
            results[index] = new RawExternalJob
            {
                Title = job.Text("title")?.Trim() ?? string.Empty,
                CompanyName = source.Company?.Name?.Trim() ?? string.Empty,
                Location = job.Field("location").Text("name")?.Trim(),
                Description = job.Text("content"),
                DescriptionIsHtml = true,
                ApplicationUrl = job.Text("absolute_url")?.Trim(),
                ExternalId = externalId,
                ExpiresAtUtc = expiry,
                ExternalCategory = job.SingleDepartment()
            };
        });
        return results;
    }

    private static async Task<DateTime?> ReadDeadlineAsync(
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
            return null;
        }
    }

    private static async Task<DateTime?> ReadDeadlineCoreAsync(
        HttpClient client, string token, string id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"v1/boards/{Uri.EscapeDataString(token)}/jobs/{Uri.EscapeDataString(id)}", cancellationToken);
        // A post may disappear between the list and detail requests. No deadline is assumed.
        if (!response.IsSuccessStatusCode)
            return null;

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var detail = payload.RootElement;
        if (detail.NumericId("id") != id) return null;
        var deadline = detail.Field("application_deadline");
        if (deadline.ValueKind != JsonValueKind.String) return null;
        var text = deadline.GetString()!;
        // JSON's ISO timestamp parser accepts offset-less values too: reject those explicitly.
        var explicitOffset = text.EndsWith('Z') ||
            (text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':');
        return explicitOffset && deadline.TryGetDateTimeOffset(out var value)
            ? value.UtcDateTime : null;
    }
}
