using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

public sealed class LeverExternalJobProvider(IHttpClientFactory clients) : IExternalJobProvider
{
    public const string HttpClientName = "LeverJobAggregation";
    public AtsType AtsType => AtsType.Lever;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var token = source.AtsIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Lever ATS identifier is required.");
        using var response = await clients.CreateClient(HttpClientName)
            .GetAsync($"v0/postings/{Uri.EscapeDataString(token)}?mode=json", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (payload.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Lever jobs response.");
        return payload.RootElement.EnumerateArray().Select(job => new RawExternalJob
        {
            Title = job.Text("text")?.Trim() ?? string.Empty,
            CompanyName = source.Company?.Name?.Trim() ?? string.Empty,
            Location = job.Field("categories").Text("location")?.Trim(),
            Description = job.Text("descriptionPlain")?.Trim(),
            ApplicationUrl = job.Text("hostedUrl")?.Trim(),
            ExternalId = job.Text("id")?.Trim(),
            EmploymentTypeText = job.Field("categories").Text("commitment"),
            WorkplaceTypeText = job.Text("workplaceType"),
            ExternalCategory = NonEmpty(job.Field("categories").Text("department")) ?? job.Field("categories").Text("team")
        }).ToArray();
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
