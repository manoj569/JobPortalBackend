using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Abstractions.CandidateRecruiterVisibility;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.EmployerCandidateFinder;
using JobPortal.Application.Abstractions.EmployerCompanies;
using JobPortal.Application.Abstractions.EmployerJobs;
using JobPortal.Application.Abstractions.EmployerTalentPool;
using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace JobPortal.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        services.AddPooledDbContextFactory<JobPortalDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.CommandTimeout(30);
                npgsql.MaxBatchSize(100);
                npgsql.MigrationsAssembly("JobPortal.Persistence.Postgres");
                npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromSeconds(5),
                    null);
            }), poolSize: 128);
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<JobPortalDbContext>>().CreateDbContext());
        services.AddScoped<JobPortal.Application.Features.HiringDrives.IHiringDriveRepository, HiringDriveRepository>();
        services.AddScoped<JobPortal.Application.Features.JobPromotions.IJobPromotionRepository, JobPromotionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorStore, AcceleratorStore>();
        services.AddScoped<AcceleratorTalentService>();
        services.Configure<JobPortal.Application.Features.CareerAccelerator.TalentOutreachOptions>(settings =>
        {
            var section = configuration.GetSection("CareerAccelerator:TalentOutreach");
            settings.EmployerOutreachEnabled = bool.TryParse(section["EmployerOutreachEnabled"], out var outreach) && outreach;
            settings.ContactDiscoveryEnabled = bool.TryParse(section["ContactDiscoveryEnabled"], out var discovery) && discovery;
            settings.GoogleMailboxEnabled = bool.TryParse(section["GoogleMailboxEnabled"], out var google) && google;
            settings.MicrosoftMailboxEnabled = bool.TryParse(section["MicrosoftMailboxEnabled"], out var microsoft) && microsoft;
            settings.RegisteredEmployerTalentInboxEnabled = !bool.TryParse(
                section["RegisteredEmployerTalentInboxEnabled"], out var inbox) || inbox;
            settings.OutreachAiDraftingEnabled = bool.TryParse(section["OutreachAiDraftingEnabled"], out var ai) && ai;
            settings.OutreachReconciliationEnabled = bool.TryParse(section["OutreachReconciliationEnabled"], out var reconcile) && reconcile;
            if (int.TryParse(section["DispatchRecoveryMinutes"], out var recovery)) settings.DispatchRecoveryMinutes = recovery;
            if (int.TryParse(section["MaxRecommendedContacts"], out var contacts)) settings.MaxRecommendedContacts = contacts;
            if (int.TryParse(section["MaxRecipientsPerDraft"], out var recipients)) settings.MaxRecipientsPerDraft = recipients;
            if (int.TryParse(section["MaxSendsPerDay"], out var perDay)) settings.MaxSendsPerDay = perDay;
            if (int.TryParse(section["MaxSendsPerMonth"], out var perMonth)) settings.MaxSendsPerMonth = perMonth;
            if (int.TryParse(section["RecipientCooldownDays"], out var cooldown)) settings.RecipientCooldownDays = cooldown;
        });
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorPhase2Store, AcceleratorPhase2Store>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorRecognitionStore, AcceleratorRecognitionStore>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorJourneyStore, AcceleratorJourneyStore>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorJourneySummaryStore, AcceleratorJourneySummaryStore>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorJourneyProvisioningStore, AcceleratorJourneyProvisioningStore>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorCatalogReadinessStore, AcceleratorCatalogReadinessStore>();
        services.AddOptions<JobPortal.Application.Features.CareerAccelerator.AcceleratorJourneySummaryOptions>()
            .Configure(x =>
            {
                var raw=configuration["CareerAccelerator:JourneySummary:MinimumJobReadyScore"];
                if (raw is not null)
                {
                    if (!decimal.TryParse(raw,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var score))
                        throw new InvalidOperationException("Journey readiness threshold must be numeric.");
                    x.MinimumJobReadyScore=score;
                }
            })
            .Validate(x=>x.MinimumJobReadyScore is >=0 and <=100,"Journey readiness threshold must be a percentage.").ValidateOnStart();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorLearningPathStore, AcceleratorLearningPathStore>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorPhase3Store, AcceleratorPhase3Store>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorPhase4Store, AcceleratorPhase4Store>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorPhase5Store, AcceleratorPhase5Store>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IInterviewDispatchStore, AcceleratorPhase5Store>();
        services.AddScoped<JobPortal.Application.Features.AIResume.IAIResumeRepository, AIResumeRepository>();
        services.AddScoped<JobPortal.Application.Features.Support.ISupportTicketRepository, SupportTicketRepository>();
        services.AddScoped<JobPortal.Application.Features.Notifications.INotificationOutbox, NotificationOutboxRepository>();
        services.AddScoped<JobPortal.Application.Features.Notifications.INotificationDeliveryRepository, NotificationDeliveryRepository>();
        services.AddScoped<JobPortal.Application.Features.Referrals.IReferralNotificationScheduler, ReferralNotificationScheduler>();
        services.AddScoped<JobPortal.Application.Features.CareerGuidance.ICareerGuidanceRepository, CareerGuidanceRepository>();
        services.AddScoped<JobPortal.Application.Features.CareerGuidance.ICareerSchedulingRepository, CareerSchedulingRepository>();
        services.AddScoped<JobPortal.Application.Features.CareerGuidance.ICareerFinanceRepository, CareerFinanceRepository>();
        services.AddScoped<JobPortal.Application.Features.CareerGuidance.ICareerSessionRepository, CareerSessionRepository>();
        services.AddScoped<JobPortal.Application.Features.CareerGuidance.ICareerTrustRepository, CareerTrustRepository>();
        services.AddScoped<CareerAnalyticsRepository>();
        services.AddScoped<IAdminImportRepository, AdminImportRepository>();
        services.AddScoped<JobPortal.Application.Features.JobDiscovery.IJobDiscoveryRepository, JobDiscoveryRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddScoped<IAuthenticationChallengeRepository,
            AuthenticationChallengeRepository>();
        services.AddScoped<IRegistrationEmailOutbox, RegistrationEmailOutbox>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IJobSourceRepository, JobSourceRepository>();
        services.AddScoped<JobPortal.Application.Features.JobAggregation.IJobSourceRunStore, JobSourceRunStore>();
        services.AddScoped<JobPortal.Application.Abstractions.Jobs.IJobSourceRunCheckpoint, JobSourceRunCheckpoint>();
        services.AddScoped<IJobSourceManagementRepository, JobSourceRepository>();
        services.AddScoped<IJobReferralRepository, JobReferralRepository>();
        services.AddScoped<JobPortal.Application.Features.Referrals.IReferralMarketplaceRepository, ReferralMarketplaceRepository>();
        services.AddScoped<IPublicJobRepository, PublicJobRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IAdminDashboardRepository, AdminDashboardRepository>();
        services.AddScoped<IAdminApplicationRepository, AdminApplicationRepository>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<ICandidateCompanyRepository, CandidateCompanyRepository>();
        services.AddScoped<IEmployerCompanyRepository, EmployerCompanyRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IEmployerJobRepository, EmployerJobRepository>();
        services.AddScoped<
    IEmployerApplicationRepository,
    EmployerApplicationRepository>();
        services.AddScoped<
    IEmployerPipelineRepository,
    EmployerPipelineRepository>();

        services.AddScoped<
    IEmployerInterviewRepository,
    EmployerInterviewRepository>();
        services.AddScoped<IEmployerCandidateFinderRepository, EmployerCandidateFinderRepository>();
        services.AddScoped<ICandidateRecruiterVisibilityRepository, CandidateRecruiterVisibilityRepository>();
        services.AddScoped<
    IEmployerOfferRepository,
    EmployerOfferRepository>();
        services.AddScoped<
    IEmployerTalentPoolRepository,
    EmployerTalentPoolRepository>();
        services.AddScoped<
    IHiringMessagingRepository,
    HiringMessagingRepository>();
        services.AddScoped<IProfilePhotoStorage, PostgresProfilePhotoStorage>();
        services.AddScoped<PostgresResumeStorage>(provider => new PostgresResumeStorage(
            provider.GetRequiredService<IDbContextFactory<JobPortalDbContext>>(), configuration["ResumeStorage:RootPath"]));
        services.AddScoped<IResumeStorage>(provider => provider.GetRequiredService<PostgresResumeStorage>());
        services.AddScoped<InterviewInsightRepository>();
        services.AddScoped<IInterviewInsightRepository>(provider => provider.GetRequiredService<InterviewInsightRepository>());
        services.AddScoped<IInterviewScheduleNotificationProcessor>(provider => provider.GetRequiredService<InterviewInsightRepository>());
        services.AddScoped<JobPortal.Application.Features.Points.ICareerHarborPointsRepository, CareerHarborPointsRepository>();
        services.AddScoped<ICandidatePortfolioRepository, CandidatePortfolioRepository>();
        services.AddScoped<ICompanyManagementRepository, CompanyManagementRepository>();
        services.AddScoped<ICategoryManagementRepository, CategoryManagementRepository>();
        services.AddScoped<JobPortal.Persistence.Repositories.FresherAcquisitionStore>();
        services.AddScoped<JobPortal.Application.Features.FresherAcquisition.IFresherAcquisitionStore>(provider =>
            provider.GetRequiredService<JobPortal.Persistence.Repositories.FresherAcquisitionStore>());
        services.AddScoped<JobPortal.Application.Features.FresherAcquisition.IDurableFresherImportProcessor>(provider =>
            provider.GetRequiredService<JobPortal.Persistence.Repositories.FresherAcquisitionStore>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IAcceleratorPhase6OperationsStore,
            JobPortal.Persistence.Repositories.AcceleratorPhase6OperationsStore>();
        services.AddScoped<JobPortal.Application.Features.CareerAccelerator.IMarketIntelligenceStore,
            JobPortal.Persistence.Repositories.MarketIntelligenceStore>();
        services.AddScoped<IAIApplyRepository, AIApplyRepository>();
        services.AddScoped<IAIApplyOperationalStore, AIApplyOperationalStore>();
        if (bool.TryParse(configuration["AIApply:ExternalSessions:Enabled"], out var externalSessionsEnabled) && externalSessionsEnabled)
            services.AddScoped<IExternalJobSiteSessionStore, ExternalJobSiteSessionStore>();
        return services;
    }
}
