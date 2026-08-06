using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Infrastructure.Authentication;
using JobPortal.Infrastructure.Payments;
using JobPortal.Infrastructure.Services;
using JobPortal.Infrastructure.Storage;
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

        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton<IRazorpayGateway, RazorpayGateway>();
        services.AddSingleton<IMembershipPlanProvider, ConfigurationMembershipPlanProvider>();
        services.AddSingleton<IResumeStorage, LocalResumeStorage>();

        return services;
    }

    private static void ValidateEmailConfiguration(IConfiguration configuration)
    {
        if (!configuration.GetValue("Email:Enabled", false)) return;
        string[] requiredKeys =
        [
            "Email:FromAddress",
            "Email:PasswordResetUrl",
            "Email:Smtp:Host", "Email:Smtp:Username", "Email:Smtp:Password"
        ];
        var missing = requiredKeys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Email delivery is enabled but required configuration is missing: {string.Join(", ", missing)}.");
        if (configuration.GetValue<int>("Email:Smtp:Port") is <= 0 or > 65535)
            throw new InvalidOperationException("Email:Smtp:Port must be between 1 and 65535.");
        var passwordResetUrl = configuration["Email:PasswordResetUrl"];
        if (!Uri.TryCreate(passwordResetUrl, UriKind.Absolute, out var parsedResetUrl) ||
            (parsedResetUrl.Scheme != Uri.UriSchemeHttp &&
                parsedResetUrl.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                "Email:PasswordResetUrl must be an absolute HTTP or HTTPS URL.");
    }
}
