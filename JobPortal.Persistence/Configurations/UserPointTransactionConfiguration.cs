using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class UserPointTransactionConfiguration : IEntityTypeConfiguration<UserPointTransaction>
{
    public void Configure(EntityTypeBuilder<UserPointTransaction> builder)
    {
        builder.ToTable("UserPointTransactions", table => table.HasCheckConstraint(
            "CK_UserPointTransactions_ValidReward",
            "(\"Reason\" = 'InterviewInsightHelpful' AND \"Points\" = 10 AND \"InsightHelpfulnessFeedbackId\" IS NOT NULL AND \"InterviewInsightId\" IS NULL AND \"JobReferralId\" IS NULL AND \"ReferralRequestId\" IS NULL) OR " +
            "(\"Reason\" = 'InterviewInsightPublished' AND \"Points\" = 5 AND \"InsightHelpfulnessFeedbackId\" IS NULL AND \"InterviewInsightId\" IS NOT NULL AND \"JobReferralId\" IS NULL AND \"ReferralRequestId\" IS NULL) OR " +
            "(\"Reason\" = 'ReferralOpportunityShared' AND \"Points\" = 5 AND \"InsightHelpfulnessFeedbackId\" IS NULL AND \"InterviewInsightId\" IS NULL AND \"JobReferralId\" IS NOT NULL AND \"ReferralRequestId\" IS NULL) OR " +
            "(\"Reason\" = 'ReferralCandidateConfirmed' AND \"Points\" = 300 AND \"InsightHelpfulnessFeedbackId\" IS NULL AND \"InterviewInsightId\" IS NULL AND \"JobReferralId\" IS NULL AND \"ReferralRequestId\" IS NOT NULL)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(80).IsRequired();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Feedback).WithOne().HasForeignKey<UserPointTransaction>(x => x.InsightHelpfulnessFeedbackId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.InsightHelpfulnessFeedbackId).IsUnique();
        builder.HasOne(x => x.InterviewInsight).WithMany().HasForeignKey(x => x.InterviewInsightId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.InterviewInsightId).IsUnique().HasFilter("\"InterviewInsightId\" IS NOT NULL");
        builder.HasOne(x => x.JobReferral).WithMany().HasForeignKey(x => x.JobReferralId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.JobReferralId).IsUnique().HasFilter("\"JobReferralId\" IS NOT NULL");
        builder.HasOne(x => x.ReferralRequest).WithMany().HasForeignKey(x => x.ReferralRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ReferralRequestId).IsUnique().HasFilter("\"ReferralRequestId\" IS NOT NULL");
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
