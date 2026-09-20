using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using JobPortal.Maintenance;
using JobPortal.Persistence.Postgres;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AggregationLockMaintenanceTests
{
    [Fact]
    public async Task ExactProductionSessionHelperPassesAgainstIndependentFakeSessions()
    {
        var server = new Server();
        using var output = new StringWriter();
        await AggregationLockTest.VerifyAsync(server.Connect, output, default);
        Assert.Contains("Same-lock exclusion: PASS", output.ToString());
        Assert.Contains("Connection-disposal release: PASS", output.ToString());
        Assert.Contains("Pre-cancelled acquisition cleanup: PASS", output.ToString());
        Assert.Empty(server.Owners);
        Assert.All(server.Connections, c => Assert.Equal(ConnectionState.Closed, c.State));
    }

    [Fact]
    public async Task BrokenExclusionFailsAndStillClosesEverySession()
    {
        var server = new Server { IgnoreExclusion = true };
        using var output = new StringWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => AggregationLockTest.VerifyAsync(server.Connect, output, default));
        Assert.Contains("Same-lock exclusion: FAIL", output.ToString());
        Assert.Empty(server.Owners);
        Assert.All(server.Connections, c => Assert.Equal(ConnectionState.Closed, c.State));
    }

    [Fact]
    public async Task CancellationClosesTheAttemptedSession()
    {
        var server = new Server();
        using var output = new StringWriter();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AggregationLockTest.VerifyAsync(server.Connect, output, new CancellationToken(true)));
        Assert.Empty(server.Owners);
        Assert.All(server.Connections, c => Assert.Equal(ConnectionState.Closed, c.State));
    }

    [Fact]
    public void TestNamespaceUsesExactProductionHashAndDistinctIdentities()
    {
        var run = Guid.Parse("e7f40a5b-4cbe-4628-ab62-c42b1e6b3cd1");
        Assert.Equal(PostgresAdvisorySession.Key($"CareerHarbor:Maintenance:AdvisoryVerification:v1:{run:D}:1"), AggregationLockTest.TestKey(run, 1));
        Assert.NotEqual(AggregationLockTest.TestKey(run, 1), AggregationLockTest.TestKey(run, 2));
        Assert.NotEqual(PostgresJobSourceExecutionLock.CreateLockKey(run), AggregationLockTest.TestKey(run, 1));
        Assert.True(AggregationLockTest.IsCommand(["job-aggregation-lock-test"]));
        Assert.False(AggregationLockTest.IsCommand(["job-aggregation-lock-test", "--apply"]));
    }

    [Fact]
    public void ProductionGuardRejectsPoolerAndDisablesPooling()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = "Host=example-pooler.neon.tech;Database=test" }).Build();
        Assert.Throws<InvalidOperationException>(() => PostgresAdvisorySession.ConnectionString(config));
        config["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Pooling=true;Multiplexing=true";
        var parsed = new NpgsqlConnectionStringBuilder(PostgresAdvisorySession.ConnectionString(config));
        Assert.False(parsed.Pooling);
        Assert.False(parsed.Multiplexing);
    }

    private sealed class Server
    {
        public Dictionary<long, Connection> Owners { get; } = [];
        public List<Connection> Connections { get; } = [];
        public bool IgnoreExclusion { get; init; }
        public DbConnection Connect() { var c = new Connection(this); Connections.Add(c); return c; }
    }

    private sealed class Connection(Server server) : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "fake";
        public override ConnectionState State => state;
        public override void Open() => state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
        public override void Close()
        {
            foreach (var key in server.Owners.Where(p => ReferenceEquals(p.Value, this)).Select(p => p.Key).ToArray()) server.Owners.Remove(key);
            state = ConnectionState.Closed;
        }
        public override async ValueTask DisposeAsync() { Close(); await base.DisposeAsync(); }
        protected override void Dispose(bool disposing) { if (disposing) Close(); base.Dispose(disposing); }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new Command(this, server);
    }

    private sealed class Command(Connection connection, Server server) : DbCommand
    {
        private readonly NpgsqlCommand parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        public override object ExecuteScalar()
        {
            var key = (long)parameters.Parameters[0].Value!;
            if (CommandText == "SELECT pg_try_advisory_lock(@key);")
            {
                if (server.Owners.ContainsKey(key) && !server.IgnoreExclusion) return false;
                server.Owners[key] = connection;
                return true;
            }
            Assert.Equal("SELECT pg_advisory_unlock(@key);", CommandText);
            return server.Owners.TryGetValue(key, out var owner) && ReferenceEquals(owner, connection) && server.Owners.Remove(key);
        }
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
    }
}
