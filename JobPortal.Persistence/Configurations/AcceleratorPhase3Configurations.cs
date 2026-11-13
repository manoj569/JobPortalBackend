using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorTutorConversationConfiguration : IEntityTypeConfiguration<AcceleratorTutorConversation>
{
    public void Configure(EntityTypeBuilder<AcceleratorTutorConversation> builder)
    {
        builder.ToTable("AcceleratorTutorConversations"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Topic).HasMaxLength(500).IsRequired(); builder.Property(x => x.Title).HasMaxLength(500).IsRequired(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc }); builder.HasIndex(x => new { x.UserId, x.GoalId, x.Status });
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorRoadmapNode>().WithMany().HasForeignKey(x => x.RoadmapNodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorTutorMessageConfiguration : IEntityTypeConfiguration<AcceleratorTutorMessage>
{
    public void Configure(EntityTypeBuilder<AcceleratorTutorMessage> builder)
    {
        builder.ToTable("AcceleratorTutorMessages", t => t.HasCheckConstraint("CK_AcceleratorTutorMessage_Tokens", "\"InputTokens\" >= 0 AND \"OutputTokens\" >= 0")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Content).HasMaxLength(16000).IsRequired(); builder.Property(x => x.MessageType).HasMaxLength(80).IsRequired(); builder.Property(x => x.Provider).HasMaxLength(80); builder.Property(x => x.Model).HasMaxLength(120); builder.Property(x => x.FailureCode).HasMaxLength(100); builder.Property(x => x.EstimatedCostInr).HasPrecision(18, 6);
        builder.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc }); builder.HasIndex(x => x.DispatchId).IsUnique().HasFilter("\"DispatchId\" IS NOT NULL"); builder.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorTutorContextSnapshotConfiguration : IEntityTypeConfiguration<AcceleratorTutorContextSnapshot>
{
    public void Configure(EntityTypeBuilder<AcceleratorTutorContextSnapshot> builder) { builder.ToTable("AcceleratorTutorContextSnapshots", t => t.HasCheckConstraint("CK_AcceleratorTutorContextSnapshot_Tokens", "\"TokenEstimate\" BETWEEN 0 AND 32000")); builder.ConfigureBaseEntity(); builder.Property(x => x.ContextVersion).HasMaxLength(80).IsRequired(); builder.Property(x => x.ContentJson).HasColumnType("jsonb").IsRequired(); builder.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc }); }
}
public sealed class AcceleratorTutorMemorySummaryConfiguration : IEntityTypeConfiguration<AcceleratorTutorMemorySummary>
{
    public void Configure(EntityTypeBuilder<AcceleratorTutorMemorySummary> builder) { builder.ToTable("AcceleratorTutorMemorySummaries"); builder.ConfigureBaseEntity(); builder.Property(x => x.Summary).HasMaxLength(8000).IsRequired(); builder.Property(x => x.SourceRange).HasMaxLength(500).IsRequired(); builder.HasIndex(x => new { x.ConversationId, x.Version }).IsUnique(); }
}
public sealed class AcceleratorPracticeSessionConfiguration : IEntityTypeConfiguration<AcceleratorPracticeSession>
{
    public void Configure(EntityTypeBuilder<AcceleratorPracticeSession> builder) { builder.ToTable("AcceleratorPracticeSessions"); builder.ConfigureBaseEntity(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); builder.Property(x => x.GenerationStrategy).HasMaxLength(80).IsRequired(); builder.Property(x => x.Revision).IsConcurrencyToken(); builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.CompetencyId, x.CreatedAtUtc }); builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorRoadmapNode>().WithMany().HasForeignKey(x => x.RoadmapNodeId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AcceleratorPracticeQuestionConfiguration : IEntityTypeConfiguration<AcceleratorPracticeQuestion>
{
    public void Configure(EntityTypeBuilder<AcceleratorPracticeQuestion> builder) { builder.ToTable("AcceleratorPracticeQuestions"); builder.ConfigureBaseEntity(); builder.Property(x => x.Prompt).HasMaxLength(12000).IsRequired(); builder.Property(x => x.PublicOptionsJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.AnswerKeyJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.RubricJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.GenerationVersion).HasMaxLength(80).IsRequired(); builder.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique(); builder.HasOne(x => x.Session).WithMany(x => x.Questions).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AcceleratorPracticeAttemptConfiguration : IEntityTypeConfiguration<AcceleratorPracticeAttempt>
{
    public void Configure(EntityTypeBuilder<AcceleratorPracticeAttempt> builder) { builder.ToTable("AcceleratorPracticeAttempts"); builder.ConfigureBaseEntity(); builder.Property(x => x.AnswerJson).HasColumnType("jsonb").IsRequired(); builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.SessionId, x.QuestionId }); builder.HasOne<AcceleratorPracticeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorPracticeQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AcceleratorPracticeEvaluationConfiguration : IEntityTypeConfiguration<AcceleratorPracticeEvaluation>
{
    public void Configure(EntityTypeBuilder<AcceleratorPracticeEvaluation> builder) { builder.ToTable("AcceleratorPracticeEvaluations", t => t.HasCheckConstraint("CK_AcceleratorPracticeEvaluation_Score", "\"Score\" >= 0 AND \"MaximumScore\" > 0 AND \"Score\" <= \"MaximumScore\" AND \"Confidence\" BETWEEN 0 AND 1")); builder.ConfigureBaseEntity(); builder.Property(x => x.Score).HasPrecision(9, 2); builder.Property(x => x.MaximumScore).HasPrecision(9, 2); builder.Property(x => x.Confidence).HasPrecision(5, 4); builder.Property(x => x.Classification).HasMaxLength(80).IsRequired(); builder.Property(x => x.StrengthsJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.WeaknessesJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.Explanation).HasMaxLength(8000).IsRequired(); builder.Property(x => x.SuggestedImprovement).HasMaxLength(4000).IsRequired(); builder.Property(x => x.RubricVersion).HasMaxLength(80).IsRequired(); builder.HasIndex(x => x.AttemptId).IsUnique(); }
}
public sealed class AcceleratorAiDispatchRecordConfiguration : IEntityTypeConfiguration<AcceleratorAiDispatchRecord>
{
    public void Configure(EntityTypeBuilder<AcceleratorAiDispatchRecord> builder) { builder.ToTable("AcceleratorAiDispatchRecords", t => t.HasCheckConstraint("CK_AcceleratorAiDispatch_ReconciliationAttempts", "\"ReconciliationAttemptCount\" >= 0")); builder.ConfigureBaseEntity(); builder.Property(x => x.Operation).HasMaxLength(80).IsRequired(); builder.Property(x => x.Provider).HasMaxLength(80).IsRequired(); builder.Property(x => x.Model).HasMaxLength(120).IsRequired(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); builder.Property(x => x.ProviderRequestId).HasMaxLength(200); builder.Property(x => x.FailureCode).HasMaxLength(100); builder.Property(x => x.LastReconciliationError).HasMaxLength(1000); builder.Property(x => x.ManualReviewReason).HasMaxLength(2000); builder.Property(x => x.Revision).IsConcurrencyToken(); builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.Status, x.NextReconciliationAtUtc }); builder.HasIndex(x => new { x.Status, x.ReconciliationClaimExpiresAtUtc }); builder.HasIndex(x => x.ReservationId).IsUnique(); }
}
