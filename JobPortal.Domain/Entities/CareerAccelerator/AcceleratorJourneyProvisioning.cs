using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorConfigurationSource { Curated = 0, AutoProvisioned = 1 }

// Durable successful-request binding; configuration is shared, candidate progress is not.
public sealed class AcceleratorJourneyProvisioningReceipt : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid? MarketRoleId { get; set; }
    public Guid? MarketSkillId { get; set; }
    public Guid LearningPathId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid JourneyContextId { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public int RequiredCompetencies { get; set; }
    // Baseline bank coverage at provisioning, never a count of candidate completions/evidence.
    public int AssessedCompetencies { get; set; }
    public string UnassessedCompetencyIdsJson { get; set; } = "[]";
}
