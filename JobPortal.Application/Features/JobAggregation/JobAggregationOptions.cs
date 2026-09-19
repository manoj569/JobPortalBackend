namespace JobPortal.Application.Features.JobAggregation;

public sealed class JobAggregationOptions
{
    public const string SectionName = "JobAggregation";

    // String values deliberately allow invalid operator input to fail closed.
    public Dictionary<string, string?> SourceCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> CategoryMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public JobAggregationSchedulerOptions Scheduler { get; set; } = new();
}

public sealed class JobAggregationSchedulerOptions
{
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 25;
    public int MaxConcurrentSources { get; set; } = 3;
}
