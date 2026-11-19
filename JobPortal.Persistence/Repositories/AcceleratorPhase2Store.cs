using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorPhase2Store(JobPortalDbContext context) : IAcceleratorPhase2Store
{
    public Task<AcceleratorAssessmentTemplate?> TemplateAsync(Guid id, CancellationToken ct) => context.AcceleratorAssessmentTemplates.Include(x => x.Role).ThenInclude(x => x!.Taxonomy).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorAssessmentTemplate?> TemplateAsync(Guid roleId, string version, CancellationToken ct) => context.AcceleratorAssessmentTemplates.SingleOrDefaultAsync(x => x.RoleId == roleId && x.Version == version && !x.IsDeleted, ct);
    public Task<AcceleratorQuestion?> QuestionAsync(Guid competencyId, string version, CancellationToken ct) => context.AcceleratorQuestions.SingleOrDefaultAsync(x => x.CompetencyId == competencyId && x.Version == version && !x.IsDeleted, ct);
    public Task<AcceleratorAssessmentAttempt?> AttemptAsync(Guid userId, Guid id, CancellationToken ct) => context.AcceleratorAssessmentAttempts.Include(x => x.Answers).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorAssessmentAttempt?> AttemptByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => context.AcceleratorAssessmentAttempts.Include(x => x.Answers).SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorQuestion>> QuestionsAsync(Guid taxonomyId, IReadOnlyCollection<Guid> competencyIds, CancellationToken ct) => await context.AcceleratorQuestions.Where(x => x.TaxonomyId == taxonomyId && x.IsActive && competencyIds.Contains(x.CompetencyId) && !x.IsDeleted).OrderBy(x => x.CompetencyId).ThenBy(x => x.Difficulty).ThenBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorQuestion>> QuestionsByIdsAsync(Guid taxonomyId, IReadOnlyCollection<Guid> questionIds, CancellationToken ct) => await context.AcceleratorQuestions.Where(x => x.TaxonomyId == taxonomyId && questionIds.Contains(x.Id) && !x.IsDeleted).OrderBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorRoleRequirement>> RequirementsAsync(Guid roleId, CancellationToken ct) => await context.AcceleratorRoleRequirements.Include(x => x.Competency).Where(x => x.RoleId == roleId && !x.IsDeleted).OrderBy(x => x.Competency.SkillNameSnapshot).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorPrerequisite>> PrerequisitesAsync(Guid taxonomyId, CancellationToken ct) => await context.AcceleratorPrerequisites.Where(x => x.TaxonomyId == taxonomyId && !x.IsDeleted).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorSkillEvidence>> EvidenceAsync(Guid userId, Guid goalId, CancellationToken ct) => await context.AcceleratorSkillEvidence.Where(x => x.UserId == userId && x.GoalId == goalId && !x.IsDeleted).OrderByDescending(x => x.EvaluatedAtUtc).ToListAsync(ct);
    public Task<AcceleratorRoadmap?> RoadmapAsync(Guid userId, Guid id, CancellationToken ct) => context.AcceleratorRoadmaps.Include(x => x.Nodes).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorRoadmap?> ActiveRoadmapAsync(Guid userId, Guid goalId, CancellationToken ct) => context.AcceleratorRoadmaps.Include(x => x.Nodes).Where(x => x.UserId == userId && x.GoalId == goalId && x.Status == AcceleratorRoadmapStatus.Active && !x.IsDeleted).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    public async Task<IReadOnlyList<AcceleratorLearningActivity>> ActivitiesAsync(Guid userId, Guid roadmapId, CancellationToken ct) => await context.AcceleratorLearningActivities.Join(context.AcceleratorRoadmapNodes, a => a.RoadmapNodeId, n => n.Id, (a, n) => new { a, n }).Where(x => x.a.UserId == userId && x.n.RoadmapId == roadmapId && !x.a.IsDeleted).OrderBy(x => x.a.ScheduledAtUtc).Select(x => x.a).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorRoadmapEdge>> EdgesAsync(Guid roadmapId, CancellationToken ct) => await context.AcceleratorRoadmapEdges.Where(x => x.RoadmapId == roadmapId && !x.IsDeleted).OrderBy(x => x.FromNodeId).ThenBy(x => x.ToNodeId).ToListAsync(ct);
    public Task<AcceleratorLearningActivity?> ActivityAsync(Guid userId, Guid id, CancellationToken ct) => context.AcceleratorLearningActivities.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorRoadmapNode?> NodeAsync(Guid id, CancellationToken ct) => context.AcceleratorRoadmapNodes.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorProgressEvent?> ProgressEventByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => context.AcceleratorProgressEvents.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorRecommendation>> RecommendationsAsync(Guid userId, Guid goalId, CancellationToken ct) => await context.AcceleratorRecommendations.Where(x => x.UserId == userId && x.GoalId == goalId && !x.IsDeleted).OrderBy(x => x.Priority).ThenByDescending(x => x.GeneratedAtUtc).ToListAsync(ct);
    public Task<int> AttemptCountAsync(Guid userId, Guid goalId, CancellationToken ct) => context.AcceleratorAssessmentAttempts.CountAsync(x => x.UserId == userId && x.GoalId == goalId && !x.IsDeleted, ct);
    public void Add(object entity) => context.Add(entity);
}
