using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Maintenance;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobUrlHashBackfillTests
{
    [Fact]
    public async Task DryRunNeverCallsWrites()
    {
        var store = new MemoryStore(NewJob("https://example.com/job?utm_source=test"), NewJob("invalid"));
        var report = new BackfillReport();
        await Backfill.RunAsync(store, new(false, 500), report, default);
        Assert.Equal(0, store.Writes);
        Assert.Equal(1, report.Eligible);
        Assert.Equal(1, report.Invalid);
        Assert.All(store.Jobs, job => Assert.Null(job.CanonicalApplicationUrlHash));
    }

    [Fact]
    public async Task ApplyPreservesEveryOtherFieldAndUsesExactHash()
    {
        var job = NewJob("https://EXAMPLE.com:443/jobs/1/?utm_source=test&key=secret#fragment");
        var before = JsonSerializer.Serialize(job);
        var store = new MemoryStore(job);
        var report = new BackfillReport();
        await Backfill.RunAsync(store, new(true, 500), report, default);
        Assert.Equal(ApplicationUrlIdentity.Hash(job.ApplicationUrl), job.CanonicalApplicationUrlHash);
        Assert.Equal(1, report.Updated);
        job.CanonicalApplicationUrlHash = null;
        Assert.Equal(before, JsonSerializer.Serialize(job));
        // Production uses this exact statement, not SaveChanges or any entity update.
        var setClause = PostgresBackfillStore.UpdateSql.Split("WHERE", StringSplitOptions.None)[0];
        Assert.Equal("UPDATE \"Jobs\" SET \"CanonicalApplicationUrlHash\" = @hash", setClause.Trim());
        Assert.Contains("\"CanonicalApplicationUrlHash\" IS NULL", PostgresBackfillStore.UpdateSql, StringComparison.Ordinal);
        Assert.Contains("\"ApplicationUrl\" = @url", PostgresBackfillStore.UpdateSql, StringComparison.Ordinal);
        Assert.Contains("\"IsDeleted\" = FALSE", PostgresBackfillStore.UpdateSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsInvalidPopulatedAndDeletedRows()
    {
        var invalid = NewJob("");
        var populated = NewJob("https://example.com/existing");
        populated.CanonicalApplicationUrlHash = "existing";
        var deleted = NewJob("https://example.com/deleted");
        deleted.IsDeleted = true;
        var store = new MemoryStore(invalid, populated, deleted);
        var report = new BackfillReport();
        await Backfill.RunAsync(store, new(true, 2), report, default);
        Assert.Equal(1, report.Scanned);
        Assert.Equal(1, report.Invalid);
        Assert.Null(invalid.CanonicalApplicationUrlHash);
        Assert.Null(deleted.CanonicalApplicationUrlHash);
        Assert.Equal("existing", populated.CanonicalApplicationUrlHash);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("url")]
    [InlineData("delete")]
    public async Task ConcurrentChangesAreNotOverwritten(string change)
    {
        var job = NewJob("https://example.com/job");
        var store = new MemoryStore(job) { BeforeUpdate = () =>
        {
            if (change == "hash") job.CanonicalApplicationUrlHash = "concurrent";
            if (change == "url") job.ApplicationUrl = "https://example.com/changed";
            if (change == "delete") job.IsDeleted = true;
        }};
        var report = new BackfillReport();
        await Backfill.RunAsync(store, new(true, 500), report, default);
        Assert.Equal(0, report.Updated);
        Assert.Equal(1, report.Conflicts);
        Assert.Equal(change == "hash" ? "concurrent" : null, job.CanonicalApplicationUrlHash);
    }

    [Fact]
    public async Task MultipleBatchesAndRerunAreIdempotent()
    {
        var store = new MemoryStore(Enumerable.Range(0, 7).Select(i => NewJob($"https://example.com/{i}")).ToArray());
        var first = new BackfillReport();
        await Backfill.RunAsync(store, new(true, 2), first, default);
        Assert.Equal(7, first.Updated);
        Assert.Equal(4, first.Batches);
        var second = new BackfillReport();
        await Backfill.RunAsync(store, new(true, 2), second, default);
        Assert.Equal(0, second.Scanned);
        Assert.Equal(0, second.Updated);
    }

    [Fact]
    public async Task FailedBatchStopsWithoutReportingUncommittedUpdates()
    {
        var store = new MemoryStore(NewJob("https://example.com/1")) { Fail = true };
        var report = new BackfillReport();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Backfill.RunAsync(store, new(true, 1), report, default));
        Assert.Equal(1, report.Failures);
        Assert.Equal(0, report.Updated);
        Assert.Equal(0, report.Batches);
    }

    [Fact]
    public async Task CancellationStopsWithoutWrites()
    {
        var store = new MemoryStore(NewJob("https://example.com/1"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Backfill.RunAsync(store, new(true, 1), new(), new CancellationToken(true)));
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("job-url-hash-backfill --unknown")]
    [InlineData("job-url-hash-backfill --batch-size 0")]
    [InlineData("job-url-hash-backfill --batch-size 5001")]
    [InlineData("job-url-hash-backfill --batch-size -1")]
    [InlineData("job-url-hash-backfill --batch-size text")]
    [InlineData("job-url-hash-backfill --batch-size")]
    [InlineData("job-url-hash-backfill --apply --apply")]
    public void InvalidArgumentsFail(string command) => Assert.Throws<ArgumentException>(() => BackfillOptions.Parse(command.Split(' ')));

    [Fact]
    public void DefaultsAreReadOnlyAndExplicitApplyWorks()
    {
        Assert.Equal(new(false, 500), BackfillOptions.Parse(["job-url-hash-backfill"]));
        Assert.Equal(new(true, 25), BackfillOptions.Parse(["job-url-hash-backfill", "--apply", "--batch-size", "25"]));
    }

    private static Job NewJob(string url) => new()
    {
        Id = Guid.NewGuid(), ApplicationUrl = url, Title = "Curated", Description = "Keep",
        CompanyId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), MinimumSalary = 100,
        CreatedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), FingerprintHash = "fingerprint"
    };

    private sealed class MemoryStore(params Job[] jobs) : IBackfillStore
    {
        public Job[] Jobs { get; } = jobs;
        public int Writes { get; private set; }
        public Action? BeforeUpdate { get; init; }
        public bool Fail { get; init; }
        public Task<BackfillCounts> CountAsync(CancellationToken cancellationToken) => Task.FromResult(new BackfillCounts(
            Jobs.Length, Jobs.Count(j => j.CanonicalApplicationUrlHash is null), Jobs.Count(j => j.CanonicalApplicationUrlHash is not null),
            Jobs.Count(j => !j.IsDeleted && j.CanonicalApplicationUrlHash is null)));
        public Task<IReadOnlyList<BackfillCandidate>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BackfillCandidate>>(Jobs.Where(j => !j.IsDeleted && j.CanonicalApplicationUrlHash is null &&
                (after is null || j.Id.CompareTo(after.Value) > 0)).OrderBy(j => j.Id).Take(limit)
                .Select(j => new BackfillCandidate(j.Id, j.ApplicationUrl)).ToArray());
        public Task<int> UpdateBatchAsync(IReadOnlyList<BackfillUpdate> updates, CancellationToken cancellationToken)
        {
            Writes++;
            if (Fail) throw new InvalidOperationException("Simulated batch rollback");
            BeforeUpdate?.Invoke();
            var count = 0;
            foreach (var update in updates)
            {
                var job = Jobs.Single(j => j.Id == update.Id);
                if (job.IsDeleted || job.CanonicalApplicationUrlHash is not null || job.ApplicationUrl != update.ApplicationUrl) continue;
                job.CanonicalApplicationUrlHash = update.Hash;
                count++;
            }
            return Task.FromResult(count);
        }
    }
}
