using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class InterviewInsightsTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NewInsightIsPendingAndOnlyOwnerCanEditOrDelete()
    {
        await using var f = await Fixture.CreateAsync();
        await f.AddPastScheduleAsync(f.AuthorId, f.CompanyAId);
        var insight = await f.Service.CreateAsync(f.AuthorId, ValidCreate(f.CompanyAId));
        Assert.Equal(InterviewInsightStatus.PendingReview, insight.Status);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.UpdateAsync(f.ReaderId, insight.Id, ValidUpdate()));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.DeleteAsync(f.ReaderId, insight.Id));
        await f.Service.DeleteAsync(f.AuthorId, insight.Id);
        Assert.True((await f.Db.InterviewInsights.IgnoreQueryFilters().SingleAsync(x => x.Id == insight.Id)).IsDeleted);
    }

    [Fact]
    public async Task FullContentRequiresApplicationOrScheduleAndAnonymousIdentityNeverLeaks()
    {
        await using var f = await Fixture.CreateAsync();
        var insight = await f.AddPublishedInsightAsync(f.AuthorId, f.CompanyAId, anonymous: true);
        var locked = await f.Service.GetAsync(f.ReaderId, insight.Id);
        Assert.False(locked.CanReadFull);
        Assert.Empty(locked.Rounds);
        Assert.Null(locked.PreparationTips);
        Assert.Null(locked.AuthorDisplayName);
        await f.AddScheduleAsync(f.ReaderId, f.CompanyAId, Now.AddDays(1));
        var unlocked = await f.Service.GetAsync(f.ReaderId, insight.Id);
        Assert.True(unlocked.CanReadFull);
        Assert.Single(unlocked.Rounds);
        Assert.DoesNotContain("author@example.com", JsonSerializer.Serialize(unlocked), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026-07-15", JsonSerializer.Serialize(unlocked), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FeedbackBeforeInterviewDifferentCompanyAndSelfFeedbackAreRejected()
    {
        await using var f = await Fixture.CreateAsync();
        var insight = await f.AddPublishedInsightAsync(f.AuthorId, f.CompanyAId);
        var future = await f.AddScheduleAsync(f.ReaderId, f.CompanyAId, Now.AddDays(1));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddFeedbackAsync(f.ReaderId, insight.Id, ValidFeedback(future.Id)));
        var other = await f.AddPastScheduleAsync(f.ReaderId, f.CompanyBId);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddFeedbackAsync(f.ReaderId, insight.Id, ValidFeedback(other.Id)));
        var authorSchedule = await f.AddPastScheduleAsync(f.AuthorId, f.CompanyAId);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.AddFeedbackAsync(f.AuthorId, insight.Id, ValidFeedback(authorSchedule.Id)));
    }

    [Fact]
    public async Task EligiblePositiveFeedbackScoresOnceAndDuplicateIsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        var insight = await f.AddPublishedInsightAsync(f.AuthorId, f.CompanyAId);
        var schedule = await f.AddPastScheduleAsync(f.ReaderId, f.CompanyAId);
        var result = await f.Service.AddFeedbackAsync(f.ReaderId, insight.Id, ValidFeedback(schedule.Id));
        Assert.Equal(1, result.HelpfulConfirmedCount);
        Assert.Equal(3, result.QualityScore);
        Assert.Single(f.Db.Notifications.Where(x => x.UserId == f.AuthorId));
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.AddFeedbackAsync(f.ReaderId, insight.Id, ValidFeedback(schedule.Id)));
        Assert.Single(f.Db.InsightHelpfulnessFeedback);
        Assert.Equal(3, (await f.Service.ContributionsAsync(f.AuthorId)).ContributionScore);
    }

    [Fact]
    public async Task RejectedOrActionedReportedInsightIsHiddenFromCandidates()
    {
        await using var f = await Fixture.CreateAsync();
        var insight = await f.AddPublishedInsightAsync(f.AuthorId, f.CompanyAId);
        await f.Admin.ModerateAsync(Guid.NewGuid(), insight.Id,
            new(InterviewInsightStatus.Rejected, "Contains material outside the guidelines."));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.ReaderId, insight.Id));

        var second = await f.AddPublishedInsightAsync(f.AuthorId, f.CompanyAId);
        var report = await f.Service.ReportAsync(f.ReaderId, second.Id, new(InsightReportReason.ConfidentialContent, "Possible confidential content."));
        await f.Admin.ModerateReportAsync(Guid.NewGuid(), report.Id, new(InsightReportStatus.Actioned));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.ReaderId, second.Id));
    }

    private static CreateInterviewInsightRequest ValidCreate(Guid companyId) => new(companyId, null,
        "Software Engineer", "2-4 years", new DateOnly(2026, 7, 1), InterviewDifficulty.Moderate,
        "The process included technical and managerial discussions.",
        "Review core concepts and explain your reasoning clearly.", InterviewOutcome.PreferNotToSay,
        true, true, [new(InterviewRoundType.Technical, "Technical discussion", 45,
            "Paraphrased data structures and API design topics.", "Think aloud and clarify assumptions.")]);
    private static UpdateInterviewInsightRequest ValidUpdate() => new("Software Engineer", null,
        new DateOnly(2026, 7, 1), InterviewDifficulty.Moderate, "Updated process summary.",
        "Updated preparation advice.", null, true, true,
        [new(InterviewRoundType.Technical, null, 30, "Paraphrased technical topics.", null)]);
    private static CreateInsightFeedbackRequest ValidFeedback(Guid scheduleId) =>
        new(scheduleId, InsightHelpfulness.Helped, InterviewMatch.Matched, "The topics were useful.");

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public JobPortalDbContext Db { get; }
        public InterviewInsightService Service { get; }
        public AdminInterviewInsightService Admin { get; }
        public Guid AuthorId { get; } = Guid.NewGuid();
        public Guid ReaderId { get; } = Guid.NewGuid();
        public Guid CompanyAId { get; } = Guid.NewGuid();
        public Guid CompanyBId { get; } = Guid.NewGuid();
        private readonly InterviewInsightRepository repository;

        private Fixture(JobPortalDbContext db)
        {
            Db = db;
            repository = new(db);
            var time = new FixedTimeProvider();
            Service = new(repository, new AuditWriterTestDouble(), new CreateInterviewInsightRequestValidator(),
                new UpdateInterviewInsightRequestValidator(), new CreateInterviewScheduleRequestValidator(),
                new UpdateInterviewScheduleRequestValidator(), new CreateInsightFeedbackRequestValidator(),
                new CreateInsightReportRequestValidator(), time);
            Admin = new(repository, new AuditWriterTestDouble(), time);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var f = new Fixture(db);
            var role = new Role { Id = SystemRoleIds.Candidate, Name = "Candidate", NormalizedName = "CANDIDATE" };
            var owner = User(Guid.NewGuid(), "owner@example.com", role);
            db.AddRange(role, owner, User(f.AuthorId, "author@example.com", role), User(f.ReaderId, "reader@example.com", role),
                new Company { Id = f.CompanyAId, Name = "Infosys", Slug = "infosys", OwnerUserId = owner.Id, OwnerUser = owner },
                new Company { Id = f.CompanyBId, Name = "Other", Slug = "other", OwnerUserId = owner.Id, OwnerUser = owner });
            await db.SaveChangesAsync();
            return f;
        }
        private static User User(Guid id, string email, Role role) => new()
        {
            Id = id, Email = email, NormalizedEmail = email, FirstName = "Candidate", LastName = "User",
            Status = UserStatus.Active, RoleId = role.Id, Role = role
        };
        public async Task<InterviewInsight> AddPublishedInsightAsync(Guid author, Guid company, bool anonymous = false)
        {
            var insight = new InterviewInsight
            {
                AuthorCandidateId = author, CompanyId = company, RoleTitle = "Engineer",
                InterviewDateMonth = new DateOnly(2026, 7, 1), OverallDifficulty = InterviewDifficulty.Moderate,
                ProcessSummary = "A concise process summary.", PreparationTips = "Prepare core concepts.",
                IsAnonymous = anonymous, Status = InterviewInsightStatus.Published, PublishedAtUtc = Now.AddDays(-1),
                Rounds = [new InterviewRound { Sequence = 1, RoundType = InterviewRoundType.Technical,
                    QuestionsOrTopics = "Paraphrased API design topics." }]
            };
            Db.InterviewInsights.Add(insight);
            await Db.SaveChangesAsync();
            return insight;
        }
        public Task<CandidateInterviewSchedule> AddPastScheduleAsync(Guid candidate, Guid company) => AddScheduleAsync(candidate, company, Now.AddDays(-1));
        public async Task<CandidateInterviewSchedule> AddScheduleAsync(Guid candidate, Guid company, DateTime at)
        {
            var schedule = new CandidateInterviewSchedule { CandidateId = candidate, CompanyId = company,
                InterviewAtUtc = at, ConfirmFeedbackAvailableAtUtc = at, Status = InterviewScheduleStatus.Scheduled };
            Db.CandidateInterviewSchedules.Add(schedule);
            await Db.SaveChangesAsync();
            return schedule;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
