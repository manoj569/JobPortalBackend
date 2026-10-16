using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class UserPointTransactionConfiguration : IEntityTypeConfiguration<UserPointTransaction>
{
    public void Configure(EntityTypeBuilder<UserPointTransaction> builder)
    {
        builder.ToTable("UserPointTransactions", table => table.HasCheckConstraint(
            "CK_UserPointTransactions_HelpfulReward", "\"Points\" = 10 AND \"Reason\" = 'InterviewInsightHelpful'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(80).IsRequired();
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Feedback).WithOne().HasForeignKey<UserPointTransaction>(x => x.InsightHelpfulnessFeedbackId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.InsightHelpfulnessFeedbackId).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
