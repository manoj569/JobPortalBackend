using System.Text.Json;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Referrals;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ReferralMarketplaceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("CareerHarborMembership")]
    public async Task MissingOrWrongPlanCannotRequest(string? plan)
    {
        await using var f = await ReferralFixture.CreateAsync(plan: plan);
        await Error("REFERRAL_ACCESS_REQUIRED", () => f.Request());
        Assert.Equal(0, await f.Db.ReferralRequests.CountAsync());
    }
    [Fact]
    public async Task RequestDoesNotConsumeAndDuplicateIsBlocked()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var row = await f.Request();
        Assert.Equal(ReferralRequestStatus.Requested, row.Status);
        Assert.Equal(f.Clock.Utc.AddHours(48), row.ExpiresAtUtc);
        Assert.Equal(0, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
        await Error("REFERRAL_ALREADY_REQUESTED", () => f.Request());
        Assert.Equal(2, await f.Db.NotificationDeliveries.CountAsync());
    }
    [Theory]
    [InlineData("pending")]
    [InlineData("hidden")]
    [InlineData("expired")]
    [InlineData("inactive-referrer")]
    public async Task UnavailableOpportunityCannotBeRequested(string reason)
    {
        await using var f = await ReferralFixture.CreateAsync();
        var opportunity = await f.Db.JobReferrals.Include(x => x.Job).Include(x => x.ReferrerUser).SingleAsync();
        if (reason == "pending") opportunity.ApprovalStatus = JobReferralApprovalStatus.Pending;
        if (reason == "hidden") opportunity.Job.IsHidden = true;
        if (reason == "expired") opportunity.Job.ExpiresAtUtc = f.Clock.Utc;
        if (reason == "inactive-referrer") opportunity.ReferrerUser.Status = UserStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Error("REFERRAL_CONTACT_NOT_AVAILABLE", () => f.Request());
    }
    [Fact]
    public async Task AcceptanceConsumesExactlyOnceAndNotificationsAreIdempotent()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var request = await f.Request();
        var accepted = await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
        Assert.Equal(ReferralRequestStatus.Accepted, accepted.Request.Status);
        await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
        Assert.Equal(1, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
        Assert.Equal(4, await f.Db.NotificationDeliveries.CountAsync());
        var row = await f.Db.ReferralRequests.AsNoTracking().SingleAsync();
        Assert.Equal(f.MembershipId, row.AcceptedMembershipId);
        Assert.Equal(f.Clock.Utc, row.AcceptedAtUtc);
    }
    [Fact]
    public async Task RejectConsumesZeroAndCannotBeAccepted()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var row = await f.Request();
        await f.Service.RejectAsync(f.ReferrerId, row.Id, new("Internal note"), default);
        await f.Service.RejectAsync(f.ReferrerId, row.Id, new("Retry"), default);
        Assert.Equal(0, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
        await Error("INVALID_REFERRAL_STATUS", () => f.Service.AcceptAsync(f.ReferrerId, row.Id, default));
        var candidate = await f.Service.CandidateDetailAsync(f.CandidateId, row.Id, default);
        Assert.DoesNotContain("Internal note", JsonSerializer.Serialize(candidate));
    }
    [Fact]
    public async Task ExpiryIsEnforcedWithoutWorkerInQueriesAndMutations()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var row = await f.Request(); f.Clock.Utc = f.Clock.Utc.AddHours(48);
        var detail = await f.Service.CandidateDetailAsync(f.CandidateId, row.Id, default);
        Assert.Equal(ReferralRequestStatus.Expired, detail.Status);
        Assert.Equal(row.ExpiresAtUtc, detail.ExpiredAtUtc);
        Assert.Empty((await f.Service.InboxAsync(f.ReferrerId, ReferralRequestStatus.Requested, 1, 20, default)).Items);
        Assert.Single((await f.Service.InboxAsync(f.ReferrerId, ReferralRequestStatus.Expired, 1, 20, default)).Items);
        await Error("REFERRAL_REQUEST_EXPIRED", () => f.Service.AcceptAsync(f.ReferrerId, row.Id, default));
        await Error("REFERRAL_REQUEST_EXPIRED", () => f.Service.RejectAsync(f.ReferrerId, row.Id, new(null), default));
        Assert.Equal(0, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
    }
    [Fact]
    public async Task CapacityBlocksFurtherRequestsAndAcceptances()
    {
        await using var f = await ReferralFixture.CreateAsync(slots: 1);
        var a = await f.Request();
        var other = await f.AddCandidate();
        var b = await f.Service.CreateAsync(other, f.ReferralId, new(null), default);
        await f.Service.AcceptAsync(f.ReferrerId, a.Id, default);
        await Error("REFERRAL_SLOTS_FULL", () => f.Service.AcceptAsync(f.ReferrerId, b.Id, default));
        var third = await f.AddCandidate();
        await Error("REFERRAL_SLOTS_FULL", () => f.Service.CreateAsync(third, f.ReferralId, new(null), default));
        var card = Assert.Single((await f.Public.GetApprovedPublicAsync(1, 20)).Items);
        Assert.Equal(1, card.AcceptedReferralCount); Assert.Equal(0, card.RemainingReferralSlots); Assert.False(card.ReferralAvailable);
    }
    [Fact]
    public async Task EleventhAcceptanceAndNewRequestsAtQuotaAreBlocked()
    {
        await using var f = await ReferralFixture.CreateAsync(slots: 20);
        var queued = await f.Request();
        for (var i = 0; i < 10; i++)
        {
            var opportunity = await f.AddOpportunity(20);
            var row = await f.Service.CreateAsync(f.CandidateId, opportunity, new(null), default);
            await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        }
        await Error("REFERRAL_CONNECTION_LIMIT_REACHED", () => f.Service.AcceptAsync(f.ReferrerId, queued.Id, default));
        var extra = await f.AddOpportunity(20);
        await Error("REFERRAL_CONNECTION_LIMIT_REACHED", () => f.Service.CreateAsync(f.CandidateId, extra, new(null), default));
        Assert.Equal(10, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("wrong-plan")]
    public async Task AcceptanceRechecksMembership(string condition)
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        var membership = await f.Db.Memberships.SingleAsync();
        if (condition == "expired") membership.EndsAtUtc = f.Clock.Utc;
        if (condition == "future") membership.StartsAtUtc = f.Clock.Utc.AddDays(1);
        if (condition == "wrong-plan") membership.PlanCode = "CareerHarborMembership";
        await f.Db.SaveChangesAsync();
        await Error("REFERRAL_ACCESS_REQUIRED", () => f.Service.AcceptAsync(f.ReferrerId, row.Id, default));
        Assert.Null((await f.Db.ReferralRequests.AsNoTracking().SingleAsync()).AcceptedAtUtc);
    }
    [Fact]
    public async Task PaymentAndRequestedCannotRevealContactIncludingLegacyRoute()
    {
        await using var f = await ReferralFixture.CreateAsync();
        Assert.Equal(ReferralUnlockStatus.RequestRequired, (await f.Public.UnlockContactAsync(f.CandidateId, f.JobId)).Status);
        var row = await f.Request();
        await Error("REFERRAL_CONTACT_NOT_AVAILABLE", () => f.Service.ContactAsync(f.CandidateId, row.Id, default));
        Assert.Equal(ReferralUnlockStatus.RequestRequired, (await f.Public.UnlockContactAsync(f.CandidateId, f.JobId)).Status);
        Assert.Equal(0, await f.Db.ReferralUnlocks.CountAsync());
    }
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task SuccessfulStatesRevealOnlyOptedInMethodsAndNeverRealName(int state)
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        if (state >= 5) await f.Service.SubmitAsync(f.ReferrerId, row.Id, new("employer-ref-1"), default);
        if (state == 6) await f.Service.ConfirmAsync(f.CandidateId, row.Id, default);
        var contact = await f.Service.ContactAsync(f.CandidateId, row.Id, default);
        Assert.Equal("Employee Referrer", contact.ReferrerLabel); Assert.Equal("employee@example.test", contact.Email);
        Assert.Null(contact.PhoneNumber); Assert.Null(contact.LinkedInUrl);
        var legacy = await f.Public.UnlockContactAsync(f.CandidateId, f.JobId);
        Assert.Equal(ReferralUnlockStatus.Granted, legacy.Status); Assert.Equal(contact, legacy.Contact);
        Assert.DoesNotContain("Private Referrer", JsonSerializer.Serialize(contact));
    }
    [Fact]
    public async Task ContactStillRequiresActiveMembershipAndActiveOpportunity()
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        var membership = await f.Db.Memberships.SingleAsync(); membership.EndsAtUtc = f.Clock.Utc; await f.Db.SaveChangesAsync();
        await Error("REFERRAL_ACCESS_REQUIRED", () => f.Service.ContactAsync(f.CandidateId, row.Id, default));
        membership = await f.Db.Memberships.SingleAsync(); membership.EndsAtUtc = f.Clock.Utc.AddDays(30); await f.Db.SaveChangesAsync();
        var opportunity = await f.Db.JobReferrals.SingleAsync(); opportunity.ApprovalStatus = JobReferralApprovalStatus.Rejected; await f.Db.SaveChangesAsync();
        await Error("REFERRAL_CONTACT_NOT_AVAILABLE", () => f.Service.ContactAsync(f.CandidateId, row.Id, default));
    }
    [Fact]
    public async Task OtherCandidateAndReferrerCannotReadOrMutatePrivateRequest()
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request(); var other = await f.AddCandidate();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ContactAsync(other, row.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.CandidateDetailAsync(other, row.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ConfirmAsync(other, row.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.AcceptAsync(other, row.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.RejectAsync(other, row.Id, new(null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SubmitAsync(other, row.Id, new(null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ReferrerDetailAsync(other, row.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ResumeAsync(other, row.Id, default));
        Assert.Empty((await f.Service.InboxAsync(other, null, 1, 20, default)).Items);
        Assert.Equal(0, f.Storage.Reads);
    }
    [Fact]
    public async Task SubmissionConfirmationAndInvestigationAreIdempotentAndMetricsUseRecords()
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        await Error("INVALID_REFERRAL_STATUS", () => f.Service.SubmitAsync(f.ReferrerId, row.Id, new(null), default));
        await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        await Error("INVALID_REFERRAL_STATUS", () => f.Service.ConfirmAsync(f.CandidateId, row.Id, default));
        await f.Service.SubmitAsync(f.ReferrerId, row.Id, new("application-id"), default);
        await f.Service.SubmitAsync(f.ReferrerId, row.Id, new("retry"), default);
        var issue = await f.Service.NotReceivedAsync(f.CandidateId, row.Id, default);
        await f.Service.NotReceivedAsync(f.CandidateId, row.Id, default);
        Assert.Equal(ReferralRequestStatus.ReferralSubmitted, issue.Status); Assert.NotNull(issue.NotReceivedAtUtc);
        Assert.Single((await f.Service.AdminListAsync(true, null, 1, 20, default)).Items);
        await f.Service.ConfirmAsync(f.CandidateId, row.Id, default); await f.Service.ConfirmAsync(f.CandidateId, row.Id, default);
        var metrics = await f.Service.MetricsAsync(f.ReferrerId, default);
        Assert.Equal(new ReferralMetricsResponse(1, 1, 1, 1, 1, 1m), metrics);
        await Error("INVALID_REFERRAL_STATUS", () => f.Service.RejectAsync(f.ReferrerId, row.Id, new(null), default));
        Assert.Equal(10, await f.Db.NotificationDeliveries.CountAsync());
        Assert.Equal("application-id", (await f.Service.ReferrerDetailAsync(f.ReferrerId, row.Id, default)).ReferralSubmissionReference);
    }
    [Fact]
    public async Task PublicSerializedApiEnvelopeContainsNoReferrerIdentityFieldsOrValues()
    {
        await using var f = await ReferralFixture.CreateAsync();
        var page = await f.Public.GetApprovedPublicAsync(1, 20, f.CandidateId);
        var envelope = new JobPortal.Shared.Models.ApiResponse<JobPortal.Shared.Models.PagedResponse<PublicReferralJobResponse>>(page);
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var forbidden in new[] { "referrerName", "referrerUserId", "email", "phone", "linkedIn", "profileImage", "Private Referrer", "employee@example.test", "+919999999999", "linkedin.com/in/private", f.ReferrerId.ToString() })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Employee Referrer", json); Assert.Contains("remainingReferralSlots", json);
    }
    [Fact]
    public async Task ResumeUsesExistingStorageOnlyAfterOwnerAuthorization()
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        var download = await f.Service.ResumeAsync(f.ReferrerId, row.Id, default);
        await using var stream = download.Content;
        Assert.Equal(f.CandidateId, f.Storage.LastOwner); Assert.Equal(1, f.Storage.Reads);
        await f.Service.RejectAsync(f.ReferrerId, row.Id, new(null), default);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.ResumeAsync(f.ReferrerId, row.Id, default));
        Assert.Equal(1, f.Storage.Reads);
    }
    [Fact]
    public async Task RenewedTermUsesNewWindowWithoutRewritingOldAcceptance()
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        var membership = await f.Db.Memberships.SingleAsync(); membership.EndsAtUtc = membership.StartsAtUtc.AddDays(60); await f.Db.SaveChangesAsync();
        var old = f.Clock.Utc; f.Clock.Utc = old.AddDays(30);
        Assert.Equal(0, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
        var nextJob = await f.AddOpportunity(1);
        var next = await f.Service.CreateAsync(f.CandidateId, nextJob, new(null), default);
        await f.Service.AcceptAsync(f.ReferrerId, next.Id, default);
        Assert.Equal(1, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
        Assert.Equal(old, (await f.Db.ReferralRequests.AsNoTracking().SingleAsync(x => x.Id == row.Id)).QuotaPeriodStartUtc);
    }
    [Fact]
    public async Task MetricsHandleNoSubmissionsAndSelfRequestIsRejected()
    {
        await using var f = await ReferralFixture.CreateAsync();
        Assert.Equal(0m, (await f.Service.MetricsAsync(f.ReferrerId, default)).CompletionRate);
        await Error("INVALID_REFERRAL_REQUEST", () => f.Service.CreateAsync(f.ReferrerId, f.ReferralId, new(null), default));
    }
    [Fact]
    public async Task OwnerSubmissionResponseUsesServerDerivedSlotsAndAcceptanceCount()
    {
        await using var f = await ReferralFixture.CreateAsync(slots: 5);
        var row = await f.Request(); await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        var own = Assert.Single((await f.Public.GetMySubmissionsAsync(f.ReferrerId, null, null, 1, 20)).Items);
        Assert.Equal(5, own.ReferralSlots); Assert.Equal(1, own.AcceptedReferralCount); Assert.Equal(4, own.RemainingReferralSlots);
    }
    [Fact]
    public async Task HistoricalSoftDeletionCannotRestoreCapacityQuotaOrPermitAnotherRequest()
    {
        await using var f = await ReferralFixture.CreateAsync(slots: 1);
        var response = await f.Request(); await f.Service.AcceptAsync(f.ReferrerId, response.Id, default);
        var row = await f.Db.ReferralRequests.SingleAsync(); row.IsDeleted = true; await f.Db.SaveChangesAsync();
        Assert.Equal(1, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
        await Error("REFERRAL_ALREADY_REQUESTED", () => f.Request());
        Assert.Equal(1, Assert.Single((await f.Public.GetApprovedPublicAsync(1, 20)).Items).AcceptedReferralCount);
    }
    [Fact]
    public async Task ChangedOpportunityOwnershipCannotAuthorizeOldOrNewReferrerOrContact()
    {
        await using var f = await ReferralFixture.CreateAsync(); var row = await f.Request();
        await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
        var other = await f.AddCandidate();
        var opportunity = await f.Db.JobReferrals.SingleAsync(); opportunity.ReferrerUserId = other; await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SubmitAsync(f.ReferrerId, row.Id, new(null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SubmitAsync(other, row.Id, new(null), default));
        await Error("REFERRAL_CONTACT_NOT_AVAILABLE", () => f.Service.ContactAsync(f.CandidateId, row.Id, default));
    }
    [Theory]
    [InlineData(NotificationSource.ReferralRequested)]
    [InlineData(NotificationSource.ReferralAccepted)]
    [InlineData(NotificationSource.ReferralRequestRejected)]
    [InlineData(NotificationSource.ReferralSubmitted)]
    [InlineData(NotificationSource.ReferralConfirmed)]
    [InlineData(NotificationSource.ReferralNotReceived)]
    public async Task CommittedLifecycleNotificationEligibilityChecksActualSourceAndRecipient(NotificationSource source)
    {
        await using var f = await ReferralFixture.CreateAsync(); var request = await f.Request();
        if (source == NotificationSource.ReferralRequestRejected)
            await f.Service.RejectAsync(f.ReferrerId, request.Id, new(null), default);
        else if (source != NotificationSource.ReferralRequested)
            await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
        if (source is NotificationSource.ReferralSubmitted or NotificationSource.ReferralConfirmed or NotificationSource.ReferralNotReceived)
            await f.Service.SubmitAsync(f.ReferrerId, request.Id, new(null), default);
        if (source == NotificationSource.ReferralConfirmed) await f.Service.ConfirmAsync(f.CandidateId, request.Id, default);
        if (source == NotificationSource.ReferralNotReceived) await f.Service.NotReceivedAsync(f.CandidateId, request.Id, default);
        var delivery = await f.Db.NotificationDeliveries.SingleAsync(x => x.Source == source && x.Channel == NotificationChannel.InApp);
        delivery.Status = NotificationDeliveryStatus.Processing; delivery.LeaseOwner = Guid.NewGuid(); delivery.LeaseExpiresAtUtc = f.Clock.Utc.AddMinutes(3);
        await f.Db.SaveChangesAsync();
        var repository = new NotificationDeliveryRepository(f.Db);
        Assert.True(await repository.IsEligibleAsync(delivery, f.Clock.Utc, default));
        delivery.UserId = await f.AddCandidate(); await f.Db.SaveChangesAsync();
        Assert.False(await repository.IsEligibleAsync(delivery, f.Clock.Utc, default));
    }
    internal static async Task Error(string code, Func<Task> call)
    { var ex = await Assert.ThrowsAnyAsync<AppException>(call); Assert.Equal(code, ex.Code); }
}

internal sealed class ReferralFixture : IAsyncDisposable
{
    public JobPortalDbContext Db { get; }
    public ReferralTestClock Clock { get; } = new();
    public ReferralTestStorage Storage { get; } = new();
    public ReferralMarketplaceService Service { get; }
    public JobReferralService Public { get; }
    public Guid CandidateId { get; private set; }
    public Guid ReferrerId { get; private set; }
    public Guid ReferralId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid MembershipId { get; private set; }
    private Guid _roleId, _companyId, _categoryId;
    private ReferralFixture(JobPortalDbContext db)
    {
        Db = db;
        var outbox = new NotificationOutbox(new NotificationOutboxRepository(db), Clock);
        Service = new(new ReferralMarketplaceRepository(db), outbox, new AuditWriterTestDouble(), Storage, Clock);
        Public = new(new JobReferralRepository(db), new JobRepository(db), null!, new MembershipRepository(db, Clock),
            new AuditWriterTestDouble(), new UnitOfWork(db), Clock, outbox, marketplace: Service);
    }
    internal static async Task<ReferralFixture> CreateAsync(int slots = 5, string? plan = "ReferralContactAccess", JobPortalDbContext? db = null)
    {
        var f = new ReferralFixture(db ?? new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        var role = new Role { Name = "Candidate" };
        var category = new Category { Name = "Engineering", Slug = Guid.NewGuid().ToString("N") };
        f.Db.AddRange(role, category);
        await f.Db.SaveChangesAsync();
        f._roleId = role.Id;
        f._categoryId = category.Id;

        f.CandidateId = await f.AddCandidate(plan);
        var referrer = new User
        {
            FirstName = "Private",
            LastName = "Referrer",
            RoleId = role.Id,
            Status = UserStatus.Active,
            Email = "employee@example.test",
            NormalizedEmail = "EMPLOYEE@EXAMPLE.TEST",
            PhoneNumber = "+919999999999",
            LinkedInUrl = "https://linkedin.com/in/private",
            ProfileImageUrl = "https://example.test/private-photo"
        };
        f.Db.Add(referrer);
        await f.Db.SaveChangesAsync();
        f.ReferrerId = referrer.Id;

        // PostgreSQL enforces FK_Companies_Users_OwnerUserId: save the owner first.
        var company = new Company { Name = "Employer", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = referrer.Id };
        f.Db.Add(company);
        await f.Db.SaveChangesAsync();
        f._companyId = company.Id;
        f.ReferralId = await f.AddOpportunity(slots);
        f.JobId = (await f.Db.JobReferrals.SingleAsync(x => x.Id == f.ReferralId)).JobId;
        f.MembershipId = await f.Db.Memberships.Where(x => x.UserId == f.CandidateId).Select(x => x.Id).SingleOrDefaultAsync();
        f.Db.ChangeTracker.Clear(); return f;
    }
    public Task<ReferralRequestResponse> Request() => Service.CreateAsync(CandidateId, ReferralId, new("Please review my profile."), default);
    public async Task<Guid> AddCandidate(string? plan = "ReferralContactAccess")
    {
        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            FirstName = "Candidate",
            LastName = "Example",
            RoleId = _roleId,
            Status = UserStatus.Active,
            Email = $"{id:N}@example.test",
            NormalizedEmail = $"{id:N}@EXAMPLE.TEST",
            ResumeStorageKey = "fixture.pdf",
            ResumeContentType = "application/pdf",
            ResumeFileName = "resume.pdf"
        };
        Db.Add(user);
        if (plan is not null) Db.Add(new Membership
        {
            UserId = id,
            PlanCode = plan,
            PlanName = plan,
            Status = MembershipStatus.Active,
            StartsAtUtc = Clock.Utc,
            EndsAtUtc = Clock.Utc.AddDays(30)
        });
        await Db.SaveChangesAsync(); return id;
    }
    public async Task<Guid> AddOpportunity(int slots)
    {
        var job = new Job
        {
            Title = "Software Engineer",
            Slug = Guid.NewGuid().ToString("N"),
            ReferenceNumber = Guid.NewGuid().ToString("N"),
            Description = "Build software",
            ApplicationUrl = "https://example.test/jobs/1",
            CompanyId = _companyId,
            CategoryId = _categoryId,
            Status = JobStatus.Published,
            PublishedAtUtc = Clock.Utc
        };
        var opportunity = new JobReferral { Job = job, ReferrerUserId = ReferrerId, ApprovalStatus = JobReferralApprovalStatus.Approved, ReferralSlots = slots, ShowEmail = true };
        Db.Add(opportunity); await Db.SaveChangesAsync(); return opportunity.Id;
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
internal sealed class ReferralTestClock : TimeProvider
{
    public DateTime Utc { get; set; } = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    public override DateTimeOffset GetUtcNow() => new(Utc);
}
internal sealed class ReferralTestStorage : IResumeStorage
{
    public int Reads { get; private set; }
    public Guid LastOwner { get; private set; }
    public Task<Stream?> OpenReadAsync(Guid ownerUserId, string storageKey, Guid? resumeId = null, string? originalFileName = null, string? contentType = null, CancellationToken cancellationToken = default)
    { Reads++; LastOwner = ownerUserId; return Task.FromResult<Stream?>(new MemoryStream([1, 2, 3])); }
    public Task<string> StoreAsync(Guid ownerUserId, Stream content, string extension, Guid? resumeId = null, string? originalFileName = null, string? contentType = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid ownerUserId, string storageKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
