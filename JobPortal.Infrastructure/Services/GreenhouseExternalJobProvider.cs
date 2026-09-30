using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

public sealed class GreenhouseExternalJobProvider(IHttpClientFactory clients) : IExternalJobProvider
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
        var results = new List<RawExternalJob>();
        foreach (var job in jobs.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var externalId = job.NumericId("id");
            // Greenhouse documents application_deadline on the detail endpoint, not the list.
            // Sequential requests use the existing hardened client; no unbounded fan-out.
            var expiry = externalId is null || string.IsNullOrWhiteSpace(job.Text("title"))
                ? null
                : await ReadDeadlineAsync(client, token, externalId, cancellationToken);
            results.Add(new RawExternalJob
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
            });
        }
        return results;
    }

    private static async Task<DateTime?> ReadDeadlineAsync(
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
