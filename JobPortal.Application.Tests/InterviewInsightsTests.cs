using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FluentValidation;
namespace JobPortal.Application.Tests;

public sealed class InterviewInsightsTests
{
    private static readonly DateTime Now =
        new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NewInsightIsPendingAndOnlyOwnerCanEditOrDelete()
    {
        await using var f = await Fixture.CreateAsync();

        await f.AddPastScheduleAsync(
            f.AuthorId,
            f.CompanyAId);

        var insight =
            await f.Service.CreateAsync(
                f.AuthorId,
                ValidCreate(f.CompanyAId));

        Assert.Equal(
            InterviewInsightStatus.PendingReview,
            insight.Status);

        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.UpdateAsync(
                f.ReaderId,
                insight.Id,
                ValidUpdate()));

        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.DeleteAsync(
                f.ReaderId,
                insight.Id));

        await f.Service.DeleteAsync(
            f.AuthorId,
            insight.Id);

        Assert.True(
            (await f.Db.InterviewInsights
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == insight.Id))
            .IsDeleted);
    }

    [Fact]
    public async Task FullContentRequiresApplicationOrScheduleAndAnonymousIdentityNeverLeaks()
    {
        await using var f = await Fixture.CreateAsync();

        var insight =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId,
                anonymous: true);

        var locked =
            await f.Service.GetAsync(
                f.ReaderId,
                insight.Id);

        Assert.False(locked.CanReadFull);
        Assert.Empty(locked.Rounds);
        Assert.Null(locked.PreparationTips);
        Assert.Null(locked.AuthorDisplayName);

        await f.AddScheduleAsync(
            f.ReaderId,
            f.CompanyAId,
            Now.AddDays(1));

        var unlocked =
            await f.Service.GetAsync(
                f.ReaderId,
                insight.Id);

        Assert.True(unlocked.CanReadFull);
        Assert.Single(unlocked.Rounds);

        Assert.DoesNotContain(
            "author@example.com",
            JsonSerializer.Serialize(unlocked),
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "2026-07-15",
            JsonSerializer.Serialize(unlocked),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FeedbackBeforeInterviewDifferentCompanyAndSelfFeedbackAreRejected()
    {
        await using var f = await Fixture.CreateAsync();

        var insight =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId);

        var future =
            await f.AddScheduleAsync(
                f.ReaderId,
                f.CompanyAId,
                Now.AddDays(1));

        await Assert.ThrowsAsync<BadRequestException>(
            () => f.Service.AddFeedbackAsync(
                f.ReaderId,
                insight.Id,
                ValidFeedback(future.Id)));

        var other =
            await f.AddPastScheduleAsync(
                f.ReaderId,
                f.CompanyBId);

        await Assert.ThrowsAsync<BadRequestException>(
            () => f.Service.AddFeedbackAsync(
                f.ReaderId,
                insight.Id,
                ValidFeedback(other.Id)));

        var authorSchedule =
            await f.AddPastScheduleAsync(
                f.AuthorId,
                f.CompanyAId);

        await Assert.ThrowsAsync<BadRequestException>(
            () => f.Service.AddFeedbackAsync(
                f.AuthorId,
                insight.Id,
                ValidFeedback(authorSchedule.Id)));
    }

    [Fact]
    public async Task EligiblePositiveFeedbackScoresOnceAndDuplicateIsRejected()
    {
        await using var f = await Fixture.CreateAsync();

        var insight =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId);

        var schedule =
            await f.AddPastScheduleAsync(
                f.ReaderId,
                f.CompanyAId);

        var result =
            await f.Service.AddFeedbackAsync(
                f.ReaderId,
                insight.Id,
                ValidFeedback(schedule.Id));

        Assert.Equal(
            1,
            result.HelpfulConfirmedCount);

        Assert.Equal(
            3,
            result.QualityScore);

        Assert.Single(
            f.Db.Notifications.Where(
                x => x.UserId == f.AuthorId));

        await Assert.ThrowsAsync<ConflictException>(
            () => f.Service.AddFeedbackAsync(
                f.ReaderId,
                insight.Id,
                ValidFeedback(schedule.Id)));

        Assert.Single(
            f.Db.InsightHelpfulnessFeedback);

        Assert.Equal(
            3,
            (await f.Service.ContributionsAsync(
                f.AuthorId))
            .ContributionScore);
    }

    [Fact]
    public async Task RejectedOrActionedReportedInsightIsHiddenFromCandidates()
    {
        await using var f = await Fixture.CreateAsync();

        var insight =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId);

        await f.Admin.ModerateAsync(
            Guid.NewGuid(),
            insight.Id,
            new(
                InterviewInsightStatus.Rejected,
                "Contains material outside the guidelines."));

        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.GetAsync(
                f.ReaderId,
                insight.Id));

        var second =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId);

        var report =
            await f.Service.ReportAsync(
                f.ReaderId,
                second.Id,
                new(
                    InsightReportReason.ConfidentialContent,
                    "Possible confidential content."));

        await f.Admin.ModerateReportAsync(
            Guid.NewGuid(),
            report.Id,
            new(InsightReportStatus.Actioned));

        await Assert.ThrowsAsync<NotFoundException>(
            () => f.Service.GetAsync(
                f.ReaderId,
                second.Id));
    }

    [Fact]
    public async Task ExploreFiltersSortsAndProjectsOnlySafePublishedCards()
    {
        await using var f = await Fixture.CreateAsync();

        var published =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId,
                anonymous: true);

        published.RoleTitle =
            "Senior Backend Engineer";

        published.ExperienceLevel =
            "Senior";

        published.Outcome =
            InterviewOutcome.Selected;

        published.InterviewFormat =
            InterviewFormat.Video;

        published.HelpfulConfirmedCount = 4;

        await f.Db.SaveChangesAsync();

        f.Db.InterviewInsights.AddRange(
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.PendingReview),
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.Rejected),
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.Hidden),
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.Published,
                deleted: true));

        var actioned =
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.Published);

        actioned.Reports.Add(
            new InsightReport
            {
                ReporterCandidateId =
                    f.ReaderId,
                Reason =
                    InsightReportReason.ConfidentialContent,
                Status =
                    InsightReportStatus.Actioned
            });

        f.Db.InterviewInsights.Add(actioned);

        await f.Db.SaveChangesAsync();

        var result =
            await f.Service.SearchAsync(
                f.ReaderId,
                new InterviewInsightQuery(
                    CompanyId: f.CompanyAId,
                    Role: "backend",
                    RoundType:
                        InterviewRoundType.Technical,
                    Difficulty:
                        InterviewDifficulty.Moderate,
                    Sort: "MostRounds",
                    Company: "info",
                    ExperienceLevel: "Senior",
                    Outcome:
                        InterviewOutcome.Selected,
                    InterviewFormat:
                        InterviewFormat.Video,
                    FromMonth: 6,
                    FromYear: 2026));

        var card =
            Assert.Single(result.Items);

        Assert.Equal(
            published.Id,
            card.Id);

        Assert.False(card.CanReadFull);
        Assert.False(card.CanGiveFeedback);
        Assert.True(card.IsAnonymous);
        Assert.Null(card.AuthorLabel);

        var json =
            JsonSerializer.Serialize(card);

        Assert.DoesNotContain(
            "interviewAtUtc",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "authorCandidate",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "application",
            json,
            StringComparison.OrdinalIgnoreCase);

        foreach (var sort in new[]
                 {
                     "MostHelpful",
                     "Newest",
                     "MostRounds"
                 })
        {
            Assert.Single(
                (await f.Service.SearchAsync(
                    f.ReaderId,
                    new(Sort: sort)))
                .Items);
        }
    }

    [Fact]
    public async Task CompanyAutocompleteContributionsAndScheduleMetadataRemainOwnerSafe()
    {
        await using var f = await Fixture.CreateAsync();

        var companies =
            await f.Service.SearchCompaniesAsync(
                f.ReaderId,
                " info ",
                5);

        var company =
            Assert.Single(companies);

        Assert.Equal(
            f.CompanyAId,
            company.Id);

        Assert.DoesNotContain(
            "Owner",
            JsonSerializer.Serialize(company),
            StringComparison.OrdinalIgnoreCase);

        var published =
            await f.AddPublishedInsightAsync(
                f.AuthorId,
                f.CompanyAId);

        published.QualityScore = 3;

        f.Db.InterviewInsights.Add(
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.PendingReview));

        var rejected =
            Insight(
                f.AuthorId,
                f.CompanyAId,
                InterviewInsightStatus.Rejected);

        rejected.ModerationReason =
            "Please remove confidential details.";

        f.Db.InterviewInsights.Add(rejected);

        await f.Db.SaveChangesAsync();

        var contributions =
            await f.Service.ContributionsAsync(
                f.AuthorId);

        Assert.Equal(
            1,
            contributions.InsightsPublished);

        Assert.Equal(
            1,
            contributions.PendingReview);

        Assert.Equal(
            1,
            contributions.NeedsChanges);

        Assert.Equal(
            3,
            contributions.Items!.Count);

        Assert.All(
            contributions.Items,
            x => Assert.Contains(
                f.Db.InterviewInsights,
                i =>
                    i.Id == x.Id &&
                    i.AuthorCandidateId ==
                    f.AuthorId));

        Assert.Null(
            contributions.Items
                .Single(x =>
                    x.Id == published.Id)
                .ReviewerChangeRequest);

        Assert.Equal(
            "Please remove confidential details.",
            contributions.Items
                .Single(x =>
                    x.Id == rejected.Id)
                .ReviewerChangeRequest);

        var schedule =
            await f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    Now.AddDays(2),
                    reminderRequested: true,
                    reminderOffsetMinutes: 30));

        Assert.Equal(
            InterviewFormat.Online,
            schedule.InterviewFormat);

        Assert.Equal(
            2,
            schedule.ExpectedRoundTypes!.Count);

        Assert.True(
            schedule.ReminderRequested);

        Assert.Equal(
            30,
            schedule.ReminderOffsetMinutes);

        Assert.Equal(
            "UTC",
            schedule.TimeZoneId);

        Assert.DoesNotContain(
            JsonSerializer.Serialize(schedule),
            JsonSerializer.Serialize(
                await f.Service.SearchAsync(
                    f.AuthorId,
                    new())),
            StringComparison.Ordinal);
    }

    // -------------------------------------------------------
    // Phase C - Interview reminder tests
    // -------------------------------------------------------

    [Fact]
    public async Task InterviewReminderThirtyMinutesBeforeCreatesInAppAndEmailDeliveries()
    {
        await using var f = await Fixture.CreateAsync();

        var interviewAt =
            new DateTime(
                2026,
                8,
                22,
                14,
                30,
                0,
                DateTimeKind.Utc);

        var schedule =
            await f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    interviewAt,
                    reminderRequested: true,
                    reminderOffsetMinutes: 30));

        Assert.True(
            schedule.ReminderRequested);

        Assert.Equal(
            30,
            schedule.ReminderOffsetMinutes);

        Assert.Equal(
            "UTC",
            schedule.TimeZoneId);

        var entity =
            await f.Db.CandidateInterviewSchedules
                .SingleAsync(x =>
                    x.Id == schedule.Id);

        Assert.NotEqual(
            Guid.Empty,
            entity.ReminderRevision);

        var deliveries =
            await f.Db.NotificationDeliveries
                .Where(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        schedule.Id)
                .OrderBy(x => x.Channel)
                .ToListAsync();

        Assert.Equal(
            2,
            deliveries.Count);

        Assert.Contains(
            deliveries,
            x => x.Channel ==
                 NotificationChannel.InApp);

        Assert.Contains(
            deliveries,
            x => x.Channel ==
                 NotificationChannel.Email);

        var expectedDue =
            new DateTime(
                2026,
                8,
                22,
                14,
                0,
                0,
                DateTimeKind.Utc);

        Assert.All(
            deliveries,
            delivery =>
            {
                Assert.Equal(
                    expectedDue,
                    delivery.ScheduledForUtc);

                Assert.Equal(
                    expectedDue,
                    delivery.NextAttemptAtUtc);

                Assert.Equal(
                    entity.ReminderRevision,
                    delivery.SourceRevision);

                Assert.Equal(
                    f.ReaderId,
                    delivery.UserId);

                Assert.Equal(
                    "/dashboard/interview-insights",
                    delivery.ActionUrl);

                Assert.Equal(
                    "Interview reminder",
                    delivery.Title);

                Assert.Contains(
                    "30 minutes",
                    delivery.Message,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public async Task ReminderDisabledCreatesNoInterviewReminderDeliveries()
    {
        await using var f = await Fixture.CreateAsync();

        var schedule =
            await f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    Now.AddDays(2),
                    reminderRequested: false,
                    reminderOffsetMinutes: 30));

        Assert.False(
            schedule.ReminderRequested);

        Assert.Empty(
            await f.Db.NotificationDeliveries
                .Where(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        schedule.Id)
                .ToListAsync());
    }

    [Fact]
    public async Task ReschedulingInterviewCreatesNewRevisionAndNewReminderDeliveries()
    {
        await using var f = await Fixture.CreateAsync();

        var originalInterviewAt =
            Now.AddDays(2);

        var created =
            await f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    originalInterviewAt,
                    reminderRequested: true,
                    reminderOffsetMinutes: 30));

        var storedBefore =
            await f.Db.CandidateInterviewSchedules
                .SingleAsync(x =>
                    x.Id == created.Id);

        var originalRevision =
            storedBefore.ReminderRevision;

        var originalDeliveries =
            await f.Db.NotificationDeliveries
                .Where(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        created.Id)
                .ToListAsync();

        Assert.Equal(
            2,
            originalDeliveries.Count);

        Assert.All(
            originalDeliveries,
            x => Assert.Equal(
                originalRevision,
                x.SourceRevision));

        var newInterviewAt =
            Now.AddDays(3).AddHours(2);

        var updated =
            await f.Service.UpdateScheduleAsync(
                f.ReaderId,
                created.Id,
                ValidScheduleUpdate(
                    newInterviewAt,
                    InterviewScheduleStatus.Scheduled,
                    reminderRequested: true,
                    reminderOffsetMinutes: 60));

        var storedAfter =
            await f.Db.CandidateInterviewSchedules
                .SingleAsync(x =>
                    x.Id == created.Id);

        Assert.NotEqual(
            originalRevision,
            storedAfter.ReminderRevision);

        Assert.Equal(
            60,
            updated.ReminderOffsetMinutes);

        var allDeliveries =
            await f.Db.NotificationDeliveries
                .Where(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        created.Id)
                .ToListAsync();

        Assert.Equal(
            4,
            allDeliveries.Count);

        var oldGeneration =
            allDeliveries
                .Where(x =>
                    x.SourceRevision ==
                    originalRevision)
                .ToList();

        var newGeneration =
            allDeliveries
                .Where(x =>
                    x.SourceRevision ==
                    storedAfter.ReminderRevision)
                .ToList();

        Assert.Equal(
            2,
            oldGeneration.Count);

        Assert.Equal(
            2,
            newGeneration.Count);

        var expectedNewDue =
            newInterviewAt.AddMinutes(-60);

        Assert.All(
            newGeneration,
            x =>
            {
                Assert.Equal(
                    expectedNewDue,
                    x.ScheduledForUtc);

                Assert.Contains(
                    "1 hour",
                    x.Message,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public async Task CancellingInterviewRotatesRevisionAndCreatesNoReplacementReminder()
    {
        await using var f = await Fixture.CreateAsync();

        var interviewAt =
            Now.AddDays(2);

        var created =
            await f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    interviewAt,
                    reminderRequested: true,
                    reminderOffsetMinutes: 30));

        var before =
            await f.Db.CandidateInterviewSchedules
                .SingleAsync(x =>
                    x.Id == created.Id);

        var originalRevision =
            before.ReminderRevision;

        Assert.Equal(
            2,
            await f.Db.NotificationDeliveries
                .CountAsync(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        created.Id));

        await f.Service.UpdateScheduleAsync(
            f.ReaderId,
            created.Id,
            ValidScheduleUpdate(
                interviewAt,
                InterviewScheduleStatus.Cancelled,
                reminderRequested: true,
                reminderOffsetMinutes: 30));

        var after =
            await f.Db.CandidateInterviewSchedules
                .SingleAsync(x =>
                    x.Id == created.Id);

        Assert.Equal(
            InterviewScheduleStatus.Cancelled,
            after.Status);

        Assert.NotEqual(
            originalRevision,
            after.ReminderRevision);

        // The old delivery records remain durable for audit/recovery,
        // but no delivery is created for the cancelled revision.
        Assert.Equal(
            2,
            await f.Db.NotificationDeliveries
                .CountAsync(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        created.Id));

        Assert.Empty(
            await f.Db.NotificationDeliveries
                .Where(x =>
                    x.Source ==
                        NotificationSource.InterviewReminder &&
                    x.SourceId ==
                        created.Id &&
                    x.SourceRevision ==
                        after.ReminderRevision)
                .ToListAsync());
    }

    [Fact]
    public async Task UnchangedScheduleUpdateDoesNotRotateRevisionOrCreateDuplicateReminder()
    {
        await using var fixture = await Fixture.CreateAsync();

        var interviewAtUtc = Now.AddHours(3);

        var created = await fixture.Service.CreateScheduleAsync(
            fixture.ReaderId,
            ValidScheduleCreate(
                fixture.CompanyAId,
                interviewAtUtc,
                reminderRequested: true,
                reminderOffsetMinutes: 30));

        var originalRevision = await fixture.Db.CandidateInterviewSchedules
            .Where(x => x.Id == created.Id)
            .Select(x => x.ReminderRevision)
            .SingleAsync();

        var deliveriesBefore = await fixture.Db.NotificationDeliveries
            .Where(x =>
                x.Source == NotificationSource.InterviewReminder &&
                x.SourceId == created.Id)
            .CountAsync();

        Assert.Equal(2, deliveriesBefore);

        await fixture.Service.UpdateScheduleAsync(
            fixture.ReaderId,
            created.Id,
            ValidScheduleUpdate(
                interviewAtUtc,
                InterviewScheduleStatus.Scheduled,
                reminderRequested: true,
                reminderOffsetMinutes: 30));

        var revisionAfter = await fixture.Db.CandidateInterviewSchedules
            .Where(x => x.Id == created.Id)
            .Select(x => x.ReminderRevision)
            .SingleAsync();

        var deliveriesAfter = await fixture.Db.NotificationDeliveries
            .Where(x =>
                x.Source == NotificationSource.InterviewReminder &&
                x.SourceId == created.Id)
            .CountAsync();

        Assert.Equal(originalRevision, revisionAfter);
        Assert.Equal(2, deliveriesAfter);
    }

    [Theory]
    [InlineData(5, "01:30 -04:00")]
    [InlineData(6, "01:30 -05:00")]
    public async Task ExplicitUtcDisambiguatesAutumnDstReminder(int hour, string displayedTime)
    {
        await using var f = await Fixture.CreateAsync();
        var at = new DateTime(2026, 11, 1, hour, 30, 0, DateTimeKind.Utc);
        var result = await f.Service.CreateScheduleAsync(f.ReaderId,
            ValidScheduleCreate(f.CompanyAId, at, true, 30) with { TimeZoneId = "America/New_York" });
        var deliveries = await f.Db.NotificationDeliveries.Where(d => d.SourceId == result.Id).ToArrayAsync();
        Assert.All(deliveries, d => { Assert.Equal(at.AddMinutes(-30), d.ScheduledForUtc); Assert.Contains(displayedTime, d.Message); });
    }

    [Fact]
    public async Task InvalidTimeZoneIsRejectedBeforePersisting()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.CreateScheduleAsync(f.ReaderId,
            ValidScheduleCreate(f.CompanyAId, Now.AddDays(1), true, 30) with { TimeZoneId = "Not/AZone" }));
        Assert.Empty(f.Db.NotificationDeliveries);
    }

    [Fact]
    public async Task UnrelatedScheduleEditDoesNotChangeReminderGeneration()
    {
        await using var f = await Fixture.CreateAsync();
        var at = Now.AddHours(3);
        var created = await f.Service.CreateScheduleAsync(f.ReaderId, ValidScheduleCreate(f.CompanyAId, at, true, 30));
        var revision = (await f.Db.CandidateInterviewSchedules.SingleAsync()).ReminderRevision;
        await f.Service.UpdateScheduleAsync(f.ReaderId, created.Id,
            ValidScheduleUpdate(at, InterviewScheduleStatus.Scheduled, true, 30) with { RoleTitle = "Changed role", PreparationStatus = InterviewPreparationStatus.Ready });
        Assert.Equal(revision, (await f.Db.CandidateInterviewSchedules.SingleAsync()).ReminderRevision);
        Assert.Equal(2, await f.Db.NotificationDeliveries.CountAsync());
    }

    [Fact]
    public async Task ReminderWhoseDueTimeAlreadyPassedIsRejected()
    {
        await using var f = await Fixture.CreateAsync();

        // Current test time = 12:00 UTC.
        // Interview = 12:20 UTC with 30-minute reminder.
        // Reminder would have been due at 11:50 UTC.
        var interviewAt =
            Now.AddMinutes(20);

        var exception =
            await Assert.ThrowsAsync<BadRequestException>(
                () => f.Service.CreateScheduleAsync(
                    f.ReaderId,
                    ValidScheduleCreate(
                        f.CompanyAId,
                        interviewAt,
                        reminderRequested: true,
                        reminderOffsetMinutes: 30)));

        Assert.Equal(
            "interview_reminder_time_passed",
            exception.Code);

        Assert.Empty(
            f.Db.NotificationDeliveries);

        Assert.Empty(
            f.Db.CandidateInterviewSchedules);
    }

    [Fact]
    public async Task ExplicitUtcTimestampIsRequiredForInterviewSchedule()
    {
        await using var f = await Fixture.CreateAsync();

        var unspecified =
            DateTime.SpecifyKind(
                Now.AddDays(2),
                DateTimeKind.Unspecified);

        await Assert.ThrowsAsync<ValidationException>(
            () => f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    unspecified,
                    reminderRequested: false,
                    reminderOffsetMinutes: 30)));

        Assert.Empty(
            f.Db.CandidateInterviewSchedules);

        Assert.Empty(
            f.Db.NotificationDeliveries);
    }

    [Fact]
    public async Task SupportedReminderOffsetsArePersistedAndScheduledCorrectly()
    {
        await using var f = await Fixture.CreateAsync();

        var offsets =
            new[] { 15, 30, 60, 1440 };

        foreach (var offset in offsets)
        {
            var interviewAt =
                Now.AddDays(5);

            var schedule =
                await f.Service.CreateScheduleAsync(
                    f.ReaderId,
                    ValidScheduleCreate(
                        f.CompanyAId,
                        interviewAt,
                        reminderRequested: true,
                        reminderOffsetMinutes: offset));

            Assert.Equal(
                offset,
                schedule.ReminderOffsetMinutes);

            var deliveries =
                await f.Db.NotificationDeliveries
                    .Where(x =>
                        x.Source ==
                            NotificationSource.InterviewReminder &&
                        x.SourceId ==
                            schedule.Id)
                    .ToListAsync();

            Assert.Equal(
                2,
                deliveries.Count);

            Assert.All(
                deliveries,
                x => Assert.Equal(
                    interviewAt.AddMinutes(-offset),
                    x.ScheduledForUtc));
        }
    }

    [Fact]
    public async Task InvalidReminderOffsetIsRejected()
    {
        await using var f = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => f.Service.CreateScheduleAsync(
                f.ReaderId,
                ValidScheduleCreate(
                    f.CompanyAId,
                    Now.AddDays(2),
                    reminderRequested: true,
                    reminderOffsetMinutes: 45)));

        Assert.Empty(
            f.Db.CandidateInterviewSchedules);

        Assert.Empty(
            f.Db.NotificationDeliveries);
    }

    [Fact]
    public async Task InvalidExploreAndAutocompleteValuesHaveSpecificErrors()
    {
        await using var f = await Fixture.CreateAsync();

        var sort =
            await Assert.ThrowsAsync<BadRequestException>(
                () => f.Service.SearchAsync(
                    f.ReaderId,
                    new(Sort: "popular")));

        Assert.Equal(
            "invalid_sort",
            sort.Code);

        var month =
            await Assert.ThrowsAsync<BadRequestException>(
                () => f.Service.SearchAsync(
                    f.ReaderId,
                    new(
                        FromMonth: 13,
                        FromYear: 2026)));

        Assert.Equal(
            "invalid_from_month",
            month.Code);

        var query =
            await Assert.ThrowsAsync<BadRequestException>(
                () => f.Service.SearchCompaniesAsync(
                    f.ReaderId,
                    "x",
                    10));

        Assert.Equal(
            "invalid_query",
            query.Code);
    }

    private static InterviewInsight Insight(
        Guid author,
        Guid company,
        InterviewInsightStatus status,
        bool deleted = false) =>
        new()
        {
            AuthorCandidateId = author,
            CompanyId = company,
            RoleTitle = "Engineer",
            InterviewDateMonth =
                new DateOnly(2026, 7, 1),
            OverallDifficulty =
                InterviewDifficulty.Moderate,
            ProcessSummary =
                "A concise process summary.",
            PreparationTips =
                "Prepare core concepts.",
            Status = status,
            IsDeleted = deleted
        };

    private static CreateInterviewInsightRequest ValidCreate(
        Guid companyId) =>
        new(
            companyId,
            null,
            "Software Engineer",
            "2-4 years",
            new DateOnly(2026, 7, 1),
            InterviewDifficulty.Moderate,
            "The process included technical and managerial discussions.",
            "Review core concepts and explain your reasoning clearly.",
            InterviewOutcome.PreferNotToSay,
            true,
            true,
            [
                new(
                    InterviewRoundType.Technical,
                    "Technical discussion",
                    45,
                    "Paraphrased data structures and API design topics.",
                    "Think aloud and clarify assumptions.")
            ]);

    private static UpdateInterviewInsightRequest ValidUpdate() =>
        new(
            "Software Engineer",
            null,
            new DateOnly(2026, 7, 1),
            InterviewDifficulty.Moderate,
            "Updated process summary.",
            "Updated preparation advice.",
            null,
            true,
            true,
            [
                new(
                    InterviewRoundType.Technical,
                    null,
                    30,
                    "Paraphrased technical topics.",
                    null)
            ]);

    private static CreateInsightFeedbackRequest ValidFeedback(
        Guid scheduleId) =>
        new(
            scheduleId,
            InsightHelpfulness.Helped,
            InterviewMatch.Matched,
            "The topics were useful.");

    private static CreateInterviewScheduleRequest ValidScheduleCreate(
        Guid companyId,
        DateTime interviewAtUtc,
        bool reminderRequested,
        int reminderOffsetMinutes) =>
        new(
            companyId,
            null,
            "Engineer",
            interviewAtUtc,
            InterviewFormat.Online,
            InterviewTimeOfDay.Morning,
            [
                InterviewRoundType.Technical,
                InterviewRoundType.HR
            ],
            InterviewPreparationStatus.Preparing,
            reminderRequested,
            reminderOffsetMinutes,
            "UTC");

    private static UpdateInterviewScheduleRequest ValidScheduleUpdate(
        DateTime interviewAtUtc,
        InterviewScheduleStatus status,
        bool reminderRequested,
        int reminderOffsetMinutes) =>
        new(
            "Engineer",
            interviewAtUtc,
            status,
            InterviewFormat.Online,
            InterviewTimeOfDay.Morning,
            [
                InterviewRoundType.Technical,
                InterviewRoundType.HR
            ],
            InterviewPreparationStatus.Preparing,
            reminderRequested,
            reminderOffsetMinutes,
            "UTC");

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(Now);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public JobPortalDbContext Db { get; }

        public InterviewInsightService Service { get; }

        public AdminInterviewInsightService Admin { get; }

        public Guid AuthorId { get; } =
            Guid.NewGuid();

        public Guid ReaderId { get; } =
            Guid.NewGuid();

        public Guid CompanyAId { get; } =
            Guid.NewGuid();

        public Guid CompanyBId { get; } =
            Guid.NewGuid();

        private readonly InterviewInsightRepository repository;

        private Fixture(
            JobPortalDbContext db)
        {
            Db = db;

            repository =
                new InterviewInsightRepository(db);

            var time =
                new FixedTimeProvider();

            var outboxRepository =
                new NotificationOutboxRepository(db);

            var notificationOutbox =
                new NotificationOutbox(
                    outboxRepository,
                    time);

            Service =
                new InterviewInsightService(
                    repository,
                    new AuditWriterTestDouble(),
                    new CreateInterviewInsightRequestValidator(),
                    new UpdateInterviewInsightRequestValidator(),
                    new CreateInterviewScheduleRequestValidator(),
                    new UpdateInterviewScheduleRequestValidator(),
                    new CreateInsightFeedbackRequestValidator(),
                    new CreateInsightReportRequestValidator(),
                    notificationOutbox,
                    time);

            Admin =
                new AdminInterviewInsightService(
                    repository,
                    new AuditWriterTestDouble(),
                    time);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db =
                new JobPortalDbContext(
                    new DbContextOptionsBuilder<JobPortalDbContext>()
                        .UseInMemoryDatabase(
                            Guid.NewGuid().ToString())
                        .Options);

            var f =
                new Fixture(db);

            var role =
                new Role
                {
                    Id = SystemRoleIds.Candidate,
                    Name = "Candidate",
                    NormalizedName = "CANDIDATE"
                };

            var owner =
                User(
                    Guid.NewGuid(),
                    "owner@example.com",
                    role);

            db.AddRange(
                role,
                owner,
                User(
                    f.AuthorId,
                    "author@example.com",
                    role),
                User(
                    f.ReaderId,
                    "reader@example.com",
                    role),
                new Company
                {
                    Id = f.CompanyAId,
                    Name = "Infosys",
                    Slug = "infosys",
                    OwnerUserId = owner.Id,
                    OwnerUser = owner
                },
                new Company
                {
                    Id = f.CompanyBId,
                    Name = "Other",
                    Slug = "other",
                    OwnerUserId = owner.Id,
                    OwnerUser = owner
                });

            await db.SaveChangesAsync();

            return f;
        }

        private static User User(
            Guid id,
            string email,
            Role role) =>
            new()
            {
                Id = id,
                Email = email,
                NormalizedEmail = email,
                FirstName = "Candidate",
                LastName = "User",
                Status = UserStatus.Active,
                RoleId = role.Id,
                Role = role
            };

        public async Task<InterviewInsight>
            AddPublishedInsightAsync(
                Guid author,
                Guid company,
                bool anonymous = false)
        {
            var insight =
                new InterviewInsight
                {
                    AuthorCandidateId = author,
                    CompanyId = company,
                    RoleTitle = "Engineer",
                    InterviewDateMonth =
                        new DateOnly(2026, 7, 1),
                    OverallDifficulty =
                        InterviewDifficulty.Moderate,
                    ProcessSummary =
                        "A concise process summary.",
                    PreparationTips =
                        "Prepare core concepts.",
                    IsAnonymous = anonymous,
                    Status =
                        InterviewInsightStatus.Published,
                    PublishedAtUtc =
                        Now.AddDays(-1),
                    Rounds =
                    [
                        new InterviewRound
                        {
                            Sequence = 1,
                            RoundType =
                                InterviewRoundType.Technical,
                            QuestionsOrTopics =
                                "Paraphrased API design topics."
                        }
                    ]
                };

            Db.InterviewInsights.Add(insight);

            await Db.SaveChangesAsync();

            return insight;
        }

        public Task<CandidateInterviewSchedule>
            AddPastScheduleAsync(
                Guid candidate,
                Guid company) =>
            AddScheduleAsync(
                candidate,
                company,
                Now.AddDays(-1));

        public async Task<CandidateInterviewSchedule>
            AddScheduleAsync(
                Guid candidate,
                Guid company,
                DateTime at)
        {
            var schedule =
                new CandidateInterviewSchedule
                {
                    CandidateId = candidate,
                    CompanyId = company,
                    InterviewAtUtc = at,
                    ConfirmFeedbackAvailableAtUtc = at,
                    Status =
                        InterviewScheduleStatus.Scheduled
                };

            Db.CandidateInterviewSchedules.Add(
                schedule);

            await Db.SaveChangesAsync();

            return schedule;
        }

        public ValueTask DisposeAsync() =>
            Db.DisposeAsync();
    }
}
