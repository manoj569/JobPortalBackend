using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities.CareerAccelerator;

public enum AcceleratorFeature { Tutor = 1, Review, Interview, Roadmap, Practice, EmployerOutreach }
public enum AcceleratorGoalStatus { Active = 1, Paused, Archived }
public enum AcceleratorUsageState { Reserved = 1, Dispatched, Completed, Released, Uncertain }

public sealed class AcceleratorTaxonomy : BaseEntity
{
    public string Version { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime PublishedAtUtc { get; set; }
    public ICollection<AcceleratorCareerRole> Roles { get; set; } = [];
    public ICollection<AcceleratorCompetency> Competencies { get; set; } = [];
}

public sealed class AcceleratorCareerRole : BaseEntity
{
    public AcceleratorConfigurationSource ConfigurationSource { get; set; }
    public Guid TaxonomyId { get; set; }
    public AcceleratorTaxonomy Taxonomy { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ICollection<AcceleratorRoleRequirement> Requirements { get; set; } = [];
}

public sealed class AcceleratorCompetency : BaseEntity
{
    public Guid TaxonomyId { get; set; }
    public AcceleratorTaxonomy Taxonomy { get; set; } = null!;
    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public string SkillNameSnapshot { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Difficulty { get; set; }
    public Guid? ParentId { get; set; }
    public AcceleratorCompetency? Parent { get; set; }
    public string AssessmentMethodsJson { get; set; } = "[]";
    public string ResourcesJson { get; set; } = "[]";
    public ICollection<AcceleratorPrerequisite> Prerequisites { get; set; } = [];
}

public sealed class AcceleratorPrerequisite : BaseEntity
{
    public Guid TaxonomyId { get; set; }
    public Guid CompetencyId { get; set; }
    public AcceleratorCompetency Competency { get; set; } = null!;
    public Guid RequiredCompetencyId { get; set; }
    public AcceleratorCompetency RequiredCompetency { get; set; } = null!;
}

public sealed class AcceleratorRoleRequirement : BaseEntity
{
    public Guid TaxonomyId { get; set; }
    public Guid RoleId { get; set; }
    public AcceleratorCareerRole Role { get; set; } = null!;
    public Guid CompetencyId { get; set; }
    public AcceleratorCompetency Competency { get; set; } = null!;
    public int ExpectedProficiency { get; set; }
}

public sealed class AcceleratorProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public decimal ExperienceYears { get; set; }
    public string EducationSummary { get; set; } = string.Empty;
    public int WeeklyStudyMinutes { get; set; }
    // References the existing user's uploaded resume version, never a copied path/blob.
    public DateTime? LinkedResumeUploadedAtUtc { get; set; }
    public ICollection<AcceleratorProfileSkill> Skills { get; set; } = [];
}

public sealed class AcceleratorProfileSkill : BaseEntity
{
    public Guid ProfileId { get; set; }
    public AcceleratorProfile Profile { get; set; } = null!;
    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public int SelfReportedProficiency { get; set; }
}

public sealed class AcceleratorCareerGoal : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid TaxonomyId { get; set; }
    public Guid RoleId { get; set; }
    public AcceleratorCareerRole Role { get; set; } = null!;
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public AcceleratorGoalStatus Status { get; set; } = AcceleratorGoalStatus.Active;
    public int WeeklyStudyMinutes { get; set; }
    public DateTime TargetAtUtc { get; set; }
}

// A product entitlement period backed by the EXISTING payment record. Not another gateway/subscription system.
public sealed class AcceleratorBillingCycle : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public string PlanVersion { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public ICollection<AcceleratorAllowance> Allowances { get; set; } = [];
}

public sealed class AcceleratorAllowance : BaseEntity
{
    public Guid CycleId { get; set; }
    public AcceleratorBillingCycle Cycle { get; set; } = null!;
    public AcceleratorFeature Feature { get; set; }
    public int Allocated { get; set; }
}

public sealed class AcceleratorUsageReservation : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid CycleId { get; set; }
    public AcceleratorBillingCycle Cycle { get; set; } = null!;
    public Guid RequestKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public AcceleratorFeature Feature { get; set; }
    public AcceleratorUsageState State { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime ReservedAtUtc { get; set; }
    public DateTime LeaseExpiresAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime BudgetDayUtc { get; set; }
    public decimal ReservedCostInr { get; set; }
    public decimal? ActualCostInr { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReservedInputTokens { get; set; }
    public int ReservedOutputTokens { get; set; }
    public bool PolicyViolation { get; set; }
}

public sealed class AcceleratorUsageEvent : BaseEntity
{
    public Guid ReservationId { get; set; }
    public AcceleratorUsageReservation Reservation { get; set; } = null!;
    public AcceleratorUsageState State { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public decimal CostInr { get; set; }
}
