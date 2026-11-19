using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Application.Abstractions.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Context;

public sealed class JobPortalDbContext(DbContextOptions<JobPortalDbContext> options) : DbContext(options)
{
    public DbSet<TalentShareLink> TalentShareLinks => Set<TalentShareLink>();
    public DbSet<TalentSubmission> TalentSubmissions => Set<TalentSubmission>();
    public DbSet<TalentSubmissionEvent> TalentSubmissionEvents => Set<TalentSubmissionEvent>();
    public DbSet<EmployerContact> EmployerContacts => Set<EmployerContact>();
    public DbSet<TalentOutreachDraft> TalentOutreachDrafts => Set<TalentOutreachDraft>();
    public DbSet<TalentOutreachRecipient> TalentOutreachRecipients => Set<TalentOutreachRecipient>();
    public DbSet<CandidateMailboxConnection> CandidateMailboxConnections => Set<CandidateMailboxConnection>();
    public DbSet<MarketSkill> MarketSkills => Set<MarketSkill>();
    public DbSet<MarketSkillAlias> MarketSkillAliases => Set<MarketSkillAlias>();
    public DbSet<MarketRole> MarketRoles => Set<MarketRole>();
    public DbSet<MarketRoleAlias> MarketRoleAliases => Set<MarketRoleAlias>();
    public DbSet<MarketJobSkill> MarketJobSkills => Set<MarketJobSkill>();
    public DbSet<MarketJobRole> MarketJobRoles => Set<MarketJobRole>();
    public DbSet<MarketSkillDiscovery> MarketSkillDiscoveries => Set<MarketSkillDiscovery>();
    public DbSet<MarketRoleDiscovery> MarketRoleDiscoveries => Set<MarketRoleDiscovery>();
    public DbSet<MarketDiscoveryObservation> MarketDiscoveryObservations => Set<MarketDiscoveryObservation>();
    public DbSet<MarketJobProcessingState> MarketJobProcessingStates => Set<MarketJobProcessingState>();
    public DbSet<MarketProcessingCheckpoint> MarketProcessingCheckpoints => Set<MarketProcessingCheckpoint>();
    public DbSet<HiringDrive> HiringDrives => Set<HiringDrive>();
    public DbSet<JobPromotion> JobPromotions => Set<JobPromotion>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<CareerConsultant> CareerConsultants => Set<CareerConsultant>();
    public DbSet<CareerConsultantAvailability> CareerConsultantAvailability => Set<CareerConsultantAvailability>();
    public DbSet<CareerConsultantAvailabilityException> CareerConsultantAvailabilityExceptions => Set<CareerConsultantAvailabilityException>();
    public DbSet<CareerGuidanceBooking> CareerGuidanceBookings => Set<CareerGuidanceBooking>();
    public DbSet<CareerConsultantTag> CareerConsultantTags => Set<CareerConsultantTag>();
    public DbSet<CareerConsultantService> CareerConsultantServices => Set<CareerConsultantService>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<CandidateResumeProfile> CandidateResumeProfiles => Set<CandidateResumeProfile>();
    public DbSet<ResumeDocumentBlob> ResumeDocumentBlobs => Set<ResumeDocumentBlob>();
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
    public DbSet<ReferralRequest> ReferralRequests => Set<ReferralRequest>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<SavedJob> SavedJobs => Set<SavedJob>();
    public DbSet<UserJobHistory> UserJobHistories => Set<UserJobHistory>();

