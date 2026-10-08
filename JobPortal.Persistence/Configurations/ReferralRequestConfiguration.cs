using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class ReferralRequestConfiguration : IEntityTypeConfiguration<ReferralRequest>
{
    public void Configure(EntityTypeBuilder<ReferralRequest> builder)
    {
        builder.ToTable("ReferralRequests", table =>
        {
            table.HasCheckConstraint("CK_ReferralRequests_Status", "\"Status\" BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_ReferralRequests_Expiry", "\"ExpiresAtUtc\" = \"RequestedAtUtc\" + INTERVAL '48 hours'");
            table.HasCheckConstraint("CK_ReferralRequests_Acceptance", "(\"Status\" IN (1, 3, 4) AND \"AcceptedAtUtc\" IS NULL AND \"AcceptedMembershipId\" IS NULL AND \"QuotaPeriodStartUtc\" IS NULL AND \"QuotaPeriodEndUtc\" IS NULL) OR (\"Status\" IN (2, 5, 6) AND \"AcceptedAtUtc\" IS NOT NULL AND \"AcceptedMembershipId\" IS NOT NULL AND \"QuotaPeriodStartUtc\" IS NOT NULL AND \"QuotaPeriodEndUtc\" IS NOT NULL AND \"QuotaPeriodEndUtc\" > \"QuotaPeriodStartUtc\")");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Property(x => x.CandidateMessage).HasMaxLength(2000);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.ReferralSubmissionReference).HasMaxLength(250);
        builder.HasIndex(x => new { x.CandidateUserId, x.JobReferralId }).IsUnique();
        builder.HasIndex(x => new { x.ReferrerUserId, x.Status });
        builder.HasIndex(x => new { x.CandidateUserId, x.Status });
        builder.HasIndex(x => new { x.JobReferralId, x.Status });
        builder.HasIndex(x => new { x.CandidateUserId, x.AcceptedMembershipId, x.QuotaPeriodStartUtc });
        builder.HasOne(x => x.JobReferral).WithMany().HasForeignKey(x => x.JobReferralId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CandidateUser).WithMany().HasForeignKey(x => x.CandidateUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReferrerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(x => x.AcceptedMembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}
