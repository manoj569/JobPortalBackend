using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorSkillProficiencyPolicyConfiguration : IEntityTypeConfiguration<AcceleratorSkillProficiencyPolicy>
{
    public void Configure(EntityTypeBuilder<AcceleratorSkillProficiencyPolicy> builder)
    {
        builder.ToTable("AcceleratorSkillProficiencyPolicies", t => t.HasCheckConstraint("CK_ProficiencyPolicy_Bounds", "\"Version\" > 0 AND \"MinimumEvidenceConfidence\" BETWEEN 0 AND 1 AND \"CertificateMinimumAssessments\" > 0 AND \"CertificateMinimumScore\" BETWEEN 0 AND 100")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Code).HasMaxLength(80).IsRequired(); builder.Property(x => x.ComponentsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.MinimumEvidenceConfidence).HasPrecision(5,4); builder.Property(x => x.CertificateMinimumScore).HasPrecision(5,2);
        builder.HasIndex(x => new { x.Code, x.Version }).IsUnique(); builder.HasIndex(x => new { x.LearningPathId, x.IsActive });
        builder.HasOne<AcceleratorLearningPath>().WithMany().HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProficiencyBandConfiguration : IEntityTypeConfiguration<AcceleratorProficiencyBand>
{
    public void Configure(EntityTypeBuilder<AcceleratorProficiencyBand> builder)
    {
        builder.ToTable("AcceleratorProficiencyBands", t => t.HasCheckConstraint("CK_ProficiencyBand_Range", "\"MinimumScore\" >= 0 AND \"MaximumScore\" <= 100 AND \"MinimumScore\" < \"MaximumScore\"")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Label).HasMaxLength(80).IsRequired(); builder.Property(x => x.MinimumScore).HasPrecision(5,2); builder.Property(x => x.MaximumScore).HasPrecision(5,2);
        builder.HasIndex(x => new { x.PolicyId, x.MinimumScore }).IsUnique();
        builder.HasOne(x => x.Policy).WithMany(x => x.Bands).HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorAchievementDefinitionConfiguration : IEntityTypeConfiguration<AcceleratorAchievementDefinition>
{
    public void Configure(EntityTypeBuilder<AcceleratorAchievementDefinition> builder)
    {
        builder.ToTable("AcceleratorAchievementDefinitions", t => t.HasCheckConstraint("CK_AchievementDefinition_Threshold", "\"Threshold\" BETWEEN 0 AND 100")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Code).HasMaxLength(80).IsRequired(); builder.Property(x => x.Title).HasMaxLength(250).IsRequired(); builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Threshold).HasPrecision(5,2); builder.HasIndex(x => x.Code).IsUnique();
    }
}
public sealed class CandidateAcceleratorAchievementConfiguration : IEntityTypeConfiguration<CandidateAcceleratorAchievement>
{
    public void Configure(EntityTypeBuilder<CandidateAcceleratorAchievement> builder)
    {
        builder.ToTable("CandidateAcceleratorAchievements"); builder.ConfigureBaseEntity(); builder.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.JourneyContextId, x.AchievementDefinitionId }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.AwardedAtUtc });
        builder.HasOne<AcceleratorJourneyContext>().WithMany().HasForeignKey(x => x.JourneyContextId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorAchievementDefinition>().WithMany().HasForeignKey(x => x.AchievementDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobPortal.Domain.Entities.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorEnrollmentCertificateConfiguration : IEntityTypeConfiguration<AcceleratorEnrollmentCertificate>
{
    public void Configure(EntityTypeBuilder<AcceleratorEnrollmentCertificate> builder)
    {
        builder.ToTable("AcceleratorEnrollmentCertificates", t => t.HasCheckConstraint("CK_Certificate_Artifact", "\"State\" <> 4 OR (\"IssuedAtUtc\" IS NOT NULL AND \"CertificateIdentifier\" IS NOT NULL AND \"ArtifactStorageKey\" IS NOT NULL)")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Revision).IsConcurrencyToken(); builder.Property(x => x.CertificateIdentifier).HasMaxLength(150); builder.Property(x => x.ArtifactStorageKey).HasMaxLength(1024);
        builder.HasIndex(x => x.JourneyContextId).IsUnique(); builder.HasIndex(x => new { x.UserId, x.State });
        builder.HasOne<AcceleratorJourneyContext>().WithMany().HasForeignKey(x => x.JourneyContextId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorSkillProficiencyPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobPortal.Domain.Entities.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
