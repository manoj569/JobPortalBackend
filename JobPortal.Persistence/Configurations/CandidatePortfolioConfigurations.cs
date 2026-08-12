using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable CA1725

namespace JobPortal.Persistence.Configurations;

public sealed class CandidatePortfolioConfiguration : IEntityTypeConfiguration<CandidatePortfolio>
{
    public void Configure(EntityTypeBuilder<CandidatePortfolio> builder)
    {
        builder.ToTable("CandidatePortfolios", table =>
        {
            table.HasCheckConstraint("CK_CandidatePortfolios_Status", "\"Status\" BETWEEN 1 AND 2");
            table.HasCheckConstraint("CK_CandidatePortfolios_Template", "\"Template\" BETWEEN 1 AND 2");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        builder.Property(x => x.NormalizedSlug).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.NormalizedSlug).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.NormalizedSlug });
        builder.HasOne(x => x.User).WithOne(x => x.CandidatePortfolio)
            .HasForeignKey<CandidatePortfolio>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PortfolioSectionSettingConfiguration : IEntityTypeConfiguration<PortfolioSectionSetting>
{
    public void Configure(EntityTypeBuilder<PortfolioSectionSetting> builder)
    {
        builder.ToTable("PortfolioSectionSettings", table =>
            table.HasCheckConstraint("CK_PortfolioSectionSettings_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.PortfolioId, x.SectionType }).IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.PortfolioId, x.DisplayOrder });
        builder.HasOne(x => x.Portfolio).WithMany(x => x.SectionSettings)
            .HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public abstract class CandidateOwnedConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : JobPortal.Domain.Common.BaseEntity
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.ConfigureBaseEntity();
        ConfigureOwned(builder);
        ConfigureEntity(builder);
    }

    protected abstract void ConfigureOwned(EntityTypeBuilder<TEntity> builder);
    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
}

public sealed class CandidateExperienceConfiguration : CandidateOwnedConfiguration<CandidateExperience>
{
    protected override void ConfigureOwned(EntityTypeBuilder<CandidateExperience> b) =>
        b.HasOne(x => x.User).WithMany(x => x.CandidateExperiences).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    protected override void ConfigureEntity(EntityTypeBuilder<CandidateExperience> b)
    {
        b.ToTable("CandidateExperiences", t => t.HasCheckConstraint("CK_CandidateExperiences_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.Property(x => x.JobTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.HasIndex(x => new { x.UserId, x.DisplayOrder });
    }
}

public sealed class CandidateEducationConfiguration : CandidateOwnedConfiguration<CandidateEducation>
{
    protected override void ConfigureOwned(EntityTypeBuilder<CandidateEducation> b) =>
        b.HasOne(x => x.User).WithMany(x => x.CandidateEducation).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    protected override void ConfigureEntity(EntityTypeBuilder<CandidateEducation> b)
    {
        b.ToTable("CandidateEducation", t => t.HasCheckConstraint("CK_CandidateEducation_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.Property(x => x.Qualification).HasMaxLength(200).IsRequired();
        b.Property(x => x.Institution).HasMaxLength(250).IsRequired();
        b.Property(x => x.FieldOfStudy).HasMaxLength(200);
        b.Property(x => x.Grade).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.HasIndex(x => new { x.UserId, x.DisplayOrder });
    }
}

public sealed class CandidateProjectConfiguration : CandidateOwnedConfiguration<CandidateProject>
{
    protected override void ConfigureOwned(EntityTypeBuilder<CandidateProject> b) =>
        b.HasOne(x => x.User).WithMany(x => x.CandidateProjects).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    protected override void ConfigureEntity(EntityTypeBuilder<CandidateProject> b)
    {
        b.ToTable("CandidateProjects", t => t.HasCheckConstraint("CK_CandidateProjects_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Role).HasMaxLength(150);
        b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        b.Property(x => x.TechnologiesJson).HasColumnType("text").IsRequired();
        b.Property(x => x.SourceUrl).HasMaxLength(2048);
        b.Property(x => x.LiveUrl).HasMaxLength(2048);
        b.HasIndex(x => new { x.UserId, x.DisplayOrder });
    }
}

public sealed class CandidateCertificationConfiguration : CandidateOwnedConfiguration<CandidateCertification>
{
    protected override void ConfigureOwned(EntityTypeBuilder<CandidateCertification> b) =>
        b.HasOne(x => x.User).WithMany(x => x.CandidateCertifications).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    protected override void ConfigureEntity(EntityTypeBuilder<CandidateCertification> b)
    {
        b.ToTable("CandidateCertifications", t => t.HasCheckConstraint("CK_CandidateCertifications_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Issuer).HasMaxLength(200);
        b.Property(x => x.CredentialId).HasMaxLength(200);
        b.Property(x => x.CredentialUrl).HasMaxLength(2048);
        b.HasIndex(x => new { x.UserId, x.DisplayOrder });
    }
}

public sealed class CandidateProfessionalLinkConfiguration : CandidateOwnedConfiguration<CandidateProfessionalLink>
{
    protected override void ConfigureOwned(EntityTypeBuilder<CandidateProfessionalLink> b) =>
        b.HasOne(x => x.User).WithMany(x => x.CandidateProfessionalLinks).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    protected override void ConfigureEntity(EntityTypeBuilder<CandidateProfessionalLink> b)
    {
        b.ToTable("CandidateProfessionalLinks", t => t.HasCheckConstraint("CK_CandidateProfessionalLinks_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.Property(x => x.Label).HasMaxLength(100);
        b.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        b.HasIndex(x => new { x.UserId, x.DisplayOrder });
    }
}

public sealed class PortfolioCustomSectionConfiguration : CandidateOwnedConfiguration<PortfolioCustomSection>
{
    protected override void ConfigureOwned(EntityTypeBuilder<PortfolioCustomSection> b) =>
        b.HasOne(x => x.User).WithMany(x => x.PortfolioCustomSections).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    protected override void ConfigureEntity(EntityTypeBuilder<PortfolioCustomSection> b)
    {
        b.ToTable("PortfolioCustomSections", t => t.HasCheckConstraint("CK_PortfolioCustomSections_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.UserId, x.DisplayOrder });
    }
}

public sealed class PortfolioCustomItemConfiguration : IEntityTypeConfiguration<PortfolioCustomItem>
{
    public void Configure(EntityTypeBuilder<PortfolioCustomItem> b)
    {
        b.ToTable("PortfolioCustomItems", t => t.HasCheckConstraint("CK_PortfolioCustomItems_DisplayOrder", "\"DisplayOrder\" BETWEEN 0 AND 1000"));
        b.ConfigureBaseEntity();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Url).HasMaxLength(2048);
        b.HasIndex(x => new { x.SectionId, x.DisplayOrder });
        b.HasOne(x => x.Section).WithMany(x => x.Items).HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
