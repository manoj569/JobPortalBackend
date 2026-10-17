using JobPortal.Application.Features.Points;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerHarborPointsRepository(JobPortalDbContext db) : ICareerHarborPointsRepository
{
    public async Task<IReadOnlyDictionary<string, int>> TotalsByReasonAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.UserPointTransactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.Reason != UserPointTransaction.InterviewInsightHelpfulReason)
            .GroupBy(x => x.Reason)
            .Select(g => new { Reason = g.Key, Points = g.Sum(x => x.Points) })
            .ToListAsync(ct);

        var helpfulPoints = await (
            from transaction in db.UserPointTransactions.AsNoTracking()
            join feedback in db.InsightHelpfulnessFeedback.AsNoTracking()
                on transaction.InsightHelpfulnessFeedbackId equals (Guid?)feedback.Id
            join insight in db.InterviewInsights.AsNoTracking()
                on feedback.InsightId equals insight.Id
            where transaction.UserId == userId &&
                  transaction.Reason == UserPointTransaction.InterviewInsightHelpfulReason &&
                  !feedback.IsDeleted && !insight.IsDeleted &&
                  feedback.Helpfulness == InsightHelpfulness.Helped &&
                  insight.Status == InterviewInsightStatus.Published
            select (int?)transaction.Points).SumAsync(ct) ?? 0;
        if (helpfulPoints > 0)
            rows.Add(new { Reason = UserPointTransaction.InterviewInsightHelpfulReason, Points = helpfulPoints });

        return rows.ToDictionary(x => x.Reason, x => x.Points, StringComparer.Ordinal);
    }

    public Task<bool> HasRewardAsync(string reason, Guid sourceId, CancellationToken ct) =>
        db.UserPointTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.Reason == reason &&
            ((reason == UserPointTransaction.InterviewInsightPublishedReason && x.InterviewInsightId == sourceId) ||
             (reason == UserPointTransaction.ReferralOpportunitySharedReason && x.JobReferralId == sourceId) ||
             (reason == UserPointTransaction.ReferralCandidateConfirmedReason && x.ReferralRequestId == sourceId) ||
             (reason == UserPointTransaction.InterviewInsightHelpfulReason && x.InsightHelpfulnessFeedbackId == sourceId)), ct);

    public async Task AddAsync(UserPointTransaction transaction, CancellationToken ct) =>
        await db.UserPointTransactions.AddAsync(transaction, ct);
}
