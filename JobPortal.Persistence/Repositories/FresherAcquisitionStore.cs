using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Data.Common;

namespace JobPortal.Persistence.Repositories;

public sealed partial class FresherAcquisitionStore(JobPortalDbContext db) : IFresherAcquisitionStore
{
    // Durable chunk receipts are the recovery boundary. Run once inside EF's
    // execution-strategy scope so Npgsql permits the explicit chunk transactions;
    // retries remain disabled because the import also has in-memory and publication
    // side effects. Recovery happens through a fresh request with the same ID.
    public Task<T> ExecuteImportAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        new DurableImportExecutionStrategy(db).ExecuteAsync(operation, cancellationToken);

    private sealed class DurableImportExecutionStrategy(DbContext context) : ExecutionStrategy(context, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    public async Task<IAsyncDisposable> AcquireRunLockAsync(Guid sourceId, string importId, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var key = $"fresher-import:{sourceId:N}:{importId}";
        bool acquired;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@lock_key, 0))";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@lock_key";
            parameter.Value = key; command.Parameters.Add(parameter);
            acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        catch { await db.Database.CloseConnectionAsync(); throw; }
        if (!acquired)
        {
            await db.Database.CloseConnectionAsync();
            throw new ImportAlreadyRunningException();
        }
        return new AdvisoryRunLock(db, connection, key);
    }

    private sealed class AdvisoryRunLock(JobPortalDbContext db, DbConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@lock_key, 0))";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@lock_key"; parameter.Value = key; command.Parameters.Add(parameter);
            try { await command.ExecuteScalarAsync(); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    public async Task<FresherImportStatus?> GetImportStatusAsync(string providerKey, string importId, CancellationToken cancellationToken)
    {
        var run = await db.AcquisitionImportRuns.AsNoTracking().SingleOrDefaultAsync(x =>
            x.AcquisitionSource.ProviderKey == providerKey && x.BatchId == importId && !x.IsDeleted, cancellationToken);
        if (run is null) return null;
        var receipt = run.Status == "Completed" ? await GetReplayResponseAsync(run.Id, importId, cancellationToken) : null;
        return new(run.BatchId, run.BatchId, run.Id,
            run.Status == "Completed" ? "COMPLETED" : run.Status == "Failed" ? "FAILED" : "IN_PROGRESS",
            run.ReceivedCount, run.EligibleCount, run.RejectedCount, run.CreatedCount, run.UpdatedCount,
            run.ExistingCount, run.PublishedCount, run.FailedCount, run.FailureSummary, receipt);
    }

    public Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken) =>
        db.AcquisitionSources.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderKey == providerKey && x.IsEnabled && !x.IsDeleted, cancellationToken);

    public Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken) =>
        db.AcquisitionSources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId && !x.IsDeleted, cancellationToken);

    public async Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId,
        string correlationId, string requestFingerprint, int received, CancellationToken cancellationToken)
        => await StartRunCoreAsync(sourceId, batchId, correlationId, requestFingerprint, null, received, cancellationToken);

    public Task<(AcquisitionImportRun Run, bool IsReplay)> StartPayloadRunAsync(Guid sourceId, string batchId,
        string correlationId, string payloadFingerprint, string legacyFingerprint, int received, CancellationToken cancellationToken) =>
        StartRunCoreAsync(sourceId, batchId, correlationId, payloadFingerprint, legacyFingerprint, received, cancellationToken);

    private async Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunCoreAsync(Guid sourceId, string batchId,
        string correlationId, string requestFingerprint, string? legacyFingerprint, int received, CancellationToken cancellationToken)
    {
        var existing = await db.AcquisitionImportRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AcquisitionSourceId == sourceId && x.BatchId == batchId && !x.IsDeleted, cancellationToken);
        if (existing is not null)
        {
            if (!MatchesFingerprint(existing.RequestFingerprint, requestFingerprint, legacyFingerprint))
                throw new ImportIdPayloadConflictException();
            return (existing, true);
        }
        var run = new AcquisitionImportRun { AcquisitionSourceId = sourceId, BatchId = batchId,
            RequestFingerprint = requestFingerprint, CorrelationId = correlationId, StartedAtUtc = DateTime.UtcNow, ReceivedCount = received };
        db.AcquisitionImportRuns.Add(run);
        try { await db.SaveChangesAsync(cancellationToken); return (run, false); }
        catch (DbUpdateException exception) when (IsRunUniqueViolation(exception))
        {
            db.Entry(run).State = EntityState.Detached;
            existing = await db.AcquisitionImportRuns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.AcquisitionSourceId == sourceId && x.BatchId == batchId && !x.IsDeleted, cancellationToken);
            if (existing is null) throw;
            if (!MatchesFingerprint(existing.RequestFingerprint, requestFingerprint, legacyFingerprint))
                throw new ImportIdPayloadConflictException();
            return (existing, true);
        }
    }

    // Existing runs only retained identity-set hashes. Preserve their recovery path;
    // full-content conflict detection is available for runs created by this version.
    private static bool MatchesFingerprint(string existing, string payload, string? legacy) =>
        string.Equals(existing, payload, StringComparison.Ordinal) ||
        (legacy is not null && string.Equals(existing, legacy, StringComparison.Ordinal));

    private static bool IsRunUniqueViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
            if (current is PostgresException pg)
                return pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                    pg.ConstraintName == "IX_AcquisitionImportRuns_AcquisitionSourceId_BatchId";
        return false;
    }

    public async Task<IReadOnlyDictionary<string, FresherImportReceiptSnapshot>> GetReceiptsAsync(Guid runId, CancellationToken cancellationToken)
    {
        return await db.AcquisitionImportItems.AsNoTracking().Where(x => x.AcquisitionImportRunId == runId && !x.IsDeleted)
            .ToDictionaryAsync(x => x.CandidateIdentityKey, x => new FresherImportReceiptSnapshot(
                new FresherImportItemOutcome(x.ItemIndex, x.Outcome, x.ReasonCode, x.JobId), x.PublicationStatus, x.PublicationError), cancellationToken);
    }

    public async Task<IFresherAcquisitionChunk> BeginChunkAsync(IReadOnlyCollection<string> externalIdentityLocks, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var identity in externalIdentityLocks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({identity}, 0))", cancellationToken);
            return new FresherAcquisitionChunk(db, transaction);
        }
        catch { await transaction.RollbackAsync(cancellationToken); await transaction.DisposeAsync(); throw; }
    }

    private sealed class FresherAcquisitionChunk(JobPortalDbContext db, IDbContextTransaction transaction) : IFresherAcquisitionChunk
    {
        private bool _completed;

        public async Task CommitAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts, CancellationToken cancellationToken)
        {
            var sourceId = await db.AcquisitionImportRuns.Where(x => x.Id == runId).Select(x => x.AcquisitionSourceId).SingleAsync(cancellationToken);
            var keys = receipts.Select(x => x.IdentityKey).Distinct().ToArray();
            var existing = await db.AcquisitionImportItems.Where(x => x.AcquisitionImportRunId == runId && keys.Contains(x.CandidateIdentityKey) && !x.IsDeleted)
                .Select(x => x.CandidateIdentityKey).ToArrayAsync(cancellationToken);
            var existingSet = existing.ToHashSet(StringComparer.Ordinal);
            foreach (var input in receipts.Where(x => !existingSet.Contains(x.IdentityKey)).GroupBy(x => x.IdentityKey).Select(x => x.First()))
            {
                var candidate = input.Candidate;
                db.AcquisitionImportItems.Add(new AcquisitionImportItem
                {
                    AcquisitionImportRunId = runId, AcquisitionSourceId = sourceId, ItemIndex = input.Index,
                    CandidateIdentityKey = input.IdentityKey, ExternalId = candidate?.ExternalJobId?.Trim() is { Length: <= 255 } externalId ? externalId : null,
                    DiscoveredUrl = SafeUrl(candidate?.DiscoveredUrl), CanonicalUrl = SafeUrl(candidate?.CanonicalJobUrl),
                    ApplyUrl = SafeUrl(candidate?.ApplyUrl), CompanyNameNormalized = Normalize(candidate?.CompanyName, 200),
                    TitleNormalized = Normalize(candidate?.Title, 250), LocationNormalized = Normalize(candidate?.LocationText ?? candidate?.City ?? candidate?.Country, 250),
                    EligibilityDecision = input.EligibilityDecision, ReasonCode = input.Outcome.ReasonCode,
                    Outcome = input.Outcome.Outcome, JobId = input.Outcome.JobId,
                    PublicationStatus = input.PublicationRequested ? "Pending" : "NotRequested",
                    FirstSeenAtUtc = input.SeenAtUtc, LastSeenAtUtc = input.SeenAtUtc,
                    LastVerifiedAtUtc = candidate?.SourceLastSeenAt?.ToUniversalTime(), SourceExpiresAtUtc = candidate?.ExpiresAt?.ToUniversalTime()
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed) await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            if (!_completed) db.ChangeTracker.Clear();
        }
    }

    public async Task CompleteRunAsync(Guid runId, FresherBulkImportResponse response, CancellationToken cancellationToken)
    {
        var run = await db.AcquisitionImportRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        var receipts = await db.AcquisitionImportItems.AsNoTracking().Where(x => x.AcquisitionImportRunId == runId && !x.IsDeleted).ToArrayAsync(cancellationToken);
        run.Status = receipts.Any(x => x.PublicationStatus is "Pending" or "Failed") ? "AwaitingPublication" : "Completed";
        run.CompletedAtUtc = run.Status == "Completed" ? DateTime.UtcNow : null;
        run.ReceivedCount = receipts.Length;
        run.EligibleCount = receipts.Count(x => x.Outcome is "Created" or "Updated" or "Existing" or "Duplicate");
        run.RejectedGeographyCount = receipts.Count(x => x.Outcome == "RejectedGeography");
        run.RejectedExperienceCount = receipts.Count(x => x.Outcome == "RejectedExperience");
        run.RejectedInvalidDataCount = receipts.Count(x => x.Outcome == "RejectedInvalidData");
        run.InvalidUrlCount = receipts.Count(x => x.Outcome == "RejectedInvalidUrl");
        run.RejectedCount = run.RejectedGeographyCount + run.RejectedExperienceCount + run.RejectedInvalidDataCount + run.InvalidUrlCount;
        run.CreatedCount = receipts.Count(x => x.Outcome == "Created");
        run.UpdatedCount = receipts.Count(x => x.Outcome == "Updated");
        run.ExistingCount = receipts.Count(x => x.Outcome is "Existing" or "Duplicate");
        run.PublishedCount = receipts.Count(x => x.PublicationStatus == "Published");
        run.FailedCount = receipts.Count(x => x.Outcome == "Failed");
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId, CancellationToken cancellationToken)
    {
        var run = await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == runId, cancellationToken);
        var items = await db.AcquisitionImportItems.AsNoTracking().Where(x => x.AcquisitionImportRunId == runId && !x.IsDeleted)
            .OrderBy(x => x.ItemIndex).Select(x => new FresherImportItemOutcome(x.ItemIndex, x.Outcome, x.ReasonCode, x.JobId)).ToArrayAsync(cancellationToken);
        return new(batchId, run.ReceivedCount, run.EligibleCount, run.RejectedGeographyCount, run.RejectedExperienceCount,
            run.RejectedInvalidDataCount, run.InvalidUrlCount, run.CreatedCount, run.UpdatedCount,
            run.ExistingCount, run.FailedCount, items) { RunId = run.Id, Published = run.PublishedCount };
    }

    public async Task UpdatePublicationAsync(Guid runId, string candidateIdentityKey, string status, string? error, CancellationToken cancellationToken)
    {
        var receipt = await db.AcquisitionImportItems.SingleOrDefaultAsync(x => x.AcquisitionImportRunId == runId &&
            x.CandidateIdentityKey == candidateIdentityKey && !x.IsDeleted, cancellationToken);
        if (receipt is null) return;
        receipt.PublicationStatus = status;
        receipt.PublicationError = error is null ? null : error[..Math.Min(error.Length, 120)];
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string? value, int maximum)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().ToUpperInvariant();
        return normalized[..Math.Min(normalized.Length, maximum)];
    }
    private static string? SafeUrl(string? value) => FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(value) ? value!.Trim() : null;
}
