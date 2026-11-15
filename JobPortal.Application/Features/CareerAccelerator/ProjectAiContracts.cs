using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record ProjectAiCriterionResult(Guid CriterionId, decimal Score);
public sealed record ProjectAiVerification(AcceleratorDispatchVerificationStatus Status, string? ProviderRequestId,
    string? StructuredJson, int? InputTokens, int? OutputTokens, decimal? ActualCostInr,
    bool ConfirmedNonChargeable, string? EvidenceReference);

public interface IProjectAiDispatchVerifier
{
    string ProviderName { get; }
    Task<ProjectAiVerification> VerifyAsync(string providerRequestId, CancellationToken ct);
}

public static class ProjectAiResultParser
{
    public static IReadOnlyList<AcceleratorCriterionObservation> Parse(string json, AcceleratorProjectRubric rubric)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16000)
            throw new ConflictException("Provider evaluation result is missing or too large.");
        ProjectAiCriterionResult[] results;
        try { results = JsonSerializer.Deserialize<ProjectAiCriterionResult[]>(json) ?? []; }
        catch (JsonException) { throw new ConflictException("Provider evaluation result is not valid JSON."); }
        if (results.Length != rubric.Criteria.Count || results.Select(x => x.CriterionId).Distinct().Count() != results.Length ||
            results.Any(x => !rubric.Criteria.Any(c => c.Id == x.CriterionId && x.Score >= 0 && x.Score <= c.MaximumScore)))
            throw new ConflictException("Provider evaluation result does not match the published rubric.");
        return results.Select(x => new AcceleratorCriterionObservation(x.CriterionId, x.Score)).ToArray();
    }
}