    // ✅ Notifications Table
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Setting> Setting => Set<Setting>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationQuotaUsage> ApplicationQuotaUsages => Set<ApplicationQuotaUsage>();
    public DbSet<JobApplicationStatusHistory> JobApplicationStatusHistory => Set<JobApplicationStatusHistory>();
    public DbSet<InterviewInsight> InterviewInsights => Set<InterviewInsight>();
    public DbSet<InterviewRound> InterviewRounds => Set<InterviewRound>();
    public DbSet<CandidateInterviewSchedule> CandidateInterviewSchedules => Set<CandidateInterviewSchedule>();
    public DbSet<InsightHelpfulnessFeedback> InsightHelpfulnessFeedback => Set<InsightHelpfulnessFeedback>();
    public DbSet<UserPointTransaction> UserPointTransactions => Set<UserPointTransaction>();
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
    public DbSet<AcquisitionSource> AcquisitionSources => Set<AcquisitionSource>();
    public DbSet<AcquisitionImportRun> AcquisitionImportRuns => Set<AcquisitionImportRun>();
    public DbSet<AcquisitionImportItem> AcquisitionImportItems => Set<AcquisitionImportItem>();
    public DbSet<JobSourceRun> JobSourceRuns => Set<JobSourceRun>();
    public DbSet<AIResumeSession> AIResumeSessions => Set<AIResumeSession>();
    public DbSet<AIResumeCreditWallet> AIResumeCreditWallets => Set<AIResumeCreditWallet>();
    public DbSet<AIResumePurchase> AIResumePurchases => Set<AIResumePurchase>();
    public DbSet<AIResumeGeneration> AIResumeGenerations => Set<AIResumeGeneration>();
    public DbSet<TailoredResume> TailoredResumes => Set<TailoredResume>();
    public DbSet<TailoredResumeEdit> TailoredResumeEdits => Set<TailoredResumeEdit>();
    public DbSet<AIResumeCreditTransaction> AIResumeCreditTransactions => Set<AIResumeCreditTransaction>();
    public DbSet<AcceleratorTaxonomy> AcceleratorTaxonomies => Set<AcceleratorTaxonomy>();
    public DbSet<AcceleratorCareerRole> AcceleratorCareerRoles => Set<AcceleratorCareerRole>();
    public DbSet<AcceleratorCompetency> AcceleratorCompetencies => Set<AcceleratorCompetency>();
    public DbSet<AcceleratorPrerequisite> AcceleratorPrerequisites => Set<AcceleratorPrerequisite>();
    public DbSet<AcceleratorRoleRequirement> AcceleratorRoleRequirements => Set<AcceleratorRoleRequirement>();
    public DbSet<AcceleratorProfile> AcceleratorProfiles => Set<AcceleratorProfile>();
    public DbSet<AcceleratorProfileSkill> AcceleratorProfileSkills => Set<AcceleratorProfileSkill>();
    public DbSet<AcceleratorCareerGoal> AcceleratorCareerGoals => Set<AcceleratorCareerGoal>();
    public DbSet<AcceleratorBillingCycle> AcceleratorBillingCycles => Set<AcceleratorBillingCycle>();
    public DbSet<AcceleratorAllowance> AcceleratorAllowances => Set<AcceleratorAllowance>();
    public DbSet<AcceleratorUsageReservation> AcceleratorUsageReservations => Set<AcceleratorUsageReservation>();
    public DbSet<AcceleratorUsageEvent> AcceleratorUsageEvents => Set<AcceleratorUsageEvent>();
    public DbSet<AcceleratorAssessmentTemplate> AcceleratorAssessmentTemplates => Set<AcceleratorAssessmentTemplate>();
    public DbSet<AcceleratorQuestion> AcceleratorQuestions => Set<AcceleratorQuestion>();
    public DbSet<AcceleratorAssessmentAttempt> AcceleratorAssessmentAttempts => Set<AcceleratorAssessmentAttempt>();
    public DbSet<AcceleratorAssessmentAnswer> AcceleratorAssessmentAnswers => Set<AcceleratorAssessmentAnswer>();
    public DbSet<AcceleratorSkillEvidence> AcceleratorSkillEvidence => Set<AcceleratorSkillEvidence>();
    public DbSet<AcceleratorRoadmap> AcceleratorRoadmaps => Set<AcceleratorRoadmap>();
    public DbSet<AcceleratorRoadmapNode> AcceleratorRoadmapNodes => Set<AcceleratorRoadmapNode>();
    public DbSet<AcceleratorRoadmapEdge> AcceleratorRoadmapEdges => Set<AcceleratorRoadmapEdge>();
    public DbSet<AcceleratorLearningActivity> AcceleratorLearningActivities => Set<AcceleratorLearningActivity>();
    public DbSet<AcceleratorProgressEvent> AcceleratorProgressEvents => Set<AcceleratorProgressEvent>();
    public DbSet<AcceleratorRecommendation> AcceleratorRecommendations => Set<AcceleratorRecommendation>();
    public DbSet<AcceleratorTutorConversation> AcceleratorTutorConversations => Set<AcceleratorTutorConversation>();
    public DbSet<AcceleratorTutorMessage> AcceleratorTutorMessages => Set<AcceleratorTutorMessage>();
    public DbSet<AcceleratorTutorContextSnapshot> AcceleratorTutorContextSnapshots => Set<AcceleratorTutorContextSnapshot>();
    public DbSet<AcceleratorTutorMemorySummary> AcceleratorTutorMemorySummaries => Set<AcceleratorTutorMemorySummary>();
    public DbSet<AcceleratorPracticeSession> AcceleratorPracticeSessions => Set<AcceleratorPracticeSession>();
    public DbSet<AcceleratorPracticeQuestion> AcceleratorPracticeQuestions => Set<AcceleratorPracticeQuestion>();
    public DbSet<AcceleratorPracticeAttempt> AcceleratorPracticeAttempts => Set<AcceleratorPracticeAttempt>();
    public DbSet<AcceleratorPracticeEvaluation> AcceleratorPracticeEvaluations => Set<AcceleratorPracticeEvaluation>();
    public DbSet<AcceleratorAiDispatchRecord> AcceleratorAiDispatchRecords => Set<AcceleratorAiDispatchRecord>();
    public DbSet<AcceleratorProjectDefinition> AcceleratorProjectDefinitions => Set<AcceleratorProjectDefinition>();
    public DbSet<AcceleratorProjectVersion> AcceleratorProjectVersions => Set<AcceleratorProjectVersion>();
    public DbSet<AcceleratorProjectCompetency> AcceleratorProjectCompetencies => Set<AcceleratorProjectCompetency>();
    public DbSet<AcceleratorProjectMilestone> AcceleratorProjectMilestones => Set<AcceleratorProjectMilestone>();
    public DbSet<AcceleratorProjectAssignment> AcceleratorProjectAssignments => Set<AcceleratorProjectAssignment>();
    public DbSet<AcceleratorProjectRubric> AcceleratorProjectRubrics => Set<AcceleratorProjectRubric>();
    public DbSet<AcceleratorProjectRubricCriterion> AcceleratorProjectRubricCriteria => Set<AcceleratorProjectRubricCriterion>();
    public DbSet<AcceleratorCandidateProject> AcceleratorCandidateProjects => Set<AcceleratorCandidateProject>();
    public DbSet<AcceleratorProjectSubmission> AcceleratorProjectSubmissions => Set<AcceleratorProjectSubmission>();
    public DbSet<AcceleratorProjectEvaluation> AcceleratorProjectEvaluations => Set<AcceleratorProjectEvaluation>();
    public DbSet<AcceleratorProjectEvaluationScore> AcceleratorProjectEvaluationScores => Set<AcceleratorProjectEvaluationScore>();
    public DbSet<AcceleratorInterviewDefinition> AcceleratorInterviewDefinitions => Set<AcceleratorInterviewDefinition>();
    public DbSet<AcceleratorInterviewVersion> AcceleratorInterviewVersions => Set<AcceleratorInterviewVersion>();
    public DbSet<AcceleratorInterviewCompetency> AcceleratorInterviewCompetencies => Set<AcceleratorInterviewCompetency>();
    public DbSet<AcceleratorInterviewSection> AcceleratorInterviewSections => Set<AcceleratorInterviewSection>();
    public DbSet<AcceleratorInterviewQuestion> AcceleratorInterviewQuestions => Set<AcceleratorInterviewQuestion>();
    public DbSet<AcceleratorInterviewRubricCriterion> AcceleratorInterviewRubricCriteria => Set<AcceleratorInterviewRubricCriterion>();
    public DbSet<AcceleratorInterviewSession> AcceleratorInterviewSessions => Set<AcceleratorInterviewSession>();
    public DbSet<AcceleratorSessionQuestion> AcceleratorSessionQuestions => Set<AcceleratorSessionQuestion>();
    public DbSet<AcceleratorInterviewAnswer> AcceleratorInterviewAnswers => Set<AcceleratorInterviewAnswer>();
    public DbSet<AcceleratorInterviewEvaluation> AcceleratorInterviewEvaluations => Set<AcceleratorInterviewEvaluation>();
    public DbSet<AcceleratorInterviewCriterionScore> AcceleratorInterviewCriterionScores => Set<AcceleratorInterviewCriterionScore>();
    public DbSet<AcceleratorReadinessSnapshot> AcceleratorReadinessSnapshots => Set<AcceleratorReadinessSnapshot>();
    public DbSet<AcceleratorPassportShare> AcceleratorPassportShares => Set<AcceleratorPassportShare>();
    public DbSet<AcceleratorJourneyContext> AcceleratorJourneyContexts => Set<AcceleratorJourneyContext>();
    public DbSet<AcceleratorSkillProficiencyPolicy> AcceleratorSkillProficiencyPolicies => Set<AcceleratorSkillProficiencyPolicy>();
    public DbSet<AcceleratorProficiencyBand> AcceleratorProficiencyBands => Set<AcceleratorProficiencyBand>();
    public DbSet<AcceleratorAchievementDefinition> AcceleratorAchievementDefinitions => Set<AcceleratorAchievementDefinition>();
    public DbSet<CandidateAcceleratorAchievement> CandidateAcceleratorAchievements => Set<CandidateAcceleratorAchievement>();
    public DbSet<AcceleratorEnrollmentCertificate> AcceleratorEnrollmentCertificates => Set<AcceleratorEnrollmentCertificate>();
    public DbSet<AcceleratorLearningPathCompetencyRequirement> AcceleratorLearningPathCompetencyRequirements => Set<AcceleratorLearningPathCompetencyRequirement>();
    public DbSet<AcceleratorLearningPathAssessment> AcceleratorLearningPathAssessments => Set<AcceleratorLearningPathAssessment>();
    public DbSet<AcceleratorLearningPath> AcceleratorLearningPaths => Set<AcceleratorLearningPath>();
    public DbSet<AcceleratorLearningModule> AcceleratorLearningModules => Set<AcceleratorLearningModule>();
    public DbSet<AcceleratorLearningModulePrerequisite> AcceleratorLearningModulePrerequisites => Set<AcceleratorLearningModulePrerequisite>();
    public DbSet<AcceleratorLesson> AcceleratorLessons => Set<AcceleratorLesson>();
    public DbSet<AcceleratorLessonResource> AcceleratorLessonResources => Set<AcceleratorLessonResource>();
    public DbSet<AcceleratorPathEnrollment> AcceleratorPathEnrollments => Set<AcceleratorPathEnrollment>();
    public DbSet<AcceleratorLessonProgress> AcceleratorLessonProgress => Set<AcceleratorLessonProgress>();
    public DbSet<AcceleratorEnrollmentProgressEvent> AcceleratorEnrollmentProgressEvents => Set<AcceleratorEnrollmentProgressEvent>();
    public DbSet<EmployerHiringPipeline> EmployerHiringPipelines =>
    Set<EmployerHiringPipeline>();
    public DbSet<EmployerInterview> EmployerInterviews =>
    Set<EmployerInterview>();
    public DbSet<EmployerOffer> EmployerOffers =>
    Set<EmployerOffer>();

