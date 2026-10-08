using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres.Migrations;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobSourceRunPostgresTests
{
    [LocalRunPostgresFact]
    public async Task RealMigrationAndStoreCoalesceRequestsClaimExclusivelyAndFenceRecoveredOwners()
    {
        // Explicit disposable localhost database only. NEVER reads API/Neon configuration.
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("JOB_SOURCE_RUN_TEST_POSTGRES"));
        Assert.True(settings.Host is "localhost" or "127.0.0.1" or "::1");
        Assert.Equal("jobsource_run_test", settings.Database);
        settings.IncludeErrorDetail = false; settings.Pooling = false; settings.Timeout = 5; settings.CommandTimeout = 15;
        var schema = "jobsource_run_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            settings.SearchPath = schema;
            var options = new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(settings.ConnectionString).Options;
            await using var setup = new JobPortalDbContext(options);
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "JobSources" ("Id" uuid PRIMARY KEY, "IsDeleted" boolean NOT NULL DEFAULT FALSE, "IsActive" boolean NOT NULL DEFAULT TRUE);
                CREATE TABLE "Users" ("Id" uuid PRIMARY KEY);
                """);
            // Exercise the ACTUAL new migration, not a separately maintained approximation.
            foreach (var command in setup.GetService<IMigrationsSqlGenerator>().Generate(new AddDurableJobSourceRuns().UpOperations, setup.Model))
                await setup.Database.ExecuteSqlRawAsync(command.CommandText);
            var source = Guid.NewGuid(); var now = DateTime.UtcNow;
            await setup.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"JobSources\" (\"Id\") VALUES ({source})");
            var requests = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var db = new JobPortalDbContext(options);
                return await new JobSourceRunStore(db).EnqueueAsync(source, null, now, default);
            }));
            var id = Assert.Single(requests.Select(x => x.Id).Distinct());
            Assert.Equal(1, await setup.JobSourceRuns.CountAsync());
            var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var db = new JobPortalDbContext(options);
                return await new JobSourceRunStore(db).TryClaimAsync(id, Guid.NewGuid(), now, now.AddSeconds(120), default);
            }));
            var owner = Assert.Single(claims.Where(x => x is not null))!;
            var repository = new JobSourceRunStore(setup);
            var progress = new JobSourceRunProgressSnapshot("Ingestion", 475, new() { TotalReceived = 1500, Created = 475 });
            Assert.True(await repository.HeartbeatAsync(owner, now.AddSeconds(20), now.AddSeconds(140), progress, default));
            var stored = (await repository.GetAsync(source, id, default))!;
            Assert.Equal(475, stored.Processed); Assert.Equal(475, stored.Created); Assert.Equal(1500, stored.TotalReceived);
            var expired = now.AddSeconds(141); var retryAt = expired.AddHours(1);
            await repository.RecoverAsync(id, expired, retryAt, default);
            Assert.Equal(JobSourceRunStatus.Interrupted, (await repository.GetAsync(source, id, default))!.Status);
            Assert.Null(await repository.TryClaimAsync(id, Guid.NewGuid(), expired, expired.AddMinutes(2), default));
            Assert.False(await repository.HeartbeatAsync(owner, expired, expired.AddMinutes(2), progress, default));
            Assert.False(await repository.FinishAsync(owner, JobSourceRunStatus.Succeeded, expired, expired, progress, default));
            var retried = await repository.TryClaimAsync(id, Guid.NewGuid(), retryAt, retryAt.AddSeconds(120), default);
            Assert.NotNull(retried); Assert.Equal(2, retried.AttemptCount);
            Assert.Equal(0, retried.Created); Assert.Equal(0, retried.Processed);
            var complete = new JobSourceRunProgressSnapshot("Finalizing", 1500, new() { TotalReceived = 1500, Created = 1025, Updated = 475, Succeeded = true });
            Assert.True(await repository.FinishAsync(retried, JobSourceRunStatus.Succeeded, retryAt, retryAt, complete, default));
            Assert.False(await repository.FinishAsync(retried, JobSourceRunStatus.Failed, retryAt, retryAt, complete, default));
            Assert.Equal(JobSourceRunStatus.Succeeded, (await repository.GetAsync(source, id, default))!.Status);
            Assert.NotEqual(id, (await repository.EnqueueAsync(source, null, retryAt, default)).Id);
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class LocalRunPostgresFactAttribute : FactAttribute
    {
        public LocalRunPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JOB_SOURCE_RUN_TEST_POSTGRES")))
                Skip = "Requires explicit JOB_SOURCE_RUN_TEST_POSTGRES for a disposable localhost jobsource_run_test database; never uses application/Neon settings.";
        }
    }
}
