using System.Text.Json;
using JobPortal.Application.Features.FresherAcquisition;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed partial class FresherAcquisitionStore : IDurableFresherImportProcessor
{
    private const int MaximumAttempts = 5;
    private static readonly JsonSerializerOptions StagingJson = new(JsonSerializerDefaults.Web);

    public Task<FresherImportStatus> StageAsync(FresherBulkImportRequest request, CancellationToken cancellationToken)
        => ExecuteImportAsync(async ct =>
        {
            var source = await GetEnabledSourceAsync(request.SourceType.Trim().ToLowerInvariant(), ct)
                ?? throw new InvalidOperationException("ACQUISITION_SOURCE_DISABLED");
            if (source.ProviderKey != "talentd" || !source.AutoPublishEnabled)
                throw new InvalidOperationException("ACQUISITION_AUTOPUBLISH_DISABLED");
            if (request.Jobs.Count is < 1 or > 20) throw new ArgumentException("BATCH_SIZE_EXCEEDED");
            var identities = request.Jobs.Select(x => FresherCandidateIdentity.Create(source.Id, x, true)).ToArray();
            if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Length)
                throw new ArgumentException("DUPLICATE_BATCH_IDENTITY");
            var fingerprint = FresherCandidateIdentity.PayloadFingerprint(request);
            var payload = JsonSerializer.Serialize(request, StagingJson);
            // Separate short acceptance lock: replay never waits for batch processing.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"acquisition-stage:" + source.Id + ":" + request.ImportId}, 0))", ct);
            var run = await db.AcquisitionImportRuns.SingleOrDefaultAsync(x =>
                x.AcquisitionSourceId == source.Id && x.BatchId == request.ImportId && !x.IsDeleted, ct);
            if (run is null)
            {
                run = new() { AcquisitionSourceId = source.Id, BatchId = request.ImportId,
                    RequestFingerprint = fingerprint, CorrelationId = Guid.NewGuid().ToString("N"),
                    StartedAtUtc = DateTime.UtcNow, ReceivedCount = request.Jobs.Count,
                    Status = "Queued", StagedRequestJson = payload, NextAttemptAtUtc = DateTime.UtcNow };
                db.AcquisitionImportRuns.Add(run);
            }
            else
            {
                // Identity-only historic hashes cannot establish exact content. Fail closed.
                if (run.RequestFingerprint != fingerprint || run.StagedRequestJson is not null && run.StagedRequestJson != payload)
                    throw new ImportIdPayloadConflictException();
                if (run.StagedRequestJson is null)
                {
                    var receipts = await db.AcquisitionImportItems.AsNoTracking()
                        .Where(x => x.AcquisitionImportRunId == run.Id && !x.IsDeleted).ToArrayAsync(ct);
                    if (run.ReceivedCount != request.Jobs.Count || receipts.Any(x =>
                        x.ItemIndex < 0 || x.ItemIndex >= identities.Length || identities[x.ItemIndex] != x.CandidateIdentityKey))
                        throw new ImportIdPayloadConflictException();
                    run.StagedRequestJson = payload;
                    // Existing completed/failed states remain terminal. Hydrate only missing input.
                    if (run.Status is not ("Completed" or "Failed")) run.NextAttemptAtUtc = DateTime.UtcNow;
                }
            }
            await db.SaveChangesAsync(ct);
            await ReconcileRunCountersAsync(run.Id, ct);
            await transaction.CommitAsync(ct);
            return (await GetImportStatusAsync(source.ProviderKey, request.ImportId, ct))!;
        }, cancellationToken);

    public Task<bool> ProcessNextAsync(IFresherBulkImportService importer, CancellationToken cancellationToken)
        => ExecuteImportAsync(async ct =>
        {
            var now = DateTime.UtcNow;
            var candidates = await db.AcquisitionImportRuns.AsNoTracking()
                .Where(x => !x.IsDeleted && x.StagedRequestJson != null && x.Status != "Completed" && x.Status != "Failed" &&
                    (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
                .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id).Take(20).ToArrayAsync(ct);
            foreach (var candidate in candidates)
            {
                IAsyncDisposable ownership;
                try { ownership = await AcquireRunLockAsync(candidate.AcquisitionSourceId, candidate.BatchId, ct); }
                catch (ImportAlreadyRunningException) { continue; }
                await using (ownership)
                {
                    // Recheck after lock. A different process may have completed the candidate.
                    db.ChangeTracker.Clear();
                    var run = await db.AcquisitionImportRuns.SingleAsync(x => x.Id == candidate.Id, ct);
                    if (run.Status is "Completed" or "Failed" || run.NextAttemptAtUtc > DateTime.UtcNow) continue;
                    var request = JsonSerializer.Deserialize<FresherBulkImportRequest>(run.StagedRequestJson!, StagingJson)!;
                    run.Status = "Running";
                    run.ProcessingAttempts++;
                    // Backoff is committed before execution, including process-crash recovery.
                    run.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * (1 << Math.Min(run.ProcessingAttempts, 6))) + Random.Shared.Next(5));
                    await db.SaveChangesAsync(ct);
                    string? error = null;
                    var permanent = false;
                    try { await importer.ProcessStagedAsync(request, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        error = exception.GetType().Name;
                        permanent = exception is ArgumentException or JsonException or ImportIdPayloadConflictException;
                    }
                    db.ChangeTracker.Clear();
                    run = await db.AcquisitionImportRuns.SingleAsync(x => x.Id == candidate.Id, ct);
                    if (run.Status is not ("Completed" or "Failed"))
                    {
                        run.Status = permanent || run.ProcessingAttempts >= MaximumAttempts ? "Failed" : "Queued";
                        run.FailureSummary = error is null ? "INCOMPLETE_ITEMS_OR_PUBLICATION" : "PROCESSING_ERROR:" + error;
                        if (run.Status == "Failed") run.CompletedAtUtc = DateTime.UtcNow;
                    }
                    else if (run.Status == "Completed") { run.NextAttemptAtUtc = null; run.FailureSummary = null; }
                    await db.SaveChangesAsync(ct);
                    return true;
                }
            }
            return false;
        }, cancellationToken);

    private async Task ReconcileRunCountersAsync(Guid runId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"fresher-import-state:" + runId.ToString("N")}, 0))",
            cancellationToken);
        var items = await db.AcquisitionImportItems.AsNoTracking()
            .Where(x => x.AcquisitionImportRunId == runId && !x.IsDeleted)
            .Select(x => new { x.Outcome, x.PublicationStatus })
            .ToArrayAsync(cancellationToken);
        var run = await db.AcquisitionImportRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        run.EligibleCount = items.Count(x => x.Outcome is "Created" or "Updated" or "Existing" or "Duplicate");
        run.RejectedCount = items.Count(x => x.Outcome.StartsWith("Rejected", StringComparison.Ordinal));
        run.CreatedCount = items.Count(x => x.Outcome == "Created");
        run.UpdatedCount = items.Count(x => x.Outcome == "Updated");
        run.ExistingCount = items.Count(x => x.Outcome is "Existing" or "Duplicate");
        run.PublishedCount = items.Count(x => x.PublicationStatus == "Published");
        run.FailedCount = items.Count(x => x.Outcome == "Failed");
        if (run.StagedRequestJson is null) run.ReceivedCount = items.Length;
        await db.SaveChangesAsync(cancellationToken);
    }
}
