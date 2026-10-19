using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.JobAggregation;

// Explicit, source-opted-in public inventory protocol. HTML/link exhaustion never
// authorizes absence. Register this identifier only after reviewing the feed.
public static class EmployerDirectInventoryContract
{
    public const string Identifier = "employer-direct:inventory-v1";
    public const string Version = "careerharbor-employer-inventory-v1";
}

public sealed record EmployerDirectInventoryPage(string Contract, string Source,
    string SnapshotVersion, DateTimeOffset ObservedAtUtc, int Total,
    string? NextCursor, IReadOnlyCollection<RawExternalJob> Jobs);

public interface IEmployerDirectInventoryScanner
{
    Task<ExternalJobSourceSnapshot> ScanAsync(JobSource source, CancellationToken cancellationToken);
}
