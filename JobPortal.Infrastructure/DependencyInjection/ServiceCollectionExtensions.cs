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

namespace JobPortal.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ValidateEmailConfiguration(configuration);

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // ✅ REMOVED: SMS and OTP services (Mobile OTP feature is retired)
        // services.AddSingleton<IOneTimePasswordService, HmacOneTimePasswordService>();
        // services.AddHttpClient<ISmsService, Fast2SmsService>(client =>
        // {
        //     client.Timeout = TimeSpan.FromSeconds(15);
        //     client.DefaultRequestHeaders.Accept.Add(
        //         new MediaTypeWithQualityHeaderValue("application/json"));
        // });

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
