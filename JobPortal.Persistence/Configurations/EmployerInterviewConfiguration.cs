using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class EmployerInterviewConfiguration
    : IEntityTypeConfiguration<EmployerInterview>
{
    public void Configure(
        EntityTypeBuilder<EmployerInterview> builder)
    {
        builder.ToTable(
            "EmployerInterviews",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_EmployerInterviews_RoundNumber",
                    "\"RoundNumber\" > 0");

                table.HasCheckConstraint(
                    "CK_EmployerInterviews_DurationMinutes",
                    "\"DurationMinutes\" BETWEEN 10 AND 480");

                table.HasCheckConstraint(
                    "CK_EmployerInterviews_Score",
                    "\"Score\" IS NULL OR " +
                    "(\"Score\" >= 0 AND \"Score\" <= 100)");
            });

        builder.ConfigureBaseEntity();

        builder.Property(x => x.Title)
            .HasMaxLength(160);

        builder.Property(x => x.MeetingUrl)
            .HasMaxLength(2048);

        builder.Property(x => x.Location)
            .HasMaxLength(500);

        builder.Property(x => x.InterviewerName)
            .HasMaxLength(160);

        builder.Property(x => x.InterviewerEmail)
            .HasMaxLength(256);

        builder.Property(x => x.Score)
            .HasPrecision(5, 2);

        builder.Property(x => x.Feedback)
            .HasMaxLength(4000);

        builder.Property(x => x.InternalNotes)
            .HasMaxLength(2000);

        builder.HasIndex(x => new
        {
            x.JobApplicationId,
            x.RoundNumber
        })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");

        builder.HasIndex(x => new
        {
            x.Status,
            x.ScheduledAtUtc
        });

        builder.HasIndex(x => new
        {
            x.PipelineId,
            x.Status
        });

        builder.HasOne(x => x.JobApplication)
            .WithMany()
            .HasForeignKey(x => x.JobApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Pipeline)
            .WithMany()
            .HasForeignKey(x => x.PipelineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
