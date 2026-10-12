namespace JobPortal.Application.Features.PublicJobs;

public sealed record ExperienceFilterRange(string Key, string Label, int MinimumInclusive, int? MaximumInclusive);
public static class ExperienceFilterPolicy
{
    // Whole-year inclusive bins for UI synchronization. Requirements themselves remain exact open/closed intervals.
    public static IReadOnlyList<ExperienceFilterRange> Ranges { get; } = Array.AsReadOnly(new ExperienceFilterRange[]
    {
        new("0", "No experience", 0, 0), new("1-2", "1–2 years", 1, 2), new("3-5", "3–5 years", 3, 5),
        new("6-10", "6–10 years", 6, 10), new("11+", "11+ years", 11, null)
    });
}
