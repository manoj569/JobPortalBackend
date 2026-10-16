using FluentValidation;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.PublicJobs;

public sealed class PublicJobService(
    IPublicJobRepository repository,
    IValidator<PublicJobQuery> validator) : IPublicJobService
{
    public async Task<PublicJobSearchResponse> SearchAsync(
        PublicJobQuery query, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        query = SalaryQueryPolicy.Normalize(JobBrowseQueryPolicy.Normalize(query));
        var result = await repository.SearchAsync(query, cancellationToken);
        return new PublicJobSearchResponse(
            result.Items,
            query.EffectivePageNumber,
            query.PageSize,
            result.TotalCount,
            query.SortBy);
    }

    public async Task<PublicJobDetails> GetDetailsAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 270)
            throw new NotFoundException("Job was not found.");
        return await repository.GetDetailsAsync(slug.Trim(), cancellationToken)
            ?? throw new NotFoundException("Job was not found.");
    }

    public async Task<IReadOnlyCollection<PublicJobSummary>> GetRelatedAsync(
        string slug, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 20)
            throw new BadRequestException("Limit must be between 1 and 20.");
        return await repository.GetRelatedAsync(slug.Trim(), limit, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PopularCompanyResponse>> GetPopularCompaniesAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 50)
            throw new BadRequestException("Limit must be between 1 and 50.");
        return await repository.GetPopularCompaniesAsync(limit, cancellationToken);
    }

    public Task<IReadOnlyCollection<PublicCompanyLogoResponse>> GetCompaniesWithCandidateVisibleJobsAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetCompaniesWithCandidateVisibleJobsAsync(cancellationToken);

    public async Task<PublicJobFilterOptionsResponse> GetFilterOptionsAsync(
        PublicJobQuery query,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        query = SalaryQueryPolicy.Normalize(JobBrowseQueryPolicy.Normalize(query));
        return await repository.GetFilterOptionsAsync(query, cancellationToken);
    }
}
