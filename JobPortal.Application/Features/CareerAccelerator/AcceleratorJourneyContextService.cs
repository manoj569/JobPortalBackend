using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorJourneyContextService(IAcceleratorStore writes, IAcceleratorJourneyStore journeys,
    IAcceleratorLearningPathStore learning, IAcceleratorPhase2Store phase2, AcceleratorService access) : IAcceleratorRequirementResolver
{
    public Task<AcceleratorJourneyContext> GetOrCreateForCareerGoalAsync(Guid actor, Guid goalId, CancellationToken ct) =>
        writes.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            var goal = await writes.GoalAsync(actor, goalId, ct) ?? throw new NotFoundException("Career goal not found.");
            if (goal.IsDeleted || goal.Status != AcceleratorGoalStatus.Active) throw Invalid("Career goal is not active.");
            var existing = await journeys.FindAsync(actor, AcceleratorJourneyScopeType.CareerGoal, goalId, ct);
            if (existing is not null) return existing;
            var context = new AcceleratorJourneyContext { UserId = actor, ScopeType = AcceleratorJourneyScopeType.CareerGoal,
                CareerGoalId = goal.Id, TaxonomyId = goal.TaxonomyId };
            journeys.Add(context);
            return context;
        }, ct);

    public Task<AcceleratorJourneyContext> GetOrCreateForEnrollmentAsync(Guid actor, Guid enrollmentId, CancellationToken ct) =>
        writes.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            var enrollment = await RequiredEnrollment(actor, enrollmentId, ct);
            var path = enrollment.LearningPath;
            Guid taxonomyId;
            if (path.Type == AcceleratorLearningPathType.Career)
            {
                var role = path.CanonicalCareerRoleId.HasValue ? await writes.RoleAsync(path.CanonicalCareerRoleId.Value, ct) : null;
                if (role is null || role.IsDeleted) throw Invalid("Learning path canonical role is unavailable.");
                taxonomyId = role.TaxonomyId;
            }
            else if (path.Type == AcceleratorLearningPathType.Skill)
            {
                var requirements = await journeys.PathRequirementsAsync(path.Id, ct);
                var competencies = await journeys.CompetenciesAsync(requirements.Select(x => x.CompetencyId).ToArray(), ct);
                if (requirements.Count == 0 || competencies.Count != requirements.Count || competencies.Select(x => x.TaxonomyId).Distinct().Count() != 1 ||
                    !competencies.Any(x => x.SkillId == path.CanonicalSkillId)) throw Invalid("Skill path requires compatible canonical competency requirements.");
                ValidateRequirements(requirements);
                taxonomyId = competencies[0].TaxonomyId;
            }
            else throw Invalid("Unsupported learning path type.");
            var existing = await journeys.FindAsync(actor, AcceleratorJourneyScopeType.LearningPathEnrollment, enrollmentId, ct);
            if (existing is not null)
            {
                if (existing.TaxonomyId != taxonomyId) throw Invalid("Journey taxonomy cannot change after context creation.");
                return existing;
            }
            var context = new AcceleratorJourneyContext { UserId = actor, ScopeType = AcceleratorJourneyScopeType.LearningPathEnrollment,
                LearningPathEnrollmentId = enrollment.Id, TaxonomyId = taxonomyId };
            journeys.Add(context);
            return context;
        }, ct);

    public async Task<AcceleratorJourneyContext> GetOwnedContextAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        return await journeys.OwnedAsync(actor, contextId, ct) ?? throw new NotFoundException("Journey context not found.");
    }

    public async Task<IReadOnlyList<AcceleratorJourneyRequirement>> ResolveAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        var context = await GetOwnedContextAsync(actor, contextId, ct);
        Guid? roleId;
        if (context.ScopeType == AcceleratorJourneyScopeType.CareerGoal)
        {
            var goal = await writes.GoalAsync(actor, context.CareerGoalId ?? throw Invalid("Invalid journey scope."), ct) ?? throw new NotFoundException("Career goal not found.");
            if (goal.IsDeleted || goal.TaxonomyId != context.TaxonomyId) throw Invalid("Journey source is unavailable.");
            roleId = goal.RoleId;
        }
        else
        {
            var enrollment = await RequiredEnrollment(actor, context.LearningPathEnrollmentId ?? throw Invalid("Invalid journey scope."), ct);
            roleId = enrollment.LearningPath.CanonicalCareerRoleId;
            if (enrollment.LearningPath.Type == AcceleratorLearningPathType.Skill)
            {
                var requirements = await journeys.PathRequirementsAsync(enrollment.LearningPathId, ct);
                ValidateRequirements(requirements);
                var competencies = await journeys.CompetenciesAsync(requirements.Select(x => x.CompetencyId).ToArray(), ct);
                if (requirements.Count == 0 || competencies.Count != requirements.Count || competencies.Any(x => x.TaxonomyId != context.TaxonomyId)) throw Invalid("Path requirements do not match the journey taxonomy.");
                return requirements.Select(x => new AcceleratorJourneyRequirement(x.CompetencyId, x.TargetProficiency, x.Weight)).ToArray();
            }
        }
        var role = roleId.HasValue ? await writes.RoleAsync(roleId.Value, ct) : null;
        if (role is null || role.IsDeleted || role.TaxonomyId != context.TaxonomyId) throw Invalid("Journey role does not match its taxonomy.");
        return (await phase2.RequirementsAsync(role.Id, ct)).Select(x => new AcceleratorJourneyRequirement(x.CompetencyId, x.ExpectedProficiency, 1m)).ToArray();
    }

    private async Task<AcceleratorPathEnrollment> RequiredEnrollment(Guid actor, Guid id, CancellationToken ct)
    {
        var enrollment = await learning.EnrollmentAsync(actor, id, ct) ?? throw new NotFoundException("Learning enrollment not found.");
        if (enrollment.IsDeleted || enrollment.Status == AcceleratorLearningEnrollmentStatus.Abandoned || enrollment.LearningPath.IsDeleted ||
            enrollment.LearningPath.Status != AcceleratorLearningCatalogStatus.Published) throw Invalid("Learning enrollment is unavailable.");
        return enrollment;
    }
    private static void ValidateRequirements(IReadOnlyList<AcceleratorLearningPathCompetencyRequirement> requirements)
    {
        if (requirements.Any(x => x.TargetProficiency is < 1 or > 5 || x.Weight <= 0)) throw Invalid("Invalid path competency requirements.");
    }
    private static ConflictException Invalid(string message) => new(message, "accelerator_journey_invalid");
}
