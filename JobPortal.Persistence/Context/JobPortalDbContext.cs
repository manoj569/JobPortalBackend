using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Application.Abstractions.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Context;

public sealed class JobPortalDbContext(DbContextOptions<JobPortalDbContext> options) : DbContext(options)
{
    public DbSet<CareerConsultant> CareerConsultants => Set<CareerConsultant>();
    public DbSet<CareerConsultantAvailability> CareerConsultantAvailability => Set<CareerConsultantAvailability>();
    public DbSet<CareerConsultantAvailabilityException> CareerConsultantAvailabilityExceptions => Set<CareerConsultantAvailabilityException>();
    public DbSet<CareerGuidanceBooking> CareerGuidanceBookings => Set<CareerGuidanceBooking>();
    public DbSet<CareerConsultantTag> CareerConsultantTags => Set<CareerConsultantTag>();
    public DbSet<CareerConsultantService> CareerConsultantServices => Set<CareerConsultantService>();
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
    public DbSet<JobReferral> JobReferrals => Set<JobReferral>();
    public DbSet<ReferralUnlock> ReferralUnlocks => Set<ReferralUnlock>();
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
    public DbSet<AIApplyProfile> AIApplyProfiles => Set<AIApplyProfile>();
    public DbSet<AIApplyPreference> AIApplyPreferences => Set<AIApplyPreference>();
    public DbSet<AIApplySetting> AIApplySettings => Set<AIApplySetting>();
    public DbSet<AIApplyRule> AIApplyRules => Set<AIApplyRule>();
    public DbSet<AIApplyApplication> AIApplyApplications => Set<AIApplyApplication>();
    public DbSet<AIApplyQuestion> AIApplyQuestions => Set<AIApplyQuestion>();
    public DbSet<UserApplicationAnswer> UserApplicationAnswers => Set<UserApplicationAnswer>();
    public DbSet<AIApplyExecutionLog> AIApplyExecutionLogs => Set<AIApplyExecutionLog>();
    public DbSet<AIApplyCost> AIApplyCosts => Set<AIApplyCost>();
    public DbSet<ExternalJobSiteSession> ExternalJobSiteSessions => Set<ExternalJobSiteSession>();
    public DbSet<AIApplyWorkerInstance> AIApplyWorkerInstances => Set<AIApplyWorkerInstance>();
    public DbSet<AIApplySiteOperationalState> AIApplySiteOperationalStates => Set<AIApplySiteOperationalState>();
    public DbSet<JobSource> JobSources => Set<JobSource>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureFinancialHistory();
        EnsureTrustHistory();
        EnsureAuditLogsAreAppendOnly();
        ApplyAuditAndSoftDelete();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureFinancialHistory();
        EnsureTrustHistory();
        EnsureAuditLogsAreAppendOnly();
        ApplyAuditAndSoftDelete();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobPortalDbContext).Assembly);
        if (Database.IsNpgsql()) modelBuilder.HasPostgresExtension("btree_gist");

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
            if (entry.Entity is Job job && entry.State is EntityState.Added or EntityState.Modified)
                job.CanonicalApplicationUrlHash = ApplicationUrlIdentity.Hash(job.ApplicationUrl);
            if (entry.Entity is Company company && entry.State is EntityState.Added or EntityState.Modified)
                company.NormalizedName = CompanyNameNormalizer.Normalize(company.Name);
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

    private void EnsureTrustHistory()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is CareerGuidanceReview or CareerGuidanceDispute or CareerGuidanceDisputeEvidence))
        {
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && entry.Entity is CareerGuidanceDisputeEvidence)
                throw new InvalidOperationException("Trust records cannot be deleted; evidence is append-only.");
            if (entry.State != EntityState.Modified) continue;
            foreach (var property in entry.Properties.Where(p => p.IsModified))
            {
                var allowed = property.Metadata.Name is "UpdatedAtUtc" or "Revision" || entry.Entity switch
                {
                    CareerGuidanceReview => property.Metadata.Name is "Rating" or "Title" or "Comment" or "ModerationStatus" or "ModerationReason" or "IsPublished" or "IsDeleted" or "DeletedAtUtc",
                    CareerGuidanceDispute => property.Metadata.Name is "Status" or "Resolution" or "AdminNotes" or "ResolvedAtUtc" or "ResolvedByUserId",
                    _ => false
                };
                if (!allowed) throw new InvalidOperationException("Trust identity and original submission are immutable.");
            }
        }
    }

    private void EnsureFinancialHistory()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is CareerGuidancePayment or CareerGuidanceEarning or CareerGuidanceRefund or CareerGuidancePaymentEvent))
        {
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && entry.Entity is CareerGuidancePaymentEvent)
                throw new InvalidOperationException("Financial history cannot be deleted; events are append-only.");
            if (entry.State != EntityState.Modified) continue;
            foreach (var property in entry.Properties.Where(p => p.IsModified))
            {
                var allowed = property.Metadata.Name is "UpdatedAtUtc" or "Revision" or "Status" || entry.Entity switch
                {
                    CareerGuidancePayment => property.Metadata.Name is "ProviderOrderId" or "ProviderPaymentId" or "FailureCode" or "PaidAtUtc" or "RequiresRefundReview",
                    CareerGuidanceRefund => property.Metadata.Name is "ProviderRefundId" or "ProcessedAtUtc" or "FailedAtUtc",
                    CareerGuidanceEarning => property.Metadata.Name is "AvailableAtUtc" or "SettledAtUtc" or "ReversedAtUtc" or "SettlementReference",
                    _ => false
                };
                if (!allowed) throw new InvalidOperationException("Financial identity and amount snapshots are immutable.");
                if (property.Metadata.Name is "ProviderOrderId" or "ProviderPaymentId" or "ProviderRefundId" && property.OriginalValue is not null && !Equals(property.OriginalValue, property.CurrentValue))
                    throw new InvalidOperationException("A financial provider identifier cannot be rebound.");
            }
        }
    }
}
