using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries", table =>
        {
            table.HasCheckConstraint("CK_NotificationDeliveries_Channel", "\"Channel\" IN (1, 2)");
            table.HasCheckConstraint("CK_NotificationDeliveries_Status", "\"Status\" BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_NotificationDeliveries_Attempts", "\"AttemptCount\" >= 0");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.BusinessKey).HasMaxLength(220).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ActionUrl).HasMaxLength(2048);
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.HasIndex(x => new { x.BusinessKey, x.UserId, x.Channel }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresAtUtc });
        builder.HasIndex(x => new { x.Source, x.SourceId, x.SourceRevision });
        builder.HasIndex(x => new { x.NotificationId, x.Channel });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        // NotificationId is deterministic but the inbox row may not exist until a scheduled delivery is due.
    }
}
