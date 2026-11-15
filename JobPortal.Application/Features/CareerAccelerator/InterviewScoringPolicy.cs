using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record InterviewScore(decimal Total, IReadOnlyList<InterviewCriterionObservation> Observations);

public static class InterviewScoringPolicy
{
    public static void ValidateConfiguration(AcceleratorInterviewQuestion question,
        ICollection<AcceleratorInterviewRubricCriterion> criteria)
    {
        if (question.EvaluationMode != AcceleratorInterviewEvaluationMode.Deterministic)
        {
            if (question.DeterministicRulesJson != "[]") throw Invalid("Non-deterministic questions cannot declare objective rules.");
            return;
        }
        _ = Score(question, criteria, string.Empty);
    }

    public static InterviewScore Score(AcceleratorInterviewQuestion question,
        ICollection<AcceleratorInterviewRubricCriterion> criteria, string content)
    {
        if (question.EvaluationMode != AcceleratorInterviewEvaluationMode.Deterministic || content.Length > 32000)
            throw Invalid("This answer cannot be scored with objective rules.");
        ProjectRule[] rules;
        try { rules = JsonSerializer.Deserialize<ProjectRule[]>(question.DeterministicRulesJson) ?? []; }
        catch (JsonException) { throw Invalid("Published objective rules are invalid."); }
        if (rules.Length != criteria.Count || rules.Select(x => x.CriterionId).Distinct().Count() != rules.Length ||
            rules.Any(x => x.CriterionId == Guid.Empty || string.IsNullOrWhiteSpace(x.Expected) || x.Expected.Length > 1000 ||
                x.Kind is not ("ExactChoice" or "NormalizedTextMatch" or "RequiredConcepts" or "Checklist" or "RequiredFields") ||
                !criteria.Any(c => c.Id == x.CriterionId))) throw Invalid("Objective rules must cover every rubric criterion once.");
        var assignment = new AcceleratorProjectAssignment { Type = AcceleratorAssignmentType.Text,
            EvaluationMode = AcceleratorProjectEvaluationMode.Deterministic,
            DeterministicRulesJson = question.DeterministicRulesJson };
        var rubric = new AcceleratorProjectRubric { Criteria = criteria.Select(c => new AcceleratorProjectRubricCriterion
        {
            Id = c.Id, Sequence = c.Sequence, Name = c.Name, Description = c.Description,
            Weight = c.Weight, MaximumScore = c.MaximumScore, CompetencyId = c.CompetencyId
        }).ToArray() };
        var result = new ProjectDeterministicEvaluator().Evaluate(assignment, rubric, content);
        var observations = result.Observations.Select(x => new InterviewCriterionObservation(x.CriterionId, x.Score)).ToArray();
        var total = decimal.Round(criteria.Sum(c => c.Weight * observations.Single(x => x.CriterionId == c.Id).Score / c.MaximumScore), 2);
        return new InterviewScore(total, observations);
    }

    private static BadRequestException Invalid(string message) => new(message, "accelerator_interview_scoring_invalid");
}
