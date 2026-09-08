using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Infrastructure.Authentication;
using JobPortal.Infrastructure.Payments;
using JobPortal.Infrastructure.Services;
using JobPortal.Infrastructure.Storage;
using JobPortal.Application.Features.JobDiscovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Infrastructure.AIApply;

namespace JobPortal.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ValidateEmailConfiguration(configuration);
        ValidateGoogleConfiguration(configuration);

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.Configure<GoogleAuthenticationOptions>(
            configuration.GetSection(GoogleAuthenticationOptions.SectionName));
        services.AddSingleton<IGoogleCredentialValidator, GoogleCredentialValidator>();
        services.AddHttpClient(GoogleAuthorizationCodeExchanger.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://oauth2.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IGoogleAuthorizationCodeExchanger, GoogleAuthorizationCodeExchanger>();

        // ✅ REMOVED: SMS and OTP services (Mobile OTP feature is retired)
        services.AddHttpClient(BrevoEmailService.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IEmailService, BrevoEmailService>();
        services.AddHttpClient(AdzunaJobSourceProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.adzuna.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IExternalJobSourceProvider, AdzunaJobSourceProvider>();
        services.AddSingleton<IRazorpayGateway, RazorpayGateway>();
        services.AddSingleton<PhonePeAccessTokenCache>();
        services.AddHttpClient<IPhonePeGateway, PhonePeGateway>(client =>
        {
            client.BaseAddress = new Uri("https://api-preprod.phonepe.com/apis/pg-sandbox/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IMembershipPlanProvider, ConfigurationMembershipPlanProvider>();
        services.AddSingleton<IResumeStorage, LocalResumeStorage>();
        services.AddSingleton<IResumeTextExtractor, ResumeTextExtractor>();
        services.AddSingleton<PlaywrightBrowserManager>();
        services.AddSingleton<IPlaywrightBrowserRuntime>(provider => provider.GetRequiredService<PlaywrightBrowserManager>());
        services.AddSingleton<IExternalHostAddressResolver, SystemExternalHostAddressResolver>();
        services.AddSingleton<ExternalNavigationPolicy>();
        services.AddSingleton<IExternalJobSiteSessionProtector, ExternalJobSiteSessionProtector>();
        if (configuration.GetValue("AIApply:ExternalSessions:Capture:Transport:Enabled", false))
            services.AddSingleton<IExternalSessionCaptureTransport, SignalRExternalSessionCaptureTransport>();
        else
            services.AddSingleton<IExternalSessionCaptureTransport, DisabledExternalSessionCaptureTransport>();
        services.AddScoped<IExternalSessionSiteRegistry, ConfiguredExternalSessionSiteRegistry>();
        services.AddSingleton<ExternalSessionCaptureManager>();
        services.AddSingleton<IExternalSessionCaptureCleanup>(sp => sp.GetRequiredService<ExternalSessionCaptureManager>());
        services.AddScoped<IExternalSessionCaptureService, ExternalSessionCaptureService>();
        services.AddScoped<IExternalSessionCaptureInteractionService, ExternalSessionCaptureInteractionService>();
        services.AddSingleton<IExternalJobSiteSessionValidator, WorkdayExternalSessionValidator>();
        services.AddSingleton<IExternalJobSiteSessionValidator, LinkedInExternalSessionValidator>();
        services.AddSingleton<IExternalJobSiteSessionValidator, NaukriExternalSessionValidator>();
        services.AddSingleton<IExternalJobSiteSessionValidator, IndeedExternalSessionValidator>();
        services.AddSingleton<IExternalJobSiteSessionValidator, FounditExternalSessionValidator>();
        services.AddSingleton<IExternalJobSiteSessionValidator, WellfoundExternalSessionValidator>();
        services.AddScoped<GenericJobSiteAdapter>();
        services.AddScoped<IJobSiteAdapter>(sp => sp.GetRequiredService<GenericJobSiteAdapter>());
        services.AddScoped<IJobSiteAdapter, GreenhouseJobSiteAdapter>();
        services.AddScoped<IJobSiteAdapter, LeverJobSiteAdapter>();
        services.AddScoped<IJobSiteAdapter, AshbyJobSiteAdapter>();
        services.AddScoped<IJobSiteAdapter, SmartRecruitersJobSiteAdapter>();
        services.AddScoped<IJobSiteAdapter, AccountOrientedJobSiteAdapter>();
        services.AddScoped<IJobSiteAdapterResolver, JobSiteAdapterResolver>();
        services.AddScoped<ICandidateExternalApplicationLinkService, CandidateExternalApplicationLinkService>();
        if (!configuration.GetValue("AIApply:ExternalSessions:Enabled", false))
            services.AddSingleton<IExternalJobSiteSessionStore, DisabledExternalJobSiteSessionStore>();
        services.AddScoped<IJobSiteAdapterHealthService, ConfiguredJobSiteAdapterHealthService>();
        services.AddSingleton<IAIApplyWorkerIdentity, AIApplyWorkerIdentity>();
        services.AddSingleton<IAIApplyWorkerState, AIApplyWorkerState>();
        services.AddScoped<IJobApplicationBrowserAgent, PlaywrightJobApplicationBrowserAgent>();

        return services;
    }

    private static void ValidateGoogleConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(GoogleAuthenticationOptions.SectionName);
        if (!section.GetValue("Enabled", false)) return;
        var clientId = section["ClientId"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 512 ||
            !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Authentication:Google:ClientId must contain a valid Google Web Client ID when Google authentication is enabled.");
        var clientSecret = section["ClientSecret"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Length > 512)
            throw new InvalidOperationException(
                "Authentication:Google:ClientSecret must be configured when Google authentication is enabled.");
        var origins = section.GetSection("AllowedCodeOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(origin => !IsValidCodeOrigin(origin)))
            throw new InvalidOperationException(
                "Authentication:Google:AllowedCodeOrigins must contain only valid HTTPS origins or HTTP loopback development origins.");
    }

    private static bool IsValidCodeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.EndsWith('/')) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        return uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    private static void ValidateEmailConfiguration(IConfiguration configuration)
    {
        if (!configuration.GetValue("Email:Enabled", false)) return;
        string[] requiredKeys =
        [
            "Email:FromAddress",
            "Email:FromName",
            "AppUrls:FrontendBaseUrl",
            "Email:Brevo:ApiKey"
        ];
        var missing = requiredKeys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Email delivery is enabled but required configuration is missing: {string.Join(", ", missing)}.");
        var frontendBaseUrl = configuration["AppUrls:FrontendBaseUrl"];
        if (!Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var parsedResetUrl) ||
            (parsedResetUrl.Scheme != Uri.UriSchemeHttp &&
                parsedResetUrl.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                "AppUrls:FrontendBaseUrl must be an absolute HTTP or HTTPS URL.");
    }
}
