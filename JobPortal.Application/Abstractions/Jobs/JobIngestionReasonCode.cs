namespace JobPortal.Application.Abstractions.Jobs;

/// <summary>
/// Machine-readable reason codes describing why an ingestion attempt produced
/// its outcome. Codes map only to real, existing processing paths so run
/// tallies remain meaningful and stable for downstream analysis.
/// </summary>
public enum JobIngestionReasonCode
{
    /// <summary>No specific reason applies (for example a successful creation).</summary>
    None = 0,

    /// <summary>Duplicate detected via the canonicalized application URL identity.</summary>
    DuplicateCanonicalUrl = 1,

    /// <summary>Duplicate detected via the title/company/location fingerprint hash.</summary>
    DuplicateFingerprint = 2,

    /// <summary>Duplicate detected via fuzzy title/company/location matching.</summary>
    DuplicateFuzzyMatch = 3,

    /// <summary>The referenced company could not be resolved.</summary>
    CompanyNotFound = 4,

    /// <summary>The source record failed validation (missing or malformed fields).</summary>
    InvalidSourceData = 5,

    /// <summary>The application URL was present but not a valid absolute HTTP(S) URL.</summary>
    InvalidApplicationUrl = 6,

    /// <summary>An external provider returned data that could not be processed.</summary>
    ProviderError = 7,

    /// <summary>A persistence failure occurred while storing or refreshing the job.</summary>
    PersistenceError = 8,

    /// <summary>The reason could not be classified by any known path.</summary>
    Unknown = 9,

    /// <summary>A Draft was created, but subsequent publication failed.</summary>
    AutoPublishFailed = 10
}

public static class JobIngestionReasonCodes
{
    // Only fixed application-generated validation messages are classified; never
    // emit provider text, URLs, descriptions or exception messages as reason keys.
    public static string ValidationDetail(string? message) => message switch
    {
        "Job title is required." => "MissingTitle",
        "Job title cannot exceed 250 characters." => "TitleTooLong",
        "Company name is required." => "MissingCompanyName",
        "Salary range is invalid." => "InvalidSalaryRange",
        "Experience range is invalid." => "InvalidExperienceRange",
        "ApplicationUrl must be an absolute HTTP or HTTPS URL." => "InvalidApplicationUrl",
        "Matched job is no longer available." => "MatchedJobUnavailable",
        "CategoryId is required when creating a new external job." => "MissingCategory",
        not null when message.StartsWith("Category '", StringComparison.Ordinal) &&
            message.EndsWith("' does not exist.", StringComparison.Ordinal) => "CategoryNotFound",
        _ => "InvalidSourceData"
    };

    /// <summary>
    /// Maps an ingestion outcome (and optional sanitized message) to a stable
    /// machine-readable reason code. Message sniffing is best-effort only; it
    /// never inspects raw exception text because messages here are already
    /// sanitized, fixed-format strings produced by the ingestion pipeline.
    /// </summary>
    public static JobIngestionReasonCode FromOutcome(
        JobIngestionOutcome outcome,
        string? message = null) => outcome switch
    {
        JobIngestionOutcome.Created => JobIngestionReasonCode.None,
        JobIngestionOutcome.MatchedByUrl => JobIngestionReasonCode.DuplicateCanonicalUrl,
        JobIngestionOutcome.MatchedByFingerprint => JobIngestionReasonCode.DuplicateFingerprint,
        JobIngestionOutcome.MatchedByFuzzy => JobIngestionReasonCode.DuplicateFuzzyMatch,
        JobIngestionOutcome.CompanyNotFound => JobIngestionReasonCode.CompanyNotFound,
        JobIngestionOutcome.Invalid => ClassifyInvalid(message),
        _ => JobIngestionReasonCode.Unknown
    };

    private static JobIngestionReasonCode ClassifyInvalid(string? message) =>
        message is not null &&
        message.Contains("ApplicationUrl", StringComparison.Ordinal)
            ? JobIngestionReasonCode.InvalidApplicationUrl
            : JobIngestionReasonCode.InvalidSourceData;
}
