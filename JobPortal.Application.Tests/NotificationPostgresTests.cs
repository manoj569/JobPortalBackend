using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class NotificationPostgresTests
{
    // Explicit opt-in, like the existing PostgreSQL tests. Never reads application connection settings.
    [LocalNotificationPostgresFact]
    public async Task ActualRepositoryClaimsAreExclusiveRecoverExpiredLeasesAndFenceOldOwners()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_POSTGRES"));
        Assert.True(settings.Host is "localhost" or "127.0.0.1" or "::1");
        Assert.Equal("notification_test", settings.Database);
        settings.IncludeErrorDetail = false; settings.Pooling = false; settings.Timeout = 5; settings.CommandTimeout = 15;
        var schema = "notification_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.SearchPath = schema;
            var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using var setup = new JobPortalDbContext(options);
            // Minimal isolated fixture for the real repository SQL (not a mock claim algorithm).
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "Users" ("Id" uuid PRIMARY KEY, "Status" integer NOT NULL, "IsDeleted" boolean NOT NULL DEFAULT FALSE);
                CREATE TABLE "Notifications" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "BusinessKey" varchar(220), "Title" varchar(250) NOT NULL,
                  "Message" varchar(4000) NOT NULL, "Type" integer NOT NULL, "ActionUrl" varchar(2048), "IsRead" boolean NOT NULL,
                  "ReadAtUtc" timestamptz, "ReferralId" uuid, "JobId" uuid, "CreatedAtUtc" timestamptz NOT NULL,
                  "UpdatedAtUtc" timestamptz, "IsDeleted" boolean NOT NULL, "DeletedAtUtc" timestamptz);
                CREATE UNIQUE INDEX "notification_key" ON "Notifications" ("UserId", "BusinessKey") WHERE "BusinessKey" IS NOT NULL;
                CREATE TABLE "CandidateInterviewSchedules" ("Id" uuid PRIMARY KEY, "CandidateId" uuid NOT NULL, "ReminderRevision" uuid NOT NULL,
                  "ReminderRequested" boolean NOT NULL, "Status" integer NOT NULL, "InterviewAtUtc" timestamptz NOT NULL, "IsDeleted" boolean NOT NULL DEFAULT FALSE);
                CREATE TABLE "NotificationDeliveries" ("Id" uuid PRIMARY KEY, "NotificationId" uuid NOT NULL, "UserId" uuid NOT NULL,
                  "Channel" integer NOT NULL, "Source" integer NOT NULL, "SourceId" uuid NOT NULL, "SourceRevision" uuid NOT NULL,
                  "BusinessKey" varchar(220) NOT NULL, "Title" varchar(250) NOT NULL, "Message" varchar(4000) NOT NULL, "ActionUrl" varchar(2048),
                  "ScheduledForUtc" timestamptz NOT NULL, "NextAttemptAtUtc" timestamptz NOT NULL, "Status" integer NOT NULL, "AttemptCount" integer NOT NULL,
                  "LeaseOwner" uuid, "LeaseExpiresAtUtc" timestamptz, "CompletedAtUtc" timestamptz, "FailureCode" varchar(64),
                  "CreatedAtUtc" timestamptz NOT NULL, "UpdatedAtUtc" timestamptz, "IsDeleted" boolean NOT NULL, "DeletedAtUtc" timestamptz,
                  UNIQUE ("BusinessKey", "UserId", "Channel"));
                """);
            var now = DateTime.UtcNow; var user = Guid.NewGuid(); var schedule = Guid.NewGuid(); var revision = Guid.NewGuid();
            await setup.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\", \"Status\") VALUES ({user}, {(int)JobPortal.Domain.Enums.UserStatus.Active})");
            await setup.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CandidateInterviewSchedules\" (\"Id\", \"CandidateId\", \"ReminderRevision\", \"ReminderRequested\", \"Status\", \"InterviewAtUtc\") VALUES ({schedule}, {user}, {revision}, TRUE, 1, {now.AddHours(1)})");
            var row = NotificationOutbox.Create(NotificationSource.InterviewReminder, schedule, revision, user, "interview:test", "Reminder", "Body", null, now).First();
            setup.NotificationDeliveries.Add(row); await setup.SaveChangesAsync();
            await using var a = new JobPortalDbContext(options); await using var b = new JobPortalDbContext(options);
            var first = new NotificationDeliveryRepository(a); var second = new NotificationDeliveryRepository(b);
            var claims = await Task.WhenAll(first.ClaimAsync(now, new(), default), second.ClaimAsync(now, new(), default));
            var owner = Assert.Single(claims.Where(x => x is not null))!;
            Assert.Null(await second.ClaimAsync(now.AddSeconds(30), new(), default));
            var recovered = await second.ClaimAsync(now.AddMinutes(4), new(), default);
            Assert.NotNull(recovered); Assert.NotEqual(owner.LeaseOwner, recovered.LeaseOwner);
            await first.CompleteAsync(owner, NotificationDeliveryStatus.Sent, now.AddMinutes(4), now, null, default);
            var pending = await setup.NotificationDeliveries.AsNoTracking().SingleAsync();
            Assert.Equal(NotificationDeliveryStatus.Processing, pending.Status); Assert.Null(pending.CompletedAtUtc);
            var notification = await second.MaterializeAsync(recovered, now.AddMinutes(4), default);
            Assert.NotNull(notification);
            Assert.Null(await second.MaterializeAsync(recovered, now.AddMinutes(4), default));
            Assert.Equal(1, await setup.Notifications.CountAsync());
            Assert.Equal(NotificationDeliveryStatus.Sent, (await setup.NotificationDeliveries.AsNoTracking().SingleAsync()).Status);
        }
        finally
        {
            // Only the randomly named schema created in this explicitly named disposable localhost database.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class LocalNotificationPostgresFactAttribute : FactAttribute
    {
        public LocalNotificationPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_POSTGRES")))
                Skip = "Requires explicit NOTIFICATION_TEST_POSTGRES for a disposable localhost notification_test database; never uses application configuration.";
        }
    }
}
