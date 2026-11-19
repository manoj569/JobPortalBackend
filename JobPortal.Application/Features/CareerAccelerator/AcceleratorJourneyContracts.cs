using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record AcceleratorJourneyRequirement(Guid CompetencyId, int TargetProficiency, decimal Weight);
public sealed record StartEnrollmentAssessmentRequest(Guid RequestKey);
public interface IAcceleratorJourneyStore
{
    Task<AcceleratorJourneyContext?> FindAsync(Guid userId, AcceleratorJourneyScopeType scope, Guid sourceId, CancellationToken ct);
    Task<AcceleratorJourneyContext?> OwnedAsync(Guid userId, Guid contextId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorLearningPathCompetencyRequirement>> PathRequirementsAsync(Guid pathId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorCompetency>> CompetenciesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    void Add(AcceleratorJourneyContext context);
    Task<IReadOnlyList<AcceleratorSkillEvidence>> EvidenceAsync(Guid actor, Guid contextId, CancellationToken ct);
    Task<AcceleratorRoadmap?> ActiveRoadmapAsync(Guid actor, Guid contextId, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorRecommendation>> RecommendationsAsync(Guid actor, Guid contextId, CancellationToken ct);
    Task<int> AttemptCountAsync(Guid actor, Guid contextId, CancellationToken ct);
    Task<AcceleratorLearningPathAssessment?> AssessmentMappingAsync(Guid pathId, AcceleratorLearningPathAssessmentPurpose purpose, CancellationToken ct);
}

public interface IAcceleratorRequirementResolver
{
    Task<IReadOnlyList<AcceleratorJourneyRequirement>> ResolveAsync(Guid actor, Guid contextId, CancellationToken ct);
}
