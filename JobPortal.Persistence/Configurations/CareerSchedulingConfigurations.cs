using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class CareerAvailabilityConfiguration : IEntityTypeConfiguration<CareerConsultantAvailability>
{
    public void Configure(EntityTypeBuilder<CareerConsultantAvailability> builder)
    {
        var b = builder;
        b.ToTable("CareerConsultantAvailability", t =>
        {
            t.HasCheckConstraint("CK_CareerAvailability_Day", "\"DayOfWeek\" BETWEEN 0 AND 6");
            t.HasCheckConstraint("CK_CareerAvailability_Time", "\"StartTime\" < \"EndTime\"");
        });
        b.ConfigureBaseEntity();
        b.HasOne(x => x.Consultant).WithMany().HasForeignKey(x => x.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ConsultantId, x.DayOfWeek, x.IsActive });
    }
}

public sealed class CareerAvailabilityExceptionConfiguration : IEntityTypeConfiguration<CareerConsultantAvailabilityException>
{
    public void Configure(EntityTypeBuilder<CareerConsultantAvailabilityException> builder)
    {
        var b = builder;
        b.ToTable("CareerConsultantAvailabilityExceptions", t => t.HasCheckConstraint("CK_CareerAvailabilityException_Time",
            "(\"StartTime\" IS NULL AND \"EndTime\" IS NULL) OR (\"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"StartTime\" < \"EndTime\")"));
        b.ConfigureBaseEntity();
        b.HasOne(x => x.Consultant).WithMany().HasForeignKey(x => x.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ConsultantId, x.LocalDate });
    }
}

public sealed class CareerBookingConfiguration : IEntityTypeConfiguration<CareerGuidanceBooking>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceBooking> builder)
    {
        var b = builder;
        b.ToTable("CareerGuidanceBookings", t =>
        {
            t.HasCheckConstraint("CK_CareerBooking_Time", "\"StartUtc\" < \"EndUtc\"");
            t.HasCheckConstraint("CK_CareerBooking_Status", "\"Status\" BETWEEN 1 AND 8");
            t.HasCheckConstraint("CK_CareerBooking_Price", "\"PriceSnapshot\" > 0");
            t.HasCheckConstraint("CK_CareerBooking_Duration", "\"DurationMinutesSnapshot\" BETWEEN 15 AND 180");
        });
        b.ConfigureBaseEntity();
        b.HasOne(x => x.Consultant).WithMany().HasForeignKey(x => x.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ConsultantServiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ConsultantTimeZoneSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.ServiceTitleSnapshot).HasMaxLength(160).IsRequired();
        b.Property(x => x.ServiceTypeSnapshot).HasMaxLength(60).IsRequired();
        b.Property(x => x.PriceSnapshot).HasPrecision(18, 2);
        b.Property(x => x.CurrencySnapshot).HasMaxLength(3).IsRequired();
        b.Property(x => x.TargetCompany).HasMaxLength(200);
        b.Property(x => x.TargetRole).HasMaxLength(200);
        b.Property(x => x.YearsOfExperience).HasPrecision(4, 1);
        b.Property(x => x.CurrentRoleOrStatus).HasMaxLength(200);
        b.Property(x => x.SessionGoal).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Questions).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CancellationReason).HasMaxLength(1000);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.ConsultantId, x.Status, x.StartUtc, x.EndUtc });
        b.HasIndex(x => new { x.CandidateUserId, x.Status, x.StartUtc });
    }
}
