namespace JobPortal.Application.Features.PublicJobs;

/// <summary>Shared normalization for public search, candidate browse and contextual facets.</summary>
public static class JobBrowseQueryPolicy
{
    public static string? NormalizeText(string? value)
    {
        var normalized = string.Join(" ", value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? []);
        return normalized.Length == 0 ? null : normalized;
    }

    public static string? SearchText(PublicJobQuery query) =>
        NormalizeText(query.Q) ?? NormalizeText(query.Keyword) ?? NormalizeText(query.Search);

    public static PublicJobQuery Normalize(PublicJobQuery query) => query with
    {
        Q = SearchText(query),
        Location = NormalizeText(query.Location)
    };
}
