using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerFinancePostgresTests
{
    [LocalFinancePostgresFact]
    public async Task DatabaseRejectsCompetingOrdersAndDuplicateFinancialEffects()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_FINANCE_TEST_POSTGRES"));
        Assert.True(builder.Host is "localhost" or "127.0.0.1" or "::1", "Only a disposable local endpoint is permitted.");
        Assert.Equal("career_guidance_test", builder.Database);
        builder.IncludeErrorDetail = false; builder.Pooling = false; builder.Timeout = 5; builder.CommandTimeout = 15;
        var schema = "cg_finance_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(builder.ConnectionString); await admin.OpenAsync();
        await Execute(admin, $"CREATE SCHEMA \"{schema}\"");
        try
        {
            builder.SearchPath = schema + ",public";
            await using var a = new NpgsqlConnection(builder.ConnectionString);
            await using var b = new NpgsqlConnection(builder.ConnectionString);
            await a.OpenAsync(); await b.OpenAsync();
            await Execute(a, """
                CREATE TABLE "Users" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerConsultants" ("Id" uuid PRIMARY KEY);
                CREATE TABLE "CareerGuidanceBookings" ("Id" uuid PRIMARY KEY);
                """);
            // Use the actual migration DDL in this private disposable fixture, never Database.Migrate.
            using var model = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
            var migration = new AddCareerGuidancePaymentsAndEarnings();
            foreach (var sql in model.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, model.GetService<IDesignTimeModel>().Model))
                await Execute(a, sql.CommandText);
            var user = Guid.NewGuid(); var consultant = Guid.NewGuid(); var booking = Guid.NewGuid();
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO "Users" VALUES (@user);
                INSERT INTO "CareerConsultants" VALUES (@consultant);
                INSERT INTO "CareerGuidanceBookings" ("Id") VALUES (@booking);
                """, a))
            {
                seed.Parameters.AddWithValue("user", user); seed.Parameters.AddWithValue("consultant", consultant); seed.Parameters.AddWithValue("booking", booking);
                await seed.ExecuteNonQueryAsync();
            }
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            var results = await Task.WhenAll(Payment(a, first, booking, user, consultant), Payment(b, second, booking, user, consultant));
            Assert.Single(results, success => success); Assert.Single(results, success => !success);
            var payment = results[0] ? first : second;
            var eventResults = await Task.WhenAll(Event(a, payment), Event(b, payment));
            Assert.Single(eventResults, success => success);
            var earningResults = await Task.WhenAll(Earning(a, payment, booking, consultant), Earning(b, payment, booking, consultant));
            Assert.Single(earningResults, success => success);
            var refundResults = await Task.WhenAll(Refund(a, payment, booking, user, consultant), Refund(b, payment, booking, user, consultant));
            Assert.Single(refundResults, success => success);
        }
        finally
        {
            // Generated identifier, confined to the local test's private schema.
            await Execute(admin, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    private static Task<bool> Payment(NpgsqlConnection connection, Guid id, Guid booking, Guid user, Guid consultant) => Attempt(connection, """
        INSERT INTO "CareerGuidancePayments" ("Id", "BookingId", "CandidateUserId", "ConsultantId", "Provider", "AmountGross", "Currency",
          "PlatformCommissionPercentSnapshot", "PlatformCommissionAmount", "ConsultantNetAmount", "Status", "RequiresRefundReview", "RefundPolicyVersion", "Revision", "CreatedAtUtc", "IsDeleted")
        VALUES (@id, @booking, @user, @consultant, 'Razorpay', 100, 'INR', 10, 10, 90, 1, FALSE, 'admin-full-v1', @id, now(), FALSE)
        """, id, id, booking, user, consultant);
    private static Task<bool> Event(NpgsqlConnection connection, Guid payment) => Attempt(connection, """
        INSERT INTO "CareerGuidancePaymentEvents" ("Id", "PaymentId", "EventKey", "EventType", "CreatedAtUtc", "IsDeleted")
        VALUES (@id, @payment, 'same_signed_event', 'payment.captured', now(), FALSE)
        """, Guid.NewGuid(), payment, Guid.Empty, Guid.Empty, Guid.Empty);
    private static Task<bool> Earning(NpgsqlConnection connection, Guid payment, Guid booking, Guid consultant) => Attempt(connection, """
        INSERT INTO "CareerGuidanceEarnings" ("Id", "PaymentId", "BookingId", "ConsultantId", "GrossAmount", "PlatformCommissionAmount", "NetAmount", "Currency", "Status", "Revision", "CreatedAtUtc", "IsDeleted")
        VALUES (@id, @payment, @booking, @consultant, 100, 10, 90, 'INR', 1, @id, now(), FALSE)
        """, Guid.NewGuid(), payment, booking, Guid.Empty, consultant);
    private static Task<bool> Refund(NpgsqlConnection connection, Guid payment, Guid booking, Guid user, Guid consultant) => Attempt(connection, """
        INSERT INTO "CareerGuidanceRefunds" ("Id", "PaymentId", "BookingId", "CandidateUserId", "ConsultantId", "Amount", "Currency", "ReasonCode", "RequestedByUserId", "RequestedAtUtc", "Status", "Revision", "CreatedAtUtc", "IsDeleted")
        VALUES (@id, @payment, @booking, @user, @consultant, 100, 'INR', 4, @user, now(), 1, @id, now(), FALSE)
        """, Guid.NewGuid(), payment, booking, user, consultant);
    private static async Task<bool> Attempt(NpgsqlConnection connection, string sql, Guid id, Guid payment, Guid booking, Guid user, Guid consultant)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("payment", payment); command.Parameters.AddWithValue("booking", booking);
        command.Parameters.AddWithValue("user", user); command.Parameters.AddWithValue("consultant", consultant);
        try { await command.ExecuteNonQueryAsync(); return true; }
        catch (PostgresException e) when (e.SqlState == "23505") { return false; }
    }
    private static async Task Execute(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private sealed class LocalFinancePostgresFactAttribute : FactAttribute
    {
        public LocalFinancePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAREER_GUIDANCE_FINANCE_TEST_POSTGRES")))
                Skip = "Requires explicitly configured disposable localhost career_guidance_test database; never uses DefaultConnection.";
        }
    }
}
