using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable CA1725

namespace JobPortal.Persistence.Configurations;

internal static class EntityTypeBuilderExtensions
{
    public static void ConfigureBaseEntity<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : BaseEntity
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        builder.HasIndex(entity => entity.IsDeleted);
        builder.HasQueryFilter(entity => !entity.IsDeleted);
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.NormalizedPhoneNumber).HasMaxLength(13);
        builder.Property(x => x.TermsAndPrivacyVersion).HasMaxLength(32);
        builder.Property(x => x.ProfileImageUrl).HasMaxLength(2048);
        builder.Property(x => x.Headline).HasMaxLength(250);
        builder.Property(x => x.Bio).HasMaxLength(4000);
        builder.Property(x => x.Location).HasMaxLength(250);
        builder.Property(x => x.LinkedInUrl).HasMaxLength(2048);
        builder.Property(x => x.PortfolioUrl).HasMaxLength(2048);
        builder.Property(x => x.SkillsJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.EducationJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.ExperienceJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.PreferredJobTypesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.CurrentCountry).HasMaxLength(100);
        builder.Property(x => x.CurrentCity).HasMaxLength(150);
        builder.Property(x => x.CurrentArea).HasMaxLength(150);
        builder.Property(x => x.CurrentAnnualSalary).HasPrecision(14, 2);
        builder.Property(x => x.CurrentFixedAnnualSalary).HasPrecision(14, 2);
        builder.Property(x => x.CurrentVariableAnnualSalary).HasPrecision(14, 2);
        builder.Property(x => x.PreferredJobRolesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.PreferredCitiesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.ExpectedAnnualSalary).HasPrecision(14, 2);
        builder.Property(x => x.CandidateJobTypesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.CandidateEmploymentTypesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.PreferredShiftsJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.DesiredOpportunitiesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.WorkPreferencesJson).HasColumnType("text").HasDefaultValue("[]").IsRequired();
        builder.Property(x => x.College).HasMaxLength(200);
        builder.Property(x => x.Degree).HasMaxLength(200);
        builder.Property(x => x.YearsOfExperience).HasPrecision(4, 1);
        builder.Property(x => x.ResumeStorageKey).HasMaxLength(255);
        builder.Property(x => x.ResumeFileName).HasMaxLength(255);
        builder.Property(x => x.ResumeContentType).HasMaxLength(100);
        builder.Property(x => x.PasswordResetTokenHash).HasMaxLength(64);
        builder.HasIndex(x => x.PasswordResetTokenHash)
            .IsUnique()
            .HasFilter("\"PasswordResetTokenHash\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.Property(x => x.EmailVerificationTokenHash).HasMaxLength(64);
        builder.HasIndex(x => x.EmailVerificationTokenHash).IsUnique()
            .HasFilter("\"EmailVerificationTokenHash\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.NormalizedPhoneNumber).IsUnique()
            .HasFilter("\"NormalizedPhoneNumber\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.IsDeleted });
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasOne(x => x.Role).WithMany(x => x.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CandidateResumeProfileConfiguration : IEntityTypeConfiguration<CandidateResumeProfile>
{
    public void Configure(EntityTypeBuilder<CandidateResumeProfile> builder)
    {
        builder.ToTable("CandidateResumeProfiles");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.SkillsJson).HasColumnType("varchar(4000)").IsRequired();
        builder.Property(x => x.RoleKeywordsJson).HasColumnType("varchar(2000)").IsRequired();
        builder.Property(x => x.EducationKeywordsJson).HasColumnType("varchar(2000)").IsRequired();
        builder.Property(x => x.LocationsJson).HasColumnType("varchar(2000)").IsRequired();
        builder.Property(x => x.YearsOfExperience).HasPrecision(4, 1);
        builder.Property(x => x.ExtractionError).HasMaxLength(1000);
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.ExtractionStatus, x.ExtractedAtUtc });
        builder.HasOne(x => x.User).WithOne(x => x.ResumeProfile).HasForeignKey<CandidateResumeProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasData(
            new Role { Id = SystemRoleIds.Administrator, Name = "Administrator", NormalizedName = "ADMINISTRATOR", Description = "System administrator", CreatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Role { Id = SystemRoleIds.Employer, Name = "Employer", NormalizedName = "EMPLOYER", Description = "Company employer", CreatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Role { Id = SystemRoleIds.Candidate, Name = "Candidate", NormalizedName = "CANDIDATE", Description = "Job candidate", CreatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Token).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ReplacedByToken).HasMaxLength(512);
        builder.Property(x => x.CreatedByIp).HasMaxLength(45);
        builder.Property(x => x.RevokedByIp).HasMaxLength(45);
        builder.HasIndex(x => x.Token).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
        builder.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.WebsiteUrl).HasMaxLength(2048);
        builder.Property(x => x.LogoUrl).HasMaxLength(2048);
        builder.Property(x => x.Industry).HasMaxLength(150);
        builder.Property(x => x.Location).HasMaxLength(250);
        builder.HasIndex(x => new { x.CompanyType, x.Industry });
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => new { x.SubmittedByCandidateId, x.CreatedAtUtc });
        builder.HasOne(x => x.OwnerUser).WithMany(x => x.OwnedCompanies).HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubmittedByCandidate).WithMany(x => x.SubmittedCompanies)
            .HasForeignKey(x => x.SubmittedByCandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(170).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.ParentCategoryId, x.DisplayOrder });
        builder.HasOne(x => x.ParentCategory).WithMany(x => x.Children).HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs", table =>
        {
            table.HasCheckConstraint("CK_Jobs_SalaryRange", "\"MinimumSalary\" IS NULL OR \"MaximumSalary\" IS NULL OR \"MinimumSalary\" <= \"MaximumSalary\"");
            table.HasCheckConstraint("CK_Jobs_ExperienceRange", "\"MinimumExperienceYears\" IS NULL OR \"MaximumExperienceYears\" IS NULL OR \"MinimumExperienceYears\" <= \"MaximumExperienceYears\"");
            table.HasCheckConstraint("CK_Jobs_InternshipDuration", "\"InternshipDurationMonths\" IS NULL OR \"InternshipDurationMonths\" IN (1, 2, 3, 6)");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.ReferenceNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(270).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(16000).IsRequired();
        builder.Property(x => x.Responsibilities).HasMaxLength(8000);
        builder.Property(x => x.Requirements).HasMaxLength(8000);
        builder.Property(x => x.Benefits).HasMaxLength(4000);
        builder.Property(x => x.ApplicationUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Location).HasMaxLength(250);
        builder.Property(x => x.MinimumSalary).HasPrecision(18, 2);
        builder.Property(x => x.MaximumSalary).HasPrecision(18, 2);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Department).HasMaxLength(150);
        builder.Property(x => x.RoleCategory).HasMaxLength(150);
        builder.Property(x => x.EducationRequirement).HasMaxLength(200);
        builder.HasIndex(x => x.ReferenceNumber).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.PublishedAtUtc });
        builder.HasIndex(x => new { x.CategoryId, x.Status });
        builder.HasIndex(x => new { x.Status, x.IsFeatured, x.IsHidden, x.PublishedAtUtc });
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
        builder.HasIndex(x => new { x.Status, x.WorkplaceType, x.EmploymentType });
        builder.HasIndex(x => new { x.Status, x.PostedByType });
        builder.HasIndex(x => x.Department);
        builder.HasIndex(x => x.RoleCategory);
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasOne(x => x.Company).WithMany(x => x.Jobs).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Category).WithMany(x => x.Jobs).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobRecruiterContactConfiguration
    : IEntityTypeConfiguration<JobRecruiterContact>
{
    public void Configure(EntityTypeBuilder<JobRecruiterContact> builder)
    {
        builder.ToTable("JobRecruiterContacts");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.ContactName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.ContactRole)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(32);

        builder.HasIndex(x => x.JobId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");

        builder.HasOne(x => x.Job)
            .WithOne(x => x.RecruiterContact)
            .HasForeignKey<JobRecruiterContact>(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("Skills");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
    }
}

public sealed class JobSkillConfiguration : IEntityTypeConfiguration<JobSkill>
{
    public void Configure(EntityTypeBuilder<JobSkill> builder)
    {
        builder.ToTable("JobSkills", table => table.HasCheckConstraint("CK_JobSkills_ProficiencyLevel", "\"ProficiencyLevel\" BETWEEN 1 AND 5"));
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.JobId, x.SkillId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.Job).WithMany(x => x.JobSkills).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Skill).WithMany(x => x.JobSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.PlanName).HasMaxLength(100).IsRequired();
        builder.Ignore(x => x.RowVersion);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Status, x.EndsAtUtc });
        builder.HasOne(x => x.User).WithMany(x => x.Memberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table => table.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" >= 0"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Ignore(x => x.RowVersion);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TransactionReference).HasMaxLength(100);
        builder.Property(x => x.ProviderPaymentId).HasMaxLength(200);
        builder.Property(x => x.ProviderOrderId).HasMaxLength(200);
        builder.Property(x => x.ProviderReceipt).HasMaxLength(100);
        builder.HasIndex(x => x.ProviderPaymentId).IsUnique().HasFilter("\"ProviderPaymentId\" IS NOT NULL");
        builder.HasIndex(x => x.ProviderOrderId).IsUnique().HasFilter("\"ProviderOrderId\" IS NOT NULL");
        builder.HasIndex(x => new { x.UserId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.PaidAtUtc, x.CurrencyCode });
        builder.HasIndex(x => new { x.Status, x.UserId });
        builder.HasIndex(x => new { x.Status, x.ProviderOrderCreatedAtUtc });
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.MembershipId);
        builder.HasOne(x => x.User).WithMany(x => x.Payments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Membership).WithMany(x => x.Payments).HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MembershipHistoryConfiguration : IEntityTypeConfiguration<MembershipHistory>
{
    public void Configure(EntityTypeBuilder<MembershipHistory> builder)
    {
        builder.ToTable("MembershipHistory");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.MembershipId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        builder.HasOne(x => x.Membership).WithMany(x => x.History).HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentHistoryConfiguration : IEntityTypeConfiguration<PaymentHistory>
{
    public void Configure(EntityTypeBuilder<PaymentHistory> builder)
    {
        builder.ToTable("PaymentHistory");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.ProviderEventId).HasMaxLength(200);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.PaymentId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        builder.HasIndex(x => x.ProviderEventId).IsUnique().HasFilter("\"ProviderEventId\" IS NOT NULL");
        builder.HasOne(x => x.Payment).WithMany(x => x.History).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SavedJobConfiguration : IEntityTypeConfiguration<SavedJob>
{
    public void Configure(EntityTypeBuilder<SavedJob> builder)
    {
        builder.ToTable("SavedJobs");
        builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.UserId, x.JobId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasOne(x => x.User).WithMany(x => x.SavedJobs).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany(x => x.SavedByUsers).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserJobHistoryConfiguration : IEntityTypeConfiguration<UserJobHistory>
{
    public void Configure(EntityTypeBuilder<UserJobHistory> builder)
    {
        builder.ToTable("UserJobHistories");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.UserId, x.JobId, x.Action, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.JobId, x.Action, x.OccurredAtUtc });
        builder.HasOne(x => x.User).WithMany(x => x.JobHistory).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany(x => x.UserHistory).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("JobApplications");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.CoverLetter).HasMaxLength(5000);
        builder.Property(x => x.ResumeStorageKey).HasMaxLength(255);
        builder.Property(x => x.ResumeFileName).HasMaxLength(255);
        builder.Property(x => x.ResumeContentType).HasMaxLength(100);
        builder.HasIndex(x => new { x.UserId, x.JobId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.UserId, x.Status, x.SubmittedAtUtc });
        builder.HasOne(x => x.User).WithMany(x => x.JobApplications)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany(x => x.Applications)
            .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobApplicationStatusHistoryConfiguration :
    IEntityTypeConfiguration<JobApplicationStatusHistory>
{
    public void Configure(EntityTypeBuilder<JobApplicationStatusHistory> builder)
    {
        builder.ToTable("JobApplicationStatusHistory");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.InternalNote).HasMaxLength(2000);
        builder.HasIndex(x => new { x.ApplicationId, x.ChangedAtUtc });
        builder.HasIndex(x => new { x.ActorUserId, x.ChangedAtUtc });
        builder.HasOne(x => x.Application).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobDiscoveryRunConfiguration : IEntityTypeConfiguration<JobDiscoveryRun>
{
    public void Configure(EntityTypeBuilder<JobDiscoveryRun> b)
    {
        b.ToTable("JobDiscoveryRuns"); b.ConfigureBaseEntity();
        b.Property(x => x.Trigger).HasMaxLength(32).IsRequired(); b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.ErrorSummary).HasMaxLength(2000); b.HasIndex(x => x.StartedAtUtc);
    }
}

public sealed class JobDiscoveryItemConfiguration : IEntityTypeConfiguration<JobDiscoveryItem>
{
    public void Configure(EntityTypeBuilder<JobDiscoveryItem> b)
    {
        b.ToTable("JobDiscoveryItems"); b.ConfigureBaseEntity();
        b.Property(x => x.Provider).HasMaxLength(64).IsRequired(); b.Property(x => x.SourceJobId).HasMaxLength(256).IsRequired();
        b.Property(x => x.Title).HasMaxLength(300).IsRequired(); b.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        b.Property(x => x.CategoryName).HasMaxLength(200).IsRequired(); b.Property(x => x.ApplicationUrl).HasMaxLength(2048).IsRequired();
        b.Property(x => x.Location).HasMaxLength(300); b.Property(x => x.EmploymentType).HasMaxLength(50);
        b.Property(x => x.Status).HasMaxLength(32).IsRequired(); b.Property(x => x.DuplicateReason).HasMaxLength(64);
        b.HasIndex(x => new { x.Provider, x.SourceJobId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        b.HasIndex(x => x.RunId); b.HasOne(x => x.Run).WithMany(x => x.Items).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ActionUrl).HasMaxLength(2048);

        // ✅ We add an index to speed up queries filtering by read time
        builder.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });

        builder.HasOne(x => x.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.EntityName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ChangesJson).HasColumnType("text");
        builder.Property(x => x.ActorRole).HasMaxLength(50);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.UserAgent).HasMaxLength(1024);
        builder.HasIndex(x => new { x.EntityName, x.EntityId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Action, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.CorrelationId, x.CreatedAtUtc });
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasOne(x => x.User).WithMany(x => x.AuditLogs).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => new { x.Scope, x.Key }).IsUnique().HasFilter("\"UserId\" IS NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Scope, x.UserId, x.Key }).IsUnique().HasFilter("\"UserId\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasOne(x => x.User).WithMany(x => x.Settings).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
    public sealed class ApplicationQuotaUsageConfiguration
    : IEntityTypeConfiguration<ApplicationQuotaUsage>
    {
        public void Configure(EntityTypeBuilder<ApplicationQuotaUsage> builder)
        {
            builder.ToTable(
                "ApplicationQuotaUsages",
                table => table.HasCheckConstraint(
                    "CK_ApplicationQuotaUsages_UsedApplications",
                    "\"UsedApplications\" >= 0"));

            builder.ConfigureBaseEntity();

            builder.Property(x => x.Period).IsRequired();

            builder.Property(x => x.PeriodStartsAtUtc).IsRequired();

            builder.Property(x => x.PeriodEndsAtUtc).IsRequired();

            builder.Property(x => x.UsedApplications).IsRequired();

            builder.Ignore(x => x.RowVersion);
            builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();

            builder.HasIndex(x => new { x.UserId, x.Period, x.PeriodStartsAtUtc })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");

            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
