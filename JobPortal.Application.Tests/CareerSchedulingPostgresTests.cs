using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

/// <summary>Opt-in only: never reads the application's database configuration.</summary>
public sealed class CareerSchedulingPostgresTests
{
    [LocalSchedulingPostgresFact]
    public async Task CompetingOverlappingInsertsCannotBothSucceedAndCancellationReleasesRange()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_TEST_POSTGRES"));
        Assert.True(builder.Host is "localhost" or "127.0.0.1" or "::1", "Only a local disposable PostgreSQL endpoint is permitted.");
        Assert.Equal("career_guidance_test", builder.Database);
        builder.IncludeErrorDetail = false;
        builder.Timeout = 5;
        builder.CommandTimeout = 15;
        var schema = "cg_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using (var extension = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')", admin))
            Assert.True((bool)(await extension.ExecuteScalarAsync())!, "Install btree_gist in the disposable test database before running this test.");
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            builder.SearchPath = schema + ",public";
            builder.Pooling = false;
            await using var a = new NpgsqlConnection(builder.ConnectionString);
            await using var b = new NpgsqlConnection(builder.ConnectionString);
            await a.OpenAsync(); await b.OpenAsync();
            await using (var table = new NpgsqlCommand("""
                CREATE TABLE "CareerGuidanceBookings" (
                    "Id" uuid PRIMARY KEY, "ConsultantId" uuid NOT NULL,
                    "StartUtc" timestamptz NOT NULL, "EndUtc" timestamptz NOT NULL,
                    "Status" integer NOT NULL, "IsDeleted" boolean NOT NULL DEFAULT FALSE
                );
                """, a)) await table.ExecuteNonQueryAsync();
            // Exercise the exact constraint from the production migration, not an approximation.
            var constraint = Assert.Single(new AddCareerGuidanceSchedulingAndBookings().UpOperations.OfType<SqlOperation>()).Sql;
            await using (var add = new NpgsqlCommand(constraint, a)) await add.ExecuteNonQueryAsync();
            var consultant = Guid.NewGuid();
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            var start = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
            var attempts = await Task.WhenAll(Insert(a, first, consultant, start), Insert(b, second, consultant, start.AddMinutes(30)));
            Assert.Single(attempts, success => success);
            Assert.Single(attempts, success => !success);
            await using (var count = new NpgsqlCommand("SELECT COUNT(*) FROM \"CareerGuidanceBookings\"", a))
                Assert.Equal(1L, await count.ExecuteScalarAsync());
            await using (var cancel = new NpgsqlCommand("UPDATE \"CareerGuidanceBookings\" SET \"Status\" = 3 WHERE \"Id\" = @id", a))
            {
                cancel.Parameters.AddWithValue("id", attempts[0] ? first : second);
                Assert.Equal(1, await cancel.ExecuteNonQueryAsync());
            }
            Assert.True(await Insert(b, Guid.NewGuid(), consultant, start));
            Assert.True(await Insert(a, Guid.NewGuid(), consultant, start.AddHours(1))); // Adjacent, not overlapping.
            Assert.True(await Insert(a, Guid.NewGuid(), Guid.NewGuid(), start)); // Different consultant.
        }
        finally
        {
            // Identifier is generated above; cleanup is confined to this test's private local schema.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> Insert(NpgsqlConnection connection, Guid id, Guid consultant, DateTime start)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO "CareerGuidanceBookings" ("Id", "ConsultantId", "StartUtc", "EndUtc", "Status")
            VALUES (@id, @consultant, @start, @end, 1)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("consultant", consultant);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", start.AddHours(1));
        try { await command.ExecuteNonQueryAsync(); return true; }
        catch (PostgresException ex) when (ex.SqlState == "23P01" && ex.ConstraintName == "EX_CareerGuidanceBookings_NoOverlap") { return false; }
    }

    private sealed class LocalSchedulingPostgresFactAttribute : FactAttribute
    {
        public LocalSchedulingPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_TEST_POSTGRES")))
                Skip = "Requires an explicitly configured localhost career_guidance_test database with btree_gist; never uses production configuration.";
        }
    }
}
