using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorProficiencyComponent { Assessment = 1, Practice, Assignment, Project, ModuleCompletion, VerifiedEvidence, Interview }
public enum AcceleratorAchievementRule { MinimumProficiency = 1, CompletedModules, CompletedJourney }
public enum AcceleratorCertificateState { NotEligible = 1, Eligible, GenerationPending, Available, GenerationFailed }

public sealed class AcceleratorSkillProficiencyPolicy : BaseEntity
{
    public Guid? LearningPathId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public string ComponentsJson { get; set; } = "[]";
    public decimal MinimumEvidenceConfidence { get; set; }
    public int CertificateMinimumAssessments { get; set; }
    public decimal CertificateMinimumScore { get; set; }
    public bool CertificateRequiresModules { get; set; }
    public ICollection<AcceleratorProficiencyBand> Bands { get; set; } = [];
}

public sealed class AcceleratorProficiencyBand : BaseEntity
{
    public Guid PolicyId { get; set; }
    public AcceleratorSkillProficiencyPolicy Policy { get; set; } = null!;
    public string Label { get; set; } = string.Empty;
    public decimal MinimumScore { get; set; }
    public decimal MaximumScore { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AcceleratorAchievementDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public AcceleratorAchievementRule Rule { get; set; }
    public decimal Threshold { get; set; }
}

public sealed class CandidateAcceleratorAchievement : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid JourneyContextId { get; set; }
    public Guid AchievementDefinitionId { get; set; }
    public DateTime AwardedAtUtc { get; set; }
    public string EvidenceJson { get; set; } = "{}";
}

public sealed class AcceleratorEnrollmentCertificate : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid JourneyContextId { get; set; }
    public Guid PolicyId { get; set; }
    public AcceleratorCertificateState State { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public string? CertificateIdentifier { get; set; }
    public string? ArtifactStorageKey { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}
