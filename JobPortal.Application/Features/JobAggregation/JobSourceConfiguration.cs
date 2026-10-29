using System.Text.RegularExpressions;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Enums;
using JobPortal.Application.Abstractions.Jobs;

namespace JobPortal.Application.Features.JobAggregation;

public sealed record ResolvedJobSourceConfiguration(bool IsValid, string ReasonCode, string Message,
    string? Identifier = null, Uri? ListingEndpoint = null);

public static partial class JobSourceConfiguration
{
    public static ResolvedJobSourceConfiguration Resolve(AtsType provider, string? careerUrl, string? identifier)
    {
        if (provider == AtsType.RippleHire)
            return RippleHireSourceConfiguration.Resolve(careerUrl, identifier);
        if (provider == AtsType.SmartRecruiters)
            return SmartRecruitersSourceConfiguration.Resolve(careerUrl, identifier);
        if (provider == AtsType.Workday)
        {
            var result = WorkdaySourceConfiguration.Validate(careerUrl, identifier);
            return new(result.IsValid, result.ReasonCode, result.Message, result.Configuration?.Identifier, result.Configuration?.ListingEndpoint);
        }
        if (provider == AtsType.OracleRecruitingCloud)
            return Invalid(OracleRecruitingCloudConfiguration.ErrorCode, OracleRecruitingCloudConfiguration.BlockedMessage);
        if (provider == AtsType.SuccessFactors)
        {
            if (!SafeUrl(careerUrl, out var board) || board!.Query.Length != 0 ||
                !BoardPath().IsMatch(RawPath(careerUrl!)))
                return Invalid("SUCCESSFACTORS_CONFIGURATION_INVALID", "Use a public HTTPS /go/board/numeric-id career URL.");
            var id = BoardPath().Match(RawPath(careerUrl!)).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(identifier) && identifier.Trim() != id)
                return Invalid("SUCCESSFACTORS_CONFIGURATION_CONFLICT", "SuccessFactors identifier must match the numeric ID in its career URL.");
            return new(true, "CONFIGURATION_VALID", "Configuration validated.", id,
                new UriBuilder(board) { Path = board.AbsolutePath.TrimEnd('/') + "/", Query = "q=&sortColumn=referencedate&sortDirection=desc" }.Uri);
        }
        var prefix = provider switch { AtsType.Greenhouse => "GREENHOUSE_BOARD", AtsType.Lever => "LEVER", AtsType.Ashby => "ASHBY", _ => null };
        if (prefix is null) return Invalid("PROVIDER_UNSUPPORTED", "No source configuration contract is registered for this provider.");
        string? derived = null;
        if (SafeUrl(careerUrl, out var career))
        {
            var known = provider switch
            {
                AtsType.Greenhouse => career!.Host is "boards.greenhouse.io" or "job-boards.greenhouse.io" or "boards-api.greenhouse.io",
                AtsType.Lever => career!.Host is "jobs.lever.co" or "jobs.eu.lever.co",
                _ => career!.Host == "jobs.ashbyhq.com"
            };
            if (known)
            {
                var path = RawPath(careerUrl!);
                var parts = path.Trim('/').Split('/');
                if (provider == AtsType.Greenhouse && career!.Host == "boards-api.greenhouse.io")
                {
                    if (parts.Length != 4 || parts[0] != "v1" || parts[1] != "boards" || parts[3] != "jobs")
                        return Invalid(prefix + "_CONFIGURATION_INVALID", "Use a canonical Greenhouse board jobs URL.");
                    parts = [parts[2]];
                }
                if (parts.Length != 1 || !IdentifierPattern().IsMatch(parts[0]) || path.Contains('%') || path.Contains('\\'))
                    return Invalid(prefix + "_CONFIGURATION_INVALID", "Use the provider's board root, not a job/detail URL.");
                derived = parts[0];
            }
        }
        var token = string.IsNullOrWhiteSpace(identifier) ? derived : identifier.Trim();
        if (string.IsNullOrEmpty(token)) return Invalid(prefix + "_IDENTIFIER_MISSING", "A provider board identifier or unambiguous board URL is required.");
        if (!IdentifierPattern().IsMatch(token)) return Invalid(prefix + "_IDENTIFIER_INVALID", "The board identifier must be one safe path segment, not a URL.");
        if (derived is not null && token != derived) return Invalid(prefix + "_CONFIGURATION_CONFLICT", "Board identifier conflicts with the configured provider URL, including its case.");
        var endpoint = provider switch
        {
            AtsType.Greenhouse => $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true",
            AtsType.Lever => $"https://{(career?.Host == "jobs.eu.lever.co" ? "api.eu.lever.co" : "api.lever.co")}/v0/postings/{token}?mode=json&skip=0&limit=1",
            _ => $"https://api.ashbyhq.com/posting-api/job-board/{token}"
        };
        return new(true, "CONFIGURATION_VALID", "Configuration validated.", token, new Uri(endpoint));
    }

    public static ResolvedJobSourceConfiguration Require(AtsType provider, string? url, string? identifier)
    {
        var result = Resolve(provider, url, identifier);
        if (!result.IsValid)
        {
            var exception = new InvalidOperationException(result.Message);
            exception.Data["JobSourceReasonCode"] = result.ReasonCode;
            throw exception;
        }
        return result;
    }

    private static ResolvedJobSourceConfiguration Invalid(string reason, string message) => new(false, reason, message);
    private static string RawPath(string value)
    {
        var text = value.Trim();
        var index = text.IndexOf('/', text.IndexOf("://", StringComparison.Ordinal) + 3);
        return index < 0 ? "" : text[index..];
    }
    private static bool SafeUrl(string? value, out Uri? uri) => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback && uri.Host.Contains('.') &&
        !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,254}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
    [GeneratedRegex(@"\A/go/[A-Za-z0-9_-]+/([0-9]+)/?\z", RegexOptions.CultureInvariant)]
    private static partial Regex BoardPath();
}

public static class JobSourceFailure
{
    public static string Stage(Exception error) => error switch
    {
        JobSourceSnapshotUnsafeException => "SnapshotValidation",
        InvalidOperationException when error.Data["JobSourceReasonCode"] is string => "ConfigurationValidation",
        HttpRequestException or TimeoutException or OperationCanceledException => "ProviderRequest",
        System.Text.Json.JsonException or InvalidDataException => "ProviderResponseValidation",
        _ => "IngestionOrPersistence"
    };

    public static string Code(Exception error) => error switch
    {
        JobSourceSnapshotUnsafeException snapshot => snapshot.ReasonCode,
        AppException app => app.Code,
        InvalidOperationException when error.Data["JobSourceReasonCode"] is string code => code,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => "PROVIDER_NOT_FOUND",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => "PROVIDER_RATE_LIMITED",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } => "PROVIDER_NOT_AUTHORIZED",
        HttpRequestException => "PROVIDER_UNAVAILABLE",
        TimeoutException or OperationCanceledException => "PROVIDER_TIMEOUT",
        System.Text.Json.JsonException or InvalidDataException => "PROVIDER_RESPONSE_INVALID",
        _ => "UNEXPECTED_INTERNAL_ERROR"
    };
}
