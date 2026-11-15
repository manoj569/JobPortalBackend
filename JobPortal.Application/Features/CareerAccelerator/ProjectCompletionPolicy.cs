using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record ProjectCompletionResult(int ProgressPercent, Guid? NextMilestoneId, decimal? FinalScore);

public static class ProjectCompletionPolicy
{
    public const decimal AcceptancePercent = 70m;

    // Every required assignment contributes equally. The latest accepted attempt for each one supplies its score.
    public static ProjectCompletionResult Calculate(AcceleratorProjectVersion version,
        IEnumerable<AcceleratorEvaluatedAssignment> evaluations)
    {
        var requiredMilestones = version.Milestones.Where(x => x.IsRequired).OrderBy(x => x.Sequence).ToArray();
        var requiredAssignments = requiredMilestones.SelectMany(x => x.Assignments).ToArray();
        if (requiredAssignments.Length == 0) throw new InvalidOperationException("Project has no required assignments.");
        var acceptedScores = evaluations.Where(x => x.Score >= AcceptancePercent)
            .GroupBy(x => x.AssignmentId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(e => e.AttemptNumber).First().Score);
        var completedCount = requiredAssignments.Count(x => acceptedScores.ContainsKey(x.Id));
        var next = requiredMilestones.FirstOrDefault(x => x.Assignments.Any(a => !acceptedScores.ContainsKey(a.Id)));
        return new ProjectCompletionResult(100 * completedCount / requiredAssignments.Length, next?.Id,
            next is null ? decimal.Round(requiredAssignments.Average(x => acceptedScores[x.Id]), 2) : null);
    }
}
