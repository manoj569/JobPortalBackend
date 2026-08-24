using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class InterviewInsightConfiguration : IEntityTypeConfiguration<InterviewInsight>
{
    public void Configure(EntityTypeBuilder<InterviewInsight> builder)
    {
        builder.ToTable("InterviewInsights", t =>
        {
            t.HasCheckConstraint("CK_InterviewInsights_HelpfulCount", "\"HelpfulConfirmedCount\" >= 0");
            t.HasCheckConstraint("CK_InterviewInsights_QualityScore", "\"QualityScore\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RoleTitle).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ExperienceLevel).HasMaxLength(80);
        builder.Property(x => x.ProcessSummary).HasMaxLength(3000).IsRequired();
        builder.Property(x => x.PreparationTips).HasMaxLength(3000).IsRequired();
        builder.Property(x => x.ModerationReason).HasMaxLength(500);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        builder.HasOne(x => x.AuthorCandidate).WithMany().HasForeignKey(x => x.AuthorCandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.PublishedAtUtc });
        builder.HasIndex(x => new { x.AuthorCandidateId, x.CreatedAtUtc });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class InterviewRoundConfiguration : IEntityTypeConfiguration<InterviewRound>
{
    public void Configure(EntityTypeBuilder<InterviewRound> builder)
    {
        builder.ToTable("InterviewRounds", t => t.HasCheckConstraint("CK_InterviewRounds_Duration", "\"DurationMinutes\" IS NULL OR (\"DurationMinutes\" BETWEEN 1 AND 1440)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RoundTitle).HasMaxLength(160);
        builder.Property(x => x.QuestionsOrTopics).HasMaxLength(3000).IsRequired();
        builder.Property(x => x.CandidateAdvice).HasMaxLength(2000);
        builder.HasOne(x => x.InterviewInsight).WithMany(x => x.Rounds).HasForeignKey(x => x.InterviewInsightId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.InterviewInsightId, x.Sequence }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class CandidateInterviewScheduleConfiguration : IEntityTypeConfiguration<CandidateInterviewSchedule>
{
    public void Configure(EntityTypeBuilder<CandidateInterviewSchedule> builder)
    {
        builder.ToTable("CandidateInterviewSchedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RoleTitle).HasMaxLength(160);
        builder.Property(x => x.ExpectedRoundTypes).HasMaxLength(160);
        builder.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.CandidateId, x.CompanyId, x.InterviewAtUtc });
        builder.HasIndex(x => new { x.FeedbackNotificationSentAtUtc, x.ConfirmFeedbackAvailableAtUtc });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class InsightHelpfulnessFeedbackConfiguration : IEntityTypeConfiguration<InsightHelpfulnessFeedback>
{
    public void Configure(EntityTypeBuilder<InsightHelpfulnessFeedback> builder)
    {
        builder.ToTable("InsightHelpfulnessFeedback");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Feedback).HasMaxLength(500);
        builder.HasOne(x => x.Insight).WithMany(x => x.Feedback).HasForeignKey(x => x.InsightId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CandidateInterviewSchedule).WithMany().HasForeignKey(x => x.CandidateInterviewScheduleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CandidateId, x.InsightId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.CandidateId, x.CreatedAtUtc });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class InsightReportConfiguration : IEntityTypeConfiguration<InsightReport>
{
    public void Configure(EntityTypeBuilder<InsightReport> builder)
    {
        builder.ToTable("InsightReports");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Details).HasMaxLength(500);
        builder.HasOne(x => x.Insight).WithMany(x => x.Reports).HasForeignKey(x => x.InsightId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ReporterCandidate).WithMany().HasForeignKey(x => x.ReporterCandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ReporterCandidateId, x.InsightId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
