using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class CareerTrustReviewConfiguration : IEntityTypeConfiguration<CareerGuidanceReview>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceReview> builder)
    {
        var b = builder;
        b.ConfigureBaseEntity();
        b.ToTable("CareerGuidanceReviews", t =>
        {
            t.HasCheckConstraint("CK_CGReview_Rating", "\"Rating\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_CGReview_Moderation", "\"ModerationStatus\" BETWEEN 1 AND 4 AND (NOT \"IsPublished\" OR (\"ModerationStatus\" = 2 AND NOT \"IsDeleted\"))");
        });
        b.HasOne(r => r.Booking).WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Session).WithMany().HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Payment).WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareerConsultant>().WithMany().HasForeignKey(r => r.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(r => r.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => r.BookingId).IsUnique();
        b.HasIndex(r => new { r.ConsultantId, r.ModerationStatus, r.CreatedAtUtc });
        b.Property(r => r.Title).HasMaxLength(120); b.Property(r => r.Comment).HasMaxLength(2000); b.Property(r => r.ModerationReason).HasMaxLength(1000);
        b.Property(r => r.Revision).IsConcurrencyToken();
    }
}
public sealed class CareerTrustDisputeConfiguration : IEntityTypeConfiguration<CareerGuidanceDispute>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceDispute> builder)
    {
        var b = builder;
        b.ConfigureBaseEntity();
        b.ToTable("CareerGuidanceDisputes", t =>
        {
            t.HasCheckConstraint("CK_CGDispute_Enums", "\"Status\" BETWEEN 1 AND 7 AND \"Category\" BETWEEN 1 AND 8 AND \"Resolution\" BETWEEN 1 AND 8");
            t.HasCheckConstraint("CK_CGDispute_Description", "length(btrim(\"Description\")) > 0");
            t.HasCheckConstraint("CK_CGDispute_Resolution", "(\"Status\" <= 4 AND \"Resolution\" = 1 AND \"ResolvedAtUtc\" IS NULL AND \"ResolvedByUserId\" IS NULL) OR (\"Status\" >= 5 AND \"Resolution\" > 1 AND \"ResolvedAtUtc\" IS NOT NULL AND \"ResolvedByUserId\" IS NOT NULL)");
        });
        b.HasOne<CareerGuidanceBooking>().WithMany().HasForeignKey(d => d.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareerGuidanceSession>().WithMany().HasForeignKey(d => d.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(d => d.Payment).WithMany().HasForeignKey(d => d.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareerConsultant>().WithMany().HasForeignKey(d => d.ConsultantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(d => d.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(d => d.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(d => d.BookingId).IsUnique(); // one lifetime case, stronger than one active case
        b.HasIndex(d => new { d.PaymentId, d.Status });
        b.HasIndex(d => new { d.Status, d.CreatedAtUtc });
        b.HasIndex(d => new { d.ConsultantId, d.Status, d.CreatedAtUtc });
        b.HasIndex(d => new { d.CandidateUserId, d.Status, d.CreatedAtUtc });
        b.Property(d => d.Description).HasMaxLength(4000); b.Property(d => d.AdminNotes).HasMaxLength(2000);
        b.Property(d => d.Revision).IsConcurrencyToken();
    }
}
public sealed class CareerTrustEvidenceConfiguration : IEntityTypeConfiguration<CareerGuidanceDisputeEvidence>
{
    public void Configure(EntityTypeBuilder<CareerGuidanceDisputeEvidence> builder)
    {
        var b = builder;
        b.ConfigureBaseEntity();
        b.ToTable("CareerGuidanceDisputeEvidence", t =>
        {
            t.HasCheckConstraint("CK_CGEvidence_Type", "\"EvidenceType\" BETWEEN 1 AND 3 AND (\"EvidenceType\" <> 3 OR \"IsPrivateToAdmin\")");
            t.HasCheckConstraint("CK_CGEvidence_Description", "length(btrim(\"Description\")) > 0");
        });
        b.HasOne(e => e.Dispute).WithMany(d => d.Evidence).HasForeignKey(e => e.DisputeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(e => e.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.DisputeId, e.RequestId }).IsUnique();
        b.Property(e => e.Description).HasMaxLength(4000);
    }
}
