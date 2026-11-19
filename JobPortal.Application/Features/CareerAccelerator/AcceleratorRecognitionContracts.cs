using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record ProficiencyContribution(AcceleratorProficiencyComponent Component, decimal Weight, bool Required);
public sealed record ProficiencyBandDefinition(string Label, decimal MinimumScore, decimal MaximumScore, int DisplayOrder);
public sealed record CreateProficiencyPolicyRequest(Guid? LearningPathId, string Code, int Version,
    IReadOnlyList<ProficiencyContribution> Components, IReadOnlyList<ProficiencyBandDefinition> Bands,
    decimal MinimumEvidenceConfidence, int CertificateMinimumAssessments, decimal CertificateMinimumScore, bool CertificateRequiresModules);
public sealed record CreateAchievementDefinitionRequest(string Code, string Title, string Description, AcceleratorAchievementRule Rule, decimal Threshold);
public sealed record ProficiencyComponentResponse(AcceleratorProficiencyComponent Component, decimal Weight, decimal? Score, string Status);
public sealed record JourneyProficiencyResponse(Guid JourneyContextId, Guid? PolicyId, string Status, decimal? Score, string? Band,
    int CompletedAssessments, int CompletedModules, int TotalModules, IReadOnlyList<Guid> EvidenceIds,
    IReadOnlyList<ProficiencyComponentResponse> Components, IReadOnlyList<DemonstratedCompetencyResponse>? DemonstratedCompetencies = null);
public sealed record DemonstratedCompetencyResponse(Guid CompetencyId, string Name, AcceleratorProficiency Proficiency, decimal Confidence);
public sealed record JourneyAchievementResponse(Guid Id, Guid DefinitionId, DateTime AwardedAtUtc, string Code, string Title, string Description);
public sealed record JourneyCertificateResponse(Guid? Id, AcceleratorCertificateState State, DateTime? EvaluatedAtUtc,
    DateTime? IssuedAtUtc, string? CertificateIdentifier);
public sealed record RecognitionSignals(decimal? AssessmentScore, int Assessments, int CompletedModules, int TotalModules, decimal? PracticeScore = null);
public sealed record EnrollmentRecognitionResponse(Guid LearningPathId, string Title, JourneyProficiencyResponse Proficiency,
    IReadOnlyList<JourneyAchievementResponse> Achievements, JourneyCertificateResponse Certificate);

public interface IAcceleratorRecognitionStore
{
    Task<AcceleratorSkillProficiencyPolicy?> PolicyAsync(Guid? pathId, CancellationToken ct);
    Task<RecognitionSignals> SignalsAsync(AcceleratorJourneyContext context, CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAchievementDefinition>> DefinitionsAsync(CancellationToken ct);
    Task<IReadOnlyList<AcceleratorAchievementDefinition>> DefinitionsByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<CandidateAcceleratorAchievement>> AwardsAsync(Guid actor, Guid contextId, CancellationToken ct);
    Task<AcceleratorEnrollmentCertificate?> CertificateAsync(Guid actor, Guid contextId, CancellationToken ct);
    void Add(object entity);
}
