using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class CareerConsultantConfiguration : IEntityTypeConfiguration<CareerConsultant>
{
    public void Configure(EntityTypeBuilder<CareerConsultant> builder)
    {
        builder.ToTable("CareerConsultants");
        builder.ConfigureBaseEntity();
        builder.HasOne(x => x.User).WithOne().HasForeignKey<CareerConsultant>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ProfessionalHeadline).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Bio).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrentRole).HasMaxLength(160).IsRequired();
        builder.Property(x => x.YearsOfExperience).HasPrecision(4, 1);
        builder.Property(x => x.LinkedInUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.VerificationMethod).HasMaxLength(50);
        builder.Property(x => x.VerificationReason).HasMaxLength(1000);
        builder.Property(x => x.PolicyVersion).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.VerificationStatus, x.CreatedAtUtc, x.Id });
        builder.HasIndex(x => new { x.ProfessionalType, x.YearsOfExperience });
    }
}

public sealed class CareerConsultantTagConfiguration : IEntityTypeConfiguration<CareerConsultantTag>
{
    public void Configure(EntityTypeBuilder<CareerConsultantTag> builder)
    {
        builder.ToTable("CareerConsultantTags");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Value).HasMaxLength(60).IsRequired();
        builder.HasOne(x => x.Consultant).WithMany(x => x.Tags).HasForeignKey(x => x.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ConsultantId, x.Kind, x.Value }).IsUnique();
        builder.HasIndex(x => new { x.Kind, x.Value, x.ConsultantId });
    }
}

public sealed class CareerConsultantServiceConfiguration : IEntityTypeConfiguration<CareerConsultantService>
{
    public void Configure(EntityTypeBuilder<CareerConsultantService> builder)
    {
        builder.ToTable("CareerConsultantServices", table =>
        {
            table.HasCheckConstraint("CK_CareerConsultantServices_Price", "\"Price\" > 0 AND \"Price\" <= 1000000");
            table.HasCheckConstraint("CK_CareerConsultantServices_Duration", "\"DurationMinutes\" >= 15 AND \"DurationMinutes\" <= 180");
        });
        builder.ConfigureBaseEntity();
        builder.HasOne(x => x.Consultant).WithMany(x => x.Services).HasForeignKey(x => x.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ServiceType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(3000).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(x => new { x.ConsultantId, x.IsActive });
        builder.HasIndex(x => new { x.ServiceType, x.Currency, x.Price });
    }
}
