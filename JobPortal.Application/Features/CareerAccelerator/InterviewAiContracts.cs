using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public interface IInterviewAiDispatchVerifier
{
    string ProviderName { get; }
    Task<ProjectAiVerification> VerifyAsync(string providerRequestId, CancellationToken ct);
}

public static class InterviewAiResultParser
{
    public static IReadOnlyList<InterviewCriterionObservation> Parse(string json,
        ICollection<AcceleratorInterviewRubricCriterion> criteria)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16000)
            throw new ConflictException("Provider interview evaluation is missing or too large.");
        ProjectAiCriterionResult[] results;
        try { results = JsonSerializer.Deserialize<ProjectAiCriterionResult[]>(json) ?? []; }
        catch (JsonException) { throw new ConflictException("Provider interview evaluation is invalid JSON."); }
        if (criteria.Count == 0 || results.Length != criteria.Count ||
            results.Select(x => x.CriterionId).Distinct().Count() != results.Length ||
            results.Any(x => !criteria.Any(c => c.Id == x.CriterionId && x.Score >= 0 && x.Score <= c.MaximumScore)))
            throw new ConflictException("Provider interview evaluation does not match the published rubric.");
        return results.Select(x => new InterviewCriterionObservation(x.CriterionId, x.Score)).ToArray();
    }
}
