using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Payments;

public sealed class PhonePeOptions
{
    public const string SectionName = "PhonePe";
    public string Environment { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ClientVersion { get; set; } = "";
    public string RedirectBaseUrl { get; set; } = "";
    public string WebhookUsername { get; set; } = "";
    public string WebhookPassword { get; set; } = "";

    public bool IsConfigured => Values().Any(x => !string.IsNullOrWhiteSpace(x.Value));

    public PhonePeEndpoints ResolveEndpoints() => Environment?.Trim().ToUpperInvariant() switch
    {
        // Verified Standard Checkout API v2 endpoints, not legacy X-VERIFY APIs.
        "SANDBOX" => new("Sandbox", new("https://api-preprod.phonepe.com/apis/pg-sandbox/"), new("https://api-preprod.phonepe.com/apis/pg-sandbox/")),
        "PRODUCTION" => new("Production", new("https://api.phonepe.com/apis/identity-manager/"), new("https://api.phonepe.com/apis/pg/")),
        _ => throw new InvalidOperationException("PhonePe:Environment must be Sandbox or Production.")
    };

    public string[] ValidationErrors()
    {
        var errors = Values().Where(x => string.IsNullOrWhiteSpace(x.Value) || x.Value.Trim().StartsWith("CONFIGURE_", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"PhonePe:{x.Key} must be configured.").ToList();
        var production = string.Equals(Environment?.Trim(), "Production", StringComparison.OrdinalIgnoreCase);
        if (!production && !string.Equals(Environment?.Trim(), "Sandbox", StringComparison.OrdinalIgnoreCase))
            errors.Add("PhonePe:Environment must be Sandbox or Production.");
        if (!Uri.TryCreate(RedirectBaseUrl?.Trim(), UriKind.Absolute, out var redirect) ||
            (redirect.Scheme != Uri.UriSchemeHttps && !(redirect.Scheme == Uri.UriSchemeHttp && redirect.IsLoopback && !production)) ||
            (production && (redirect.IsLoopback || redirect.Host.TrimEnd('.').Equals("localhost", StringComparison.OrdinalIgnoreCase) || redirect.Host.TrimEnd('.').EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))) ||
            !string.IsNullOrEmpty(redirect.UserInfo) || !string.IsNullOrEmpty(redirect.Query) || !string.IsNullOrEmpty(redirect.Fragment))
            errors.Add("PhonePe:RedirectBaseUrl must be a safe HTTPS URL; Production cannot use localhost. Sandbox permits loopback HTTP.");
        return errors.ToArray();
    }

    internal static PhonePeOptions Load(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<PhonePeOptions>() ?? new();
        var errors = options.ValidationErrors();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join(" ", errors));
        return options;
    }

    private KeyValuePair<string, string>[] Values() =>
    [new(nameof(Environment), Environment), new(nameof(ClientId), ClientId), new(nameof(ClientSecret), ClientSecret),
        new(nameof(ClientVersion), ClientVersion), new(nameof(RedirectBaseUrl), RedirectBaseUrl),
        new(nameof(WebhookUsername), WebhookUsername), new(nameof(WebhookPassword), WebhookPassword)];
}

public sealed record PhonePeEndpoints(string Environment, Uri OAuthBaseUri, Uri ApiBaseUri);

public sealed class PhonePeOptionsValidator : IValidateOptions<PhonePeOptions>
{
    public ValidateOptionsResult Validate(string? name, PhonePeOptions options)
    {
        // No section means the feature is unconfigured; using the gateway still fails closed.
        if (!options.IsConfigured) return ValidateOptionsResult.Success;
        var errors = options.ValidationErrors();
        return errors.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
