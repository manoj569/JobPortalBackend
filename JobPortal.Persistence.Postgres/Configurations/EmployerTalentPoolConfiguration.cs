using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Postgres.Configurations;

public sealed class EmployerTalentPoolConfiguration
    : IEntityTypeConfiguration<EmployerTalentPool>
{
    public void Configure(
        EntityTypeBuilder<EmployerTalentPool> builder)
    {
        builder.ToTable("EmployerTalentPool");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Notes)
            .HasMaxLength(2000);

        builder.Property(x => x.AddedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.EmployerUserId,
            x.CandidateUserId
        })
        .IsUnique()
        .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(x => new
        {
            x.EmployerUserId,
            x.AddedAtUtc
        });

        builder.HasOne(x => x.EmployerUser)
            .WithMany()
            .HasForeignKey(x => x.EmployerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CandidateUser)
            .WithMany()
            .HasForeignKey(x => x.CandidateUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
