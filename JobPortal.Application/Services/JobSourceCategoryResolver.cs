using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Services;

public sealed class JobSourceCategoryResolver(
    IOptionsMonitor<JobAggregationOptions> options,
    ICategoryManagementRepository categories,
    IExternalJobCategoryClassifier? classifier = null) : IJobSourceCategoryResolver, IJobSourceCategoryRunCache
{
    private bool _running;
    private readonly HashSet<Guid> _validCategories = [];
    public void BeginRun() { _running = true; _validCategories.Clear(); _categoryOptions = null; }
    public void EndRun() { _running = false; _validCategories.Clear(); _categoryOptions = null; }
    private async Task<bool> ExistsAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_running && _validCategories.Contains(id)) return true;
        var exists = await categories.ExistsAsync(id, token);
        if (_running && exists) _validCategories.Add(id);
        return exists;
    }
    private IReadOnlyCollection<JobPortal.Application.Features.AdminManagement.AdminOptionResponse>? _categoryOptions;
    public async Task<Guid?> ResolveCategoryIdAsync(JobSource source, RawExternalJob rawJob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rawJob);
        cancellationToken.ThrowIfCancellationRequested();
        if (rawJob.CategoryId is { } explicitId && explicitId != Guid.Empty &&
            await ExistsAsync(explicitId, cancellationToken)) return explicitId;

        var key = ExternalJobNormalizer.NormalizeText(rawJob.ExternalCategory);
        if (key is not null)
        {
            // Conflicting keys after normalization are ambiguous: fail closed to source fallback.
            var values = options.CurrentValue.CategoryMappings
                .Where(x => string.Equals(ExternalJobNormalizer.NormalizeText(x.Key), key, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length == 1 && Guid.TryParse(values[0], out var mappedId) && mappedId != Guid.Empty &&
                await ExistsAsync(mappedId, cancellationToken)) return mappedId;
        }
        var classified = classifier?.Classify(rawJob);
        if (classified is not null)
        {
            _categoryOptions ??= await categories.GetOptionsAsync(cancellationToken);
            var matches = _categoryOptions.Where(c => ExternalJobCategoryClassifier.MatchesCategory(classified, c.Slug, c.Name)).ToArray();
            // Prefer the canonical slug; otherwise reuse a unique existing equivalent category.
            var exact = matches.Where(c => string.Equals(c.Slug, classified, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length == 1) return exact[0].Id;
            // Strong evidence without an unambiguous taxonomy entry must not become an unrelated fallback.
            return matches.Length == 1 ? matches[0].Id : null;
        }
        return await ResolveCategoryIdAsync(source, cancellationToken);
    }

    public async Task<Guid?> ResolveCategoryIdAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Id == Guid.Empty) return null;
        if (!options.CurrentValue.SourceCategories.TryGetValue(source.Id.ToString("D"), out var value) ||
            !Guid.TryParse(value, out var categoryId) || categoryId == Guid.Empty)
            return null;

        return await ExistsAsync(categoryId, cancellationToken) ? categoryId : null;
    }
}
