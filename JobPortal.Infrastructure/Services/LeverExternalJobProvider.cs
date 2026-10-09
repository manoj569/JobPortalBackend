using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

public sealed class LeverExternalJobProvider(IHttpClientFactory clients) : ICompleteExternalJobProvider
{
    public const string HttpClientName = "LeverJobAggregation";
    public AtsType AtsType => AtsType.Lever;

    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        var jobs = new List<RawExternalJob>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset <= PublicAtsSnapshot.MaximumRecords; offset += pageSize)
        {
            var page = await ReadPageAsync(source, $"&skip={offset}&limit={pageSize}", cancellationToken);
            if (page.Count > pageSize || jobs.Count + page.Count > PublicAtsSnapshot.MaximumRecords)
                throw new InvalidDataException("lever_pagination_record_limit");
            foreach (var job in page)
                if (job.ExternalId is { Length: > 0 } id && !seen.Add(id))
                    throw new InvalidDataException("lever_repeated_page_or_identity");
            jobs.AddRange(page);
            if (page.Count < pageSize) return PublicAtsSnapshot.Validate(source, jobs, true);
        }
        throw new InvalidDataException("lever_pagination_record_limit");
    }

    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
        => await ReadPageAsync(source, string.Empty, cancellationToken);

    private async Task<IReadOnlyCollection<RawExternalJob>> ReadPageAsync(JobSource source, string pagination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var token = source.AtsIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Lever ATS identifier is required.");
        var client = clients.CreateClient(HttpClientName);
        // EU boards use Lever's separately documented EU API, never a source-controlled host.
        var path = $"v0/postings/{Uri.EscapeDataString(token)}?mode=json{pagination}";
        var url = Uri.TryCreate(source.CareerPageUrl, UriKind.Absolute, out var board) && board.Host == "jobs.eu.lever.co"
            ? "https://api.eu.lever.co/" + path : path;
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (payload.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Lever jobs response.");
        return payload.RootElement.EnumerateArray().Select(job => new RawExternalJob
        {
            Title = job.Text("text")?.Trim() ?? string.Empty,
            CompanyName = source.Company?.Name?.Trim() ?? string.Empty,
            Location = job.Field("categories").Text("location")?.Trim(),
            AdditionalLocations = PublicAtsSnapshot.Strings(job.Field("categories").Field("allLocations")),
            CountryCodes = job.Text("country") is { Length: > 0 } country ? [country] : [],
            Description = Description(job),
            ApplicationUrl = NonEmpty(job.Text("applyUrl")) ?? job.Text("hostedUrl")?.Trim(),
            ExternalId = job.Text("id")?.Trim(),
            EmploymentTypeText = job.Field("categories").Text("commitment"),
            WorkplaceTypeText = job.Text("workplaceType"),
            ExternalCategory = NonEmpty(job.Field("categories").Text("department")) ?? job.Field("categories").Text("team")
        }).ToArray();
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Description(JsonElement job)
    {
        var parts = new List<string>();
        if (NonEmpty(job.Text("descriptionPlain")) is { } main) parts.Add(main.Trim());
        var lists = job.Field("lists");
        if (lists.ValueKind == JsonValueKind.Array)
            foreach (var list in lists.EnumerateArray())
            {
                if (NonEmpty(list.Text("text")) is { } heading) parts.Add(heading);
                // Convert only HTML fields; do not erase literal code syntax in plaintext.
                if (NonEmpty(list.Text("content")) is { } content)
                    parts.Add(new JobPortal.Application.Services.ExternalJobNormalizer().Normalize(
                        new RawExternalJob { Description = content, DescriptionIsHtml = true }).Description ?? string.Empty);
            }
        if (NonEmpty(job.Text("additionalPlain")) is { } closing) parts.Add(closing);
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }
}