    public DbSet<HiringConversation> HiringConversations =>
    Set<HiringConversation>();

    public DbSet<HiringMessage> HiringMessages =>
        Set<HiringMessage>();
    public DbSet<EmployerTalentPool> EmployerTalentPools => Set<EmployerTalentPool>();
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AcceleratorPersistencePolicy.Enforce(this);
        EnsureFinancialHistory();
        EnsureTrustHistory();
        EnsureAuditLogsAreAppendOnly();
        EnsureAIResumeLedgerIsAppendOnly();
        ApplyAuditAndSoftDelete();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AcceleratorPersistencePolicy.Enforce(this);
        EnsureFinancialHistory();
        EnsureTrustHistory();
        EnsureAuditLogsAreAppendOnly();
        EnsureAIResumeLedgerIsAppendOnly();
        ApplyAuditAndSoftDelete();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobPortalDbContext).Assembly);
        ConfigureJourneyReference<AcceleratorAssessmentAttempt>(modelBuilder);
        ConfigureJourneyReference<AcceleratorSkillEvidence>(modelBuilder);
        ConfigureJourneyReference<AcceleratorRoadmap>(modelBuilder);
        ConfigureJourneyReference<AcceleratorRecommendation>(modelBuilder);
        if (Database.IsNpgsql()) modelBuilder.HasPostgresExtension("btree_gist");

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureJourneyReference<TEntity>(ModelBuilder modelBuilder) where TEntity : BaseEntity
    {
        var builder = modelBuilder.Entity<TEntity>();
        builder.HasOne<AcceleratorJourneyContext>().WithMany().HasForeignKey("JourneyContextId").OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex("UserId", "JourneyContextId");
    }

    private void ApplyAuditAndSoftDelete()
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.Entity is Job job && entry.State is EntityState.Added or EntityState.Modified)
                job.CanonicalApplicationUrlHash = ApplicationUrlIdentity.Hash(job.ApplicationUrl);
            if (entry.Entity is Company company && entry.State is EntityState.Added or EntityState.Modified)
            {
                company.NormalizedName = CompanyNameNormalizer.Normalize(company.Name);
                company.NormalizedWebsiteDomain = CompanyDomainNormalizer.Normalize(company.WebsiteUrl);
            }
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

    private void EnsureAIResumeLedgerIsAppendOnly()
    {
        if (ChangeTracker.Entries<AIResumeCreditTransaction>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("AI Resume credit transactions are append-only and cannot be updated or deleted.");
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
        foreach (var entry in ChangeTracker.Entries().Where(e =>
            e.Entity is CareerGuidancePayment
                or CareerGuidanceEarning
                or CareerGuidanceRefund
                or CareerGuidancePaymentEvent
                or CareerConsultantPayout))
        {
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && entry.Entity is CareerGuidancePaymentEvent)
                throw new InvalidOperationException("Financial history cannot be deleted; events are append-only.");
            if (entry.State != EntityState.Modified) continue;
            foreach (var property in entry.Properties.Where(p => p.IsModified))
            {
                var allowed =
      property.Metadata.Name is "UpdatedAtUtc" or "Revision" or "Status"
      || entry.Entity switch
      {
          CareerGuidancePayment =>
              property.Metadata.Name is
                  "ProviderOrderId"
                  or "ProviderPaymentId"
                  or "FailureCode"
                  or "PaidAtUtc"
                  or "RequiresRefundReview",

          CareerGuidanceRefund =>
              property.Metadata.Name is
                  "ProviderRefundId"
                  or "ProcessedAtUtc"
                  or "FailedAtUtc",

          CareerGuidanceEarning =>
              property.Metadata.Name is
                  "AvailableAtUtc"
                  or "SettledAtUtc"
                  or "ReversedAtUtc"
                  or "SettlementReference",

          CareerConsultantPayout =>
              property.Metadata.Name is
                  "ProviderPayoutId"
                  or "ProcessingAtUtc"
                  or "PaidAtUtc"
                  or "FailedAtUtc"
                  or "FailureCode"
                  or "FailureReason",

          _ => false
      };
                if (!allowed) throw new InvalidOperationException("Financial identity and amount snapshots are immutable.");
                if (property.Metadata.Name is
                        "ProviderOrderId"
                        or "ProviderPaymentId"
                        or "ProviderRefundId"
                        or "ProviderPayoutId"
                    && property.OriginalValue is not null
                    && !Equals(property.OriginalValue, property.CurrentValue))
                    throw new InvalidOperationException("A financial provider identifier cannot be rebound.");
            }
        }
    }
}
