using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.JobAggregation;

public sealed record EmployerDirectSnapshotRequest(Guid CompanyId, string CareerPageUrl,
    DateTimeOffset ObservedAtUtc, bool IsComplete, string CompletionReason, IReadOnlyCollection<RawExternalJob> Jobs);

public sealed record EmployerDirectSnapshotEnvelope(string Kind, string Fingerprint, EmployerDirectSnapshotRequest Snapshot);

public sealed class EmployerDirectSnapshotService(IJobSourceRepository sources, IJobSourceRunStore runs, TimeProvider clock)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JobSourceRunResponse> SubmitAsync(Guid sourceId, Guid snapshotId, EmployerDirectSnapshotRequest request,
        Guid? requestedBy, CancellationToken ct)
    {
        if (snapshotId == Guid.Empty) throw new BadRequestException("SnapshotId is required.");
        var source = await sources.GetByIdAsync(sourceId, ct) ?? throw new NotFoundException("Job source was not found.");
        if (source.AtsType != AtsType.Custom || source.AtsIdentifier != "employer-direct" || !source.IsActive || source.IsDeleted)
            throw new BadRequestException("A configured active employer-direct Custom JobSource is required.");
        if (request.CompanyId != source.CompanyId || request.CareerPageUrl != source.CareerPageUrl)
            throw new BadRequestException("Snapshot company/source binding mismatch.");
        if (request.IsComplete) throw new BadRequestException("No generic complete-enumeration contract is enabled.");
        if (request.Jobs is null || request.Jobs.Any(job => string.IsNullOrWhiteSpace(job.ExternalId) || job.ExternalId.Length > 255 ||
            job.AcquisitionSourceId.HasValue || job.AcquisitionProviderKey is not null || !SafeUrl(job.ApplicationUrl) ||
            !SafeUrl(job.DiscoveredUrl) || new Uri(job.DiscoveredUrl!).Host != new Uri(source.CareerPageUrl).Host))
            throw new BadRequestException("Invalid or unbound employer observations.");
        if (request.Jobs.Select(job => job.ExternalId).Distinct(StringComparer.Ordinal).Count() != request.Jobs.Count)
            throw new BadRequestException("Duplicate external job identities.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions))));
        var existing = await runs.GetAsync(sourceId, snapshotId, ct);
        if (existing is not null)
        {
            var retained = existing.CheckpointJson is null ? null : JsonSerializer.Deserialize<EmployerDirectSnapshotEnvelope>(existing.CheckpointJson, JsonOptions);
            if (retained?.Kind != "employer-direct-v1" || retained.Fingerprint != fingerprint)
                throw new ConflictException("SnapshotId payload conflict.", "snapshot_payload_conflict");
            return JobSourceRunResponse.From(existing);
        }
        var now = clock.GetUtcNow();
        if (source.LastRunAtUtc is { } lastRun && lastRun.AddMinutes(source.ScanIntervalMinutes) > now.UtcDateTime)
            throw new ConflictException("Source scan interval has not elapsed.", "job_source_not_due");
        if (request.ObservedAtUtc > now.AddMinutes(5) || request.ObservedAtUtc < now.AddHours(-24))
            throw new BadRequestException("Snapshot observations are stale or in the future.");
        if (source.LastRunAtUtc is { } prior && request.ObservedAtUtc.UtcDateTime <= prior)
            throw new BadRequestException("Snapshot observation predates the previous source scan.");
        var payload = JsonSerializer.Serialize(new EmployerDirectSnapshotEnvelope("employer-direct-v1", fingerprint, request), JsonOptions);
        return JobSourceRunResponse.From(await runs.EnqueueSnapshotAsync(sourceId, snapshotId, requestedBy,
            now.UtcDateTime, payload, ct));
    }

    private static bool SafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.IsDefaultPort &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback && uri.Host.Contains('.');
}

// Input adapter only: all eligibility, identity, upsert, publication, events and
// receipts remain in the existing JobSourceRunner and durable JobSourceRun queue.
public sealed class EmployerDirectSnapshotProvider(IJobSourceRunStore? runs = null, IJobSourceRunCheckpoint? checkpoint = null,
    IEmployerDirectInventoryScanner? scanner = null) : ICompleteExternalJobProvider
{
    public AtsType AtsType => AtsType.Custom;
    public async Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        if (source.AtsType != AtsType.Custom)
            throw new InvalidOperationException("Employer acquisition requires a Custom source.");
        if (source.AtsIdentifier is EmployerDirectInventoryContract.Identifier or GenericEmployerContract.Identifier)
            return await (scanner ?? throw new InvalidOperationException("Inventory scanner is not registered.")).ScanAsync(source, cancellationToken);
        var runId = checkpoint?.RunId ?? throw new InvalidOperationException("Employer snapshot requires a durable source run.");
        var run = await (runs ?? throw new InvalidOperationException("Employer run store is not registered.")).GetAsync(source.Id, runId, cancellationToken) ?? throw new InvalidOperationException("Source run not found.");
        // The old UI alias also supports explicit snapshot submissions. Only runs
        // without a submitted payload may fall through to live acquisition.
        if (source.AtsIdentifier == GenericEmployerContract.LegacyIdentifier && string.IsNullOrEmpty(run.CheckpointJson))
            return await (scanner ?? throw new InvalidOperationException("Generic scanner is not registered.")).ScanAsync(source, cancellationToken);
        var envelope = run.CheckpointJson is null ? null : JsonSerializer.Deserialize<EmployerDirectSnapshotEnvelope>(run.CheckpointJson, EmployerDirectSnapshotService.JsonOptions);
        if (source.AtsIdentifier != "employer-direct" || envelope?.Kind != "employer-direct-v1" || envelope.Snapshot.CompanyId != source.CompanyId ||
            envelope.Snapshot.CareerPageUrl != source.CareerPageUrl || envelope.Snapshot.IsComplete)
            throw new InvalidOperationException("Unbound or unsupported employer snapshot.");
        return new(envelope.Snapshot.Jobs, 0, false) { ObservedAtUtc = envelope.Snapshot.ObservedAtUtc.UtcDateTime };
    }
    public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
        (await FetchSnapshotAsync(source, cancellationToken)).Jobs;
}
