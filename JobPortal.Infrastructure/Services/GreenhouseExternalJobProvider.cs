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
        using var response = await clients.CreateClient(HttpClientName)
            .GetAsync($"v1/boards/{Uri.EscapeDataString(token)}/jobs?content=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var jobs = payload.RootElement.Field("jobs");
        if (jobs.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Greenhouse jobs response.");
        return jobs.EnumerateArray().Select(job => new RawExternalJob
        {
            Title = job.Text("title")?.Trim() ?? string.Empty,
            CompanyName = source.Company?.Name?.Trim() ?? string.Empty,
            Location = job.Field("location").Text("name")?.Trim(),
            Description = job.Text("content"),
            DescriptionIsHtml = true,
            ApplicationUrl = job.Text("absolute_url")?.Trim(),
            ExternalId = job.NumericId("id"),
            ExternalCategory = job.SingleDepartment()
        }).ToArray();
    }
}
