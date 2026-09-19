using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobAggregationHardeningTests
{
    [Fact]
    public void PostgreSqlUrlLookupTranslatesToBoundedIndexedEquality()
    {
        using var context = new JobPortal.Persistence.Context.JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortal.Persistence.Context.JobPortalDbContext>()
                .UseNpgsql("Host=localhost;Database=query_only").Options);
        var sql = new JobRepository(context).CanonicalUrlQuery(new string('a', 64)).Take(1).ToQueryString();
        Assert.Contains("\"CanonicalApplicationUrlHash\" =", sql);
        Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("lower(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualCancellationAfterAcquireReleasesBothLeases()
    {
        using var f = new JobSourceFixture();
        using var cancellation = new CancellationTokenSource();
        var service = f.CreateService(new CancellingRunner(cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(f.Source.Id, cancellation.Token));
        Assert.Equal(1, f.Locks.SourceReleases);
        Assert.Equal(0, f.Source.ConsecutiveFailures);
        using var local = f.Guard.Acquire(f.Source.Id);
    }

    private sealed class CancellingRunner(CancellationTokenSource cancellation) : IJobSourceRunner
    {
        public Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default)
        { cancellation.Cancel(); return Task.FromCanceled<JobSourceRunResult>(cancellationToken); }
    }

    [Fact]
    public void SourceKeyIsDeterministicDomainSeparatedAndUsesSigned64Bits()
    {
        var id = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var expected = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("CareerHarbor:JobSourceExecution:v1:" + id.ToString("D"))));
        Assert.Equal(expected, PostgresJobSourceExecutionLock.CreateLockKey(id));
        Assert.Equal(expected, PostgresJobSourceExecutionLock.CreateLockKey(id));
        Assert.NotEqual(expected, PostgresJobSourceExecutionLock.CreateLockKey(Guid.Empty));
        Assert.DoesNotContain(expected, PostgresExternalJobCreationLock.CreateKeys(id.ToString(), id.ToString()));
        var keys = Enumerable.Range(0, 100).Select(i => PostgresAdvisorySession.Key(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Assert.Contains(keys, x => x < 0);
        Assert.Contains(keys, x => x > 0);
    }

    [Fact]
    public void CreationKeysCoordinateUrlAndFingerprintWithoutGlobalSerialization()
    {
        var first = PostgresExternalJobCreationLock.CreateKeys("https://example.test/1", "fp1");
        var changedMetadata = PostgresExternalJobCreationLock.CreateKeys("https://example.test/1", "fp2");
        var changedUrl = PostgresExternalJobCreationLock.CreateKeys("https://example.test/2", "fp1");
        var different = PostgresExternalJobCreationLock.CreateKeys("https://example.test/3", "fp3");
        Assert.Single(first.Intersect(changedMetadata));
        Assert.Single(first.Intersect(changedUrl));
        Assert.Empty(first.Intersect(different));
        Assert.Equal(first.Order().ToArray(), first);
        Assert.Single(PostgresExternalJobCreationLock.CreateKeys(null, "fp1"));
    }

    [Fact]
    public void SessionConfigurationDisablesPoolingAndRejectsKnownTransactionPooler()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unit;Pooling=true;Multiplexing=true" }).Build();
        var parsed = new NpgsqlConnectionStringBuilder(PostgresAdvisorySession.ConnectionString(config));
        Assert.False(parsed.Pooling);
        Assert.False(parsed.Multiplexing);
        config["ConnectionStrings:DefaultConnection"] = "Host=example-pooler.neon.tech;Database=unit";
        Assert.Throws<InvalidOperationException>(() => PostgresAdvisorySession.ConnectionString(config));
    }

    [Fact]
    public async Task LeaseKeepsSameSessionThenExplicitlyUnlocksAndDisposesOnce()
    {
        var connection = new LockConnection();
        var lease = await PostgresAdvisorySession.AcquireAsync(connection, [123], true, default);
        Assert.NotNull(lease);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(0, connection.Disposals);
        Assert.Equal("SELECT pg_try_advisory_lock(@key);", Assert.Single(connection.Commands).Sql);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(1, connection.Disposals);
        Assert.Equal("SELECT pg_advisory_unlock(@key);", connection.Commands[1].Sql);
        Assert.All(connection.Commands, command => Assert.Equal(123L, command.Key));
        Assert.Equal(5, connection.Commands[1].Timeout);
    }

    [Fact]
    public async Task BusySessionIsClosedAndNoLeaseReturned()
    {
        var connection = new LockConnection { Available = false };
        Assert.Null(await PostgresAdvisorySession.AcquireAsync(connection, [123], true, default));
        Assert.Equal(1, connection.Disposals);
        Assert.Single(connection.Commands);
    }

    [Fact]
    public async Task UnlockFailureStillPhysicallyClosesSession()
    {
        var connection = new LockConnection { FailUnlock = true };
        var lease = await PostgresAdvisorySession.AcquireAsync(connection, [123], true, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease!.DisposeAsync().AsTask());
        Assert.Equal(1, connection.Disposals);
    }

    [Fact]
    public async Task AcquisitionCancellationDisposesSession()
    {
        var connection = new LockConnection { CancelCommand = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgresAdvisorySession.AcquireAsync(connection, [123], true, default));
        Assert.Equal(1, connection.Disposals);
    }

    [Fact]
    public async Task CreationWaitUsesBoundedCommandAndReleasesInReverseOrder()
    {
        var connection = new LockConnection();
        var lease = await PostgresAdvisorySession.AcquireAsync(connection, [1, 2], false, default);
        Assert.All(connection.Commands, c => { Assert.Equal(30, c.Timeout); Assert.Equal("SELECT pg_advisory_lock(@key);", c.Sql); });
        await lease!.DisposeAsync();
        Assert.Equal(new long[] { 1, 2, 2, 1 }, connection.Commands.Select(x => x.Key));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ManualOperationsConflictWithoutChangingSourceOrFailureCount(string operation)
    {
        using var f = new JobSourceFixture();
        f.Locks.Busy = true;
        var exception = await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            if (operation == "run") await f.Service.RunAsync(f.Source.Id);
            else if (operation == "update") await f.Service.UpdateAsync(f.Source.Id, f.Request);
            else await f.Service.DeleteAsync(f.Source.Id);
        });
        Assert.Equal("job_source_busy", exception.Code);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Equal(0, f.Source.ConsecutiveFailures);
        Assert.Null(f.Source.LastRunAtUtc);
        Assert.False(f.Source.IsDeleted);
        Assert.Empty(f.Audit.Events);
        using var local = f.Guard.Acquire(f.Source.Id);
    }

    [Fact]
    public async Task ManualSuccessAndProviderFailureReleaseLeaseAndAuditCounts()
    {
        using var f = new JobSourceFixture();
        await f.Service.RunAsync(f.Source.Id);
        Assert.Equal(1, f.Locks.SourceReleases);
        Assert.Contains(f.Audit.Events, x => x.Metadata?.GetValueOrDefault("received") == "1");
        f.Provider.Exception = new HttpRequestException("sensitive response");
        Assert.False((await f.Service.RunAsync(f.Source.Id)).Succeeded);
        Assert.Equal(2, f.Locks.SourceReleases);
        Assert.Equal(1, f.Source.ConsecutiveFailures);
    }

    [Fact]
    public async Task IndependentSchedulersUseSharedDistributedLockAndSkipWithoutFailure()
    {
        var distributed = new TestAggregationLocks();
        using var first = new SchedulerFixture(executionLock: distributed);
        using var second = new SchedulerFixture(executionLock: distributed);
        first.Store.AddSources(1);
        second.Store.Sources.Add(first.Store.Sources[0]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Store.Run = async (id, token) => { started.SetResult(); await release.Task.WaitAsync(token); return new() { JobSourceId = id, Succeeded = true }; };
        var running = first.Scheduler.RunOnceAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await second.Scheduler.RunOnceAsync();
            Assert.Empty(second.Store.Ran);
            Assert.Contains(second.Logger.Messages, m => m.Contains("distributed execution lock busy", StringComparison.Ordinal));
            Assert.Equal(0, first.Store.Sources[0].ConsecutiveFailures);
        }
        finally { release.TrySetResult(); await running; }
        Assert.Equal(1, distributed.SourceReleases);
    }

    [Fact]
    public async Task SchedulerCancellationReleasesDistributedAndLocalLeases()
    {
        var locks = new TestAggregationLocks();
        using var f = new SchedulerFixture(executionLock: locks);
        f.Store.AddSources(1);
        using var cancellation = new CancellationTokenSource();
        f.Store.Run = (_, _) => { cancellation.Cancel(); return Task.FromCanceled<JobSourceRunResult>(cancellation.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Scheduler.RunOnceAsync(cancellation.Token));
        Assert.Equal(1, locks.SourceReleases);
        using var local = f.Guard.Acquire(f.Store.Sources[0].Id);
    }

    [Theory]
    [InlineData("https://EXAMPLE.test:443/Jobs/1/?b=2&utm_source=x&a=1#section", "https://example.test/Jobs/1?a=1&b=2")]
    [InlineData("https://example.test/1?UTM_MEDIUM=x&job=2", "https://example.test/1?job=2")]
    public async Task IndexedCanonicalLookupMatchesVariantsWithoutChangingActionableUrl(string stored, string lookup)
    {
        using var f = new JobSourceFixture();
        var job = new Job { Company = f.Company, Category = f.Category, Title = "Curated", ApplicationUrl = stored };
        f.Context.Jobs.Add(job);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var match = await new JobRepository(f.Context).FindByExternalUrlAsync(lookup);
        Assert.NotNull(match);
        Assert.Equal(job.Id, match.Id);
        Assert.Equal(stored, match.ApplicationUrl);
        Assert.Equal(ApplicationUrlIdentity.Hash(lookup), match.CanonicalApplicationUrlHash);
        Assert.Null(await new JobRepository(f.Context).FindByExternalUrlAsync(lookup + "&other=3"));
    }

    [Fact]
    public void UnknownQueryMultiplicityCaseAndFlagsAreNotCollapsed()
    {
        var canonicalizer = new UrlCanonicalizer();
        Assert.NotEqual(canonicalizer.Canonicalize("https://e.test/?id=1&id=2"), canonicalizer.Canonicalize("https://e.test/?id=2"));
        Assert.NotEqual(canonicalizer.Canonicalize("https://e.test/?ID=1&id=2"), canonicalizer.Canonicalize("https://e.test/?id=2"));
        Assert.NotEqual(canonicalizer.Canonicalize("https://e.test/?flag"), canonicalizer.Canonicalize("https://e.test/?flag="));
        Assert.Null(ApplicationUrlIdentity.Hash("not-a-url"));
        Assert.Null(ApplicationUrlIdentity.Hash("ftp://example.test/a"));
    }

    [Fact]
    public async Task UrlEditRecomputesHashAndNonUniqueIndexPreservesDistinctOpenings()
    {
        using var f = new JobSourceFixture();
        var job = new Job { Company = f.Company, Category = f.Category, ApplicationUrl = "https://example.test/1" };
        f.Context.Add(job);
        await f.Context.SaveChangesAsync();
        var old = job.CanonicalApplicationUrlHash;
        job.ApplicationUrl = "https://example.test/2";
        await f.Context.SaveChangesAsync();
        Assert.NotEqual(old, job.CanonicalApplicationUrlHash);
        var index = f.Context.Model.FindEntityType(typeof(Job))!.GetIndexes().Single(x => x.Properties.Any(p => p.Name == nameof(Job.CanonicalApplicationUrlHash)));
        Assert.False(index.IsUnique);
    }

    private sealed class LockConnection : DbConnection
    {
        private ConnectionState state;
        public int Disposals { get; private set; }
        public bool Available { get; init; } = true;
        public bool FailUnlock { get; init; }
        public bool CancelCommand { get; init; }
        public List<(string Sql, long Key, int Timeout)> Commands { get; } = [];
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => state;
        public override void Open() => state = ConnectionState.Open;
        public override void Close() => state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new LockCommand(this);
        public override ValueTask DisposeAsync() { Disposals++; Close(); return base.DisposeAsync(); }
    }

    private sealed class LockCommand(LockConnection owner) : DbCommand
    {
        private readonly NpgsqlCommand parameters = new();
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = owner;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        public override object ExecuteScalar()
        {
            owner.Commands.Add((CommandText, (long)parameters.Parameters[0].Value!, CommandTimeout));
            if (owner.CancelCommand) throw new OperationCanceledException();
            if (owner.FailUnlock && CommandText.Contains("unlock", StringComparison.Ordinal)) throw new InvalidOperationException("unlock failed");
            return owner.Available;
        }
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<object?>(ExecuteScalar()); }
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
    }
}
