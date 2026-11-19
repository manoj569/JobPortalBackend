using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class EmployerOfferConfiguration
    : IEntityTypeConfiguration<EmployerOffer>
{
    public void Configure(
        EntityTypeBuilder<EmployerOffer> builder)
    {
        builder.ToTable(
            "EmployerOffers",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_EmployerOffers_AnnualSalary",
                    "\"AnnualSalary\" IS NULL OR " +
                    "\"AnnualSalary\" >= 0");
            });

        builder.ConfigureBaseEntity();

        builder.Property(x => x.JobTitle)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.AnnualSalary)
            .HasPrecision(18, 2);

        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.WorkLocation)
            .HasMaxLength(300);

        builder.Property(x => x.Notes)
            .HasMaxLength(4000);

        builder.Property(x => x.OfferLetterUrl)
            .HasMaxLength(2048);

        builder.HasIndex(x => x.JobApplicationId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");

        builder.HasIndex(x => new
        {
            x.Status,
            x.ExpiresAtUtc
        });

        builder.HasOne(x => x.JobApplication)
            .WithMany()
            .HasForeignKey(x =>
                x.JobApplicationId)
            .OnDelete(
                DeleteBehavior.Restrict);

        builder.HasOne(x => x.Pipeline)
            .WithMany()
            .HasForeignKey(x =>
                x.PipelineId)
            .OnDelete(
                DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x =>
                x.CreatedByUserId)
            .OnDelete(
                DeleteBehavior.Restrict);
    }
}
