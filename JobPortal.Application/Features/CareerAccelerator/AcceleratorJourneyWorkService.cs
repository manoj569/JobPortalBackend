using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record AcceleratorJourneyWorkScope(AcceleratorJourneyContext Context, Guid? RoleId,
    IReadOnlyList<AcceleratorJourneyRequirement> Requirements, IReadOnlyList<AcceleratorSkillEvidence> Evidence, AcceleratorRoadmap? Roadmap);

// Shared ownership/requirements adapter. Project and interview engines retain their existing scoring and dispatch flows.
public sealed class AcceleratorJourneyWorkService(AcceleratorJourneyContextService contexts, IAcceleratorJourneyStore journeys,
    IAcceleratorStore writes, IAcceleratorLearningPathStore learning)
{
    public async Task<AcceleratorJourneyWorkScope> ResolveAsync(Guid actor, Guid goalId, Guid? enrollmentId, CancellationToken ct)
    {
        if ((goalId != Guid.Empty) == enrollmentId.HasValue) throw new BadRequestException("Specify exactly one goal or enrollment.");
        var context = enrollmentId.HasValue ? await contexts.GetOrCreateForEnrollmentAsync(actor, enrollmentId.Value, ct)
            : await contexts.GetOrCreateForCareerGoalAsync(actor, goalId, ct);
        return await OwnedAsync(actor, context.Id, ct);
    }
    public async Task<AcceleratorJourneyWorkScope> OwnedAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        var context = await contexts.GetOwnedContextAsync(actor, contextId, ct);
        var requirements = await contexts.ResolveAsync(actor, contextId, ct);
        Guid? roleId = context.CareerGoalId.HasValue
            ? (await writes.GoalAsync(actor, context.CareerGoalId.Value, ct))?.RoleId
            : (await learning.EnrollmentAsync(actor, context.LearningPathEnrollmentId!.Value, ct))?.LearningPath.CanonicalCareerRoleId;
        return new(context, roleId, requirements, await journeys.EvidenceAsync(actor, contextId, ct), await journeys.ActiveRoadmapAsync(actor, contextId, ct));
    }
    public static bool Compatible(AcceleratorJourneyWorkScope scope, Guid roleId, IEnumerable<Guid> competencyIds)
    {
        if (scope.RoleId.HasValue) return scope.RoleId == roleId;
        var required = scope.Requirements.Select(x => x.CompetencyId).ToHashSet();
        var supplied = competencyIds.Distinct().ToArray();
        // Skill paths never borrow a role's unrelated competencies. Catalog role remains provenance only.
        return supplied.Length > 0 && supplied.All(required.Contains);
    }
}
