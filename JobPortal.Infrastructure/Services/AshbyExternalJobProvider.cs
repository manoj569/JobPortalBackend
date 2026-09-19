using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

public sealed class AshbyExternalJobProvider(IHttpClientFactory clients) : IExternalJobProvider
{
    public const string HttpClientName = "AshbyJobAggregation";
    public AtsType AtsType => AtsType.Ashby;

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var token = source.AtsIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Ashby ATS identifier is required.");
        using var response = await clients.CreateClient(HttpClientName)
            .GetAsync($"posting-api/job-board/{Uri.EscapeDataString(token)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var jobs = payload.RootElement.Field("jobs");
        if (jobs.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Ashby jobs response.");
        // Unlisted/private-link postings must not become discoverable through ingestion.
        return jobs.EnumerateArray().Where(job => job.Field("isListed").ValueKind == JsonValueKind.True)
            .Select(job => new RawExternalJob
            {
                Title = job.Text("title") ?? string.Empty,
                CompanyName = source.Company?.Name ?? string.Empty,
                Location = job.Text("location"),
                Description = job.Text("descriptionPlain"),
                ApplicationUrl = job.Text("jobUrl"),
                EmploymentTypeText = job.Text("employmentType"),
                WorkplaceTypeText = job.Text("workplaceType"),
                ExternalCategory = string.IsNullOrWhiteSpace(job.Text("department")) ? job.Text("team") : job.Text("department")
                // Public contract has no stable ID field: do not fabricate one from URL.
            }).ToArray();
    }
}
