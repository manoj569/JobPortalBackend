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
