using JobPortal.Application.Features.Points;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Tests;

internal sealed class CareerHarborPointsServiceTestDouble : ICareerHarborPointsService
{
    public int TotalPoints { get; set; } = CareerHarborPointsService.MentorEligibilityThreshold;

    public Task<CareerHarborPointsBalance> BalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var pointsEligible = TotalPoints >= CareerHarborPointsService.MentorEligibilityThreshold;
        return Task.FromResult(new CareerHarborPointsBalance(TotalPoints,
            CareerHarborPointsService.MentorEligibilityThreshold,
            Math.Max(0, CareerHarborPointsService.MentorEligibilityThreshold - TotalPoints),
            pointsEligible, 0, 0, 0, 0));
    }

    public Task AwardInterviewInsightPublishedAsync(InterviewInsight insight, CancellationToken ct = default) => Task.CompletedTask;
    public Task AwardReferralOpportunitySharedAsync(JobReferral opportunity, CancellationToken ct = default) => Task.CompletedTask;
    public Task AwardReferralCandidateConfirmedAsync(ReferralRequest request, CancellationToken ct = default) => Task.CompletedTask;
}
