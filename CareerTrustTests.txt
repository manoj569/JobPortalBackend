using System.Reflection;
using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerTrustTests
{
    [Fact]
    public async Task CompletedPaidReviewIsPendingAndIdenticalReplayIsSafe()
    {
        using var f = new Fixture(); var s = await f.Completed();
        var r = await f.Review(s.BookingId); Assert.Equal(CareerReviewStatus.Pending, r.ModerationStatus); Assert.False(r.IsPublished);
        Assert.Equal(r.Id, (await f.Review(s.BookingId)).Id);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.SubmitReviewAsync(f.Candidate, s.BookingId, new(2, null, "Changed"), default));
        Assert.Empty((await f.Service.PublicReviewsAsync(s.ConsultantId, new(), default)).Items);
        Assert.Single(await f.Db.Set<CareerGuidanceReview>().ToArrayAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RatingBoundsAreEnforced(int rating)
    {
        using var f = new Fixture(); var s = await f.Completed();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.SubmitReviewAsync(f.Candidate, s.BookingId, new(rating, null, null), default));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("text\0control")]
    public async Task HtmlAndControlCharactersAreRejected(string text)
    {
        using var f = new Fixture(); var s = await f.Completed();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.SubmitReviewAsync(f.Candidate, s.BookingId, new(5, null, text), default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.OpenDisputeAsync(f.Candidate, s.BookingId, new(CareerDisputeCategory.Other, text), default));
    }

    [Fact]
    public async Task UnpaidUnstartedAndWrongOwnerCannotReview()
    {
        using var f = new Fixture(); var s = await f.Session.Setup(true);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(s.BookingId));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SubmitReviewAsync(f.Other, s.BookingId, new(5, null, null), default));
        f.Clock.Utc = s.ScheduledStartUtc; await f.Session.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(s.BookingId));
        using var unpaid = new Fixture(); var booking = await unpaid.Session.Finance.Setup();
        await Assert.ThrowsAsync<NotFoundException>(() => unpaid.Review(booking.Id));
    }

    [Fact]
    public async Task ModerationEditWithdrawalAndAggregateStayConsistent()
    {
        using var f = new Fixture(); var s = await f.Completed(); var r = await f.Review(s.BookingId);
        r = await f.Approve(r); Assert.True(r.IsPublished);
        var summary = (await new CareerGuidanceRepository(f.Db).RatingsAsync([s.ConsultantId], default))[s.ConsultantId];
        Assert.Equal(5m, summary.AverageRating); Assert.Equal(1, summary.ReviewCount);
        var visible = Assert.Single((await f.Service.PublicReviewsAsync(s.ConsultantId, new(), default)).Items);
        Assert.Equal("Verified candidate", visible.CandidateDisplayName);
        Assert.DoesNotContain("CandidateUserId", JsonSerializer.Serialize(visible), StringComparison.Ordinal);
        r = await f.Service.EditReviewAsync(f.Candidate, s.BookingId, new(4, "Updated", "Changed after approval", r.Revision), default);
        Assert.Equal(CareerReviewStatus.Pending, r.ModerationStatus); Assert.False(r.IsPublished);
        Assert.Empty(await new CareerGuidanceRepository(f.Db).RatingsAsync([s.ConsultantId], default));
        r = await f.Approve(r);
        await f.Service.WithdrawReviewAsync(f.Candidate, s.BookingId, r.Revision, default);
        Assert.Empty((await f.Service.PublicReviewsAsync(s.ConsultantId, new(), default)).Items);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(s.BookingId));
    }

    [Theory]
    [InlineData(CareerReviewStatus.Hidden)]
    [InlineData(CareerReviewStatus.Rejected)]
    public async Task ModerationLockExcludesRatingsAndStopsCandidateEdits(CareerReviewStatus status)
    {
        using var f = new Fixture(); var s = await f.Completed(); var r = await f.Review(s.BookingId);
        r = await f.Service.ModerateAsync(f.Admin, r.Id, new(r.Revision, status, "Private moderation reason"), default);
        Assert.Null((await f.Service.GetReviewAsync(f.Candidate, s.BookingId, false, default)).ModerationReason);
        Assert.Empty(await new CareerGuidanceRepository(f.Db).RatingsAsync([s.ConsultantId], default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.EditReviewAsync(f.Candidate, s.BookingId, new(3, null, null, r.Revision), default));
    }

    [Fact]
    public async Task ReviewEditWindowAndStaleRevisionAreEnforced()
    {
        using var f = new Fixture(); var s = await f.Completed(); var r = await f.Review(s.BookingId);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.EditReviewAsync(f.Candidate, s.BookingId, new(4, null, null, Guid.NewGuid()), default));
        f.Clock.Utc = r.CreatedAtUtc.AddDays(7).AddTicks(1);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.EditReviewAsync(f.Candidate, s.BookingId, new(4, null, null, r.Revision), default));
    }

    [Fact]
    public async Task FullRefundHidesPublishedReviewAndCannotBeRepublished()
    {
        using var f = new Fixture(); var s = await f.Completed(); var r = await f.Approve(await f.Review(s.BookingId));
        var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        await f.Session.Finance.Service.RefundAsync(f.Admin, p.Id, new(CareerRefundReason.AdminCorrection), default);
        Assert.False((await f.Db.Set<CareerGuidanceReview>().SingleAsync()).IsPublished);
        Assert.Empty(await new CareerGuidanceRepository(f.Db).RatingsAsync([s.ConsultantId], default));
        var hidden = await f.Service.GetReviewAsync(f.Admin, r.Id, true, default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ModerateAsync(f.Admin, r.Id, new(hidden.Revision, CareerReviewStatus.Approved, "Retry"), default));
        Assert.Equal(CareerEarningStatus.Reversed, p.Earning!.Status); Assert.Null(p.Earning.AvailableAtUtc);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task DisputeWindowHasExplicitBoundary(int ticks, bool permitted)
    {
        using var f = new Fixture(); var s = await f.Completed(); f.Clock.Utc = s.ScheduledEndUtc.AddHours(72).AddTicks(ticks);
        if (permitted) Assert.Equal(CareerDisputeStatus.Open, (await f.Open(s.BookingId)).Status);
        else await Assert.ThrowsAsync<ConflictException>(() => f.Open(s.BookingId));
    }

    [Fact]
    public async Task DisputeHoldsEvenPastAvailabilityWithoutMovingMoneyAndResolvesWithNewDelay()
    {
        using var f = new Fixture(); var s = await f.Completed(); var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        f.Clock.Utc = s.ScheduledEndUtc.AddHours(49); Assert.True(p.Earning!.AvailableAtUtc < f.Clock.Utc);
        var d = await f.Open(s.BookingId); Assert.Null(p.Earning.AvailableAtUtc);
        Assert.Equal(CareerPaymentStatus.Captured, p.Status); Assert.Equal(CareerEarningStatus.Pending, p.Earning.Status); Assert.Null(p.Refund);
        Assert.Equal(d.Id, (await f.Open(s.BookingId)).Id);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.OpenDisputeAsync(f.Candidate, s.BookingId, new(CareerDisputeCategory.Other, "Different"), default));
        d = await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "Reviewed evidence"), default);
        Assert.Equal(f.Clock.Utc.AddHours(48), p.Earning.AvailableAtUtc);
        f.Clock.Utc = f.Clock.Utc.AddHours(1);
        await f.Service.ResolveAsync(f.Admin, d.Id, new(Guid.Empty, CareerDisputeResolution.NoAction, "Reviewed evidence"), default);
        Assert.Equal(d.ResolvedAtUtc!.Value.AddHours(48), p.Earning.AvailableAtUtc);
    }

    [Fact]
    public async Task CompletionDuringActiveDisputeCannotRestoreAvailability()
    {
        using var f = new Fixture(); var s = await f.Session.Setup(true); f.Clock.Utc = s.ScheduledStartUtc;
        s = await f.Session.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default);
        var d = await f.Open(s.BookingId);
        s = await f.Session.Service.GetAsync(f.Owner, s.Id, CareerSessionAudience.Consultant, false, default);
        f.Clock.Utc = s.ScheduledEndUtc;
        await f.Session.Service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default);
        Assert.Null((await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
        await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.RefundDenied, "No refund warranted"), default);
        Assert.Equal(f.Clock.Utc.AddHours(48), (await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
    }

    [Fact]
    public async Task RefundApprovalIsRecommendationAndUsesExistingRefundWorkflow()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.RefundApproved, "Approved after evidence review"), default);
        var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        Assert.True(p.RequiresRefundReview); Assert.Null(p.Refund); Assert.Equal(0, f.Session.Finance.Gateway.RefundCalls);
        Assert.Null(p.Earning!.AvailableAtUtc);
        await f.Session.Finance.Service.RefundAsync(f.Admin, p.Id, new(CareerRefundReason.AdminCorrection), default);
        Assert.Equal(1, f.Session.Finance.Gateway.RefundCalls); Assert.Equal(CareerEarningStatus.Reversed, p.Earning.Status);
    }

    [Fact]
    public async Task RefundedDisputeResolutionNeverResurrectsEarning()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        await f.Session.Finance.Service.RefundAsync(f.Admin, p.Id, new(CareerRefundReason.AdminCorrection), default);
        await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "Refund already processed"), default);
        Assert.Equal(CareerEarningStatus.Reversed, p.Earning!.Status); Assert.Null(p.Earning.AvailableAtUtc);
    }

    [Fact]
    public async Task EvidencePrivacyOwnershipAndReplayAreEnforced()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        var request = new CareerEvidenceRequest(d.Revision, Guid.NewGuid(), CareerEvidenceType.AdminNote, "Private admin assessment");
        d = await f.Service.EvidenceAsync(f.Admin, d.Id, CareerSessionAudience.Administrator, request, default);
        Assert.Single(d.Evidence);
        Assert.Single((await f.Service.EvidenceAsync(f.Admin, d.Id, CareerSessionAudience.Administrator, request, default)).Evidence);
        var owner = await f.Service.GetDisputeAsync(f.Owner, d.Id, CareerSessionAudience.Consultant, false, default);
        Assert.Empty(owner.Evidence); Assert.Null(owner.AdminNotes);
        Assert.Empty((await f.Service.GetDisputeAsync(f.Candidate, d.Id, CareerSessionAudience.Candidate, false, default)).Evidence);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetDisputeAsync(f.Other, d.Id, CareerSessionAudience.Candidate, false, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.EvidenceAsync(f.Other, d.Id, CareerSessionAudience.Consultant, request, default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.EvidenceAsync(f.Candidate, d.Id, CareerSessionAudience.Candidate, request, default));
        d = await f.Service.EvidenceAsync(f.Owner, d.Id, CareerSessionAudience.Consultant, new(d.Revision, Guid.NewGuid(), CareerEvidenceType.Response, "Consultant response"), default);
        Assert.Single(d.Evidence); Assert.Equal("Consultant response", d.Evidence.Single().Description);
        Assert.Single((await f.Service.DisputesAsync(f.Owner, CareerSessionAudience.Consultant, new(), default)).Items);
        Assert.Empty((await f.Service.DisputesAsync(f.Other, CareerSessionAudience.Consultant, new(), default)).Items);
    }

    [Fact]
    public async Task AdminTransitionsRejectStaleOrTerminalChangesAndKeepNotesPrivate()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.StatusAsync(f.Admin, d.Id, new(Guid.NewGuid(), CareerDisputeStatus.UnderReview, "Private"), default));
        d = await f.Service.StatusAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeStatus.AwaitingConsultant, "Private internal note"), default);
        Assert.Null((await f.Service.GetDisputeAsync(f.Candidate, d.Id, CareerSessionAudience.Candidate, false, default)).AdminNotes);
        d = await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.WarningIssued, "Warning recorded"), default);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.StatusAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeStatus.UnderReview, "Reopen"), default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.RefundApproved, "Change"), default));
    }

    [Fact]
    public async Task NoShowConfirmationAndNoRefundResolutionDoNotReleaseEarnings()
    {
        using var f = new Fixture(); var s = await f.Session.Setup(); f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(15);
        s = await f.Session.Service.ReportAsync(f.Candidate, s.BookingId, new(s.Revision), default);
        var d = await f.Service.OpenDisputeAsync(f.Candidate, s.BookingId, new(CareerDisputeCategory.ConsultantNoShow, "Did not attend"), default);
        await f.Session.Service.NoShowAsync(f.Admin, s.Id, new(s.Revision), true, default);
        await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "Retain objective no-show review"), default);
        var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync(); Assert.True(p.RequiresRefundReview); Assert.Null(p.Earning!.AvailableAtUtc);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(s.BookingId));
    }

    [Fact]
    public async Task BillingDisputeCanExistBeforeProvisioningWithoutTriggeringRefund()
    {
        using var f = new Fixture(); var b = await f.Session.Finance.Setup(); var order = await f.Session.Finance.Order(b.Id); await f.Session.Finance.Verify(b.Id, order);
        var d = await f.Service.OpenDisputeAsync(f.Candidate, b.Id, new(CareerDisputeCategory.BillingIssue, "Question about charge", true), default);
        Assert.Null(d.SessionId); Assert.Null((await f.Db.Set<CareerGuidancePayment>().SingleAsync()).Refund);
    }

    [Fact]
    public async Task TrustRecordsCannotLoseIdentityOrEvidenceHistory()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        await f.Service.EvidenceAsync(f.Candidate, d.Id, CareerSessionAudience.Candidate, new(d.Revision, Guid.NewGuid(), CareerEvidenceType.Text, "Evidence"), default);
        (await f.Db.Set<CareerGuidanceDisputeEvidence>().SingleAsync()).Description = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleResolutionAfterRefundCannotRestoreRelease()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        using var stale = new JobPortalDbContext(f.Session.Finance.Scheduling.Options);
        var repo = new CareerTrustRepository(stale); await repo.DisputeAsync(d.Id, false, default);
        var service = f.CreateService(stale);
        var p = await f.Db.Set<CareerGuidancePayment>().SingleAsync();
        await f.Session.Finance.Service.RefundAsync(f.Admin, p.Id, new(CareerRefundReason.AdminCorrection), default);
        await Assert.ThrowsAsync<ConflictException>(() => service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "Stale decision"), default));
        using var read = new JobPortalDbContext(f.Session.Finance.Scheduling.Options);
        Assert.Null((await read.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
    }

    [Fact]
    public void PublicContractsAndAuthMetadataContainNoSensitiveFields()
    {
        Assert.Equal("Candidate", typeof(CareerCandidateTrustController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("Administrator", typeof(AdminCareerTrustController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(typeof(CareerConsultantTrustController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(typeof(CareerPublicReviewsController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(new[] { "CandidateDisplayName", "Comment", "CreatedAtUtc", "Id", "Rating", "Title" }, typeof(CareerPublicReview).GetProperties().Select(p => p.Name).Order().ToArray());
        Assert.False(new CareerTrustOptions { DisputeOpenWindowHours = 0 }.IsValid());
    }

    [Fact]
    public async Task CompetingAdminResolutionsAndStalePublicationCannotBothCommit()
    {
        using var f = new Fixture(); var s = await f.Completed(); var r = await f.Review(s.BookingId); var d = await f.Open(s.BookingId);
        using var stale = new JobPortalDbContext(f.Session.Finance.Scheduling.Options);
        var repo = new CareerTrustRepository(stale);
        await repo.DisputeAsync(d.Id, false, default); await repo.ReviewAsync(r.Id, false, default);
        var service = f.CreateService(stale);
        await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "First decision"), default);
        await Assert.ThrowsAsync<ConflictException>(() => service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.RefundApproved, "Stale decision"), default));
        await repo.ReviewAsync(r.Id, false, default);
        await f.Service.WithdrawReviewAsync(f.Candidate, s.BookingId, r.Revision, default);
        await Assert.ThrowsAsync<ConflictException>(() => service.ModerateAsync(f.Admin, r.Id, new(r.Revision, CareerReviewStatus.Approved, "Stale approval"), default));
        Assert.Empty((await f.Service.PublicReviewsAsync(s.ConsultantId, new(), default)).Items);
    }

    [Fact]
    public async Task StaleResolutionAfterNoShowCannotRecreateEligibility()
    {
        using var f = new Fixture(); var s = await f.Session.Setup(); f.Clock.Utc = s.ScheduledStartUtc.AddMinutes(15);
        var d = await f.Open(s.BookingId);
        using var stale = new JobPortalDbContext(f.Session.Finance.Scheduling.Options);
        await new CareerTrustRepository(stale).DisputeAsync(d.Id, false, default);
        await f.Session.Service.NoShowAsync(f.Admin, s.Id, new(s.Revision), true, default);
        await Assert.ThrowsAsync<ConflictException>(() => f.CreateService(stale).ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "Stale resolution"), default));
        Assert.Null((await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
    }

    [Fact]
    public async Task StaleCompletionAfterOpeningDisputeCannotReleaseEarning()
    {
        using var f = new Fixture(); var s = await f.Session.Setup(true); f.Clock.Utc = s.ScheduledStartUtc;
        s = await f.Session.Service.StartAsync(f.Owner, s.Id, new(s.Revision), default);
        using var stale = new JobPortalDbContext(f.Session.Finance.Scheduling.Options);
        var repo = new CareerSessionRepository(stale);
        await repo.GetAsync(s.Id, default); await repo.PaymentAsync(s.BookingId, default);
        await f.Open(s.BookingId); f.Clock.Utc = s.ScheduledEndUtc;
        var service = new CareerSessionService(repo, new UserRepository(stale), new AuditWriterTestDouble(),
            new JobPortal.Infrastructure.CareerGuidance.ManualCareerMeetingProvider(), f.Session.Protector, f.Clock, Options.Create(new CareerSessionOptions()));
        await Assert.ThrowsAsync<ConflictException>(() => service.CompleteAsync(f.Owner, s.Id, new(s.Revision), default));
        Assert.Null((await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public async Task PaginationRejectsUnboundedOrOverflowingQueries(int page, int size)
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.PublicReviewsAsync(Guid.NewGuid(), new(PageNumber: page, PageSize: size), default));
    }

    [Fact]
    public async Task OwnershipAndTextLimitsApplyToDisputesAndEvidence()
    {
        using var f = new Fixture(); var s = await f.Completed();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.OpenDisputeAsync(f.Other, s.BookingId, new(CareerDisputeCategory.Other, "Not mine"), default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.OpenDisputeAsync(f.Candidate, s.BookingId, new(CareerDisputeCategory.Other, new string('x', 4001)), default));
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.SubmitReviewAsync(f.Candidate, s.BookingId, new(5, new string('x', 121), null), default));
        var d = await f.Open(s.BookingId);
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.EvidenceAsync(f.Candidate, d.Id, CareerSessionAudience.Candidate, new(d.Revision, Guid.NewGuid(), CareerEvidenceType.Text, new string('x', 4001)), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.EvidenceAsync(f.Other, d.Id, CareerSessionAudience.Candidate, new(d.Revision, Guid.NewGuid(), CareerEvidenceType.Text, "Other candidate"), default));
    }

    [Fact]
    public async Task InactiveCandidateOrRestrictedConsultantDoesNotRegainReleaseEligibility()
    {
        using var f = new Fixture(); var s = await f.Completed(); var d = await f.Open(s.BookingId);
        f.Session.Finance.Scheduling.Candidate.IsDeleted = true; await f.Db.SaveChangesAsync();
        await f.Service.ResolveAsync(f.Admin, d.Id, new(d.Revision, CareerDisputeResolution.NoAction, "Account inactive"), default);
        Assert.Null((await f.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
        using var second = new Fixture(); var next = await second.Completed(); var case2 = await second.Open(next.BookingId);
        await second.Service.ResolveAsync(second.Admin, case2.Id, new(case2.Revision, CareerDisputeResolution.ConsultantRestricted, "Explicit restriction"), default);
        Assert.Null((await second.Db.Set<CareerGuidanceEarning>().SingleAsync()).AvailableAtUtc);
        Assert.Equal(ConsultantVerificationStatus.Suspended, (await second.Db.Set<CareerConsultant>().SingleAsync()).VerificationStatus);
    }

    [Fact]
    public async Task DecimalRatingsMatchDiscoveryDetailAndPagedPublicReviews()
    {
        using var f = new Fixture(); var s = await f.Completed(); await f.Approve(await f.Review(s.BookingId));
        var profile = f.Session.Finance.Scheduling.Profile;
        for (var i = 0; i < 2; i++)
        {
            // Additional completed paid graphs exercise decimal averaging independently of provider test IDs.
            var b = new CareerGuidanceBooking { CandidateUserId = f.Candidate, Candidate = f.Session.Finance.Scheduling.Candidate,
                ConsultantId = profile.Id, Consultant = profile, Status = CareerBookingStatus.Completed };
            var session = new CareerGuidanceSession { BookingId = b.Id, Booking = b, CandidateUserId = f.Candidate,
                ConsultantId = profile.Id, Status = CareerSessionStatus.Completed };
            var payment = new CareerGuidancePayment { BookingId = b.Id, Booking = b, CandidateUserId = f.Candidate,
                ConsultantId = profile.Id, Consultant = profile, Status = CareerPaymentStatus.Captured, PaidAtUtc = f.Clock.Utc };
            f.Db.Add(new CareerGuidanceReview { BookingId = b.Id, Booking = b, SessionId = session.Id, Session = session,
                PaymentId = payment.Id, Payment = payment, ConsultantId = profile.Id, CandidateUserId = f.Candidate,
                Rating = 4, ModerationStatus = CareerReviewStatus.Approved, IsPublished = true });
        }
        await f.Db.SaveChangesAsync();
        var service = new CareerGuidanceService(new CareerGuidanceRepository(f.Db), new UserRepository(f.Db), new CompanyManagementRepository(f.Db),
            new UnitOfWork(f.Db), new AuditWriterTestDouble(), f.Clock, new ConsultantProfileRequestValidator(), new ConsultantServiceRequestValidator(),
            new ConsultantSearchQueryValidator(), new ConsultantReviewRequestValidator(), new ConsultantAdminQueryValidator());
        var detail = await service.GetAsync(profile.Id, default);
        var discovery = Assert.Single((await service.SearchAsync(new(), default)).Items);
        Assert.Equal(4.33m, detail.AverageRating); Assert.Equal(3, detail.ReviewCount);
        Assert.Equal(detail.AverageRating, discovery.AverageRating); Assert.Equal(detail.ReviewCount, discovery.ReviewCount);
        var page1 = await f.Service.PublicReviewsAsync(profile.Id, new(PageSize: 2), default);
        var page2 = await f.Service.PublicReviewsAsync(profile.Id, new(PageNumber: 2, PageSize: 2), default);
        Assert.Equal(3, page1.TotalCount); Assert.Equal(2, page1.Items.Count); Assert.Single(page2.Items);
        Assert.Empty(page1.Items.Select(r => r.Id).Intersect(page2.Items.Select(r => r.Id)));
    }

    internal sealed class Fixture : IDisposable
    {
        public CareerSessionTests.Fixture Session { get; } = new();
        public JobPortalDbContext Db => Session.Db;
        public Guid Candidate => Session.Candidate;
        public Guid Owner => Session.Owner;
        public Guid Other => Session.Finance.Scheduling.Other.Id;
        public Guid Admin => Session.Finance.Admin.Id;
        public CareerSchedulingTests.TestClock Clock => Session.Clock;
        public CareerTrustService Service { get; }
        public Fixture() { Service = CreateService(Db); }
        public CareerTrustService CreateService(JobPortalDbContext db) => new(new CareerTrustRepository(db), new CareerGuidanceRepository(db),
            new UserRepository(db), new AuditWriterTestDouble(), Clock, Options.Create(new CareerTrustOptions()), Options.Create(new CareerSessionOptions()));
        public async Task<CareerSessionResponse> Completed()
        {
            var s = await Session.Setup(true); Clock.Utc = s.ScheduledStartUtc;
            s = await Session.Service.StartAsync(Owner, s.Id, new(s.Revision), default); Clock.Utc = s.ScheduledEndUtc;
            return await Session.Service.CompleteAsync(Owner, s.Id, new(s.Revision), default);
        }
        public Task<CareerReviewResponse> Review(Guid bookingId) => Service.SubmitReviewAsync(Candidate, bookingId, new(5, "Helpful", "Useful guidance"), default);
        public Task<CareerReviewResponse> Approve(CareerReviewResponse r) => Service.ModerateAsync(Admin, r.Id, new(r.Revision, CareerReviewStatus.Approved, "Approved"), default);
        public Task<CareerDisputeResponse> Open(Guid bookingId) => Service.OpenDisputeAsync(Candidate, bookingId, new(CareerDisputeCategory.SessionQuality, "Please review the session"), default);
        public void Dispose() => Session.Dispose();
    }
}
