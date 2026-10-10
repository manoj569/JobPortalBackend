using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using JobPortal.Application.Abstractions.AIApply;

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
        services.AddScoped<IUserRepository, UserRepository>();
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
        services.AddScoped<IProfilePhotoStorage, PostgresProfilePhotoStorage>();
        services.AddScoped<PostgresResumeStorage>(provider => new PostgresResumeStorage(
            provider.GetRequiredService<IDbContextFactory<JobPortalDbContext>>(), configuration["ResumeStorage:RootPath"]));
        services.AddScoped<IResumeStorage>(provider => provider.GetRequiredService<PostgresResumeStorage>());
        services.AddScoped<InterviewInsightRepository>();
        services.AddScoped<IInterviewInsightRepository>(provider => provider.GetRequiredService<InterviewInsightRepository>());
        services.AddScoped<IInterviewScheduleNotificationProcessor>(provider => provider.GetRequiredService<InterviewInsightRepository>());
        services.AddScoped<ICandidatePortfolioRepository, CandidatePortfolioRepository>();
        services.AddScoped<ICompanyManagementRepository, CompanyManagementRepository>();
        services.AddScoped<ICategoryManagementRepository, CategoryManagementRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAIApplyRepository, AIApplyRepository>();
        services.AddScoped<IAIApplyOperationalStore, AIApplyOperationalStore>();
        if (bool.TryParse(configuration["AIApply:ExternalSessions:Enabled"], out var externalSessionsEnabled) && externalSessionsEnabled)
            services.AddScoped<IExternalJobSiteSessionStore, ExternalJobSiteSessionStore>();
        return services;
    }
}
