using JobPortal.Application.Features.PublicJobs;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IPublicJobRepository
{
    Task<(IReadOnlyCollection<PublicJobSummary> Items, int TotalCount)> SearchAsync(
        PublicJobQuery query, CancellationToken cancellationToken = default);
    Task<PublicJobDetails?> GetDetailsAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PublicJobSummary>> GetRelatedAsync(
        string slug, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PopularCompanyResponse>> GetPopularCompaniesAsync(
        int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PublicCompanyLogoResponse>> GetCompaniesWithCandidateVisibleJobsAsync(
        CancellationToken cancellationToken = default);
    Task<PublicJobFilterOptionsResponse> GetFilterOptionsAsync(
        PublicJobQuery query, CancellationToken cancellationToken = default);
}
