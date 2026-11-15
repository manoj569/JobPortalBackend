using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record ProjectRule(string Kind, Guid CriterionId, string Expected);
public sealed record ProjectCheckResult(bool Supported, IReadOnlyList<AcceleratorCriterionObservation> Observations);

public interface IProjectDeterministicEvaluator
{
    void ValidateConfiguration(AcceleratorProjectAssignment assignment, AcceleratorProjectRubric rubric);
    ProjectCheckResult Evaluate(AcceleratorProjectAssignment assignment, AcceleratorProjectRubric rubric, string submittedContent);
}

public sealed class ProjectDeterministicEvaluator : IProjectDeterministicEvaluator
{
    public void ValidateConfiguration(AcceleratorProjectAssignment assignment, AcceleratorProjectRubric rubric)
    {
        if (!Enum.IsDefined(assignment.EvaluationMode))
            throw new BadRequestException("Assignment evaluation mode is required.", "accelerator_project_evaluation_invalid");
        if (assignment.EvaluationMode != AcceleratorProjectEvaluationMode.Deterministic)
        {
            if (assignment.DeterministicRulesJson != "[]")
                throw new BadRequestException("Subjective assignments cannot declare deterministic rules.", "accelerator_project_evaluation_invalid");
            return;
        }
        if (assignment.Type is not (AcceleratorAssignmentType.Structured or AcceleratorAssignmentType.Text))
            throw new BadRequestException("This assignment type cannot use deterministic evaluation.", "accelerator_project_evaluation_invalid");

        try { _ = Evaluate(assignment, rubric, string.Empty); }
        catch (InvalidOperationException exception)
        {
            throw new BadRequestException(exception.Message, "accelerator_project_evaluation_invalid");
        }
    }

    public ProjectCheckResult Evaluate(AcceleratorProjectAssignment assignment, AcceleratorProjectRubric rubric, string submittedContent)
    {
        if (assignment.Type is not (AcceleratorAssignmentType.Structured or AcceleratorAssignmentType.Text)) return new(false, []);
        if (assignment.DeterministicRulesJson.Length > 16000 || submittedContent.Length > 32000)
            throw new BadRequestException("Project evaluator input exceeds its limit.", "accelerator_project_evaluation_invalid");
        ProjectRule[] rules;
        try { rules = JsonSerializer.Deserialize<ProjectRule[]>(assignment.DeterministicRulesJson) ?? []; }
        catch (JsonException) { throw new InvalidOperationException("Published project evaluator rules are invalid."); }
        AcceleratorPhase4Service.ValidateRubric(rubric);
        if (rules.Length != rubric.Criteria.Count || rules.Select(x => x.CriterionId).Distinct().Count() != rules.Length ||
            rules.Any(x => x.CriterionId == Guid.Empty || string.IsNullOrWhiteSpace(x.Expected) || x.Expected.Length > 1000 ||
                x.Kind is not ("ExactChoice" or "NormalizedTextMatch" or "RequiredFields" or "RequiredConcepts" or "Checklist") ||
                !rubric.Criteria.Any(c => c.Id == x.CriterionId)))
            throw new InvalidOperationException("Published project evaluator rules do not match the rubric.");
        var observations = new List<AcceleratorCriterionObservation>(rules.Length);
        foreach (var rule in rules)
        {
            var criterion = rubric.Criteria.Single(x => x.Id == rule.CriterionId);
            var matched = rule.Kind switch
            {
                "ExactChoice" => StringComparer.OrdinalIgnoreCase.Equals(rule.Expected.Trim(), submittedContent.Trim()),
                "NormalizedTextMatch" => StringComparer.OrdinalIgnoreCase.Equals(Normalize(rule.Expected), Normalize(submittedContent)),
                "RequiredConcepts" => ParseTerms(rule.Expected).All(term => Normalize(submittedContent).Contains(Normalize(term), StringComparison.OrdinalIgnoreCase)),
                "Checklist" => ParseTerms(rule.Expected).All(term => submittedContent.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(term, StringComparer.OrdinalIgnoreCase)),
                "RequiredFields" => HasRequiredFields(submittedContent, ParseTerms(rule.Expected)),
                _ => false
            };
            observations.Add(new(criterion.Id, matched ? criterion.MaximumScore : 0));
        }
        return new(true, observations);
    }

    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static string[] ParseTerms(string value)
    {
        var terms = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length is < 1 or > 20 || terms.Any(x => x.Length > 120) || terms.Distinct(StringComparer.OrdinalIgnoreCase).Count() != terms.Length)
            throw new InvalidOperationException("Published project evaluator terms are invalid.");
        return terms;
    }

    private static bool HasRequiredFields(string content, string[] fields)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object && fields.All(field => document.RootElement.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));
        }
        catch (JsonException) { return false; }
    }
}
