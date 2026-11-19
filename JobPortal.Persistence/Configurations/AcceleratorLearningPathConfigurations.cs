using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class AcceleratorLearningPathConfiguration : IEntityTypeConfiguration<AcceleratorLearningPath>
{
    public void Configure(EntityTypeBuilder<AcceleratorLearningPath> builder)
    {
        builder.ToTable("AcceleratorLearningPaths", table =>
        {
            table.HasCheckConstraint("CK_AcceleratorLearningPath_Bounds", "\"Difficulty\" BETWEEN 1 AND 5 AND \"EstimatedMinutes\" >= 0");
            table.HasCheckConstraint("CK_AcceleratorLearningPath_CanonicalReference", "(\"Type\" = 1 AND \"CanonicalCareerRoleId\" IS NOT NULL AND \"CanonicalSkillId\" IS NULL) OR (\"Type\" = 2 AND \"CanonicalSkillId\" IS NOT NULL AND \"CanonicalCareerRoleId\" IS NULL)");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Slug).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.ShortDescription).HasMaxLength(800).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => new { x.Type, x.Status, x.DisplayOrder });
        builder.HasOne(x => x.CanonicalCareerRole).WithMany().HasForeignKey(x => x.CanonicalCareerRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CanonicalSkill).WithMany().HasForeignKey(x => x.CanonicalSkillId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLearningModuleConfiguration : IEntityTypeConfiguration<AcceleratorLearningModule>
{
    public void Configure(EntityTypeBuilder<AcceleratorLearningModule> builder)
    {
        builder.ToTable("AcceleratorLearningModules", table => table.HasCheckConstraint("CK_AcceleratorLearningModule_Bounds", "\"Sequence\" >= 1 AND \"Difficulty\" BETWEEN 1 AND 5 AND \"EstimatedMinutes\" >= 0"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.LearningPathId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.LearningPathId, x.Status });
        builder.HasOne(x => x.LearningPath).WithMany(x => x.Modules).HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorAssessmentTemplate>().WithMany().HasForeignKey(x => x.AssessmentTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorProjectVersion>().WithMany().HasForeignKey(x => x.ProjectVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorInterviewVersion>().WithMany().HasForeignKey(x => x.InterviewVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLearningModulePrerequisiteConfiguration : IEntityTypeConfiguration<AcceleratorLearningModulePrerequisite>
{
    public void Configure(EntityTypeBuilder<AcceleratorLearningModulePrerequisite> builder)
    {
        builder.ToTable("AcceleratorLearningModulePrerequisites", table => table.HasCheckConstraint("CK_AcceleratorLearningModulePrerequisite_NotSelf", "\"ModuleId\" <> \"RequiredModuleId\""));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.ModuleId, x.RequiredModuleId }).IsUnique();
        builder.HasIndex(x => new { x.LearningPathId, x.RequiredModuleId });
        builder.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequiredModule).WithMany().HasForeignKey(x => x.RequiredModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorLearningPath>().WithMany(x => x.ModulePrerequisites).HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLessonConfiguration : IEntityTypeConfiguration<AcceleratorLesson>
{
    public void Configure(EntityTypeBuilder<AcceleratorLesson> builder)
    {
        builder.ToTable("AcceleratorLessons", table => table.HasCheckConstraint("CK_AcceleratorLesson_Bounds", "\"Sequence\" >= 1 AND \"EstimatedMinutes\" >= 0"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(64000).IsRequired();
        builder.HasIndex(x => new { x.ModuleId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.ModuleId, x.Status });
        builder.HasOne(x => x.Module).WithMany(x => x.Lessons).HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLessonResourceConfiguration : IEntityTypeConfiguration<AcceleratorLessonResource>
{
    public void Configure(EntityTypeBuilder<AcceleratorLessonResource> builder)
    {
        builder.ToTable("AcceleratorLessonResources"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired(); builder.Property(x => x.Url).HasMaxLength(2048).IsRequired(); builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        builder.HasIndex(x => new { x.LessonId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Lesson).WithMany(x => x.Resources).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorPathEnrollmentConfiguration : IEntityTypeConfiguration<AcceleratorPathEnrollment>
{
    public void Configure(EntityTypeBuilder<AcceleratorPathEnrollment> builder)
    {
        builder.ToTable("AcceleratorPathEnrollments", table => table.HasCheckConstraint("CK_AcceleratorPathEnrollment_Bounds", "\"CurrentLevel\" BETWEEN 0 AND 5 AND \"TargetLevel\" BETWEEN 1 AND 5 AND \"WeeklyCommitmentMinutes\" BETWEEN 1 AND 10080 AND \"OverallProgressPercent\" BETWEEN 0 AND 100"));
        builder.ConfigureBaseEntity(); builder.Property(x => x.Revision).IsConcurrencyToken(); builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.LearningPathId }).IsUnique().HasFilter("\"IsDeleted\" = false AND \"Status\" IN (1, 2, 4)");
        builder.HasIndex(x => new { x.UserId, x.Status, x.LastActivityAtUtc });
        builder.HasOne(x => x.LearningPath).WithMany().HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorLearningModule>().WithMany().HasForeignKey(x => x.CurrentModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorLesson>().WithMany().HasForeignKey(x => x.CurrentLessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLessonProgressConfiguration : IEntityTypeConfiguration<AcceleratorLessonProgress>
{
    public void Configure(EntityTypeBuilder<AcceleratorLessonProgress> builder)
    {
        builder.ToTable("AcceleratorLessonProgress"); builder.ConfigureBaseEntity(); builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.EnrollmentId, x.LessonId }).IsUnique();
        builder.HasIndex(x => new { x.EnrollmentId, x.Status });
        builder.HasOne(x => x.Enrollment).WithMany(x => x.LessonProgress).HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorEnrollmentProgressEventConfiguration : IEntityTypeConfiguration<AcceleratorEnrollmentProgressEvent>
{
    public void Configure(EntityTypeBuilder<AcceleratorEnrollmentProgressEvent> builder)
    {
        builder.ToTable("AcceleratorEnrollmentProgressEvents"); builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.EnrollmentId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => new { x.EnrollmentId, x.OccurredAtUtc });
        builder.HasOne(x => x.Enrollment).WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorLesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLearningPathCompetencyRequirementConfiguration : IEntityTypeConfiguration<AcceleratorLearningPathCompetencyRequirement>
{
    public void Configure(EntityTypeBuilder<AcceleratorLearningPathCompetencyRequirement> builder)
    {
        builder.ToTable("AcceleratorLearningPathCompetencyRequirements", t => t.HasCheckConstraint("CK_AcceleratorLearningPathCompetencyRequirement_Bounds", "\"TargetProficiency\" BETWEEN 1 AND 5 AND \"Weight\" > 0")); builder.ConfigureBaseEntity(); builder.Property(x => x.Weight).HasPrecision(9, 4);
        builder.HasIndex(x => new { x.LearningPathId, x.CompetencyId }).IsUnique(); builder.HasOne<AcceleratorLearningPath>().WithMany().HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorCompetency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcceleratorLearningPathAssessmentConfiguration : IEntityTypeConfiguration<AcceleratorLearningPathAssessment>
{
    public void Configure(EntityTypeBuilder<AcceleratorLearningPathAssessment> builder)
    {
        builder.ToTable("AcceleratorLearningPathAssessments"); builder.ConfigureBaseEntity(); builder.HasIndex(x => new { x.LearningPathId, x.Purpose }).IsUnique(); builder.HasOne<AcceleratorLearningPath>().WithMany().HasForeignKey(x => x.LearningPathId).OnDelete(DeleteBehavior.Restrict); builder.HasOne<AcceleratorAssessmentTemplate>().WithMany().HasForeignKey(x => x.AssessmentTemplateId).OnDelete(DeleteBehavior.Restrict);
    }
}
