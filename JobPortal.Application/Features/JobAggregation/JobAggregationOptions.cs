namespace JobPortal.Application.Features.JobAggregation;

public sealed class JobAggregationOptions
{
    public const string SectionName = "JobAggregation";

    // Safe default. Automatic publishing must be explicitly enabled.
    public bool AutoPublishEnabled { get; set; }

    public int GreenhouseDetailConcurrency { get; set; } = 2;

    // Optional legacy configuration key retained for geographic filters, test limits and licensed logos.
    // No entry or rights evidence is required to execute or publish a source. Defaults remain India-only.
    // Keys are persistent JobSource IDs, never guessed company/board names.
    public Dictionary<string, JobSourcePublicationApproval> SourceApprovals { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // String values deliberately allow invalid operator input to fail closed.
    public Dictionary<string, string?> SourceCategories { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> CategoryMappings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public JobAggregationSchedulerOptions Scheduler { get; set; } = new();
}

public sealed class JobSourcePublicationApproval
{
    public Guid CompanyId { get; set; }
    public JobPortal.Domain.Enums.AtsType AtsType { get; set; }
    public string AtsIdentifier { get; set; } = string.Empty;
    public string CareerPageUrl { get; set; } = string.Empty;
    public string RightsEvidence { get; set; } = string.Empty;
    public DateTimeOffset RightsExpireAtUtc { get; set; }
    public bool IndiaOnly { get; set; } = true;
    // Exact provider location labels whose Indian geography was independently verified by the operator.
    public string[] VerifiedIndiaLocations { get; set; } = [];
    public bool AllowExplicitWorldwideRemote { get; set; }
    // Zero = normal scan. Any positive limit ALWAYS means an incomplete validation run.
    public int TestImportLimit { get; set; }
    public string? LogoUrl { get; set; }
    public string? LogoRightsEvidence { get; set; }
}

public sealed class JobAggregationSchedulerOptions
{
    public bool Enabled { get; set; }

    public int PollIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 25;

    public int MaxConcurrentSources { get; set; } = 3;

    // Minimum automatic retry delay after an unsuccessful/interrupted attempt.
    // The existing source scan interval still applies when it is longer.
    public int InterruptedRunCooldownMinutes { get; set; } = 60;
}
