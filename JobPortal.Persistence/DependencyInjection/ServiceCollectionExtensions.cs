using JobPortal.Application.Abstractions.Persistence;
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

        services.AddDbContextPool<JobPortalDbContext>(options =>
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
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAdminImportRepository, AdminImportRepository>();
        services.AddScoped<JobPortal.Application.Features.JobDiscovery.IJobDiscoveryRepository, JobDiscoveryRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthenticationChallengeRepository,
            AuthenticationChallengeRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IPublicJobRepository, PublicJobRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IAdminDashboardRepository, AdminDashboardRepository>();
        services.AddScoped<IAdminApplicationRepository, AdminApplicationRepository>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<ICandidatePortfolioRepository, CandidatePortfolioRepository>();
        services.AddScoped<ICompanyManagementRepository, CompanyManagementRepository>();
        services.AddScoped<ICategoryManagementRepository, CategoryManagementRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
