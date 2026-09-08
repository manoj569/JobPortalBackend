using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

#pragma warning disable CA1725

public sealed class AIApplyProfileConfiguration : IEntityTypeConfiguration<AIApplyProfile>
{
    public void Configure(EntityTypeBuilder<AIApplyProfile> b) { b.ToTable("AIApplyProfiles"); b.ConfigureBaseEntity(); b.Property(x => x.GitHubUrl).HasMaxLength(2048); b.Property(x => x.WorkAuthorization).HasMaxLength(200); b.Property(x => x.VisaSponsorshipPreference).HasMaxLength(200); b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplyPreferenceConfiguration : IEntityTypeConfiguration<AIApplyPreference>
{
    public void Configure(EntityTypeBuilder<AIApplyPreference> b) { b.ToTable("AIApplyPreferences"); b.ConfigureBaseEntity(); b.Property(x => x.JobTitlesJson).HasColumnType("text"); b.Property(x => x.SkillsJson).HasColumnType("text"); b.Property(x => x.PreferredLocationsJson).HasColumnType("text"); b.Property(x => x.WorkplaceTypesJson).HasColumnType("text"); b.Property(x => x.EmploymentTypesJson).HasColumnType("text"); b.Property(x => x.MinimumExperience).HasPrecision(4,1); b.Property(x => x.MaximumExperience).HasPrecision(4,1); b.Property(x => x.MinimumSalary).HasPrecision(14,2); b.Property(x => x.MaximumSalary).HasPrecision(14,2); b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplySettingConfiguration : IEntityTypeConfiguration<AIApplySetting>
{
    public void Configure(EntityTypeBuilder<AIApplySetting> b) { b.ToTable("AIApplySettings"); b.ConfigureBaseEntity(); b.Property(x => x.Timezone).HasMaxLength(100); b.Property(x => x.PreferredDaysJson).HasColumnType("text"); b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasIndex(x => new { x.Enabled, x.Paused }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplyRuleConfiguration : IEntityTypeConfiguration<AIApplyRule>
{
    public void Configure(EntityTypeBuilder<AIApplyRule> b) { b.ToTable("AIApplyRules"); b.ConfigureBaseEntity(); b.Property(x => x.Value).HasMaxLength(1000); b.HasIndex(x => new { x.UserId, x.IsEnabled }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplyApplicationConfiguration : IEntityTypeConfiguration<AIApplyApplication>
{
    public void Configure(EntityTypeBuilder<AIApplyApplication> b) { b.ToTable("AIApplyApplications"); b.ConfigureBaseEntity(); b.Property(x => x.ResumeStorageKey).HasMaxLength(255); b.Property(x => x.ResumeFileName).HasMaxLength(255); b.Property(x => x.ResumeContentType).HasMaxLength(100); b.Property(x => x.ExternalApplicationUrl).HasMaxLength(2048); b.Property(x => x.NormalizedApplicationUrl).HasMaxLength(2048); b.Property(x => x.LastErrorCode).HasMaxLength(100); b.Property(x => x.LeaseOwner).HasMaxLength(100); b.Property(x => x.FailureClassification).HasMaxLength(50); b.Property(x => x.MatchScore).HasPrecision(5,2); b.HasIndex(x => new { x.UserId, x.JobId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasIndex(x => new { x.UserId, x.NormalizedApplicationUrl }).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasIndex(x => new { x.Status, x.ScheduledAtUtc, x.Priority }); b.HasIndex(x => new { x.Status, x.LeaseExpiresAtUtc }); b.HasIndex(x => new { x.UserId, x.Status, x.CreatedAtUtc }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); b.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplyQuestionConfiguration : IEntityTypeConfiguration<AIApplyQuestion>
{
    public void Configure(EntityTypeBuilder<AIApplyQuestion> b) { b.ToTable("AIApplyQuestions"); b.ConfigureBaseEntity(); b.Property(x => x.Question).HasMaxLength(2000); b.Property(x => x.NormalizedQuestion).HasMaxLength(1000); b.Property(x => x.QuestionType).HasMaxLength(50); b.Property(x => x.SuggestedAnswer).HasMaxLength(4000); b.Property(x => x.FinalAnswer).HasMaxLength(4000); b.HasIndex(x => new { x.UserId, x.Status, x.CreatedAtUtc }); b.HasIndex(x => x.ApplicationId); b.HasOne<AIApplyApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class UserApplicationAnswerConfiguration : IEntityTypeConfiguration<UserApplicationAnswer>
{
    public void Configure(EntityTypeBuilder<UserApplicationAnswer> b) { b.ToTable("UserApplicationAnswers"); b.ConfigureBaseEntity(); b.Property(x => x.Question).HasMaxLength(2000); b.Property(x => x.NormalizedQuestion).HasMaxLength(1000); b.Property(x => x.Answer).HasMaxLength(4000); b.Property(x => x.Category).HasMaxLength(100); b.Property(x => x.Confidence).HasPrecision(5,4); b.HasIndex(x => new { x.UserId, x.NormalizedQuestion }).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasIndex(x => new { x.UserId, x.IsActive }); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplyExecutionLogConfiguration : IEntityTypeConfiguration<AIApplyExecutionLog>
{
    public void Configure(EntityTypeBuilder<AIApplyExecutionLog> b) { b.ToTable("AIApplyExecutionLogs"); b.ConfigureBaseEntity(); b.Property(x => x.MessageCode).HasMaxLength(100); b.Property(x => x.MetadataJson).HasColumnType("text"); b.HasIndex(x => new { x.ApplicationId, x.CreatedAtUtc }); b.HasOne<AIApplyApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class AIApplyCostConfiguration : IEntityTypeConfiguration<AIApplyCost>
{
    public void Configure(EntityTypeBuilder<AIApplyCost> b) { b.ToTable("AIApplyCosts"); b.ConfigureBaseEntity(); b.Property(x => x.BrowserExecutionSeconds).HasPrecision(12,3); b.Property(x => x.ProxyCost).HasPrecision(18,6); b.Property(x => x.EstimatedCost).HasPrecision(18,6); b.HasIndex(x => x.ApplicationId).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); b.HasIndex(x => new { x.UserId, x.CreatedAtUtc }); b.HasOne<AIApplyApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class ExternalJobSiteSessionConfiguration : IEntityTypeConfiguration<ExternalJobSiteSession>
{
    public void Configure(EntityTypeBuilder<ExternalJobSiteSession> b)
    {
        b.ToTable("ExternalJobSiteSessions");
        b.ConfigureBaseEntity();
        b.Property(x => x.EncryptedStorageState).HasColumnType("bytea").IsRequired();
        b.Property(x => x.EncryptionPurposeVersion).HasMaxLength(16).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.Site }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        b.HasIndex(x => x.ExpiresAtUtc);
        b.HasIndex(x => x.Status);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AIApplyWorkerInstanceConfiguration : IEntityTypeConfiguration<AIApplyWorkerInstance>
{
    public void Configure(EntityTypeBuilder<AIApplyWorkerInstance> b)
    {
        b.ToTable("AIApplyWorkerInstances"); b.ConfigureBaseEntity();
        b.Property(x => x.WorkerInstanceId).HasMaxLength(100).IsRequired();
        b.Property(x => x.HostVersion).HasMaxLength(64).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.WorkerInstanceId).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        b.HasIndex(x => new { x.Status, x.LastHeartbeatAtUtc });
    }
}

public sealed class AIApplySiteOperationalStateConfiguration : IEntityTypeConfiguration<AIApplySiteOperationalState>
{
    public void Configure(EntityTypeBuilder<AIApplySiteOperationalState> b)
    {
        b.ToTable("AIApplySiteOperationalStates"); b.ConfigureBaseEntity();
        b.Property(x => x.HalfOpenProbeOwner).HasMaxLength(100);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Site).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        b.HasIndex(x => new { x.CircuitState, x.CooldownUntilUtc });
    }
}
