using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Services;

public sealed class JobSourceCategoryResolver(
    IOptionsMonitor<JobAggregationOptions> options,
    ICategoryManagementRepository categories) : IJobSourceCategoryResolver
{
    public async Task<Guid?> ResolveCategoryIdAsync(JobSource source, RawExternalJob rawJob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rawJob);
        cancellationToken.ThrowIfCancellationRequested();
        if (rawJob.CategoryId is { } explicitId && explicitId != Guid.Empty &&
            await categories.ExistsAsync(explicitId, cancellationToken)) return explicitId;

        var key = ExternalJobNormalizer.NormalizeText(rawJob.ExternalCategory);
        if (key is not null)
        {
            // Conflicting keys after normalization are ambiguous: fail closed to source fallback.
            var values = options.CurrentValue.CategoryMappings
                .Where(x => string.Equals(ExternalJobNormalizer.NormalizeText(x.Key), key, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length == 1 && Guid.TryParse(values[0], out var mappedId) && mappedId != Guid.Empty &&
                await categories.ExistsAsync(mappedId, cancellationToken)) return mappedId;
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

        return await categories.ExistsAsync(categoryId, cancellationToken) ? categoryId : null;
    }
}
