using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorTaxonomyConfiguration : IEntityTypeConfiguration<AcceleratorTaxonomy>
{
    public void Configure(EntityTypeBuilder<AcceleratorTaxonomy> builder)
    {
        builder.ToTable("AcceleratorTaxonomies"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Version).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.Version).IsUnique();
        builder.HasIndex(x => x.PublishedAtUtc);
    }
}
public sealed class AcceleratorCareerRoleConfiguration : IEntityTypeConfiguration<AcceleratorCareerRole>
{
    public void Configure(EntityTypeBuilder<AcceleratorCareerRole> builder)
    {
        builder.ToTable("AcceleratorCareerRoles"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Code).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.HasAlternateKey(x => new { x.Id, x.TaxonomyId });
        builder.HasIndex(x => new { x.TaxonomyId, x.Code }).IsUnique();
        builder.HasOne(x => x.Taxonomy).WithMany(x => x.Roles).HasForeignKey(x => x.TaxonomyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorCompetencyConfiguration : IEntityTypeConfiguration<AcceleratorCompetency>
{
    public void Configure(EntityTypeBuilder<AcceleratorCompetency> builder)
    {
        builder.ToTable("AcceleratorCompetencies", t => t.HasCheckConstraint("CK_AcceleratorCompetency_Difficulty", "\"Difficulty\" BETWEEN 1 AND 5"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SkillNameSnapshot).HasMaxLength(150).IsRequired();
        builder.Property(x => x.AssessmentMethodsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ResourcesJson).HasColumnType("jsonb").IsRequired();
        builder.HasAlternateKey(x => new { x.Id, x.TaxonomyId });
        builder.HasIndex(x => new { x.TaxonomyId, x.SkillId }).IsUnique();
        builder.HasOne(x => x.Taxonomy).WithMany(x => x.Competencies).HasForeignKey(x => x.TaxonomyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Skill).WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Parent).WithMany().HasForeignKey(x => new { x.ParentId, x.TaxonomyId })
            .HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorPrerequisiteConfiguration : IEntityTypeConfiguration<AcceleratorPrerequisite>
{
    public void Configure(EntityTypeBuilder<AcceleratorPrerequisite> builder)
    {
        builder.ToTable("AcceleratorPrerequisites", t => t.HasCheckConstraint("CK_AcceleratorPrerequisite_NotSelf", "\"CompetencyId\" <> \"RequiredCompetencyId\""));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.CompetencyId, x.RequiredCompetencyId }).IsUnique();
        builder.HasOne(x => x.Competency).WithMany(x => x.Prerequisites).HasForeignKey(x => new { x.CompetencyId, x.TaxonomyId })
            .HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequiredCompetency).WithMany().HasForeignKey(x => new { x.RequiredCompetencyId, x.TaxonomyId })
            .HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorRoleRequirementConfiguration : IEntityTypeConfiguration<AcceleratorRoleRequirement>
{
    public void Configure(EntityTypeBuilder<AcceleratorRoleRequirement> builder)
    {
        builder.ToTable("AcceleratorRoleRequirements", t => t.HasCheckConstraint("CK_AcceleratorRequirement_Proficiency", "\"ExpectedProficiency\" BETWEEN 1 AND 5"));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.RoleId, x.CompetencyId }).IsUnique();
        builder.HasOne(x => x.Role).WithMany(x => x.Requirements).HasForeignKey(x => new { x.RoleId, x.TaxonomyId })
            .HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Competency).WithMany().HasForeignKey(x => new { x.CompetencyId, x.TaxonomyId })
            .HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProfileConfiguration : IEntityTypeConfiguration<AcceleratorProfile>
{
    public void Configure(EntityTypeBuilder<AcceleratorProfile> builder)
    {
        builder.ToTable("AcceleratorProfiles", t =>
        {
            t.HasCheckConstraint("CK_AcceleratorProfile_Experience", "\"ExperienceYears\" BETWEEN 0 AND 60");
            t.HasCheckConstraint("CK_AcceleratorProfile_Study", "\"WeeklyStudyMinutes\" BETWEEN 15 AND 10080");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.EducationSummary).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ExperienceYears).HasPrecision(3, 1);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProfileSkillConfiguration : IEntityTypeConfiguration<AcceleratorProfileSkill>
{
    public void Configure(EntityTypeBuilder<AcceleratorProfileSkill> builder)
    {
        builder.ToTable("AcceleratorProfileSkills", t => t.HasCheckConstraint("CK_AcceleratorProfileSkill_Proficiency", "\"SelfReportedProficiency\" BETWEEN 0 AND 5"));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.ProfileId, x.SkillId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.Profile).WithMany(x => x.Skills).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Skill).WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorCareerGoalConfiguration : IEntityTypeConfiguration<AcceleratorCareerGoal>
{
    public void Configure(EntityTypeBuilder<AcceleratorCareerGoal> builder)
    {
        builder.ToTable("AcceleratorCareerGoals", t =>
        {
            t.HasCheckConstraint("CK_AcceleratorGoal_Status", "\"Status\" BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_AcceleratorGoal_Study", "\"WeeklyStudyMinutes\" BETWEEN 15 AND 10080");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc, x.Id });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => new { x.RoleId, x.TaxonomyId })
            .HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorBillingCycleConfiguration : IEntityTypeConfiguration<AcceleratorBillingCycle>
{
    public void Configure(EntityTypeBuilder<AcceleratorBillingCycle> builder)
    {
        builder.ToTable("AcceleratorBillingCycles", t => t.HasCheckConstraint("CK_AcceleratorCycle_Bounds", "\"EndsAtUtc\" > \"StartsAtUtc\""));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.PlanVersion).HasMaxLength(80).IsRequired();
        builder.HasAlternateKey(x => new { x.Id, x.UserId });
        builder.HasIndex(x => x.PaymentId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.StartsAtUtc, x.EndsAtUtc });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorAllowanceConfiguration : IEntityTypeConfiguration<AcceleratorAllowance>
{
    public void Configure(EntityTypeBuilder<AcceleratorAllowance> builder)
    {
        builder.ToTable("AcceleratorAllowances", t => t.HasCheckConstraint("CK_AcceleratorAllowance_Bounds", "\"Allocated\" BETWEEN 1 AND 10000 AND \"Feature\" BETWEEN 1 AND 5"));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.CycleId, x.Feature }).IsUnique();
        builder.HasOne(x => x.Cycle).WithMany(x => x.Allowances).HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorUsageReservationConfiguration : IEntityTypeConfiguration<AcceleratorUsageReservation>
{
    public void Configure(EntityTypeBuilder<AcceleratorUsageReservation> builder)
    {
        builder.ToTable("AcceleratorUsageReservations", t =>
        {
            t.HasCheckConstraint("CK_AcceleratorUsage_State", "\"State\" BETWEEN 1 AND 5 AND \"Feature\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_AcceleratorUsage_Cost", "\"ReservedCostInr\" >= 0 AND (\"ActualCostInr\" IS NULL OR \"ActualCostInr\" >= 0)");
            t.HasCheckConstraint("CK_AcceleratorUsage_Tokens", "\"InputTokens\" >= 0 AND \"OutputTokens\" >= 0 AND \"ReservedInputTokens\" >= 0 AND \"ReservedOutputTokens\" >= 0");
            t.HasCheckConstraint("CK_AcceleratorUsage_Lease", "\"LeaseExpiresAtUtc\" > \"ReservedAtUtc\"");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ReservedCostInr).HasPrecision(18, 6);
        builder.Property(x => x.ActualCostInr).HasPrecision(18, 6);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => new { x.CycleId, x.Feature, x.State });
        builder.HasIndex(x => new { x.BudgetDayUtc, x.State });
        builder.HasIndex(x => new { x.State, x.LeaseExpiresAtUtc });
        builder.HasIndex(x => new { x.UserId, x.ReservedAtUtc });
        builder.HasOne(x => x.Cycle).WithMany().HasForeignKey(x => new { x.CycleId, x.UserId })
            .HasPrincipalKey(x => new { x.Id, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorUsageEventConfiguration : IEntityTypeConfiguration<AcceleratorUsageEvent>
{
    public void Configure(EntityTypeBuilder<AcceleratorUsageEvent> builder)
    {
        builder.ToTable("AcceleratorUsageEvents", t => t.HasCheckConstraint("CK_AcceleratorUsageEvent_State", "\"State\" BETWEEN 1 AND 5 AND \"CostInr\" >= 0"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.CostInr).HasPrecision(18, 6);
        builder.HasIndex(x => new { x.ReservationId, x.State }).IsUnique();
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
    }
}
