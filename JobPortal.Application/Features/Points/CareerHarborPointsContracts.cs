using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.Points;

public sealed record CareerHarborPointsBalance(
    int TotalPoints,
    int MentorEligibilityThreshold,
    int RemainingPoints,
    bool IsPointsEligibleForMentor,
    int InterviewInsightPublishedPoints,
    int InterviewInsightHelpfulPoints,
    int ReferralOpportunitySharedPoints,
    int ReferralCandidateConfirmedPoints,
    bool IsPaidEligibilityAvailable = false,
    bool IsPaidEligibleForMentor = false)
{
    public bool IsEligibleForMentorOnboarding => IsPointsEligibleForMentor || IsPaidEligibleForMentor;
}

public interface ICareerHarborPointsRepository
{
    Task<IReadOnlyDictionary<string, int>> TotalsByReasonAsync(Guid userId, CancellationToken ct);
    Task<bool> HasRewardAsync(string reason, Guid sourceId, CancellationToken ct);
    Task AddAsync(UserPointTransaction transaction, CancellationToken ct);
}

public interface ICareerHarborPointsService
{
    Task<CareerHarborPointsBalance> BalanceAsync(Guid userId, CancellationToken ct = default);
    Task AwardInterviewInsightPublishedAsync(InterviewInsight insight, CancellationToken ct = default);
    Task AwardReferralOpportunitySharedAsync(JobReferral opportunity, CancellationToken ct = default);
    Task AwardReferralCandidateConfirmedAsync(ReferralRequest request, CancellationToken ct = default);
}
