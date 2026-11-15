using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorProjectDefinitionConfiguration : IEntityTypeConfiguration<AcceleratorProjectDefinition>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectDefinition> builder)
    {
        builder.ToTable("AcceleratorProjectDefinitions"); builder.ConfigureBaseEntity();
        builder.Property(x => x.StableKey).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => x.StableKey).IsUnique();
    }
}
public sealed class AcceleratorProjectVersionConfiguration : IEntityTypeConfiguration<AcceleratorProjectVersion>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectVersion> builder)
    {
        builder.ToTable("AcceleratorProjectVersions", t => t.HasCheckConstraint("CK_AcceleratorProjectVersion_Bounds", "\"Version\" > 0 AND \"EstimatedMinutes\" > 0 AND \"CatalogStatus\" BETWEEN 1 AND 3")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired(); builder.Property(x => x.ShortDescription).HasMaxLength(2000).IsRequired(); builder.Property(x => x.BusinessContext).HasMaxLength(12000).IsRequired(); builder.Property(x => x.Instructions).HasMaxLength(12000).IsRequired(); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.DefinitionId, x.Version }).IsUnique(); builder.HasIndex(x => new { x.TargetRoleId, x.IsActive, x.Difficulty });
        builder.HasOne(x => x.Definition).WithMany(x => x.Versions).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCareerRole>().WithMany().HasForeignKey(x => x.TargetRoleId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectCompetencyConfiguration : IEntityTypeConfiguration<AcceleratorProjectCompetency>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectCompetency> builder)
    {
        builder.ToTable("AcceleratorProjectCompetencies", t => t.HasCheckConstraint("CK_AcceleratorProjectCompetency_Proficiency", "\"RequiredProficiency\" BETWEEN 0 AND 4")); builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.ProjectVersionId, x.CompetencyId }).IsUnique();
        builder.HasOne<AcceleratorProjectVersion>().WithMany(x => x.Competencies).HasForeignKey(x => x.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectMilestoneConfiguration : IEntityTypeConfiguration<AcceleratorProjectMilestone>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectMilestone> builder)
    {
        builder.ToTable("AcceleratorProjectMilestones", t => t.HasCheckConstraint("CK_AcceleratorProjectMilestone_Bounds", "\"Sequence\" > 0 AND \"EstimatedMinutes\" > 0")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired(); builder.Property(x => x.Description).HasMaxLength(8000).IsRequired(); builder.HasIndex(x => new { x.ProjectVersionId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.ProjectVersion).WithMany(x => x.Milestones).HasForeignKey(x => x.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectAssignmentConfiguration : IEntityTypeConfiguration<AcceleratorProjectAssignment>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectAssignment> builder)
    {
        builder.ToTable("AcceleratorProjectAssignments", t => { t.HasCheckConstraint("CK_AcceleratorProjectAssignment_Retry", "\"Sequence\" > 0 AND \"MaximumAttempts\" BETWEEN 1 AND 20 AND \"CooldownMinutes\" BETWEEN 0 AND 10080"); t.HasCheckConstraint("CK_AcceleratorProjectAssignment_Mode", "\"EvaluationMode\" BETWEEN 1 AND 3"); }); builder.ConfigureBaseEntity();
        builder.Property(x => x.Prompt).HasMaxLength(12000).IsRequired(); builder.Property(x => x.CompletionRequirements).HasMaxLength(8000).IsRequired(); builder.Property(x => x.DeterministicRulesJson).HasColumnType("jsonb").IsRequired(); builder.HasIndex(x => new { x.MilestoneId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Milestone).WithMany(x => x.Assignments).HasForeignKey(x => x.MilestoneId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectRubricConfiguration : IEntityTypeConfiguration<AcceleratorProjectRubric>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectRubric> builder)
    {
        builder.ToTable("AcceleratorProjectRubrics", t => t.HasCheckConstraint("CK_AcceleratorProjectRubric_Version", "\"Version\" > 0")); builder.ConfigureBaseEntity(); builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.ProjectVersionId, x.Version }).IsUnique(); builder.HasOne<AcceleratorProjectVersion>().WithMany(x => x.Rubrics).HasForeignKey(x => x.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectRubricCriterionConfiguration : IEntityTypeConfiguration<AcceleratorProjectRubricCriterion>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectRubricCriterion> builder)
    {
        builder.ToTable("AcceleratorProjectRubricCriteria", t => t.HasCheckConstraint("CK_AcceleratorProjectRubricCriterion_Weight", "\"Sequence\" > 0 AND \"Weight\" > 0 AND \"MaximumScore\" > 0")); builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired(); builder.Property(x => x.Description).HasMaxLength(2000).IsRequired(); builder.Property(x => x.Weight).HasPrecision(7, 3); builder.Property(x => x.MaximumScore).HasPrecision(9, 2); builder.HasIndex(x => new { x.RubricId, x.Sequence }).IsUnique();
        builder.HasOne<AcceleratorProjectRubric>().WithMany(x => x.Criteria).HasForeignKey(x => x.RubricId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorCandidateProjectConfiguration : IEntityTypeConfiguration<AcceleratorCandidateProject>
{
    public void Configure(EntityTypeBuilder<AcceleratorCandidateProject> builder)
    {
        builder.ToTable("AcceleratorCandidateProjects", t => t.HasCheckConstraint("CK_AcceleratorCandidateProject_Progress", "\"ProgressPercent\" BETWEEN 0 AND 100")); builder.ConfigureBaseEntity(); builder.Property(x => x.Revision).IsConcurrencyToken(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); builder.Property(x => x.FinalScore).HasPrecision(9, 2);
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc }); builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorProjectVersion>().WithMany().HasForeignKey(x => x.ProjectVersionId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorProjectMilestone>().WithMany().HasForeignKey(x => x.CurrentMilestoneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorProjectAssignment>().WithMany().HasForeignKey(x => x.RequiredRevisionAssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectSubmissionConfiguration : IEntityTypeConfiguration<AcceleratorProjectSubmission>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectSubmission> builder)
    {
        builder.ToTable("AcceleratorProjectSubmissions", t => t.HasCheckConstraint("CK_AcceleratorProjectSubmission_Attempt", "\"AttemptNumber\" > 0")); builder.ConfigureBaseEntity(); builder.Property(x => x.Content).HasMaxLength(32000).IsRequired(); builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.CandidateProjectId, x.RequestKey }).IsUnique(); builder.HasIndex(x => new { x.CandidateProjectId, x.AssignmentId, x.AttemptNumber }).IsUnique(); builder.HasOne<AcceleratorCandidateProject>().WithMany().HasForeignKey(x => x.CandidateProjectId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorProjectAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectEvaluationConfiguration : IEntityTypeConfiguration<AcceleratorProjectEvaluation>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectEvaluation> builder)
    {
        builder.ToTable("AcceleratorProjectEvaluations", t => t.HasCheckConstraint("CK_AcceleratorProjectEvaluation_Bounds", "\"Score\" >= 0 AND \"MaximumScore\" > 0 AND \"Score\" <= \"MaximumScore\" AND \"Confidence\" BETWEEN 0 AND 1")); builder.ConfigureBaseEntity(); builder.Property(x => x.Score).HasPrecision(9, 2); builder.Property(x => x.MaximumScore).HasPrecision(9, 2); builder.Property(x => x.Confidence).HasPrecision(5, 4); builder.Property(x => x.Feedback).HasMaxLength(8000).IsRequired(); builder.HasIndex(x => x.SubmissionId).IsUnique(); builder.HasOne<AcceleratorProjectSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AcceleratorProjectEvaluationScoreConfiguration : IEntityTypeConfiguration<AcceleratorProjectEvaluationScore>
{
    public void Configure(EntityTypeBuilder<AcceleratorProjectEvaluationScore> builder)
    {
        builder.ToTable("AcceleratorProjectEvaluationScores", t => t.HasCheckConstraint("CK_AcceleratorProjectEvaluationScore_Nonnegative", "\"Score\" >= 0")); builder.ConfigureBaseEntity(); builder.Property(x => x.Score).HasPrecision(9, 2); builder.Property(x => x.Feedback).HasMaxLength(4000).IsRequired(); builder.HasIndex(x => new { x.EvaluationId, x.CriterionId }).IsUnique(); builder.HasOne<AcceleratorProjectEvaluation>().WithMany(x => x.Scores).HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorProjectRubricCriterion>().WithMany().HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
    }
}
