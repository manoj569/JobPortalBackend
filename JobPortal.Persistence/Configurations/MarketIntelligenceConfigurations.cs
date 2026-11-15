using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class MarketSkillConfiguration : IEntityTypeConfiguration<MarketSkill>
{
    public void Configure(EntityTypeBuilder<MarketSkill> builder)
    {
        builder.ToTable("MarketSkills"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.SkillId).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.Category });
        builder.HasOne(x => x.Skill).WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Competency).WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MarketSkill>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketSkillAliasConfiguration : IEntityTypeConfiguration<MarketSkillAlias>
{
    public void Configure(EntityTypeBuilder<MarketSkillAlias> builder)
    {
        builder.ToTable("MarketSkillAliases"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Text).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedText).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => x.NormalizedText).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.MarketSkill).WithMany(x => x.Aliases).HasForeignKey(x => x.MarketSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketRoleConfiguration : IEntityTypeConfiguration<MarketRole>
{
    public void Configure(EntityTypeBuilder<MarketRole> builder)
    {
        builder.ToTable("MarketRoles"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Family).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.Family });
        builder.HasOne(x => x.AcceleratorRole).WithMany().HasForeignKey(x => x.AcceleratorRoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MarketRole>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketRoleAliasConfiguration : IEntityTypeConfiguration<MarketRoleAlias>
{
    public void Configure(EntityTypeBuilder<MarketRoleAlias> builder)
    {
        builder.ToTable("MarketRoleAliases"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Text).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedText).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => x.NormalizedText).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.MarketRole).WithMany(x => x.Aliases).HasForeignKey(x => x.MarketRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketJobSkillConfiguration : IEntityTypeConfiguration<MarketJobSkill>
{
    public void Configure(EntityTypeBuilder<MarketJobSkill> builder)
    {
        builder.ToTable("MarketJobSkills", t => t.HasCheckConstraint("CK_MarketJobSkill_Confidence", "\"Confidence\" BETWEEN 0 AND 1"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Confidence).HasPrecision(4, 3);
        builder.HasIndex(x => new { x.JobId, x.MarketSkillId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.MarketSkillId, x.JobId });
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MarketSkill).WithMany().HasForeignKey(x => x.MarketSkillId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketJobRoleConfiguration : IEntityTypeConfiguration<MarketJobRole>
{
    public void Configure(EntityTypeBuilder<MarketJobRole> builder)
    {
        builder.ToTable("MarketJobRoles", t => t.HasCheckConstraint("CK_MarketJobRole_Confidence", "\"Confidence\" BETWEEN 0 AND 1"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Confidence).HasPrecision(4, 3);
        builder.HasIndex(x => new { x.JobId, x.MarketRoleId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.JobId).IsUnique().HasFilter("\"IsPrimary\" = TRUE AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.MarketRoleId, x.JobId });
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MarketRole).WithMany().HasForeignKey(x => x.MarketRoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketSkillDiscoveryConfiguration : IEntityTypeConfiguration<MarketSkillDiscovery>
{
    public void Configure(EntityTypeBuilder<MarketSkillDiscovery> builder)
    {
        builder.ToTable("MarketSkillDiscoveries"); builder.ConfigureBaseEntity();
        builder.Property(x => x.RawTerm).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedTerm).HasMaxLength(160).IsRequired();
        builder.Property(x => x.SuggestedCategory).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReviewReason).HasMaxLength(1000);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.NormalizedTerm).IsUnique();
        builder.HasIndex(x => new { x.Status, x.LastSeenAtUtc });
        builder.HasOne<MarketSkill>().WithMany().HasForeignKey(x => x.ResolvedMarketSkillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Skill>().WithMany().HasForeignKey(x => x.SuggestedSkillId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class MarketRoleDiscoveryConfiguration : IEntityTypeConfiguration<MarketRoleDiscovery>
{
    public void Configure(EntityTypeBuilder<MarketRoleDiscovery> builder)
    {
        builder.ToTable("MarketRoleDiscoveries"); builder.ConfigureBaseEntity();
        builder.Property(x => x.RawTitle).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedTitle).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ReviewReason).HasMaxLength(1000);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.NormalizedTitle).IsUnique();
        builder.HasIndex(x => new { x.Status, x.LastSeenAtUtc });
        builder.HasOne<MarketRole>().WithMany().HasForeignKey(x => x.ResolvedMarketRoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketDiscoveryObservationConfiguration : IEntityTypeConfiguration<MarketDiscoveryObservation>
{
    public void Configure(EntityTypeBuilder<MarketDiscoveryObservation> builder)
    {
        builder.ToTable("MarketDiscoveryObservations", t => t.HasCheckConstraint("CK_MarketObservation_OneConcept",
            "(\"SkillDiscoveryId\" IS NULL) <> (\"RoleDiscoveryId\" IS NULL)"));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.JobId, x.SkillDiscoveryId }).IsUnique().HasFilter("\"SkillDiscoveryId\" IS NOT NULL");
        builder.HasIndex(x => new { x.JobId, x.RoleDiscoveryId }).IsUnique().HasFilter("\"RoleDiscoveryId\" IS NOT NULL");
        builder.HasIndex(x => new { x.SkillDiscoveryId, x.CompanyId });
        builder.HasIndex(x => new { x.RoleDiscoveryId, x.CompanyId });
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MarketSkillDiscovery>().WithMany().HasForeignKey(x => x.SkillDiscoveryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MarketRoleDiscovery>().WithMany().HasForeignKey(x => x.RoleDiscoveryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MarketJobProcessingStateConfiguration : IEntityTypeConfiguration<MarketJobProcessingState>
{
    public void Configure(EntityTypeBuilder<MarketJobProcessingState> builder)
    {
        builder.ToTable("MarketJobProcessingStates"); builder.ConfigureBaseEntity();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.JobId).IsUnique();
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MarketProcessingCheckpointConfiguration : IEntityTypeConfiguration<MarketProcessingCheckpoint>
{
    public void Configure(EntityTypeBuilder<MarketProcessingCheckpoint> builder)
    {
        builder.ToTable("MarketProcessingCheckpoints"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasIndex(x => x.LeaseExpiresAtUtc);
    }
}
