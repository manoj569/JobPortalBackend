namespace JobPortal.Application.Features.JobAggregation;

public sealed class JobAggregationOptions
{
    public const string SectionName = "JobAggregation";

    // String values deliberately allow invalid operator input to fail closed.
    public Dictionary<string, string?> SourceCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
