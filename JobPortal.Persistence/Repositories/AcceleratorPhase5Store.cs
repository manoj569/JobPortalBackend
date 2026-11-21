using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorPhase5Store(JobPortalDbContext context) : IAcceleratorPhase5Store, IInterviewDispatchStore
{
    public Task<AcceleratorInterviewDefinition?> DefinitionAsync(Guid id, CancellationToken ct) =>
        context.AcceleratorInterviewDefinitions.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorInterviewDefinition?> DefinitionByKeyAsync(string stableKey, CancellationToken ct) =>
        context.AcceleratorInterviewDefinitions.SingleOrDefaultAsync(x => x.StableKey == stableKey && !x.IsDeleted, ct);
    public Task<AcceleratorInterviewVersion?> VersionAsync(Guid id, CancellationToken ct) =>
        context.AcceleratorInterviewVersions.Include(x => x.Definition).Include(x => x.Competencies)
            .Include(x => x.Sections).ThenInclude(x => x.Questions).Include(x => x.Criteria)
            .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorInterviewVersion?> VersionByNumberAsync(Guid definitionId, int version, CancellationToken ct) =>
        context.AcceleratorInterviewVersions.SingleOrDefaultAsync(x => x.DefinitionId == definitionId &&
            x.Version == version && !x.IsDeleted, ct);
    public Task<bool> VersionUsedAsync(Guid versionId, CancellationToken ct) =>
        context.AcceleratorInterviewSessions.AnyAsync(x => x.InterviewVersionId == versionId, ct);
    public async Task<IReadOnlyList<AcceleratorInterviewVersion>> PublishedVersionsAsync(Guid? roleId, int take, CancellationToken ct, IReadOnlyCollection<Guid>? competencyIds = null)
    {
        var query=context.AcceleratorInterviewVersions.Include(x=>x.Definition).Include(x=>x.Competencies)
            .Where(x=>x.Definition.IsActive && !x.Definition.IsDeleted && x.Status==AcceleratorInterviewCatalogStatus.Published && !x.IsDeleted);
        if (roleId.HasValue) query=query.Where(x=>x.Definition.TargetRoleId==roleId);
        else if (competencyIds is not null)
        {
            var ids=competencyIds.ToArray();
            query=query.Where(x=>x.Competencies.Any() && x.Competencies.All(c=>ids.Contains(c.CompetencyId)));
        }
        return await query.OrderBy(x=>x.Difficulty).ThenBy(x=>x.Definition.StableKey).ThenByDescending(x=>x.Version).Take(take).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<AcceleratorInterviewSession>> JourneySessionsAsync(Guid userId, Guid contextId, int skip, int take, CancellationToken ct) =>
        await Owned(userId).Include(x => x.Questions).Where(x => x.UserId == userId && x.JourneyContextId == contextId && !x.IsDeleted)
            .OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);
    public async Task<IReadOnlyList<AcceleratorReadinessSnapshot>> JourneyReadinessHistoryAsync(Guid userId, Guid contextId, int skip, int take, CancellationToken ct) =>
        await context.AcceleratorReadinessSnapshots.Where(x => x.UserId == userId && x.JourneyContextId == contextId && !x.IsDeleted)
            .OrderByDescending(x => x.CalculatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);
    public Task<AcceleratorInterviewSession?> SessionAsync(Guid userId, Guid id, CancellationToken ct) =>
        Owned(userId).Include(x => x.Questions)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorInterviewSession?> SessionByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) =>
        Owned(userId).Include(x => x.Questions)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorInterviewSession>> SessionsAsync(Guid userId, Guid goalId, int skip, int take, CancellationToken ct) =>
        await Owned(userId).Include(x => x.Questions)
            .Where(x => x.UserId == userId && x.GoalId == goalId && !x.IsDeleted)
            .OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
    public Task<AcceleratorInterviewAnswer?> AnswerByKeyAsync(Guid userId, Guid sessionId, Guid requestKey, CancellationToken ct) =>
        context.AcceleratorInterviewAnswers.Join(Owned(userId), a => a.SessionId, s => s.Id,
                (a, s) => new { a, s })
            .Where(x => x.s.UserId == userId && x.s.Id == sessionId && x.a.RequestKey == requestKey && !x.a.IsDeleted)
            .Select(x => x.a).SingleOrDefaultAsync(ct);
    public Task<AcceleratorInterviewAnswer?> AnswerAsync(Guid userId, Guid sessionId, Guid questionId, CancellationToken ct) =>
        context.AcceleratorInterviewAnswers.Join(Owned(userId), a => a.SessionId, s => s.Id,
                (a, s) => new { a, s })
            .Where(x => x.s.UserId == userId && x.s.Id == sessionId && x.a.SessionQuestionId == questionId && !x.a.IsDeleted)
            .Select(x => x.a).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<AcceleratorInterviewAnswer>> AnswersAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        await context.AcceleratorInterviewAnswers.Join(Owned(userId), a => a.SessionId, s => s.Id,
                (a, s) => new { a, s })
            .Where(x => x.s.UserId == userId && x.s.Id == sessionId && !x.a.IsDeleted)
            .OrderBy(x => x.a.SubmittedAtUtc).Select(x => x.a).ToListAsync(ct);
    public Task<AcceleratorInterviewEvaluation?> EvaluationAsync(Guid userId, Guid answerId, CancellationToken ct) =>
        context.AcceleratorInterviewEvaluations.Include(x => x.CriterionScores)
            .Join(context.AcceleratorInterviewAnswers, e => e.AnswerId, a => a.Id, (e, a) => new { e, a })
            .Join(Owned(userId), x => x.a.SessionId, s => s.Id, (x, s) => new { x.e, s })
            .Where(x => x.s.UserId == userId && x.e.AnswerId == answerId && !x.e.IsDeleted)
            .Select(x => x.e).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<AcceleratorInterviewEvaluation>> EvaluationsAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        await context.AcceleratorInterviewEvaluations.Include(x => x.CriterionScores)
            .Join(context.AcceleratorInterviewAnswers, e => e.AnswerId, a => a.Id, (e, a) => new { e, a })
            .Join(Owned(userId), x => x.a.SessionId, s => s.Id, (x, s) => new { x.e, s })
            .Where(x => x.s.UserId == userId && x.s.Id == sessionId && !x.e.IsDeleted)
            .OrderBy(x => x.e.EvaluatedAtUtc).Select(x => x.e).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorReadinessSnapshot>> ReadinessHistoryAsync(Guid userId, Guid goalId, int skip, int take, CancellationToken ct) =>
        await context.AcceleratorReadinessSnapshots.Where(x => x.UserId == userId && x.GoalId == goalId && !x.IsDeleted)
            .OrderByDescending(x => x.CalculatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
    public Task<AcceleratorReadinessSnapshot?> ReadinessForSessionAsync(Guid sessionId, CancellationToken ct) =>
        context.AcceleratorReadinessSnapshots.SingleOrDefaultAsync(x => x.TriggerInterviewSessionId == sessionId && !x.IsDeleted, ct);
    public Task<AcceleratorPassportShare?> ShareAsync(Guid userId, Guid goalId, CancellationToken ct) =>
        context.AcceleratorPassportShares.SingleOrDefaultAsync(x => x.UserId == userId && x.GoalId == goalId && !x.IsDeleted, ct);
    public Task<AcceleratorPassportShare?> PublicShareAsync(string token, CancellationToken ct) =>
        context.AcceleratorPassportShares.AsNoTracking().SingleOrDefaultAsync(x => x.PublicToken == token && x.IsEnabled && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorCompetency>> CompetenciesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await context.AcceleratorCompetencies.AsNoTracking().Where(x => ids.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleInterviewDispatchesAsync(DateTime now, int take, CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.AsNoTracking().Where(x =>
            (x.Operation == "InterviewEvaluation" || x.Operation == "InterviewQuestionGeneration") &&
            (x.Status == AcceleratorAiDispatchStatus.Uncertain || x.Status == AcceleratorAiDispatchStatus.Succeeded ||
                (x.Status == AcceleratorAiDispatchStatus.Reserved || x.Status == AcceleratorAiDispatchStatus.Dispatched) &&
                x.CreatedAtUtc <= now.AddMinutes(-2)) && !x.RequiresManualReview && x.NextReconciliationAtUtc <= now &&
            (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now) && !x.IsDeleted)
            .OrderBy(x => x.NextReconciliationAtUtc).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
    public async Task<AcceleratorAiDispatchRecord?> ClaimInterviewDispatchAsync(Guid dispatchId, Guid claimId, DateTime now,
        DateTime expiresAt, CancellationToken ct)
    {
        var count = await context.AcceleratorAiDispatchRecords.Where(x => x.Id == dispatchId &&
            (x.Operation == "InterviewEvaluation" || x.Operation == "InterviewQuestionGeneration") &&
            (x.Status == AcceleratorAiDispatchStatus.Uncertain || x.Status == AcceleratorAiDispatchStatus.Succeeded ||
                (x.Status == AcceleratorAiDispatchStatus.Reserved || x.Status == AcceleratorAiDispatchStatus.Dispatched) &&
                x.CreatedAtUtc <= now.AddMinutes(-2)) && !x.RequiresManualReview && !x.IsDeleted &&
            (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReconciliationClaimId, claimId)
                .SetProperty(x => x.ReconciliationClaimExpiresAtUtc, expiresAt)
                .SetProperty(x => x.LastReconciliationAtUtc, now)
                .SetProperty(x => x.ReconciliationAttemptCount, x => x.ReconciliationAttemptCount + 1)
                .SetProperty(x => x.Revision, Guid.NewGuid()), ct);
        return count == 0 ? null : await context.AcceleratorAiDispatchRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == dispatchId && x.ReconciliationClaimId == claimId, ct);
    }
    public async Task<AcceleratorAiDispatchRecord?> ClaimManualInterviewDispatchAsync(Guid dispatchId, Guid claimId,
        DateTime now, DateTime expiresAt, CancellationToken ct)
    {
        var count = await context.AcceleratorAiDispatchRecords.Where(x => x.Id == dispatchId &&
            (x.Operation == "InterviewEvaluation" || x.Operation == "InterviewQuestionGeneration") &&
            (x.Status == AcceleratorAiDispatchStatus.Uncertain || x.Status == AcceleratorAiDispatchStatus.Dispatched ||
             x.Status == AcceleratorAiDispatchStatus.Succeeded) && x.ProviderRequestId != null && !x.IsDeleted &&
            (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReconciliationClaimId, claimId)
                .SetProperty(x => x.ReconciliationClaimExpiresAtUtc, expiresAt)
                .SetProperty(x => x.LastReconciliationAtUtc, now)
                .SetProperty(x => x.ReconciliationAttemptCount, x => x.ReconciliationAttemptCount + 1)
                .SetProperty(x => x.Revision, Guid.NewGuid()), ct);
        return count == 0 ? null : await context.AcceleratorAiDispatchRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == dispatchId && x.ReconciliationClaimId == claimId, ct);
    }
    public async Task<bool> UpdateClaimedInterviewDispatchAsync(AcceleratorAiDispatchRecord record, Guid claimId,
        CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.Where(x => x.Id == record.Id &&
            x.ReconciliationClaimId == claimId && x.ReconciliationClaimExpiresAtUtc > DateTime.UtcNow &&
            (x.Operation == "InterviewEvaluation" || x.Operation == "InterviewQuestionGeneration"))
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
    public async Task<bool> PersistClaimedInterviewResultAsync(AcceleratorAiDispatchRecord record, Guid claimId, CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.Where(x => x.Id == record.Id && x.ReconciliationClaimId == claimId &&
            x.ReconciliationClaimExpiresAtUtc > DateTime.UtcNow &&
            (x.Operation == "InterviewEvaluation" || x.Operation == "InterviewQuestionGeneration") && x.ResultJson == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ResultJson, record.ResultJson)
                .SetProperty(x => x.InputTokens, record.InputTokens)
                .SetProperty(x => x.OutputTokens, record.OutputTokens)
                .SetProperty(x => x.ActualCostInr, record.ActualCostInr)
                .SetProperty(x => x.Revision, Guid.NewGuid()), ct) == 1;
    public async Task<IReadOnlyList<AcceleratorAiDispatchRecord>> UnresolvedInterviewDispatchesAsync(int skip, int take, CancellationToken ct) =>
        await context.AcceleratorAiDispatchRecords.AsNoTracking().Where(x =>
            (x.Operation == "InterviewEvaluation" || x.Operation == "InterviewQuestionGeneration") &&
            x.Status != AcceleratorAiDispatchStatus.Reconciled && x.Status != AcceleratorAiDispatchStatus.Failed && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take).ToListAsync(ct);
    public void Add(object entity) => context.Add(entity);
    private IQueryable<AcceleratorInterviewSession> Owned(Guid userId) => context.AcceleratorInterviewSessions.Where(x => x.UserId == userId && !x.IsDeleted &&
        (!x.JourneyContextId.HasValue || context.AcceleratorJourneyContexts.Any(j => j.Id == x.JourneyContextId && j.UserId == userId && !j.IsDeleted && j.CareerGoalId == x.GoalId)));
}
