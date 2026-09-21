using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSessionPostgresTests
{
    [LocalSessionPostgresFact]
    public async Task ActualMigrationEnforcesUniquenessIntervalsAndRevisionCompareAndSwap()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_SESSION_TEST_POSTGRES"));
        Assert.True(builder.Host is "localhost" or "127.0.0.1" or "::1", "Only disposable local PostgreSQL is permitted.");
        Assert.Equal("career_guidance_test", builder.Database);
        builder.IncludeErrorDetail = false; builder.Pooling = false; builder.Timeout = 5; builder.CommandTimeout = 15;
        var schema = "cg_session_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(builder.ConnectionString); await admin.OpenAsync();
        await Execute(admin, $"CREATE SCHEMA \"{schema}\"");
        try
        {
            builder.SearchPath = schema + ",public";
            await using var a = new NpgsqlConnection(builder.ConnectionString); await a.OpenAsync();
            await Execute(a, """
                CREATE TABLE "Users" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerConsultants" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerGuidanceBookings" ("Id" uuid PRIMARY KEY);
                """);
            using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(new AddCareerGuidanceSessionsAndReminders().UpOperations, db.GetService<IDesignTimeModel>().Model))
                await Execute(a, command.CommandText);
            var user = Guid.NewGuid(); var consultant = Guid.NewGuid(); var booking = Guid.NewGuid(); var secondBooking = Guid.NewGuid();
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO "Users" VALUES (@user);
                INSERT INTO "CareerConsultants" VALUES (@consultant);
                INSERT INTO "CareerGuidanceBookings" VALUES (@booking), (@second);
                """, a))
            {
                seed.Parameters.AddWithValue("user", user); seed.Parameters.AddWithValue("consultant", consultant);
                seed.Parameters.AddWithValue("booking", booking); seed.Parameters.AddWithValue("second", secondBooking);
                await seed.ExecuteNonQueryAsync();
            }
            var session = Guid.NewGuid(); var secondSession = Guid.NewGuid();
            await InsertSession(a, session, booking, user, consultant);
            await InsertSession(a, secondSession, secondBooking, user, consultant); // multiple NULL provider IDs allowed
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertSession(a, Guid.NewGuid(), booking, user, consultant));
            Assert.Equal("23505", duplicate.SqlState);
            await Execute(a, $"UPDATE \"CareerGuidanceSessions\" SET \"ProviderMeetingId\" = 'test_meeting' WHERE \"Id\" = '{session:D}'");
            var providerDuplicate = await Assert.ThrowsAsync<PostgresException>(() => Execute(a, $"UPDATE \"CareerGuidanceSessions\" SET \"ProviderMeetingId\" = 'test_meeting' WHERE \"Id\" = '{secondSession:D}'"));
            Assert.Equal("23505", providerDuplicate.SqlState);
            var interval = await Assert.ThrowsAsync<PostgresException>(() => Execute(a, "UPDATE \"CareerGuidanceSessions\" SET \"ScheduledEndUtc\" = \"ScheduledStartUtc\""));
            Assert.Equal("23514", interval.SqlState);
            await InsertReminder(a, session, user);
            Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => InsertReminder(a, session, user))).SqlState);
            await using var b = new NpgsqlConnection(builder.ConnectionString); await b.OpenAsync();
            var results = await Task.WhenAll(CompareAndSwap(a, session), CompareAndSwap(b, session));
            Assert.Equal(1, results.Sum());
        }
        finally { await Execute(admin, $"DROP SCHEMA \"{schema}\" CASCADE"); }
    }
    private static async Task InsertSession(NpgsqlConnection connection, Guid id, Guid booking, Guid user, Guid consultant)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO "CareerGuidanceSessions" ("Id", "BookingId", "CandidateUserId", "ConsultantId", "MeetingProvider",
              "ScheduledStartUtc", "ScheduledEndUtc", "Status", "MeetingProvisioningAttemptedAtUtc", "EarningReleaseDelayHours", "Revision", "CreatedAtUtc", "IsDeleted")
            VALUES (@id, @booking, @user, @consultant, 'Manual', now(), now() + interval '1 hour', 1, now(), 48, @id, now(), FALSE)
            """, connection);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("booking", booking);
        command.Parameters.AddWithValue("user", user); command.Parameters.AddWithValue("consultant", consultant);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task InsertReminder(NpgsqlConnection connection, Guid session, Guid user)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO "CareerGuidanceSessionReminders" ("Id", "SessionId", "RecipientUserId", "OffsetMinutes", "ScheduledForUtc", "Status", "Revision", "CreatedAtUtc", "IsDeleted")
            VALUES (@id, @session, @user, 10, now(), 1, @id, now(), FALSE)
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("session", session); command.Parameters.AddWithValue("user", user);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<int> CompareAndSwap(NpgsqlConnection connection, Guid session)
    {
        await using var command = new NpgsqlCommand("UPDATE \"CareerGuidanceSessions\" SET \"Revision\" = @next WHERE \"Id\" = @id AND \"Revision\" = @id", connection);
        command.Parameters.AddWithValue("id", session); command.Parameters.AddWithValue("next", Guid.NewGuid()); return await command.ExecuteNonQueryAsync();
    }
    private static async Task Execute(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private sealed class LocalSessionPostgresFactAttribute : FactAttribute
    {
        public LocalSessionPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_SESSION_TEST_POSTGRES")))
                Skip = "Requires explicitly configured disposable localhost career_guidance_test database; never uses DefaultConnection.";
        }
    }
}
