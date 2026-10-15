using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class FresherAcquisitionStore(JobPortalDbContext db) : IFresherAcquisitionStore
{
    public Task<AcquisitionSource?> GetEnabledSourceAsync(string providerKey, CancellationToken cancellationToken) =>
        db.AcquisitionSources.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderKey == providerKey && x.IsEnabled && !x.IsDeleted, cancellationToken);

    public Task<AcquisitionSource?> GetSourceByIdAsync(Guid sourceId, CancellationToken cancellationToken) =>
        db.AcquisitionSources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId && !x.IsDeleted, cancellationToken);

    public async Task<(AcquisitionImportRun Run, bool IsReplay)> StartRunAsync(Guid sourceId, string batchId,
        string correlationId, int received, CancellationToken cancellationToken)
    {
        var existing = await db.AcquisitionImportRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AcquisitionSourceId == sourceId && x.BatchId == batchId && !x.IsDeleted, cancellationToken);
        if (existing is not null) return (existing, true);
        var run = new AcquisitionImportRun { AcquisitionSourceId = sourceId, BatchId = batchId,
            CorrelationId = correlationId, StartedAtUtc = DateTime.UtcNow, ReceivedCount = received };
        db.AcquisitionImportRuns.Add(run);
        try { await db.SaveChangesAsync(cancellationToken); return (run, false); }
        catch (DbUpdateException)
        {
            db.Entry(run).State = EntityState.Detached;
            existing = await db.AcquisitionImportRuns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.AcquisitionSourceId == sourceId && x.BatchId == batchId && !x.IsDeleted, cancellationToken);
            if (existing is null) throw;
            return (existing, true);
        }
    }

    public async Task SaveReceiptsAsync(Guid runId, IReadOnlyCollection<FresherReceiptInput> receipts,
        CancellationToken cancellationToken)
    {
        if (receipts.Count == 0) return;
        var sourceId = await db.AcquisitionImportRuns.Where(x => x.Id == runId)
            .Select(x => x.AcquisitionSourceId).SingleAsync(cancellationToken);
        foreach (var input in receipts)
        {
            var candidate = input.Candidate;
            db.AcquisitionImportItems.Add(new AcquisitionImportItem
            {
                AcquisitionImportRunId = runId,
                AcquisitionSourceId = sourceId, ItemIndex = input.Index,
                IdentityKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.IdentityKey + ":" + input.Index))).ToLowerInvariant(), ExternalId = candidate.ExternalJobId,
                DiscoveredUrl = SafeUrl(candidate.DiscoveredUrl), CanonicalUrl = SafeUrl(candidate.CanonicalJobUrl),
                ApplyUrl = SafeUrl(candidate.ApplyUrl), CompanyNameNormalized = Normalize(candidate.CompanyName),
                TitleNormalized = Normalize(candidate.Title), LocationNormalized = Normalize(candidate.LocationText ?? candidate.City ?? candidate.Country),
                EligibilityDecision = input.EligibilityDecision, ReasonCode = input.Outcome.ReasonCode,
                Outcome = input.Outcome.Outcome, JobId = input.Outcome.JobId,
                FirstSeenAtUtc = input.SeenAtUtc, LastSeenAtUtc = input.SeenAtUtc,
                LastVerifiedAtUtc = candidate.SourceLastSeenAt?.ToUniversalTime(), SourceExpiresAtUtc = candidate.ExpiresAt?.ToUniversalTime()
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(Guid runId, FresherBulkImportResponse response, CancellationToken cancellationToken)
    {
        var run = await db.AcquisitionImportRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        run.Status = "Completed"; run.CompletedAtUtc = DateTime.UtcNow;
        run.EligibleCount = response.Accepted; run.RejectedCount = response.RejectedGeography + response.RejectedExperience + response.RejectedInvalidData + response.InvalidUrl;
        run.RejectedGeographyCount = response.RejectedGeography; run.RejectedExperienceCount = response.RejectedExperience;
        run.RejectedInvalidDataCount = response.RejectedInvalidData; run.InvalidUrlCount = response.InvalidUrl;
        run.CreatedCount = response.Created; run.UpdatedCount = response.Updated; run.ExistingCount = response.Existing;
        run.PublishedCount = response.Published; run.FailedCount = response.Failed;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FresherBulkImportResponse?> GetReplayResponseAsync(Guid runId, string batchId, CancellationToken cancellationToken)
    {
        var run = await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == runId, cancellationToken);
        if (run.Status != "Completed") return null;
        var items = await db.AcquisitionImportItems.AsNoTracking().Where(x => x.AcquisitionImportRunId == runId && !x.IsDeleted)
            .OrderBy(x => x.ItemIndex).Select(x => new FresherImportItemOutcome(x.ItemIndex, x.Outcome, x.ReasonCode, x.JobId)).ToArrayAsync(cancellationToken);
        return new(batchId, run.ReceivedCount, run.EligibleCount, run.RejectedGeographyCount, run.RejectedExperienceCount,
            run.RejectedInvalidDataCount, run.InvalidUrlCount, run.CreatedCount, run.UpdatedCount,
            run.ExistingCount, run.FailedCount, items) { RunId = run.Id, Published = run.PublishedCount };
    }

    private static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().ToUpperInvariant();
    private static string? SafeUrl(string? value) => FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(value) ? value!.Trim() : null;
}
