using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerTrustPostgresTests
{
    [LocalTrustPostgresFact]
    public async Task ActualMigrationProtectsConcurrentCreationResolutionAndConstraints()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_TRUST_TEST_POSTGRES"));
        Assert.True(builder.Host is "localhost" or "127.0.0.1" or "::1", "Only disposable localhost PostgreSQL is permitted.");
        Assert.Equal("career_guidance_test", builder.Database);
        builder.IncludeErrorDetail = false; builder.Pooling = false; builder.Timeout = 5; builder.CommandTimeout = 15;
        var schema = "cg_trust_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(builder.ConnectionString); await admin.OpenAsync();
        await Execute(admin, $"CREATE SCHEMA \"{schema}\"");
        try
        {
            builder.SearchPath = schema;
            await using var a = new NpgsqlConnection(builder.ConnectionString); await a.OpenAsync();
            await Execute(a, """
                CREATE TABLE "Users" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerConsultants" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerGuidanceBookings" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerGuidanceSessions" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerGuidancePayments" ("Id" uuid PRIMARY KEY);
                """);
            using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(new AddCareerGuidanceReviewsDisputesAndTrust().UpOperations, db.GetService<IDesignTimeModel>().Model))
                await Execute(a, command.CommandText);
            var identity = Guid.NewGuid();
            foreach (var table in new[] { "Users", "CareerConsultants", "CareerGuidanceBookings", "CareerGuidanceSessions", "CareerGuidancePayments" })
                await Execute(a, $"INSERT INTO \"{table}\" VALUES ('{identity:D}')");
            await using var b = new NpgsqlConnection(builder.ConnectionString); await b.OpenAsync();
            Assert.Equal(1, (await Task.WhenAll(Insert(a, identity, true), Insert(b, identity, true))).Sum());
            Assert.Equal(1, (await Task.WhenAll(Insert(a, identity, false), Insert(b, identity, false))).Sum());
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Execute(a, "UPDATE \"CareerGuidanceReviews\" SET \"Rating\" = 6"))).SqlState);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Execute(a, "UPDATE \"CareerGuidanceReviews\" SET \"IsPublished\" = TRUE"))).SqlState);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => Execute(a, "UPDATE \"CareerGuidanceDisputes\" SET \"Status\" = 5"))).SqlState);
            Assert.Equal(1, (await Task.WhenAll(Resolve(a, identity), Resolve(b, identity))).Sum());
            // Lifetime uniqueness also covers terminal disputes and withdrawn reviews.
            Assert.Equal(0, await Insert(a, identity, false));
            await Execute(a, "UPDATE \"CareerGuidanceReviews\" SET \"IsDeleted\" = TRUE");
            Assert.Equal(0, await Insert(a, identity, true));
        }
        finally { await Execute(admin, $"DROP SCHEMA \"{schema}\" CASCADE"); }
    }

    private static async Task<int> Insert(NpgsqlConnection connection, Guid identity, bool review)
    {
        var sql = review ? """
            INSERT INTO "CareerGuidanceReviews" ("Id", "BookingId", "SessionId", "PaymentId", "ConsultantId", "CandidateUserId",
                "Rating", "IsPublished", "ModerationStatus", "Revision", "CreatedAtUtc", "IsDeleted")
            VALUES (@id, @identity, @identity, @identity, @identity, @identity, 5, FALSE, 1, @identity, now(), FALSE)
            """ : """
            INSERT INTO "CareerGuidanceDisputes" ("Id", "BookingId", "PaymentId", "CandidateUserId", "ConsultantId", "Category",
                "Description", "RequestedRefund", "Status", "Resolution", "SubmittedAtUtc", "Revision", "CreatedAtUtc", "IsDeleted")
            VALUES (@id, @identity, @identity, @identity, @identity, 2, 'Test evidence', FALSE, 1, 1, now(), @identity, now(), FALSE)
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("identity", identity);
        try { return await command.ExecuteNonQueryAsync(); }
        catch (PostgresException e) when (e.SqlState == "23505") { return 0; }
    }

    private static async Task<int> Resolve(NpgsqlConnection connection, Guid identity)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE "CareerGuidanceDisputes" SET "Status" = 5, "Resolution" = 2, "ResolvedAtUtc" = now(),
              "ResolvedByUserId" = @identity, "Revision" = @next WHERE "BookingId" = @identity AND "Revision" = @identity
            """, connection);
        command.Parameters.AddWithValue("identity", identity); command.Parameters.AddWithValue("next", Guid.NewGuid());
        return await command.ExecuteNonQueryAsync();
    }
    private static async Task Execute(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private sealed class LocalTrustPostgresFactAttribute : FactAttribute
    {
        public LocalTrustPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_TRUST_TEST_POSTGRES")))
                Skip = "Requires explicitly configured disposable localhost career_guidance_test database; never uses DefaultConnection.";
        }
    }
}
