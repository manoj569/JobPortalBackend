using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class CareerSessionConfiguration : IEntityTypeConfiguration<CareerGuidanceSession>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceSession> builder)
    {
        builder.ConfigureBaseEntity();
        builder.ToTable("CareerGuidanceSessions", table =>
        {
            table.HasCheckConstraint("CK_CGSession_Interval", "\"ScheduledStartUtc\" < \"ScheduledEndUtc\"");
            table.HasCheckConstraint("CK_CGSession_Status", "\"Status\" BETWEEN 1 AND 7");
            table.HasCheckConstraint("CK_CGSession_ReleaseDelay", "\"EarningReleaseDelayHours\" BETWEEN 24 AND 2160");
        });
        builder.HasOne(s => s.Booking).WithOne().HasForeignKey<CareerGuidanceSession>(s => s.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CareerConsultant>().WithMany().HasForeignKey(s => s.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.BookingId).IsUnique();
        builder.HasIndex(s => new { s.MeetingProvider, s.ProviderMeetingId }).IsUnique().HasFilter("\"ProviderMeetingId\" IS NOT NULL");
        builder.HasIndex(s => new { s.CandidateUserId, s.Status, s.ScheduledStartUtc });
        builder.HasIndex(s => new { s.ConsultantId, s.Status, s.ScheduledStartUtc });
        builder.Property(s => s.MeetingProvider).HasMaxLength(30);
        builder.Property(s => s.ProviderMeetingId).HasMaxLength(200);
        builder.Property(s => s.Revision).IsConcurrencyToken();
    }
}
public sealed class CareerSessionReminderConfiguration : IEntityTypeConfiguration<CareerGuidanceSessionReminder>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceSessionReminder> builder)
    {
        builder.ConfigureBaseEntity();
        builder.ToTable("CareerGuidanceSessionReminders", table =>
        {
            table.HasCheckConstraint("CK_CGReminder_Status", "\"Status\" BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_CGReminder_Offset", "\"OffsetMinutes\" BETWEEN 1 AND 10080");
        });
        builder.HasOne(r => r.Session).WithMany(s => s.Reminders).HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.SessionId, r.RecipientUserId, r.OffsetMinutes }).IsUnique();
        builder.HasIndex(r => new { r.Status, r.ScheduledForUtc });
        builder.Property(r => r.Revision).IsConcurrencyToken();
    }
}
