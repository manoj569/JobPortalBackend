using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorJourneyContextConfiguration : IEntityTypeConfiguration<AcceleratorJourneyContext>
{
    public void Configure(EntityTypeBuilder<AcceleratorJourneyContext> builder)
    {
        builder.ToTable("AcceleratorJourneyContexts", t => t.HasCheckConstraint("CK_AcceleratorJourneyContext_Scope", "(\"ScopeType\" = 1 AND \"CareerGoalId\" IS NOT NULL AND \"LearningPathEnrollmentId\" IS NULL) OR (\"ScopeType\" = 2 AND \"CareerGoalId\" IS NULL AND \"LearningPathEnrollmentId\" IS NOT NULL)")); builder.ConfigureBaseEntity(); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.CareerGoalId).IsUnique().HasFilter("\"CareerGoalId\" IS NOT NULL AND \"IsDeleted\" = false"); builder.HasIndex(x => x.LearningPathEnrollmentId).IsUnique().HasFilter("\"LearningPathEnrollmentId\" IS NOT NULL AND \"IsDeleted\" = false"); builder.HasIndex(x => new { x.UserId, x.ScopeType });
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.CareerGoalId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorPathEnrollment>().WithMany().HasForeignKey(x => x.LearningPathEnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorTaxonomy>().WithMany().HasForeignKey(x => x.TaxonomyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorAssessmentTemplateConfiguration : IEntityTypeConfiguration<AcceleratorAssessmentTemplate>
{
    public void Configure(EntityTypeBuilder<AcceleratorAssessmentTemplate> builder)
    {
        builder.ToTable("AcceleratorAssessmentTemplates", t => t.HasCheckConstraint("CK_AcceleratorAssessmentTemplate_Limits", "\"TimeLimitMinutes\" BETWEEN 1 AND 240 AND \"MaximumQuestions\" BETWEEN 1 AND 100")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Version).HasMaxLength(80).IsRequired(); builder.Property(x => x.SelectionStrategy).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.RoleId, x.Version }).IsUnique(); builder.HasIndex(x => new { x.RoleId, x.IsActive });
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorTaxonomy>().WithMany().HasForeignKey(x => x.TaxonomyId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("AcceleratorAssessmentTemplates", t => t.HasCheckConstraint("CK_AcceleratorAssessmentTemplate_Applicability", "\"RoleId\" IS NOT NULL OR \"TaxonomyId\" IS NOT NULL"));
    }
}
public sealed class AcceleratorQuestionConfiguration : IEntityTypeConfiguration<AcceleratorQuestion>
{
    public void Configure(EntityTypeBuilder<AcceleratorQuestion> builder)
    {
        builder.ToTable("AcceleratorQuestions", t => t.HasCheckConstraint("CK_AcceleratorQuestion_Bounds", "\"Difficulty\" BETWEEN 1 AND 5 AND \"Weight\" > 0")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Version).HasMaxLength(80).IsRequired(); builder.Property(x => x.Weight).HasPrecision(9, 4); builder.Property(x => x.Prompt).HasMaxLength(8000).IsRequired(); builder.Property(x => x.Explanation).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.OptionsJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.AnswerKeyJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.RubricJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.CompetencyId, x.Version }).IsUnique(); builder.HasIndex(x => new { x.TaxonomyId, x.IsActive, x.Difficulty });
        builder.HasOne(x => x.Competency).WithMany().HasForeignKey(x => new { x.CompetencyId, x.TaxonomyId }).HasPrincipalKey(x => new { x.Id, x.TaxonomyId }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorAssessmentAttemptConfiguration : IEntityTypeConfiguration<AcceleratorAssessmentAttempt>
{
    public void Configure(EntityTypeBuilder<AcceleratorAssessmentAttempt> builder)
    {
        builder.ToTable("AcceleratorAssessmentAttempts", t => t.HasCheckConstraint("CK_AcceleratorAssessmentAttempt_Number", "\"AttemptNumber\" >= 1")); builder.ConfigureBaseEntity();
        builder.Property(x => x.TemplateVersion).HasMaxLength(80).IsRequired(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); builder.Property(x => x.SelectionStrategy).HasMaxLength(80).IsRequired(); builder.Property(x => x.Score).HasPrecision(5, 2); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.GoalId, x.Status });
        builder.HasOne(x => x.Goal).WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorAssessmentAnswerConfiguration : IEntityTypeConfiguration<AcceleratorAssessmentAnswer>
{
    public void Configure(EntityTypeBuilder<AcceleratorAssessmentAnswer> builder)
    {
        builder.ToTable("AcceleratorAssessmentAnswers"); builder.ConfigureBaseEntity(); builder.Property(x => x.QuestionVersion).HasMaxLength(80).IsRequired(); builder.Property(x => x.SubmittedAnswerJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.EvaluationEvidenceJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.Score).HasPrecision(5, 2);
        builder.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique(); builder.HasIndex(x => new { x.AttemptId, x.Sequence }).IsUnique(); builder.HasOne(x => x.Attempt).WithMany(x => x.Answers).HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorSkillEvidenceConfiguration : IEntityTypeConfiguration<AcceleratorSkillEvidence>
{
    public void Configure(EntityTypeBuilder<AcceleratorSkillEvidence> builder)
    {
        builder.ToTable("AcceleratorSkillEvidence", t => t.HasCheckConstraint("CK_AcceleratorSkillEvidence_Confidence", "\"Confidence\" BETWEEN 0 AND 1")); builder.ConfigureBaseEntity(); builder.Property(x => x.Confidence).HasPrecision(5, 4); builder.Property(x => x.ScoringVersion).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Score).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.UserId, x.GoalId, x.CompetencyId, x.EvaluatedAtUtc }); builder.HasIndex(x => x.AttemptId).IsUnique().HasFilter("\"AttemptId\" IS NOT NULL");
        builder.HasIndex(x => new { x.ProjectEvaluationId, x.CompetencyId }).IsUnique().HasFilter("\"ProjectEvaluationId\" IS NOT NULL");
        builder.HasIndex(x => new { x.InterviewEvaluationId, x.CompetencyId }).IsUnique().HasFilter("\"InterviewEvaluationId\" IS NOT NULL");
        builder.HasOne<AcceleratorCandidateProject>().WithMany().HasForeignKey(x => x.CandidateProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorProjectVersion>().WithMany().HasForeignKey(x => x.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorProjectAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorProjectSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorProjectEvaluation>().WithMany().HasForeignKey(x => x.ProjectEvaluationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewSession>().WithMany().HasForeignKey(x => x.InterviewSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorSessionQuestion>().WithMany().HasForeignKey(x => x.SessionQuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewEvaluation>().WithMany().HasForeignKey(x => x.InterviewEvaluationId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorRoadmapConfiguration : IEntityTypeConfiguration<AcceleratorRoadmap>
{
    public void Configure(EntityTypeBuilder<AcceleratorRoadmap> builder)
    {
        builder.HasIndex(x => new { x.JourneyContextId, x.Version }).IsUnique().HasFilter("\"JourneyContextId\" IS NOT NULL");
        builder.ToTable("AcceleratorRoadmaps", t => t.HasCheckConstraint("CK_AcceleratorRoadmap_Timeline", "\"TargetAtUtc\" > \"PlannedStartAtUtc\" AND \"EstimatedMinutes\" >= 0")); builder.ConfigureBaseEntity(); builder.Property(x => x.GenerationStrategy).HasMaxLength(80).IsRequired(); builder.Property(x => x.Explanation).HasMaxLength(8000).IsRequired();
        builder.HasIndex(x => new { x.GoalId, x.Version }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.Status }); builder.HasMany(x => x.Nodes).WithOne(x => x.Roadmap).HasForeignKey(x => x.RoadmapId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorRoadmapNodeConfiguration : IEntityTypeConfiguration<AcceleratorRoadmapNode>
{
    public void Configure(EntityTypeBuilder<AcceleratorRoadmapNode> builder)
    {
        builder.ToTable("AcceleratorRoadmapNodes", t => t.HasCheckConstraint("CK_AcceleratorRoadmapNode_Progress", "\"EstimatedMinutes\" >= 0 AND \"ProgressPercent\" BETWEEN 0 AND 100")); builder.ConfigureBaseEntity(); builder.Property(x => x.StableKey).HasMaxLength(120).IsRequired(); builder.Property(x => x.CompletionEvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.RoadmapId, x.StableKey }).IsUnique(); builder.HasIndex(x => new { x.RoadmapId, x.Sequence }).IsUnique();
    }
}
public sealed class AcceleratorRoadmapEdgeConfiguration : IEntityTypeConfiguration<AcceleratorRoadmapEdge>
{
    public void Configure(EntityTypeBuilder<AcceleratorRoadmapEdge> builder) { builder.ToTable("AcceleratorRoadmapEdges", t => t.HasCheckConstraint("CK_AcceleratorRoadmapEdge_NotSelf", "\"FromNodeId\" <> \"ToNodeId\"")); builder.ConfigureBaseEntity(); builder.HasIndex(x => new { x.RoadmapId, x.FromNodeId, x.ToNodeId }).IsUnique(); builder.HasIndex(x => new { x.RoadmapId, x.ToNodeId }); }
}
public sealed class AcceleratorLearningActivityConfiguration : IEntityTypeConfiguration<AcceleratorLearningActivity>
{
    public void Configure(EntityTypeBuilder<AcceleratorLearningActivity> builder) { builder.ToTable("AcceleratorLearningActivities", t => t.HasCheckConstraint("CK_AcceleratorLearningActivity_Effort", "\"EstimatedMinutes\" BETWEEN 1 AND 1440")); builder.ConfigureBaseEntity(); builder.Property(x => x.Title).HasMaxLength(500).IsRequired(); builder.Property(x => x.Revision).IsConcurrencyToken(); builder.HasIndex(x => new { x.UserId, x.ScheduledAtUtc, x.Status }); builder.HasOne<AcceleratorRoadmapNode>().WithMany().HasForeignKey(x => x.RoadmapNodeId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AcceleratorProgressEventConfiguration : IEntityTypeConfiguration<AcceleratorProgressEvent>
{
    public void Configure(EntityTypeBuilder<AcceleratorProgressEvent> builder) { builder.ToTable("AcceleratorProgressEvents"); builder.ConfigureBaseEntity(); builder.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired(); builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.ActivityId, x.OccurredAtUtc }); builder.HasOne<AcceleratorLearningActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AcceleratorRecommendationConfiguration : IEntityTypeConfiguration<AcceleratorRecommendation>
{
    public void Configure(EntityTypeBuilder<AcceleratorRecommendation> builder) { builder.ToTable("AcceleratorRecommendations", t => t.HasCheckConstraint("CK_AcceleratorRecommendation_Priority", "\"Priority\" BETWEEN 1 AND 100")); builder.ConfigureBaseEntity(); builder.Property(x => x.Type).HasMaxLength(100).IsRequired(); builder.Property(x => x.Reason).HasMaxLength(4000).IsRequired(); builder.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired(); builder.HasIndex(x => new { x.UserId, x.GoalId, x.GeneratedAtUtc }); }
}
