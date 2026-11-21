using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorJourneyProvisioningConfiguration : IEntityTypeConfiguration<AcceleratorJourneyProvisioningReceipt>
{
    public void Configure(EntityTypeBuilder<AcceleratorJourneyProvisioningReceipt> builder)
    {
        builder.ToTable("AcceleratorJourneyProvisioningReceipts",t=>
        {
            t.HasCheckConstraint("CK_JourneyProvisioning_Source","(\"MarketRoleId\" IS NOT NULL AND \"MarketSkillId\" IS NULL) OR (\"MarketRoleId\" IS NULL AND \"MarketSkillId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_JourneyProvisioning_Coverage","\"RequiredCompetencies\" > 0 AND \"AssessedCompetencies\" > 0 AND \"AssessedCompetencies\" <= \"RequiredCompetencies\"");
        });
        builder.ConfigureBaseEntity();
        builder.HasIndex(x=>new { x.UserId,x.RequestKey }).IsUnique();
        builder.Property(x=>x.PolicyVersion).HasMaxLength(80).IsRequired();
        builder.Property(x=>x.UnassessedCompetencyIdsJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MarketRole>().WithMany().HasForeignKey(x=>x.MarketRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MarketSkill>().WithMany().HasForeignKey(x=>x.MarketSkillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorLearningPath>().WithMany().HasForeignKey(x=>x.LearningPathId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorPathEnrollment>().WithMany().HasForeignKey(x=>x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorJourneyContext>().WithMany().HasForeignKey(x=>x.JourneyContextId).OnDelete(DeleteBehavior.Restrict);
    }
}
