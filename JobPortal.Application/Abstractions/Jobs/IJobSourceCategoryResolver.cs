using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Jobs;

public interface IJobSourceCategoryResolver
{
    Task<Guid?> ResolveCategoryIdAsync(JobSource source, CancellationToken cancellationToken = default);
    Task<Guid?> ResolveCategoryIdAsync(JobSource source, RawExternalJob rawJob, CancellationToken cancellationToken = default);
}
