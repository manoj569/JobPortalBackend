using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Context;

public sealed class JobPortalDbContext(DbContextOptions<JobPortalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<CandidateResumeProfile> CandidateResumeProfiles => Set<CandidateResumeProfile>();
    public DbSet<CandidateSkill> CandidateSkills => Set<CandidateSkill>();
    public DbSet<CandidateProfilePhoto> CandidateProfilePhotos => Set<CandidateProfilePhoto>();
    public DbSet<CandidatePortfolio> CandidatePortfolios => Set<CandidatePortfolio>();
    public DbSet<PortfolioSectionSetting> PortfolioSectionSettings => Set<PortfolioSectionSetting>();
    public DbSet<CandidateExperience> CandidateExperiences => Set<CandidateExperience>();
    public DbSet<CandidateEducation> CandidateEducation => Set<CandidateEducation>();
    public DbSet<CandidateProject> CandidateProjects => Set<CandidateProject>();
    public DbSet<CandidateCertification> CandidateCertifications => Set<CandidateCertification>();
    public DbSet<CandidateProfessionalLink> CandidateProfessionalLinks => Set<CandidateProfessionalLink>();
    public DbSet<PortfolioCustomSection> PortfolioCustomSections => Set<PortfolioCustomSection>();
    public DbSet<PortfolioCustomItem> PortfolioCustomItems => Set<PortfolioCustomItem>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RegistrationEmailRequest> RegistrationEmailRequests => Set<RegistrationEmailRequest>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentHistory> PaymentHistories => Set<PaymentHistory>();
    public DbSet<MembershipHistory> MembershipHistories => Set<MembershipHistory>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobDiscoveryRun> JobDiscoveryRuns => Set<JobDiscoveryRun>();
    public DbSet<JobDiscoveryItem> JobDiscoveryItems => Set<JobDiscoveryItem>();
    public DbSet<JobRecruiterContact> JobRecruiterContacts => Set<JobRecruiterContact>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<SavedJob> SavedJobs => Set<SavedJob>();
    public DbSet<UserJobHistory> UserJobHistories => Set<UserJobHistory>();

    // ✅ Notifications Table
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Setting => Set<Setting>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationQuotaUsage> ApplicationQuotaUsages => Set<ApplicationQuotaUsage>();
    public DbSet<JobApplicationStatusHistory> JobApplicationStatusHistory => Set<JobApplicationStatusHistory>();
    public DbSet<InterviewInsight> InterviewInsights => Set<InterviewInsight>();
    public DbSet<InterviewRound> InterviewRounds => Set<InterviewRound>();
    public DbSet<CandidateInterviewSchedule> CandidateInterviewSchedules => Set<CandidateInterviewSchedule>();
    public DbSet<InsightHelpfulnessFeedback> InsightHelpfulnessFeedback => Set<InsightHelpfulnessFeedback>();
    public DbSet<InsightReport> InsightReports => Set<InsightReport>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditLogsAreAppendOnly();
        ApplyAuditAndSoftDelete();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditLogsAreAppendOnly();
        ApplyAuditAndSoftDelete();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobPortalDbContext).Assembly);

        // ✅ Configure Notifications table
        modelBuilder.Entity<Notification>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Restrict); // Prevents cascade delete

        // ✅ High-performance Index for fetching user notifications
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead });

        base.OnModelCreating(modelBuilder);
    }

    private void ApplyAuditAndSoftDelete()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAtUtc = utcNow;
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }
    }

    private void EnsureAuditLogsAreAppendOnly()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException(
                "Audit logs are append-only and cannot be updated or deleted.");
    }
}
