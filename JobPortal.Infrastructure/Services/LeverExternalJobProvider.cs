using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

public sealed class LeverExternalJobProvider(
    IHttpClientFactory clients) : IExternalJobProvider
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    public const string HttpClientName = "LeverJobAggregation";

    public AtsType AtsType => AtsType.Lever;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(
        JobSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var site = source.AtsIdentifier?.Trim();

        if (string.IsNullOrWhiteSpace(site))
        {
            throw new InvalidOperationException(
                "Lever ATS identifier is required.");
        }

        var uri =
            $"v0/postings/{Uri.EscapeDataString(site)}?mode=json";

        using var response = await clients
            .CreateClient(HttpClientName)
            .GetAsync(uri, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var jobs = JsonSerializer.Deserialize<JsonElement[]>(
            json,
            JsonOptions);

        if (jobs is null)
            return [];

        var companyName =
            source.Company?.Name?.Trim() ?? string.Empty;

        return jobs.Select(item => MapJob(item, companyName)).ToArray();
    }

    private static RawExternalJob MapJob(JsonElement item, string companyName)
    {
        try
        {
            var job = item.Deserialize<LeverJob>(JsonOptions);
            if (job is null) return new RawExternalJob();
            return new RawExternalJob
            {
                Title = job.Text?.Trim() ?? string.Empty,
                CompanyName = companyName,
                Location = job.Categories?.Location?.Trim(),
                Description = job.DescriptionPlain?.Trim(),
                ApplicationUrl = job.HostedUrl?.Trim(),
                ExternalId = job.Id?.Trim()
            };
        }
        catch (JsonException)
        {
            // Keep malformed records invalid without losing the remaining postings.
            return new RawExternalJob();
        }
    }

    private sealed record LeverJob(
        [property: JsonPropertyName("id")]
        string? Id,

        [property: JsonPropertyName("text")]
        string? Text,

        [property: JsonPropertyName("hostedUrl")]
        string? HostedUrl,

        [property: JsonPropertyName("descriptionPlain")]
        string? DescriptionPlain,

        [property: JsonPropertyName("categories")]
        LeverCategories? Categories);

    private sealed record LeverCategories(
        [property: JsonPropertyName("location")]
        string? Location);
}
