using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IJobSourceManagementRepository : IJobSourceRepository
{
    Task<(IReadOnlyCollection<JobSource> Items, int TotalCount)> SearchAsync(
        JobSourceSearchQuery query, CancellationToken cancellationToken = default);
    Task<bool> ConfigurationExistsAsync(Guid companyId, AtsType atsType, string? atsIdentifier,
        Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task AddAsync(JobSource source, CancellationToken cancellationToken = default);
    void Remove(JobSource source);
}
