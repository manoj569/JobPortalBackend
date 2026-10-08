using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Referrals;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ReferralMarketplacePostgresTests
{
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
        settings.IncludeErrorDetail = false; settings.Pooling = false; settings.Timeout = 5; settings.CommandTimeout = 30;
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
