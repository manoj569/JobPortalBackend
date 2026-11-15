using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorPhase4Store(JobPortalDbContext context) : IAcceleratorPhase4Store
{
    public Task<AcceleratorProjectDefinition?> DefinitionByStableKeyAsync(string stableKey, CancellationToken ct) =>
        context.AcceleratorProjectDefinitions.SingleOrDefaultAsync(x => x.StableKey == stableKey && !x.IsDeleted, ct);
    public Task<AcceleratorProjectDefinition?> DefinitionAsync(Guid id, CancellationToken ct) =>
        context.AcceleratorProjectDefinitions.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorCompetency?> CompetencyAsync(Guid id, CancellationToken ct) =>
        context.AcceleratorCompetencies.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorProjectVersion?> VersionByNumberAsync(Guid definitionId, int version, CancellationToken ct) =>
        context.AcceleratorProjectVersions.SingleOrDefaultAsync(x => x.DefinitionId == definitionId && x.Version == version && !x.IsDeleted, ct);
    public Task<bool> VersionUsedAsync(Guid versionId, CancellationToken ct) =>
        context.AcceleratorCandidateProjects.AnyAsync(x => x.ProjectVersionId == versionId, ct);
    public async Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleProjectDispatchesAsync(DateTime now, int take, CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.AsNoTracking().Where(x => x.Operation == "ProjectEvaluation" &&
            (x.Status == AcceleratorAiDispatchStatus.Uncertain || x.Status == AcceleratorAiDispatchStatus.Succeeded ||
                (x.Status == AcceleratorAiDispatchStatus.Reserved || x.Status == AcceleratorAiDispatchStatus.Dispatched) &&
                x.CreatedAtUtc <= now.AddMinutes(-2)) &&
            !x.RequiresManualReview && x.NextReconciliationAtUtc <= now &&
            (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now) && !x.IsDeleted)
            .OrderBy(x => x.NextReconciliationAtUtc).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
    public async Task<AcceleratorAiDispatchRecord?> ClaimProjectDispatchAsync(Guid dispatchId, Guid claimId, DateTime now,
        DateTime expiresAt, CancellationToken ct)
    {
        var claimed = await context.AcceleratorAiDispatchRecords.Where(x => x.Id == dispatchId &&
            x.Operation == "ProjectEvaluation" &&
            (x.Status == AcceleratorAiDispatchStatus.Uncertain || x.Status == AcceleratorAiDispatchStatus.Succeeded ||
                (x.Status == AcceleratorAiDispatchStatus.Reserved || x.Status == AcceleratorAiDispatchStatus.Dispatched) &&
                x.CreatedAtUtc <= now.AddMinutes(-2)) &&
            !x.RequiresManualReview && !x.IsDeleted &&
            (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReconciliationClaimId, claimId)
                .SetProperty(x => x.ReconciliationClaimExpiresAtUtc, expiresAt)
                .SetProperty(x => x.LastReconciliationAtUtc, now)
                .SetProperty(x => x.ReconciliationAttemptCount, x => x.ReconciliationAttemptCount + 1)
                .SetProperty(x => x.Revision, Guid.NewGuid()), ct);
        return claimed == 0 ? null : await context.AcceleratorAiDispatchRecords.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dispatchId && x.ReconciliationClaimId == claimId, ct);
    }
    public async Task<bool> UpdateClaimedProjectDispatchAsync(AcceleratorAiDispatchRecord record, Guid claimId, CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.Where(x => x.Id == record.Id && x.ReconciliationClaimId == claimId &&
            x.ReconciliationClaimExpiresAtUtc > DateTime.UtcNow && x.Operation == "ProjectEvaluation")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, record.Status)
                .SetProperty(x => x.ResultJson, record.ResultJson)
                .SetProperty(x => x.ProviderRequestId, record.ProviderRequestId)
                .SetProperty(x => x.InputTokens, record.InputTokens)
                .SetProperty(x => x.OutputTokens, record.OutputTokens)
                .SetProperty(x => x.ActualCostInr, record.ActualCostInr)
                .SetProperty(x => x.NextReconciliationAtUtc, record.NextReconciliationAtUtc)
                .SetProperty(x => x.LastReconciliationError, record.LastReconciliationError)
                .SetProperty(x => x.RequiresManualReview, record.RequiresManualReview)
                .SetProperty(x => x.ManualReviewReason, record.ManualReviewReason)
                .SetProperty(x => x.FinishedAtUtc, record.FinishedAtUtc)
                .SetProperty(x => x.ReconciliationClaimId, (Guid?)null)
                .SetProperty(x => x.ReconciliationClaimExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.Revision, Guid.NewGuid()), ct) == 1;
    public async Task<IReadOnlyList<AcceleratorAiDispatchRecord>> UnresolvedProjectDispatchesAsync(int skip, int take, CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.AsNoTracking().Where(x => x.Operation == "ProjectEvaluation" &&
            x.Status != AcceleratorAiDispatchStatus.Reconciled && x.Status != AcceleratorAiDispatchStatus.Failed && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorProjectVersion>> ActiveVersionsAsync(Guid roleId, int take, CancellationToken ct) =>
        await context.AcceleratorProjectVersions.Include(x => x.Definition).Include(x => x.Competencies)
            .Where(x => x.TargetRoleId == roleId && x.CatalogStatus == AcceleratorProjectCatalogStatus.Published && x.IsActive && x.Definition.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Difficulty).ThenBy(x => x.Definition.StableKey).ThenByDescending(x => x.Version).Take(take).ToListAsync(ct);

    public Task<AcceleratorProjectVersion?> VersionAsync(Guid id, CancellationToken ct) =>
        context.AcceleratorProjectVersions.Include(x => x.Definition).Include(x => x.Competencies)
            .Include(x => x.Milestones).ThenInclude(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public Task<AcceleratorCandidateProject?> CandidateProjectAsync(Guid userId, Guid id, CancellationToken ct) =>
        context.AcceleratorCandidateProjects.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorCandidateProject?> CandidateProjectByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) =>
        context.AcceleratorCandidateProjects.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorCandidateProject>> CandidateProjectsAsync(Guid userId, int skip, int take, CancellationToken ct) =>
        await context.AcceleratorCandidateProjects.Where(x => x.UserId == userId && !x.IsDeleted).OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
    public Task<AcceleratorProjectAssignment?> AssignmentAsync(Guid projectVersionId, Guid assignmentId, CancellationToken ct) =>
        context.AcceleratorProjectAssignments.Include(x => x.Milestone).SingleOrDefaultAsync(x => x.Id == assignmentId && x.Milestone.ProjectVersionId == projectVersionId && !x.IsDeleted, ct);
    public Task<AcceleratorProjectSubmission?> SubmissionByKeyAsync(Guid userId, Guid projectId, Guid requestKey, CancellationToken ct) =>
        context.AcceleratorProjectSubmissions.Join(context.AcceleratorCandidateProjects, x => x.CandidateProjectId, p => p.Id, (x, p) => new { x, p })
            .Where(row => row.p.UserId == userId && row.p.Id == projectId && row.x.RequestKey == requestKey && !row.x.IsDeleted).Select(row => row.x).SingleOrDefaultAsync(ct);
    public Task<AcceleratorProjectSubmission?> SubmissionAsync(Guid userId, Guid projectId, Guid submissionId, CancellationToken ct) =>
        context.AcceleratorProjectSubmissions.Join(context.AcceleratorCandidateProjects, x => x.CandidateProjectId, p => p.Id, (x, p) => new { x, p })
            .Where(row => row.p.UserId == userId && row.p.Id == projectId && row.x.Id == submissionId && !row.x.IsDeleted).Select(row => row.x).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<AcceleratorProjectSubmission>> SubmissionsAsync(Guid userId, Guid projectId, Guid assignmentId, CancellationToken ct) =>
        await context.AcceleratorProjectSubmissions.Join(context.AcceleratorCandidateProjects, x => x.CandidateProjectId, p => p.Id, (x, p) => new { x, p })
            .Where(row => row.p.UserId == userId && row.p.Id == projectId && row.x.AssignmentId == assignmentId && !row.x.IsDeleted)
            .OrderBy(row => row.x.AttemptNumber).Select(row => row.x).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorProjectSubmission>> ProjectSubmissionsAsync(Guid userId, Guid projectId, int skip, int take, CancellationToken ct) =>
        await context.AcceleratorProjectSubmissions.Join(context.AcceleratorCandidateProjects, x => x.CandidateProjectId, p => p.Id, (x, p) => new { x, p })
            .Where(row => row.p.UserId == userId && row.p.Id == projectId && !row.x.IsDeleted)
            .OrderByDescending(row => row.x.SubmittedAtUtc).ThenBy(row => row.x.Id)
            .Skip(skip).Take(take).Select(row => row.x).ToListAsync(ct);
    public Task<AcceleratorProjectEvaluation?> EvaluationAsync(Guid userId, Guid submissionId, CancellationToken ct) =>
        context.AcceleratorProjectEvaluations.Join(context.AcceleratorProjectSubmissions, e => e.SubmissionId, s => s.Id, (e, s) => new { e, s })
            .Join(context.AcceleratorCandidateProjects, row => row.s.CandidateProjectId, p => p.Id, (row, p) => new { row.e, p })
            .Where(row => row.p.UserId == userId && row.e.SubmissionId == submissionId && !row.e.IsDeleted).Select(row => row.e).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<AcceleratorEvaluatedAssignment>> EvaluatedAssignmentsAsync(Guid userId, Guid projectId, CancellationToken ct) =>
        await context.AcceleratorProjectEvaluations.Join(context.AcceleratorProjectSubmissions, e => e.SubmissionId, s => s.Id, (e, s) => new { e, s })
            .Join(context.AcceleratorCandidateProjects, row => row.s.CandidateProjectId, p => p.Id, (row, p) => new { row.e, row.s, p })
            .Where(row => row.p.UserId == userId && row.p.Id == projectId && !row.e.IsDeleted && !row.s.IsDeleted)
            .Select(row => new AcceleratorEvaluatedAssignment(row.s.AssignmentId, row.s.AttemptNumber, row.e.Score)).ToListAsync(ct);
    public Task<AcceleratorProjectRubric?> ActiveRubricAsync(Guid projectVersionId, CancellationToken ct) =>
        context.AcceleratorProjectRubrics.Include(x => x.Criteria).Where(x => x.ProjectVersionId == projectVersionId && x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    public void Add(object entity) => context.Add(entity);
    public void Remove(object entity) => context.Remove(entity);
}
