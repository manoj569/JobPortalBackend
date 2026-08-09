using System.Text;
using JobPortal.Application.Abstractions.AdminImports;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.AdminImports;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1848

namespace JobPortal.Application.Features.JobDiscovery;

public sealed class JobDiscoveryService(IJobDiscoveryRepository repository, IUnitOfWork unitOfWork,
    IEnumerable<IExternalJobSourceProvider> providers, IAdminImportService imports,
    IOptions<JobDiscoveryOptions> options, TimeProvider clock, ILogger<JobDiscoveryService> logger) : IJobDiscoveryService
{
    public async Task<JobDiscoveryRunSummary> RunAsync(string trigger, JobDiscoveryCriteria? criteria, CancellationToken ct)
    {
        var run = new JobDiscoveryRun { Trigger = trigger, StartedAtUtc = clock.GetUtcNow().UtcDateTime };
        await repository.AddAsync(run, ct);
        var errors = new List<string>();
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configured = providers.Where(p => p.IsConfigured && options.Value.Providers.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (var provider in configured)
        {
            try
            {
                var searches = criteria is null ? BuildSearches(options.Value) : [criteria];
                foreach (var search in searches)
                    foreach (var candidate in await provider.SearchAsync(search, ct))
                    {
                        if (string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.CompanyName) ||
                            string.IsNullOrWhiteSpace(candidate.CategoryName) || !Uri.TryCreate(candidate.ApplicationUrl, UriKind.Absolute, out _)) continue;
                        if (!seenSources.Add($"{provider.Name}:{candidate.SourceJobId.Trim()}")) continue;
                        var duplicate = await repository.FindDuplicateAsync(provider.Name, candidate,
                            clock.GetUtcNow().UtcDateTime.AddDays(-30), ct);
                        run.Items.Add(new JobDiscoveryItem
                        {
                            Provider = provider.Name, SourceJobId = candidate.SourceJobId.Trim(), Title = candidate.Title.Trim(),
                            CompanyName = candidate.CompanyName.Trim(), CategoryName = candidate.CategoryName.Trim(),
                            ApplicationUrl = candidate.ApplicationUrl.Trim(), Location = Null(candidate.Location),
                            Description = Null(candidate.Description), EmploymentType = Null(candidate.EmploymentType),
                            PublishedAtUtc = candidate.PublishedAtUtc, Status = duplicate.Reason is null ? "Candidate" : "Duplicate",
                            DuplicateReason = duplicate.Reason, ExistingJobId = duplicate.JobId
                        });
                    }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{provider.Name}: provider request failed");
                logger.LogWarning(ex, "Job discovery provider {Provider} failed for run {RunId}", provider.Name, run.Id);
            }
        }
        run.CandidateCount = run.Items.Count;
        run.DuplicateCount = run.Items.Count(x => x.Status == "Duplicate");
        run.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        run.Status = errors.Count == 0 ? "Completed" : run.Items.Count > 0 ? "CompletedWithErrors" : "Failed";
        run.ErrorSummary = errors.Count == 0 ? null : string.Join("; ", errors);
        await unitOfWork.SaveChangesAsync(ct);
        return Map(run);
    }

    public Task<IReadOnlyCollection<JobDiscoveryRunSummary>> ListAsync(int take, CancellationToken ct) =>
        repository.ListAsync(Math.Clamp(take, 1, 100), ct);

    public Task<JobDiscoveryRunDetailsResponse?> GetAsync(Guid id, CancellationToken ct) =>
        repository.GetDetailsAsync(id, ct);

    public async Task<CsvImportResult> PreviewAsync(Guid runId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var run = await repository.GetDetailsAsync(runId, ct) ?? throw new KeyNotFoundException("Discovery run was not found.");
        await using var file = Csv(run.Items.Where(x => itemIds.Contains(x.Id) && x.Status == "Candidate"));
        return await imports.PreviewJobsAsync(new("job-discovery.csv", file.Length, file), ct);
    }

    public async Task<JobDiscoveryCommitResult> CommitAsync(Guid administratorId, Guid runId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var run = await RequireRun(runId, ct);
        var selected = run.Items.Where(x => itemIds.Contains(x.Id) && x.Status == "Candidate").ToArray();
        await using var file = Csv(selected);
        var result = await imports.CommitJobsAsync(administratorId, new("job-discovery.csv", file.Length, file), ct);
        if (result.InvalidRows == 0)
        {
            foreach (var item in selected) item.Status = "Imported";
            run.ImportedCount += selected.Length;
            await unitOfWork.SaveChangesAsync(ct);
        }
        return new(selected.Length, result);
    }

    private async Task<JobDiscoveryRun> RequireRun(Guid id, CancellationToken ct) =>
        await repository.GetForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Discovery run was not found.");
    private static JobDiscoveryRunSummary Map(JobDiscoveryRun x) => new(x.Id, x.Trigger, x.Status, x.StartedAtUtc, x.CompletedAtUtc, x.CandidateCount, x.DuplicateCount, x.ImportedCount, x.ErrorSummary);
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static JobDiscoveryCriteria[] BuildSearches(JobDiscoveryOptions o) =>
        (from q in o.Queries.DefaultIfEmpty("") from l in o.Locations.DefaultIfEmpty("") select new JobDiscoveryCriteria(q, l, o.DefaultCountry)).ToArray();
    private static MemoryStream Csv(IEnumerable<JobDiscoveryItem> items)
    {
        static string E(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
        var text = new StringBuilder("title,companyName,categoryName,description,applicationUrl,employmentType,location\r\n");
        foreach (var x in items) text.AppendJoin(',', E(x.Title), E(x.CompanyName), E(x.CategoryName), E(x.Description), E(x.ApplicationUrl), E(x.EmploymentType), E(x.Location)).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
    }
    private static MemoryStream Csv(IEnumerable<JobDiscoveryItemResponse> items)
    {
        static string E(string? value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
        var text = new StringBuilder("title,companyName,categoryName,description,applicationUrl,employmentType,location\r\n");
        foreach (var x in items) text.AppendJoin(',', E(x.Title), E(x.CompanyName), E(x.CategoryName), E(x.Description), E(x.ApplicationUrl), E(x.EmploymentType), E(x.Location)).Append("\r\n");
        return new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
