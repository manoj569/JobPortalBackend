using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorJourneyStore(JobPortalDbContext db) : IAcceleratorJourneyStore
{
    public Task<AcceleratorJourneyContext?> FindAsync(Guid userId, AcceleratorJourneyScopeType scope, Guid sourceId, CancellationToken ct) =>
        db.AcceleratorJourneyContexts.SingleOrDefaultAsync(x => !x.IsDeleted && x.UserId == userId && x.ScopeType == scope &&
            (scope == AcceleratorJourneyScopeType.CareerGoal ? x.CareerGoalId == sourceId : x.LearningPathEnrollmentId == sourceId), ct);

    public Task<AcceleratorJourneyContext?> OwnedAsync(Guid userId, Guid contextId, CancellationToken ct) =>
        db.AcceleratorJourneyContexts.SingleOrDefaultAsync(x => !x.IsDeleted && x.UserId == userId && x.Id == contextId, ct);

    public async Task<IReadOnlyList<AcceleratorLearningPathCompetencyRequirement>> PathRequirementsAsync(Guid pathId, CancellationToken ct) =>
        await db.AcceleratorLearningPathCompetencyRequirements.AsNoTracking().Where(x => !x.IsDeleted && x.LearningPathId == pathId)
            .OrderBy(x => x.CompetencyId).ToArrayAsync(ct);

    public async Task<IReadOnlyList<AcceleratorCompetency>> CompetenciesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.AcceleratorCompetencies.AsNoTracking().Where(x => !x.IsDeleted && ids.Contains(x.Id)).ToArrayAsync(ct);

    public void Add(AcceleratorJourneyContext context) => db.Add(context);
    public Task<int> AttemptCountAsync(Guid actor, Guid contextId, CancellationToken ct) =>
        db.AcceleratorAssessmentAttempts.CountAsync(x => !x.IsDeleted && x.UserId == actor && x.JourneyContextId == contextId, ct);
    public Task<AcceleratorRoadmap?> ActiveRoadmapAsync(Guid actor, Guid contextId, CancellationToken ct) =>
        db.AcceleratorRoadmaps.Include(x => x.Nodes).Where(x => !x.IsDeleted && x.UserId == actor && x.JourneyContextId == contextId && x.Status == AcceleratorRoadmapStatus.Active)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    public async Task<IReadOnlyList<AcceleratorRecommendation>> RecommendationsAsync(Guid actor, Guid contextId, CancellationToken ct) =>
        await db.AcceleratorRecommendations.AsNoTracking().Where(x => !x.IsDeleted && x.UserId == actor && x.JourneyContextId == contextId)
            .OrderBy(x => x.Priority).ThenByDescending(x => x.GeneratedAtUtc).ToArrayAsync(ct);
    public async Task<IReadOnlyList<AcceleratorSkillEvidence>> EvidenceAsync(Guid actor, Guid contextId, CancellationToken ct) =>
        await db.AcceleratorSkillEvidence.AsNoTracking().Where(x => !x.IsDeleted && x.UserId == actor && x.JourneyContextId == contextId)
            .OrderByDescending(x => x.EvaluatedAtUtc).ToArrayAsync(ct);
    public Task<AcceleratorLearningPathAssessment?> AssessmentMappingAsync(Guid pathId, AcceleratorLearningPathAssessmentPurpose purpose, CancellationToken ct) =>
        db.AcceleratorLearningPathAssessments.SingleOrDefaultAsync(x => !x.IsDeleted && x.LearningPathId == pathId && x.Purpose == purpose, ct);
}
