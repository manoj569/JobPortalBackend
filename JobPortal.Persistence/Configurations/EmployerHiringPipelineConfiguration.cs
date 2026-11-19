using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class EmployerHiringPipelineConfiguration
    : IEntityTypeConfiguration<EmployerHiringPipeline>
{
    public void Configure(
        EntityTypeBuilder<EmployerHiringPipeline> builder)
    {
        builder.ToTable("EmployerHiringPipelines");

        builder.ConfigureBaseEntity();

        builder.Property(x => x.ScreeningNotes)
            .HasMaxLength(2000);

   

        builder.Property(x => x.ScreeningScore)
            .HasPrecision(5, 2);

        builder.HasIndex(x => x.JobApplicationId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");

        builder.HasIndex(x => new
        {
            x.Stage,
            x.ScreeningStatus
        });

        builder.HasOne(x => x.JobApplication)
            .WithOne()
            .HasForeignKey<EmployerHiringPipeline>(
                x => x.JobApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ScreenedByUser)
            .WithMany()
            .HasForeignKey(x => x.ScreenedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
