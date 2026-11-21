using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorInterviewDefinitionConfiguration : IEntityTypeConfiguration<AcceleratorInterviewDefinition>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewDefinition> builder)
    {
        builder.ToTable("AcceleratorInterviewDefinitions"); builder.ConfigureBaseEntity();
        builder.Property(x => x.StableKey).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => x.StableKey).IsUnique();
        builder.HasIndex(x => new { x.TargetRoleId, x.IsActive });
        builder.HasOne<AcceleratorCareerRole>().WithMany().HasForeignKey(x => x.TargetRoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewVersionConfiguration : IEntityTypeConfiguration<AcceleratorInterviewVersion>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewVersion> builder)
    {
        builder.ToTable("AcceleratorInterviewVersions", t => t.HasCheckConstraint("CK_InterviewVersion_Bounds",
            "\"Version\" > 0 AND \"EstimatedMinutes\" > 0 AND \"Status\" BETWEEN 1 AND 3"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.TargetExperienceLevel).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Instructions).HasMaxLength(12000).IsRequired(); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.DefinitionId, x.Version }).IsUnique();
        builder.HasOne(x => x.Definition).WithMany(x => x.Versions).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewCompetencyConfiguration : IEntityTypeConfiguration<AcceleratorInterviewCompetency>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewCompetency> builder)
    {
        builder.ToTable("AcceleratorInterviewCompetencies", t => t.HasCheckConstraint("CK_InterviewCompetency_Bounds",
            "\"ExpectedProficiency\" BETWEEN 0 AND 4 AND \"Weight\" > 0 AND \"QuestionCoverageTarget\" BETWEEN 0 AND 100"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Weight).HasPrecision(7, 3);
        builder.HasIndex(x => new { x.InterviewVersionId, x.CompetencyId }).IsUnique();
        builder.HasOne<AcceleratorInterviewVersion>().WithMany(x => x.Competencies).HasForeignKey(x => x.InterviewVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewSectionConfiguration : IEntityTypeConfiguration<AcceleratorInterviewSection>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewSection> builder)
    {
        builder.ToTable("AcceleratorInterviewSections", t => t.HasCheckConstraint("CK_InterviewSection_Bounds",
            "\"Sequence\" > 0 AND \"EstimatedMinutes\" > 0 AND \"QuestionCount\" BETWEEN 1 AND 50 AND \"MaxFollowUpsPerQuestion\" BETWEEN 0 AND 3"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.InterviewVersionId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Version).WithMany(x => x.Sections).HasForeignKey(x => x.InterviewVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewQuestionConfiguration : IEntityTypeConfiguration<AcceleratorInterviewQuestion>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewQuestion> builder)
    {
        builder.ToTable("AcceleratorInterviewQuestions", t => t.HasCheckConstraint("CK_InterviewQuestion_Sequence", "\"Sequence\" > 0"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.PublicPrompt).HasMaxLength(12000).IsRequired();
        builder.Property(x => x.ExpectedConceptsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DeterministicRulesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.HiddenEvaluatorGuidance).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.FollowUpPrompt).HasMaxLength(8000).IsRequired();
        builder.HasIndex(x => new { x.SectionId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Section).WithMany(x => x.Questions).HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewRubricCriterionConfiguration : IEntityTypeConfiguration<AcceleratorInterviewRubricCriterion>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewRubricCriterion> builder)
    {
        builder.ToTable("AcceleratorInterviewRubricCriteria", t => t.HasCheckConstraint("CK_InterviewCriterion_Bounds",
            "\"Sequence\" > 0 AND \"Weight\" > 0 AND \"MaximumScore\" > 0"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Weight).HasPrecision(7, 3); builder.Property(x => x.MaximumScore).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.InterviewVersionId, x.Sequence }).IsUnique();
        builder.HasOne<AcceleratorInterviewVersion>().WithMany(x => x.Criteria).HasForeignKey(x => x.InterviewVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewSessionConfiguration : IEntityTypeConfiguration<AcceleratorInterviewSession>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewSession> builder)
    {
        builder.ToTable("AcceleratorInterviewSessions", t => t.HasCheckConstraint("CK_InterviewSession_Score",
            "\"OverallScore\" IS NULL OR (\"OverallScore\" BETWEEN 0 AND 100)"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken(); builder.Property(x => x.OverallScore).HasPrecision(9, 2);
        builder.Property(x => x.ReadinessImpact).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.StartedAtUtc });
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewVersion>().WithMany().HasForeignKey(x => x.InterviewVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorJourneyContext>().WithMany().HasForeignKey(x => x.JourneyContextId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.JourneyContextId });
        builder.ToTable(t => t.HasCheckConstraint("CK_InterviewSession_Journey", "\"JourneyContextId\" IS NOT NULL"));
    }
}

public sealed class AcceleratorSessionQuestionConfiguration : IEntityTypeConfiguration<AcceleratorSessionQuestion>
{
    public void Configure(EntityTypeBuilder<AcceleratorSessionQuestion> builder)
    {
        builder.ToTable("AcceleratorSessionQuestions", t => t.HasCheckConstraint("CK_SessionQuestion_Sequence", "\"Sequence\" > 0"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.PublicPrompt).HasMaxLength(12000).IsRequired();
        builder.Property(x => x.GenerationRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.GenerationProvider).HasMaxLength(80).IsRequired();
        builder.Property(x => x.GenerationModel).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CompetencyIdsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.GenerationMetadataJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.SessionId, x.SectionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.SessionId, x.GenerationRequestKey }).IsUnique().HasFilter("\"GenerationRequestKey\" IS NOT NULL");
        builder.HasIndex(x => x.GenerationDispatchId).IsUnique().HasFilter("\"GenerationDispatchId\" IS NOT NULL");
        builder.HasIndex(x => new { x.SessionId, x.SourceQuestionId, x.ParentQuestionId });
        builder.HasOne(x => x.Session).WithMany(x => x.Questions).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewQuestion>().WithMany().HasForeignKey(x => x.SourceQuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorSessionQuestion>().WithMany().HasForeignKey(x => x.ParentQuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewSection>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorAiDispatchRecord>().WithMany().HasForeignKey(x => x.GenerationDispatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewAnswerConfiguration : IEntityTypeConfiguration<AcceleratorInterviewAnswer>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewAnswer> builder)
    {
        builder.ToTable("AcceleratorInterviewAnswers"); builder.ConfigureBaseEntity();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); builder.Property(x => x.Content).HasMaxLength(16000).IsRequired();
        builder.HasIndex(x => new { x.SessionId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => x.SessionQuestionId).IsUnique();
        builder.HasOne<AcceleratorInterviewSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorSessionQuestion>().WithMany().HasForeignKey(x => x.SessionQuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewEvaluationConfiguration : IEntityTypeConfiguration<AcceleratorInterviewEvaluation>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewEvaluation> builder)
    {
        builder.ToTable("AcceleratorInterviewEvaluations", t => t.HasCheckConstraint("CK_InterviewEvaluation_Bounds",
            "\"Score\" BETWEEN 0 AND 100 AND \"Confidence\" BETWEEN 0 AND 1"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Score).HasPrecision(9, 2);
        builder.Property(x => x.Confidence).HasPrecision(5, 4);
        builder.Property(x => x.Feedback).HasMaxLength(8000).IsRequired();
        builder.HasIndex(x => x.AnswerId).IsUnique();
        builder.HasOne<AcceleratorInterviewAnswer>().WithMany().HasForeignKey(x => x.AnswerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorInterviewCriterionScoreConfiguration : IEntityTypeConfiguration<AcceleratorInterviewCriterionScore>
{
    public void Configure(EntityTypeBuilder<AcceleratorInterviewCriterionScore> builder)
    {
        builder.ToTable("AcceleratorInterviewCriterionScores", t => t.HasCheckConstraint("CK_InterviewScore_Nonnegative", "\"Score\" >= 0"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Score).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.EvaluationId, x.CriterionId }).IsUnique();
        builder.HasOne<AcceleratorInterviewEvaluation>().WithMany(x => x.CriterionScores).HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewRubricCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorReadinessSnapshotConfiguration : IEntityTypeConfiguration<AcceleratorReadinessSnapshot>
{
    public void Configure(EntityTypeBuilder<AcceleratorReadinessSnapshot> builder)
    {
        builder.ToTable("AcceleratorReadinessSnapshots", t => t.HasCheckConstraint("CK_Readiness_Bounds",
            "\"Total\" BETWEEN 0 AND 100 AND \"SkillCoverage\" BETWEEN 0 AND 100 AND \"PracticePerformance\" BETWEEN 0 AND 100 AND \"ProjectPerformance\" BETWEEN 0 AND 100 AND \"InterviewPerformance\" BETWEEN 0 AND 100"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.CalculationVersion).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Explanation).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SkillCoverage).HasPrecision(9, 2); builder.Property(x => x.PracticePerformance).HasPrecision(9, 2);
        builder.Property(x => x.ProjectPerformance).HasPrecision(9, 2); builder.Property(x => x.InterviewPerformance).HasPrecision(9, 2);
        builder.Property(x => x.Total).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.UserId, x.GoalId, x.CalculatedAtUtc });
        builder.HasIndex(x => x.TriggerInterviewSessionId).IsUnique().HasFilter("\"TriggerInterviewSessionId\" IS NOT NULL");
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewSession>().WithMany().HasForeignKey(x => x.TriggerInterviewSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorJourneyContext>().WithMany().HasForeignKey(x => x.JourneyContextId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.JourneyContextId, x.CalculatedAtUtc });
        builder.ToTable(t => t.HasCheckConstraint("CK_ReadinessSnapshot_Journey", "\"JourneyContextId\" IS NOT NULL"));
    }
}

public sealed class AcceleratorPassportShareConfiguration : IEntityTypeConfiguration<AcceleratorPassportShare>
{
    public void Configure(EntityTypeBuilder<AcceleratorPassportShare> builder)
    {
        builder.ToTable("AcceleratorPassportShares"); builder.ConfigureBaseEntity();
        builder.Property(x => x.PublicToken).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.GoalId }).IsUnique();
        builder.HasIndex(x => x.PublicToken).IsUnique();
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
    }
}
