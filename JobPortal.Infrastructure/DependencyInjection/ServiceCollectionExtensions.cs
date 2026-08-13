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
        services.AddSingleton<IMembershipPlanProvider, ConfigurationMembershipPlanProvider>();
        services.AddSingleton<IResumeStorage, LocalResumeStorage>();
        services.AddSingleton<IResumeTextExtractor, ResumeTextExtractor>();

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
