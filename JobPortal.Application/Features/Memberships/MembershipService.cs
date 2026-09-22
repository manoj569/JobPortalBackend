using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Memberships;

public sealed class MembershipService(
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork) : IMembershipService
{
    private const string JobApplicationPlanCode = "CareerHarborMembership";

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

        var membership = await memberships.GetActiveForUserAsync(
            userId.Value,
            JobApplicationPlanCode,
            cancellationToken);

        if (membership is null)
        {
            return new ApplicationAccessResponse(
                ApplicationAccessStatus.PaymentRequired,
                "Payment Required");
        }

        await memberships.RecordApplicationAsync(
            userId.Value,
            job.JobId,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

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
