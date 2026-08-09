using JobPortal.Application.Features.AdminImports;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.JobDiscovery;

public sealed class JobDiscoveryOptions
{
    public const string SectionName = "JobDiscovery";
    public bool Enabled { get; set; }
    public int RunHourUtc { get; set; } = 2;
    public string DefaultCountry { get; set; } = "in";
    public string[] Queries { get; set; } = [];
    public string[] Locations { get; set; } = [];
    public string[] Providers { get; set; } = ["Adzuna"];
}

public sealed record JobDiscoveryCriteria(string? Query = null, string? Location = null, string? Country = null);
public sealed record ExternalJobCandidate(string SourceJobId, string Title, string CompanyName,
    string CategoryName, string ApplicationUrl, string? Location = null, string? Description = null,
    string? EmploymentType = null, DateTime? PublishedAtUtc = null);
public sealed record JobDiscoveryRunSummary(Guid Id, string Trigger, string Status, DateTime StartedAtUtc,
    DateTime? CompletedAtUtc, int CandidateCount, int DuplicateCount, int ImportedCount, string? ErrorSummary);
public sealed record JobDiscoveryItemResponse(Guid Id, string Provider, string SourceJobId, string Title,
    string CompanyName, string CategoryName, string ApplicationUrl, string? Location, string? Description,
    string? EmploymentType, DateTime? PublishedAtUtc, string Status, string? DuplicateReason,
    Guid? ExistingJobId, Guid? ImportedJobId, DateTime CreatedAtUtc);
public sealed record JobDiscoveryRunDetailsResponse(Guid Id, string Trigger, string Status,
    DateTime StartedAtUtc, DateTime? CompletedAtUtc, int CandidateCount, int DuplicateCount,
    int ImportedCount, string? ErrorSummary, IReadOnlyCollection<JobDiscoveryItemResponse> Items);
public sealed record JobDiscoveryCommitRequest(IReadOnlyCollection<Guid> ItemIds);
public sealed record JobDiscoveryCommitResult(int Selected, CsvImportResult Import);

public interface IExternalJobSourceProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyCollection<ExternalJobCandidate>> SearchAsync(JobDiscoveryCriteria criteria, CancellationToken cancellationToken);
}

public interface IJobDiscoveryRepository
{
    Task AddAsync(JobDiscoveryRun run, CancellationToken cancellationToken);
    Task<JobDiscoveryRun?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<JobDiscoveryRunDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<JobDiscoveryRunSummary>> ListAsync(int take, CancellationToken cancellationToken);
    Task<(Guid? JobId, string? Reason)> FindDuplicateAsync(string provider, ExternalJobCandidate candidate, DateTime cutoffUtc, CancellationToken cancellationToken);
}

public interface IJobDiscoveryService
{
    Task<JobDiscoveryRunSummary> RunAsync(string trigger, JobDiscoveryCriteria? criteria, CancellationToken ct);
    Task<IReadOnlyCollection<JobDiscoveryRunSummary>> ListAsync(int take, CancellationToken ct);
    Task<JobDiscoveryRunDetailsResponse?> GetAsync(Guid id, CancellationToken ct);
    Task<CsvImportResult> PreviewAsync(Guid runId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct);
    Task<JobDiscoveryCommitResult> CommitAsync(Guid administratorId, Guid runId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct);
}
