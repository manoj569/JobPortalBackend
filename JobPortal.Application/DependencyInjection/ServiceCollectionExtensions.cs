using FluentValidation;
using JobPortal.Application.Abstractions.AdminApplications;
using JobPortal.Application.Abstractions.AdminDashboard;
using JobPortal.Application.Abstractions.AdminImports;
using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Abstractions.Dashboard;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Portfolios;
using JobPortal.Application.Abstractions.Settings;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Application.Features.AdminDashboard;
using JobPortal.Application.Features.AdminImports;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Auditing;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Application.Features.JobDiscovery;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Application.Features.Portfolios;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Features.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace JobPortal.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGoogleAuthenticationService, GoogleAuthenticationService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<AdminBootstrapService>();
        services.AddScoped<ICandidateService, CandidateService>();
        services.AddScoped<ICandidateCompanyService, CandidateCompanyService>();
        services.AddScoped<ICandidatePortfolioService, CandidatePortfolioService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IInterviewInsightService, InterviewInsightService>();
        services.AddScoped<IAdminInterviewInsightService, AdminInterviewInsightService>();
        services.AddScoped<IJobExpiryService, JobExpiryService>();
        services.AddScoped<IPublicJobService, PublicJobService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAccountSettingsService, AccountSettingsService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminApplicationService, AdminApplicationService>();
        services.AddScoped<IAdminImportService, AdminImportService>();
        services.AddScoped<IJobDiscoveryService, JobDiscoveryService>();
        services.AddScoped<ICompanyManagementService, CompanyManagementService>();
        services.AddScoped<ICategoryManagementService, CategoryManagementService>();
        return services;
    }
}
