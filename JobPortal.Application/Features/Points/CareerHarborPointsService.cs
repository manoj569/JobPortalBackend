using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Points;

public sealed class CareerHarborPointsService(ICareerHarborPointsRepository repository) : ICareerHarborPointsService
{
    public const int MentorEligibilityThreshold = 3000;

    public async Task<CareerHarborPointsBalance> BalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var totals = await repository.TotalsByReasonAsync(userId, ct);
        var published = Total(UserPointTransaction.InterviewInsightPublishedReason);
        var helpful = Total(UserPointTransaction.InterviewInsightHelpfulReason);
        var shared = Total(UserPointTransaction.ReferralOpportunitySharedReason);
        var confirmed = Total(UserPointTransaction.ReferralCandidateConfirmedReason);
        var total = published + helpful + shared + confirmed;
        return new(total, MentorEligibilityThreshold, Math.Max(0, MentorEligibilityThreshold - total),
            total >= MentorEligibilityThreshold, published, helpful, shared, confirmed);

        int Total(string reason) => totals.GetValueOrDefault(reason);
    }

    public Task AwardInterviewInsightPublishedAsync(InterviewInsight insight, CancellationToken ct = default)
    {
        if (insight.Status != InterviewInsightStatus.Published || insight.IsDeleted)
            throw new InvalidOperationException("Only a published, active interview insight can earn points.");
        return AddOnceAsync(UserPointTransaction.InterviewInsightPublishedReason, insight.Id,
            UserPointTransaction.ForInterviewInsightPublished(insight), ct);
    }

    public Task AwardReferralOpportunitySharedAsync(JobReferral opportunity, CancellationToken ct = default)
    {
        if (opportunity.ApprovalStatus != JobReferralApprovalStatus.Approved || opportunity.IsDeleted)
            throw new InvalidOperationException("Only an approved, active referral opportunity can earn points.");
        return AddOnceAsync(UserPointTransaction.ReferralOpportunitySharedReason, opportunity.Id,
            UserPointTransaction.ForReferralOpportunityShared(opportunity), ct);
    }

    public Task AwardReferralCandidateConfirmedAsync(ReferralRequest request, CancellationToken ct = default)
    {
        if (request.Status != ReferralRequestStatus.CandidateConfirmed || !request.CandidateConfirmedAtUtc.HasValue || request.IsDeleted)
            throw new InvalidOperationException("Only a candidate-confirmed referral can earn points.");
        return AddOnceAsync(UserPointTransaction.ReferralCandidateConfirmedReason, request.Id,
            UserPointTransaction.ForReferralCandidateConfirmed(request), ct);
    }

    private async Task AddOnceAsync(string reason, Guid sourceId, UserPointTransaction transaction, CancellationToken ct)
    {
        // This read makes normal retries a no-op; unique source indexes remain the concurrency guarantee.
        if (await repository.HasRewardAsync(reason, sourceId, ct)) return;
        await repository.AddAsync(transaction, ct);
    }
}
