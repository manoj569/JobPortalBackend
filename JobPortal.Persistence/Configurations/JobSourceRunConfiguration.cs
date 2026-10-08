using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class JobSourceRunConfiguration : IEntityTypeConfiguration<JobSourceRun>
{
    public void Configure(EntityTypeBuilder<JobSourceRun> builder)
    {
        builder.ToTable("JobSourceRuns", table =>
        {
            table.HasCheckConstraint("CK_JobSourceRuns_Status", "\"Status\" BETWEEN 0 AND 4");
            table.HasCheckConstraint("CK_JobSourceRuns_Attempts", "\"AttemptCount\" BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_JobSourceRuns_Lease", "(\"Status\" = 1 AND \"LeaseOwner\" IS NOT NULL AND \"LeaseExpiresAtUtc\" IS NOT NULL) OR (\"Status\" <> 1 AND \"LeaseOwner\" IS NULL AND \"LeaseExpiresAtUtc\" IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Phase).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(64);
        builder.HasOne<JobSource>().WithMany().HasForeignKey(x => x.JobSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.JobSourceId).IsUnique().HasDatabaseName("UX_JobSourceRuns_ActiveSource")
            .HasFilter("\"Status\" IN (0, 1) OR (\"Status\" = 4 AND \"AttemptCount\" < 3)");
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresAtUtc });
        builder.HasIndex(x => new { x.JobSourceId, x.QueuedAtUtc });
    }
}
