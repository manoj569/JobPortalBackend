using JobPortal.Application.Features.Points;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerHarborPointsTests
{
    [Fact]
    public async Task BalanceIsLedgerDerivedAndReportsBreakdownAndEligibilityThreshold()
    {
        var options = new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new JobPortalDbContext(options);
        var user = new User { Email = "points@example.test", NormalizedEmail = "POINTS@EXAMPLE.TEST",
            FirstName = "Points", LastName = "User", RoleId = SystemRoleIds.Candidate, Status = UserStatus.Active };
        var company = new Company { Name = "Employer", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = user.Id };
        var insight = new InterviewInsight
        {
            AuthorCandidateId = user.Id, CompanyId = company.Id, RoleTitle = "Engineer",
            InterviewDateMonth = new DateOnly(2026, 9, 1), ProcessSummary = "Rounds", PreparationTips = "Practice",
            Status = InterviewInsightStatus.Published
        };
        var feedback = new InsightHelpfulnessFeedback
        {
            InsightId = insight.Id, CandidateId = Guid.NewGuid(), CandidateInterviewScheduleId = Guid.NewGuid(),
            Helpfulness = InsightHelpfulness.Helped
        };
        db.AddRange(user, company, insight, feedback);
        db.UserPointTransactions.AddRange(
            UserPointTransaction.ForInterviewInsightHelpful(user.Id, feedback),
            new UserPointTransaction { UserId = user.Id, InterviewInsightId = Guid.NewGuid(), Points = 5,
                Reason = UserPointTransaction.InterviewInsightPublishedReason },
            new UserPointTransaction { UserId = user.Id, JobReferralId = Guid.NewGuid(), Points = 5,
                Reason = UserPointTransaction.ReferralOpportunitySharedReason },
            new UserPointTransaction { UserId = user.Id, ReferralRequestId = Guid.NewGuid(), Points = 300,
                Reason = UserPointTransaction.ReferralCandidateConfirmedReason });
        await db.SaveChangesAsync();

        var service = new CareerHarborPointsService(new CareerHarborPointsRepository(db));
        var balance = await service.BalanceAsync(user.Id);
        Assert.Equal(320, balance.TotalPoints);
        Assert.Equal(3000, balance.MentorEligibilityThreshold);
        Assert.Equal(2680, balance.RemainingPoints);
        Assert.False(balance.IsPointsEligibleForMentor);
        Assert.False(balance.IsPaidEligibilityAvailable);
        Assert.False(balance.IsPaidEligibleForMentor);
        Assert.False(balance.IsEligibleForMentorOnboarding);
        Assert.Equal(5, balance.InterviewInsightPublishedPoints);
        Assert.Equal(10, balance.InterviewInsightHelpfulPoints);
        Assert.Equal(5, balance.ReferralOpportunitySharedPoints);
        Assert.Equal(300, balance.ReferralCandidateConfirmedPoints);

        var thresholdUserId = Guid.NewGuid();
        db.UserPointTransactions.AddRange(Enumerable.Range(0, 10).Select(_ => new UserPointTransaction
        {
            UserId = thresholdUserId, ReferralRequestId = Guid.NewGuid(), Points = UserPointTransaction.ReferralCandidateConfirmedPoints,
            Reason = UserPointTransaction.ReferralCandidateConfirmedReason
        }));
        await db.SaveChangesAsync();
        balance = await service.BalanceAsync(thresholdUserId);
        Assert.Equal(3000, balance.TotalPoints);
        Assert.True(balance.IsPointsEligibleForMentor);
        Assert.Equal(0, balance.RemainingPoints);
        db.UserPointTransactions.Add(new UserPointTransaction
        {
            UserId = thresholdUserId, ReferralRequestId = Guid.NewGuid(), Points = UserPointTransaction.ReferralCandidateConfirmedPoints,
            Reason = UserPointTransaction.ReferralCandidateConfirmedReason
        });
        await db.SaveChangesAsync();
        balance = await service.BalanceAsync(thresholdUserId);
        Assert.Equal(3300, balance.TotalPoints);
        Assert.Equal(0, balance.RemainingPoints);
    }
}
