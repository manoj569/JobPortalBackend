using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Abstractions.Auditing;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed record UnresolvedOutreachView(Guid Id, Guid CandidateId,
    TalentOutreachStatus Status, DateTime? DispatchStartedAtUtc, Guid? UsageReservationId);

public sealed partial class AcceleratorTalentService
{
    public async Task<int> AdminReconcileOutreachAsync(Guid actor, string reason,
        CancellationToken ct)
    {
        await Administrator(actor, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BadRequestException("A reconciliation reason is required.");
        var changed = await ReconcileOutreachAsync(25, ct);
        await Audit(actor, AuditAction.Update, "TalentOutreachReconciliation",
            actor, "Administrator", ct);
        return changed;
    }

    public async Task<IReadOnlyList<UnresolvedOutreachView>> UnresolvedAsync(Guid actor,
        int page, int pageSize, CancellationToken ct)
    {
        await Administrator(actor, ct); CheckPage(page, pageSize);
        return await db.TalentOutreachDrafts.AsNoTracking().Where(x => !x.IsDeleted &&
            (x.Status == TalentOutreachStatus.Pending ||
             x.Status == TalentOutreachStatus.Dispatching ||
             x.Status == TalentOutreachStatus.Unknown ||
             x.Status == TalentOutreachStatus.Failed))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new UnresolvedOutreachView(x.Id, x.CandidateId, x.Status,
                x.DispatchStartedAtUtc, x.UsageReservationId)).ToArrayAsync(ct);
    }

    public async Task<int> ReconcileOutreachAsync(int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var cutoff = Now.AddMinutes(-Math.Clamp(Outreach.DispatchRecoveryMinutes, 1, 60));
        var rows = await db.TalentOutreachDrafts.AsNoTracking().Where(x => !x.IsDeleted &&
            x.UsageReservationId != null &&
            db.Set<AcceleratorUsageReservation>().Any(r => r.Id == x.UsageReservationId &&
                (r.State == AcceleratorUsageState.Dispatched ||
                 r.State == AcceleratorUsageState.Uncertain ||
                 r.State == AcceleratorUsageState.Reserved &&
                    (x.Status == TalentOutreachStatus.Pending ||
                     x.Status == TalentOutreachStatus.Dispatching))) &&
            (x.Status == TalentOutreachStatus.Sent ||
             x.Status == TalentOutreachStatus.Failed && x.FailureEvidenceReference != null ||
             x.Status == TalentOutreachStatus.Pending && x.CreatedAtUtc < cutoff ||
             x.Status == TalentOutreachStatus.Dispatching && x.DispatchStartedAtUtc < cutoff))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(batchSize).ToArrayAsync(ct);
        var changed = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var reservation = await db.Set<AcceleratorUsageReservation>().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == row.UsageReservationId &&
                    x.UserId == row.CandidateId && x.Feature == AcceleratorFeature.EmployerOutreach, ct);
            if (reservation is null) continue;
            if (row.Status == TalentOutreachStatus.Sent)
            {
                if (reservation.State is AcceleratorUsageState.Dispatched or AcceleratorUsageState.Uncertain)
                {
                    await quota.CompleteAsync(row.CandidateId, reservation.Id, 0, 0, 0, ct);
                    await AuditRecovery(AuditAction.Confirm,
                        "TalentOutreachQuotaRecovered", row.Id, ct);
                    changed++;
                }
                continue;
            }
            if (row.Status == TalentOutreachStatus.Failed)
            {
                if (reservation.State is AcceleratorUsageState.Dispatched or AcceleratorUsageState.Uncertain &&
                    !string.IsNullOrWhiteSpace(row.FailureEvidenceReference))
                {
                    await quota.ResolveVerifiedNonChargeableAsync(row.CandidateId, reservation.Id,
                        row.SendIdempotencyKey, row.FailureEvidenceReference, ct);
                    await AuditRecovery(AuditAction.Confirm,
                        "TalentOutreachQuotaReleased", row.Id, ct);
                    changed++;
                }
                continue;
            }
            // A stale intent is never automatically sent. The ledger state tells us
            // whether dispatch could have been attempted; unknown remains unresolved.
            if (reservation.State == AcceleratorUsageState.Reserved)
            {
                await quota.ReleaseUnusedAsync(row.CandidateId, reservation.Id, ct);
                if (await RecoverStatusAsync(row, TalentOutreachStatus.Failed, ct)) changed++;
            }
            else if (reservation.State is AcceleratorUsageState.Dispatched or AcceleratorUsageState.Uncertain)
            {
                await quota.MarkOutreachUncertainAsync(row.CandidateId, reservation.Id, ct);
                if (await RecoverStatusAsync(row, TalentOutreachStatus.Unknown, ct)) changed++;
            }
        }
        return changed;
    }

    private async Task<bool> RecoverStatusAsync(TalentOutreachDraft original,
        TalentOutreachStatus target, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var row = await db.TalentOutreachDrafts.SingleAsync(x => x.Id == original.Id, ct);
            if (row.Status != original.Status) return false;
            row.Status = target;
            await db.SaveChangesAsync(ct);
        }
        else if (await db.TalentOutreachDrafts.Where(x => x.Id == original.Id &&
            x.Status == original.Status && x.UsageReservationId == original.UsageReservationId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, target), ct) != 1)
            return false;
        await AuditRecovery(AuditAction.Update, "TalentOutreachRecovery", original.Id, ct);
        return true;
    }

    private Task AuditRecovery(AuditAction action, string type, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new AuditEvent(action, type, id.ToString(),
            Actor: new AuditActor(null, "System")), ct);
}
