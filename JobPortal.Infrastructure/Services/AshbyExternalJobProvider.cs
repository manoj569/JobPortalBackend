using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Application.Features.JobAggregation;

namespace JobPortal.Infrastructure.Services;

public sealed class AshbyExternalJobProvider(IHttpClientFactory clients) : IBatchedExternalJobProvider
{
    public const string HttpClientName = "AshbyJobAggregation";
    public AtsType AtsType => AtsType.Ashby;

    public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) =>
        PublicAtsSnapshot.CollectAsync(FetchBatchesAsync(source,cancellationToken),cancellationToken);
    public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (records, complete) = await ReadJobsAsync(source, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal); var healthy = complete; var entries = records.ToArray();
        if (entries.Length == 0) { yield return new([], 0, complete); yield break; }
        for (var start = 0; start < entries.Length; start += 50)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = entries.Skip(start).Take(50).ToArray();
            var unique = group.Where(job => job.ExternalId is not { Length: > 0 } id || seen.Add(id)).ToArray();
            var duplicate = group.Length - unique.Length;
            var batch = PublicAtsSnapshot.Validate(source, unique, true); healthy &= duplicate == 0 && batch.Skipped == 0;
            var reasons = new Dictionary<string,int>(batch.ProviderReasonCounts ?? new Dictionary<string,int>(),StringComparer.Ordinal);
            if (duplicate > 0) reasons["PROVIDER_DUPLICATE_IDENTITY"] = reasons.GetValueOrDefault("PROVIDER_DUPLICATE_IDENTITY") + duplicate;
            yield return batch with { Skipped = batch.Skipped + duplicate, IsComplete = healthy && start + group.Length == entries.Length,
                ProviderReasonCounts = reasons };
        }
    }
    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
        => (await ReadJobsAsync(source, cancellationToken)).Jobs;

    private async Task<(IReadOnlyCollection<RawExternalJob> Jobs, bool Complete)> ReadJobsAsync(JobSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var token = JobSourceConfiguration.Require(AtsType.Ashby, source.CareerPageUrl, source.AtsIdentifier).Identifier!;
        using var response = await clients.CreateClient(HttpClientName)
            .GetAsync($"posting-api/job-board/{Uri.EscapeDataString(token)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var jobs = payload.RootElement.Field("jobs");
        if (jobs.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Ashby jobs response.");
        if (jobs.GetArrayLength() > PublicAtsSnapshot.MaximumRecords) throw new InvalidDataException("public_ats_record_limit");
        // Unlisted/private-link postings must not become discoverable through ingestion.
        var complete = payload.RootElement.Text("apiVersion") == "1" &&
            jobs.EnumerateArray().All(job => job.Field("isListed").ValueKind is JsonValueKind.True or JsonValueKind.False);
        var records = jobs.EnumerateArray().Where(job => job.Field("isListed").ValueKind == JsonValueKind.True)
            .Select(job => new RawExternalJob
            {
                Title = job.Text("title") ?? string.Empty,
                CompanyName = source.Company?.Name ?? string.Empty,
                Location = job.Text("location"),
                AdditionalLocations = Secondary(job).Select(x => x.Text("location")).OfType<string>().ToArray(),
                CountryCodes = Secondary(job).Select(x => x.Field("address").Text("addressCountry"))
                    .Append(job.Field("address").Field("postalAddress").Text("addressCountry"))
                    .OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
                Description = job.Text("descriptionPlain"),
                ApplicationUrl = string.IsNullOrWhiteSpace(job.Text("applyUrl")) ? job.Text("jobUrl") : job.Text("applyUrl"),
                // The public posting UUID is published in the official job URL. Never hash an arbitrary URL into an ID.
                ExternalId = PublicAtsSnapshot.AshbyId(job.Text("jobUrl"), token),
                SourcePostedAtUtc = PublishedAt(job),
                EmploymentTypeText = job.Text("employmentType"),
                WorkplaceTypeText = job.Text("workplaceType"),
                ExternalCategory = string.IsNullOrWhiteSpace(job.Text("department")) ? job.Text("team") : job.Text("department")
            }).ToArray();
        return (records, complete);
    }

    private static JsonElement[] Secondary(JsonElement job) => job.Field("secondaryLocations") is var value &&
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];

    private static DateTime? PublishedAt(JsonElement job)
    {
        var value = job.Field("publishedAt");
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()!;
        var offset = text.EndsWith('Z') || text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':';
        return offset && value.TryGetDateTimeOffset(out var instant) ? instant.UtcDateTime : null;
    }
}
