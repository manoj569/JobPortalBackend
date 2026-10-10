using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Referrals;
using JobPortal.Application.Features.Notifications;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ReferralMarketplacePostgresTests
{
    [LocalReferralPostgresFact]
    public async Task Phase3aExpirySweepsAcrossWorkersDoNotDuplicateDurableIntents()
    {
        await Isolated(async (f, options) =>
        {
            var request = await f.Request();
            f.Clock.Utc = request.ExpiresAtUtc;
            await using var first = new JobPortalDbContext(options);
            await using var second = new JobPortalDbContext(options);
            var settings = Microsoft.Extensions.Options.Options.Create(new ReferralNotificationOptions());
            await Task.WhenAll(new ReferralNotificationScheduler(first, f.Clock, settings).EnqueueDueAsync(default),
                new ReferralNotificationScheduler(second, f.Clock, settings).EnqueueDueAsync(default));
            Assert.Equal(4, await f.Db.NotificationDeliveries.CountAsync(d => d.Source == NotificationSource.ReferralExpired));
            Assert.Equal(ReferralRequestStatus.Requested, (await f.Db.ReferralRequests.AsNoTracking().SingleAsync()).Status);
        });
    }

    [LocalReferralPostgresFact]
    public async Task Phase3aProviderFailureCannotUndoAcceptanceAndSuccessfulDeliveryIsNotRepeated()
    {
        await Isolated(async (f, _) =>
        {
            var request = await f.Request();
            await f.Service.AcceptAsync(f.ReferrerId, request.Id, default);
            var candidate = await f.Db.Users.SingleAsync(u => u.Id == f.CandidateId);
            candidate.EmailConfirmed = true;
            await f.Db.SaveChangesAsync();
            var email = new Phase3aEmail();
            var dispatcher = new NotificationDispatcher(new NotificationDeliveryRepository(f.Db), email, new Phase3aRealtime(), f.Clock,
                Microsoft.Extensions.Options.Options.Create(new NotificationDeliveryOptions()));
            while (await dispatcher.ProcessOneAsync(default)) { }
            Assert.Equal(ReferralRequestStatus.Accepted, (await f.Db.ReferralRequests.AsNoTracking().SingleAsync()).Status);
            Assert.Equal(1, email.Calls);
            Assert.Equal(1, await f.Db.Notifications.CountAsync());
            email.Result = JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult.Sent;
            f.Clock.Utc = f.Clock.Utc.AddMinutes(1);
            while (await dispatcher.ProcessOneAsync(default)) { }
            Assert.Equal(2, email.Calls);
            Assert.False(await dispatcher.ProcessOneAsync(default));
            Assert.Equal(2, email.Calls);
            Assert.Equal(NotificationDeliveryStatus.Sent, (await f.Db.NotificationDeliveries.AsNoTracking()
                .SingleAsync(d => d.Source == NotificationSource.ReferralAccepted && d.Channel == NotificationChannel.Email)).Status);
        });
    }

    private sealed class Phase3aEmail : JobPortal.Application.Abstractions.Authentication.IEmailService
    {
        public JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult Result { get; set; } = JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult.Failed;
        public int Calls { get; private set; }
        public Task<JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult> SendNotificationAsync(User user, Notification notification, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Result); }
        public Task<JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult> SendPasswordResetAsync(User user, string rawToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult> SendApplicationStatusAsync(User user, string jobTitle, JobApplicationStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobPortal.Application.Abstractions.Authentication.EmailDeliveryResult> SendRegistrationVerificationAsync(User user, string rawToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Phase3aRealtime : INotificationRealtime
    {
        public Task PublishAsync(Notification notification, CancellationToken ct) => Task.CompletedTask;
    }
    [LocalReferralPostgresFact]
    public async Task PublicReferralAndSavedPagingReuseCompanyLogoWithoutPrivateReferrerData()
    {
        await Isolated(async (f, _) =>
        {
            var secondId = await f.AddOpportunity(5);
            var jobs = await f.Db.Jobs.Include(x => x.Company).ToArrayAsync();
            jobs[0].Company.LogoUrl = "https://assets.example.test/licensed.png";
            foreach (var job in jobs) f.Db.SavedJobs.Add(new SavedJob { UserId = f.CandidateId, JobId = job.Id });
            await f.Db.SaveChangesAsync();
            var publicJobs = new PublicJobRepository(f.Db, f.Clock);
            var first = await publicJobs.SearchAsync(new(ReferralOnly: true, Search: "Software", PageSize: 1));
            var second = await publicJobs.SearchAsync(new(ReferralOnly: true, Search: "Software", PageNumber: 2, PageSize: 1));
            Assert.Equal(2, first.TotalCount);
            Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id);
            Assert.Equal("Employee Referrer", first.Items.Single().ReferrerName);
            var saved = await new DashboardRepository(f.Db, f.Clock).GetSavedJobsAsync(f.CandidateId, new(1, 1));
            Assert.Equal(2, saved.TotalCount);
            Assert.Equal(jobs[0].Company.LogoUrl, Assert.Single(saved.Items).Job.CompanyLogoUrl);
            Assert.Empty((await new DashboardRepository(f.Db, f.Clock).GetSavedJobsAsync(Guid.NewGuid(), new())).Items);
            var referral = await f.Db.JobReferrals.Include(x => x.Job).SingleAsync(x => x.Id == secondId);
            referral.Job.ExpiresAtUtc = f.Clock.Utc;
            await f.Db.SaveChangesAsync();
            Assert.Equal(1, (await new JobReferralRepository(f.Db, f.Clock).GetApprovedAsync(1, 20)).TotalCount);
        });
    }

    [LocalReferralPostgresFact]
    public async Task CompetingAcceptancesCannotExceedOneRemainingOpportunitySlot()
    {
        await Isolated(async (f, options) =>
        {
            var a = await f.Request(); var other = await f.AddCandidate();
            var b = await f.Service.CreateAsync(other, f.ReferralId, new(null), default);
            await using var first = new JobPortalDbContext(options); await using var second = new JobPortalDbContext(options);
            var results = await Task.WhenAll(Attempt(() => Service(first, f).AcceptAsync(f.ReferrerId, a.Id, default)),
                Attempt(() => Service(second, f).AcceptAsync(f.ReferrerId, b.Id, default)));
            Assert.Single(results, x => x == "accepted"); Assert.Single(results, x => x == "REFERRAL_SLOTS_FULL");
            Assert.Equal(1, await f.Db.ReferralRequests.AsNoTracking().CountAsync(x => x.AcceptedAtUtc != null));
            Assert.Equal(6, await f.Db.NotificationDeliveries.CountAsync());
        });
    }
    [LocalReferralPostgresFact]
    public async Task CompetingAcceptancesAcrossDifferentJobsCannotCreateEleventhConnection()
    {
        await Isolated(async (f, options) =>
        {
            for (var i = 0; i < 9; i++)
            {
                var job = await f.AddOpportunity(1); var row = await f.Service.CreateAsync(f.CandidateId, job, new(null), default);
                await f.Service.AcceptAsync(f.ReferrerId, row.Id, default);
            }
            var a = await f.Request(); var extra = await f.AddOpportunity(1);
            var b = await f.Service.CreateAsync(f.CandidateId, extra, new(null), default);
            await using var first = new JobPortalDbContext(options); await using var second = new JobPortalDbContext(options);
            var results = await Task.WhenAll(Attempt(() => Service(first, f).AcceptAsync(f.ReferrerId, a.Id, default)),
                Attempt(() => Service(second, f).AcceptAsync(f.ReferrerId, b.Id, default)));
            Assert.Single(results, x => x == "accepted"); Assert.Single(results, x => x == "REFERRAL_CONNECTION_LIMIT_REACHED");
            Assert.Equal(10, await f.Db.ReferralRequests.AsNoTracking().CountAsync(x => x.AcceptedAtUtc != null));
        });
    }
    [LocalReferralPostgresFact]
    public async Task ConcurrentDuplicateAcceptIsExactlyOnceAndDatabaseRejectsDuplicateRequest()
    {
        await Isolated(async (f, options) =>
        {
            var row = await f.Request();
            await using var first = new JobPortalDbContext(options); await using var second = new JobPortalDbContext(options);
            await Task.WhenAll(Service(first, f).AcceptAsync(f.ReferrerId, row.Id, default), Service(second, f).AcceptAsync(f.ReferrerId, row.Id, default));
            Assert.Equal(1, (await f.Service.QuotaAsync(f.CandidateId, default)).AcceptedConnections);
            Assert.Equal(4, await f.Db.NotificationDeliveries.CountAsync());
            f.Db.ReferralRequests.Add(new ReferralRequest { CandidateUserId = f.CandidateId, ReferrerUserId = f.ReferrerId,
                JobReferralId = f.ReferralId, RequestedAtUtc = f.Clock.Utc, ExpiresAtUtc = f.Clock.Utc.AddHours(48) });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        });
    }
    private static ReferralMarketplaceService Service(JobPortalDbContext db, ReferralFixture f) => new(new ReferralMarketplaceRepository(db),
        new NotificationOutbox(new NotificationOutboxRepository(db), f.Clock), new AuditWriterTestDouble(), f.Storage, f.Clock);
    private static async Task<string> Attempt(Func<Task> action)
    { try { await action(); return "accepted"; } catch (ConflictException ex) { return ex.Code; } }
    private static async Task Isolated(Func<ReferralFixture, DbContextOptions<JobPortalDbContext>, Task> test)
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("REFERRAL_TEST_POSTGRES"));
        if (settings.Host is not ("localhost" or "127.0.0.1") || settings.Database != "careerharbor_referral_test")
            throw new InvalidOperationException("Referral integration tests require localhost/127.0.0.1 and database careerharbor_referral_test.");
        // Disposable Docker databases can start/connect slowly on a busy Windows host.
        settings.IncludeErrorDetail = false; settings.Pooling = false; settings.Timeout = 60; settings.CommandTimeout = 120;
        var schema = "referral_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.SearchPath = schema;
            var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using var setup = new JobPortalDbContext(options);
            await setup.Database.ExecuteSqlRawAsync(setup.Database.GenerateCreateScript());
            await using var f = await ReferralFixture.CreateAsync(slots: 1, db: setup);
            await test(f, options);
        }
        finally
        {
            // Only the schema created above, in the explicitly named disposable localhost database.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin); await cleanup.ExecuteNonQueryAsync();
        }
    }
    private sealed class LocalReferralPostgresFactAttribute : FactAttribute
    {
        public LocalReferralPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REFERRAL_TEST_POSTGRES")))
                Skip = "Requires REFERRAL_TEST_POSTGRES for disposable localhost careerharbor_referral_test; never uses application credentials.";
        }
    }
}
