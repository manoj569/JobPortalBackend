using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyDistributedPostgresTests
{
    [Fact]
    public async Task ConcurrentClaimsRespectPerUserLimitAndNeverDuplicateApplications()
    {
        var connection = Environment.GetEnvironmentVariable("AIAPPLY_STEP5_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var stamp = Guid.NewGuid().ToString("N");
        await using (var setup = Context(connection))
        {
            await setup.AIApplyApplications
                .Where(item => item.Status == AIApplyRunStatus.Queued || item.Status == AIApplyRunStatus.Processing)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, AIApplyRunStatus.Cancelled)
                    .SetProperty(item => item.CompletedAtUtc, DateTime.UtcNow)
                    .SetProperty(item => item.LeaseOwner, (string?)null)
                    .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null));
            var role = new Role { Name = $"Step6 {stamp}", NormalizedName = $"STEP6_{stamp}" };
            var users = Enumerable.Range(1, 4).Select(index => new User
            {
                Email = $"{stamp}-{index}@example.invalid", NormalizedEmail = $"{stamp}-{index}@EXAMPLE.INVALID",
                FirstName = "Synthetic", LastName = $"Candidate {index}", Status = UserStatus.Active,
                EmailConfirmed = true, Role = role
            }).ToArray();
            var company = new Company { Name = $"Step6 {stamp}", Slug = $"step6-{stamp}", OwnerUser = users[0] };
            var category = new Category { Name = $"Step6 {stamp}", Slug = $"step6-{stamp}" };
            var jobs = Enumerable.Range(1, 12).Select(index => new Job
            {
                ReferenceNumber = $"{stamp}-{index}", Title = $"Synthetic {index}", Slug = $"{stamp}-{index}",
                Description = "Deterministic concurrency fixture", ApplicationUrl = $"https://fixture.invalid/{stamp}/{index}",
                Company = company, Category = category
            }).ToArray();
            setup.AddRange(role); setup.AddRange(users); setup.Add(company); setup.Add(category); setup.AddRange(jobs);
            setup.AddRange(jobs.Select((job, index) => new AIApplyApplication
            {
                UserId = users[index % users.Length].Id, JobId = job.Id, ExternalApplicationUrl = job.ApplicationUrl,
                NormalizedApplicationUrl = job.ApplicationUrl, Status = AIApplyRunStatus.Queued,
                Priority = index < 4 ? 200 : 100, ScheduledAtUtc = DateTime.UtcNow.AddMinutes(-1)
            }));
            await setup.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        var claims = await Task.WhenAll(Enumerable.Range(1, 12).Select(async index =>
        {
            await using var context = Context(connection);
            return await new AIApplyRepository(context).ClaimNextAsync(now, 1, $"step6-worker-{index}", now.AddMinutes(1));
        }));
        var claimed = claims.Where(item => item is not null).Cast<AIApplyApplication>().ToArray();

        Assert.Equal(4, claimed.Length);
        Assert.Equal(4, claimed.Select(item => item.Id).Distinct().Count());
        Assert.Equal(4, claimed.Select(item => item.UserId).Distinct().Count());
        Assert.All(claimed, item => Assert.Equal(200, item.Priority));
    }

    [Fact]
    public async Task TwoInstancesShareWorkersCircuitProbeAndManualOverride()
    {
        var connection = Environment.GetEnvironmentVariable("AIAPPLY_STEP5_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new AIApplyOptions { Reliability = new() { SiteHealthMinimumSamples = 2, SiteFailureThreshold = .5m, SiteObservationMinutes = 15, CircuitCooldownSeconds = 10 }, Observability = new() { HalfOpenProbeLeaseSeconds = 30 } };
        await using var a = Context(connection); await using var b = Context(connection);
        await a.AIApplySiteOperationalStates.Where(x => x.Site == JobSiteIdentifier.Greenhouse).ExecuteDeleteAsync();
        await a.AIApplyWorkerInstances.Where(x => x.WorkerInstanceId == "step5-a" || x.WorkerInstanceId == "step5-b").ExecuteDeleteAsync();
        var storeA = new AIApplyOperationalStore(a, Options.Create(options)); var storeB = new AIApplyOperationalStore(b, Options.Create(options));
        var now = DateTime.UtcNow;
        await storeA.UpsertWorkerAsync("step5-a", now, now, 1, 2, true, false, default);
        await storeB.UpsertWorkerAsync("step5-b", now, now, 0, 2, true, false, default);
        var workers = await storeB.GetWorkersAsync(now.AddMinutes(-1), default);
        Assert.Contains(workers, x => x.WorkerInstanceId == "step5-a" && x.Status == "Running"); Assert.Contains(workers, x => x.WorkerInstanceId == "step5-b" && x.Status == "Running");

        await storeA.RecordSiteOutcomeAsync(JobSiteIdentifier.Greenhouse, "step5-a", false, now, default);
        await storeA.RecordSiteOutcomeAsync(JobSiteIdentifier.Greenhouse, "step5-a", false, now, default);
        Assert.Equal(AIApplyCircuitState.Open, (await storeB.GetSiteAsync(JobSiteIdentifier.Greenhouse, default)).CircuitState);
        Assert.False(await storeB.TryAcquireSiteExecutionAsync(JobSiteIdentifier.Greenhouse, "step5-b", now, default));

        var afterCooldown = now.AddSeconds(11);
        var probes = await Task.WhenAll(
            Task.Run(() => storeA.TryAcquireSiteExecutionAsync(JobSiteIdentifier.Greenhouse, "step5-a", afterCooldown, default)),
            Task.Run(() => storeB.TryAcquireSiteExecutionAsync(JobSiteIdentifier.Greenhouse, "step5-b", afterCooldown, default)));
        Assert.Single(probes, x => x); var owner = probes[0] ? "step5-a" : "step5-b"; var ownerStore = probes[0] ? storeA : storeB;
        await ownerStore.RecordSiteOutcomeAsync(JobSiteIdentifier.Greenhouse, owner, true, afterCooldown, default);
        Assert.Equal(AIApplyCircuitState.Closed, (await storeB.GetSiteAsync(JobSiteIdentifier.Greenhouse, default)).CircuitState);

        await storeA.SetManualCircuitAsync(JobSiteIdentifier.Greenhouse, true, now, default);
        Assert.Equal(AIApplyCircuitState.Open, (await storeB.GetSiteAsync(JobSiteIdentifier.Greenhouse, default)).CircuitState);
        await storeB.SetManualCircuitAsync(JobSiteIdentifier.Greenhouse, false, now, default);
        Assert.Equal(AIApplyCircuitState.Closed, (await storeA.GetSiteAsync(JobSiteIdentifier.Greenhouse, default)).CircuitState);
        await storeA.MarkWorkerInactiveAsync("step5-a", now.AddMinutes(1), default);
        Assert.Contains(await storeB.GetWorkersAsync(now, default), x => x.WorkerInstanceId == "step5-a" && x.Status == "Inactive");
    }

    [Fact]
    public async Task FailedHalfOpenProbeReopensForAllInstances()
    {
        var connection = Environment.GetEnvironmentVariable("AIAPPLY_STEP5_POSTGRES"); if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new AIApplyOptions { Reliability = new() { SiteHealthMinimumSamples = 2, SiteFailureThreshold = .5m, SiteObservationMinutes = 15, CircuitCooldownSeconds = 10 } };
        await using var a = Context(connection); await using var b = Context(connection); var sa = new AIApplyOperationalStore(a, Options.Create(options)); var sb = new AIApplyOperationalStore(b, Options.Create(options)); var now = DateTime.UtcNow;
        await a.AIApplySiteOperationalStates.Where(x => x.Site == JobSiteIdentifier.Lever).ExecuteDeleteAsync();
        await sa.RecordSiteOutcomeAsync(JobSiteIdentifier.Lever, "a", false, now, default); await sa.RecordSiteOutcomeAsync(JobSiteIdentifier.Lever, "a", false, now, default);
        Assert.True(await sb.TryAcquireSiteExecutionAsync(JobSiteIdentifier.Lever, "b", now.AddSeconds(11), default)); await sb.RecordSiteOutcomeAsync(JobSiteIdentifier.Lever, "b", false, now.AddSeconds(11), default);
        Assert.Equal(AIApplyCircuitState.Open, (await sa.GetSiteAsync(JobSiteIdentifier.Lever, default)).CircuitState);
    }

    private static JobPortalDbContext Context(string connection) => new(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql(connection, x => x.EnableRetryOnFailure()).Options);
}
