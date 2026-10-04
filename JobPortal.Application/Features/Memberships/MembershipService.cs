using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;
using JobPortal.Shared.Models;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Memberships;

public sealed class MembershipService(
    IMembershipRepository memberships,
    ICandidateService candidates) : IMembershipService
{
    public async Task<ApplicationAccessResponse> GetApplicationAccessAsync(
        Guid? userId,
        string jobSlug,
        CancellationToken cancellationToken = default)
    {
        var job = await memberships.GetAvailableJobAsync(jobSlug, cancellationToken)
            ?? throw new NotFoundException("Job was not found.");

        if (!userId.HasValue)
        {
            return new ApplicationAccessResponse(
                ApplicationAccessStatus.LoginRequired,
                "Login Required");
        }

        // The shared candidate workflow owns authorization, duplicate detection,
        // quota consumption and the atomic save before an employer URL is returned.
        await candidates.ApplyJobAsync(userId.Value, job.JobId,
            new CreateJobApplicationRequest(ApplicationMethod: ApplicationMethod.External), cancellationToken);

        return new ApplicationAccessResponse(
            ApplicationAccessStatus.Granted,
            "Application access granted.",
            job.ApplicationUrl);
    }

    public Task<IReadOnlyCollection<MembershipResponse>> GetMyMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        memberships.GetMembershipsForUserAsync(userId, cancellationToken);

    public async Task<PagedResponse<MembershipHistoryResponse>> GetHistoryAsync(
        Guid userId,
        HistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);

        var result = await memberships.GetHistoryAsync(
            userId,
            query,
            cancellationToken);

        return new PagedResponse<MembershipHistoryResponse>(
            result.Items,
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }
}
