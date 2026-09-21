using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class CareerPaymentConfiguration : IEntityTypeConfiguration<CareerGuidancePayment>
{
    public void Configure(EntityTypeBuilder<CareerGuidancePayment> builder)
    {
        builder.ConfigureBaseEntity();
        builder.ToTable("CareerGuidancePayments", t =>
        {
            t.HasCheckConstraint("CK_CGPayment_Amounts", "\"AmountGross\" > 0 AND \"PlatformCommissionAmount\" >= 0 AND \"ConsultantNetAmount\" >= 0 AND \"AmountGross\" = \"PlatformCommissionAmount\" + \"ConsultantNetAmount\"");
            t.HasCheckConstraint("CK_CGPayment_Commission", "\"PlatformCommissionPercentSnapshot\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_CGPayment_Status", "\"Status\" BETWEEN 1 AND 6");
        });
        builder.HasOne(p => p.Booking).WithOne().HasForeignKey<CareerGuidancePayment>(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Consultant).WithMany().HasForeignKey(p => p.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.BookingId).IsUnique();
        builder.HasIndex(p => p.ProviderOrderId).IsUnique().HasFilter("\"ProviderOrderId\" IS NOT NULL");
        builder.HasIndex(p => p.ProviderPaymentId).IsUnique().HasFilter("\"ProviderPaymentId\" IS NOT NULL");
        builder.HasIndex(p => new { p.CandidateUserId, p.Status, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.Status, p.RequiresRefundReview, p.CreatedAtUtc });
        builder.Property(p => p.Provider).HasMaxLength(30);
        builder.Property(p => p.ProviderOrderId).HasMaxLength(100);
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(100);
        builder.Property(p => p.Currency).HasMaxLength(3);
        builder.Property(p => p.FailureCode).HasMaxLength(60);
        builder.Property(p => p.RefundPolicyVersion).HasMaxLength(40);
        builder.Property(p => p.AmountGross).HasPrecision(18, 2);
        builder.Property(p => p.PlatformCommissionAmount).HasPrecision(18, 2);
        builder.Property(p => p.ConsultantNetAmount).HasPrecision(18, 2);
        builder.Property(p => p.PlatformCommissionPercentSnapshot).HasPrecision(7, 4);
        builder.Property(p => p.Revision).IsConcurrencyToken();
    }
}
public sealed class CareerPaymentEventConfiguration : IEntityTypeConfiguration<CareerGuidancePaymentEvent>
{
    public void Configure(EntityTypeBuilder<CareerGuidancePaymentEvent> builder)
    {
        builder.ConfigureBaseEntity(); builder.ToTable("CareerGuidancePaymentEvents");
        builder.HasOne(p => p.Payment).WithMany().HasForeignKey(p => p.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.EventKey).HasMaxLength(64);
        builder.Property(p => p.EventType).HasMaxLength(60);
        builder.HasIndex(p => p.EventKey).IsUnique();
    }
}
public sealed class CareerEarningConfiguration : IEntityTypeConfiguration<CareerGuidanceEarning>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceEarning> builder)
    {
        builder.ConfigureBaseEntity();
        builder.ToTable("CareerGuidanceEarnings", t =>
        {
            t.HasCheckConstraint("CK_CGEarning_Amounts", "\"GrossAmount\" > 0 AND \"PlatformCommissionAmount\" >= 0 AND \"NetAmount\" >= 0 AND \"GrossAmount\" = \"PlatformCommissionAmount\" + \"NetAmount\"");
            t.HasCheckConstraint("CK_CGEarning_Status", "\"Status\" BETWEEN 1 AND 4");
        });
        builder.HasOne(e => e.Payment).WithOne(p => p.Earning).HasForeignKey<CareerGuidanceEarning>(e => e.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CareerGuidanceBooking>().WithMany().HasForeignKey(e => e.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Consultant).WithMany().HasForeignKey(e => e.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.PaymentId).IsUnique();
        builder.HasIndex(e => new { e.ConsultantId, e.Status, e.CreatedAtUtc });
        builder.HasIndex(e => new { e.Status, e.CreatedAtUtc });
        builder.Property(e => e.GrossAmount).HasPrecision(18, 2);
        builder.Property(e => e.PlatformCommissionAmount).HasPrecision(18, 2);
        builder.Property(e => e.NetAmount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.SettlementReference).HasMaxLength(100);
        builder.Property(e => e.Revision).IsConcurrencyToken();
    }
}
public sealed class CareerRefundConfiguration : IEntityTypeConfiguration<CareerGuidanceRefund>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceRefund> builder)
    {
        builder.ConfigureBaseEntity();
        builder.ToTable("CareerGuidanceRefunds", t =>
        {
            t.HasCheckConstraint("CK_CGRefund_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_CGRefund_Status", "\"Status\" BETWEEN 1 AND 4 AND \"ReasonCode\" BETWEEN 1 AND 4");
        });
        builder.HasOne(r => r.Payment).WithOne(p => p.Refund).HasForeignKey<CareerGuidanceRefund>(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CareerGuidanceBooking>().WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CareerConsultant>().WithMany().HasForeignKey(r => r.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.PaymentId).IsUnique();
        builder.HasIndex(r => r.ProviderRefundId).IsUnique().HasFilter("\"ProviderRefundId\" IS NOT NULL");
        builder.HasIndex(r => new { r.CandidateUserId, r.Status, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc });
        builder.Property(r => r.ProviderRefundId).HasMaxLength(100);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.Revision).IsConcurrencyToken();
    }
}
