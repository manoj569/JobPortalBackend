using System.Text.RegularExpressions;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.JobAggregation;

// Configuration support only. Neither a public career URL nor an admin-supplied
// status constitutes Oracle partner approval or employer republication rights.
public sealed partial record OracleRecruitingCloudConfiguration(string OracleHost, string CareerSiteId)
{
    public const string DisplayName = "Oracle Recruiting Cloud";
    public const string AuthorizationRequired = "AuthorizationRequired";
    public const string ErrorCode = "oracle_authorization_required";
    public const string BlockedMessage = "Oracle Recruiting Cloud ingestion is disabled. Approved Oracle partner access, employer-specific authorization and a validated feed/publication contract are required.";

    public string SourceIdentifier => $"oracle:{OracleHost}:{CareerSiteId}";

    public static OracleRecruitingCloudConfiguration? Parse(string? identifier)
    {
        if (identifier is null || identifier.Length > 255) return null;
        var parts = identifier.Trim().Split(':');
        if (parts.Length != 3 || parts[0] != "oracle" || !OracleHostPattern().IsMatch(parts[1]) ||
            !SitePattern().IsMatch(parts[2])) return null;
        return new(parts[1].ToLowerInvariant(), parts[2]);
    }

    public bool MatchesCareerPage(string? value)
    {
        var text = value?.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, OracleHost, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        var path = CareerPathPattern().Match(uri.AbsolutePath);
        // Raw equality rejects escaped separators, dot segments, explicit ports,
        // backslashes and other forms that Uri might otherwise normalize.
        return path.Success && path.Groups[1].Value == CareerSiteId &&
            string.Equals(text, $"https://{uri.Host}{uri.AbsolutePath}", StringComparison.Ordinal);
    }

    public static void RequireAuthorization(JobSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.AtsType == Domain.Enums.AtsType.OracleRecruitingCloud)
            throw new BadRequestException(BlockedMessage, ErrorCode);
    }

    // Only the verified Oracle SaaS host family is supported. Custom domains,
    // IP literals and arbitrary oraclecloud.com subdomains are not accepted.
    // No transport is registered; DNS resolution and redirects never occur.
    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.fa\.ocs\.oraclecloud\.com\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex OracleHostPattern();

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex SitePattern();

    [GeneratedRegex(@"\A/hcmUI/CandidateExperience/[a-z]{2}(?:-[A-Z]{2})?/sites/([A-Za-z0-9][A-Za-z0-9_-]{0,63})/(?:jobs|requisitions)/?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, 1000)]
    private static partial Regex CareerPathPattern();
}

public sealed record OracleRecruitingCloudSourceRequest(
    string OracleHost, string CareerSiteId, bool AutoPublishEnabled = false,
    string AuthorizationStatus = OracleRecruitingCloudConfiguration.AuthorizationRequired);

public sealed record OracleRecruitingCloudSourceStatus(string? OracleHost, string? CareerSiteId, string? SourceIdentifier)
{
    public string ProviderDisplayName { get; } = OracleRecruitingCloudConfiguration.DisplayName;
    public bool IsEnabled { get; }
    public bool AutoPublishEnabled { get; }
    public string AuthorizationStatus { get; } = OracleRecruitingCloudConfiguration.AuthorizationRequired;
    public string ErrorCode { get; } = OracleRecruitingCloudConfiguration.ErrorCode;
    public string BlockingReason { get; } = OracleRecruitingCloudConfiguration.BlockedMessage;
    public bool LiveIngestionSupported { get; }
}
