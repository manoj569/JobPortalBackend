using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

public sealed class GreenhouseExternalJobProvider(
    IHttpClientFactory clients) : IExternalJobProvider
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    public const string HttpClientName = "GreenhouseJobAggregation";

    public AtsType AtsType => AtsType.Greenhouse;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(
        JobSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var boardToken = source.AtsIdentifier?.Trim();

        if (string.IsNullOrWhiteSpace(boardToken))
        {
            throw new InvalidOperationException(
                "Greenhouse ATS identifier is required.");
        }

        var uri =
            $"v1/boards/{Uri.EscapeDataString(boardToken)}/jobs?content=true";

        using var response = await clients
            .CreateClient(HttpClientName)
            .GetAsync(uri, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var payload = JsonSerializer.Deserialize<Response>(
            json,
            JsonOptions);

        if (payload?.Jobs is null)
            return [];

        var companyName = source.Company?.Name?.Trim() ?? string.Empty;

        return payload.Jobs.Select(item => MapJob(item, companyName)).ToArray();
    }

    private static RawExternalJob MapJob(JsonElement item, string companyName)
    {
        try
        {
            var job = item.Deserialize<GreenhouseJob>(JsonOptions);
            if (job is null) return new RawExternalJob();
            return new RawExternalJob
            {
                Title = job.Title?.Trim() ?? string.Empty,
                CompanyName = companyName,
                Location = job.Location?.Name?.Trim(),
                Description = job.Content,
                ApplicationUrl = job.AbsoluteUrl?.Trim(),
                ExternalId = job.Id?.ToString(CultureInfo.InvariantCulture)
            };
        }
        catch (JsonException)
        {
            // Keep malformed records invalid without losing the rest of the board.
            return new RawExternalJob();
        }
    }

    private sealed record Response(
        [property: JsonPropertyName("jobs")]
        JsonElement[]? Jobs);

    private sealed record GreenhouseJob(
        [property: JsonPropertyName("id")]
        long? Id,

        [property: JsonPropertyName("title")]
        string? Title,

        [property: JsonPropertyName("absolute_url")]
        string? AbsoluteUrl,

        [property: JsonPropertyName("location")]
        GreenhouseLocation? Location,

        [property: JsonPropertyName("content")]
        string? Content);

    private sealed record GreenhouseLocation(
        [property: JsonPropertyName("name")]
        string? Name);
}
