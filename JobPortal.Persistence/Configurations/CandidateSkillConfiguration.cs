using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class CandidateSkillConfiguration : IEntityTypeConfiguration<CandidateSkill>
{
    public void Configure(EntityTypeBuilder<CandidateSkill> builder)
    {
        builder.ToTable("CandidateSkills", table =>
        {
            table.HasCheckConstraint("CK_CandidateSkills_YearsOfExperience",
                "\"YearsOfExperience\" IS NULL OR (\"YearsOfExperience\" >= 0 AND \"YearsOfExperience\" <= 50)");
            table.HasCheckConstraint("CK_CandidateSkills_Proficiency",
                "\"Proficiency\" IS NULL OR \"Proficiency\" BETWEEN 1 AND 4");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.YearsOfExperience).HasPrecision(4, 1);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.UserId, x.NormalizedName })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.User)
            .WithMany(x => x.CandidateSkills)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
