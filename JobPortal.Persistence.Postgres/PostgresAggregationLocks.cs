using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JobPortal.Persistence.Postgres;

public static class AggregationLockRegistration
{
    public static IServiceCollection AddPostgresAggregationLocks(this IServiceCollection services)
    {
        services.AddSingleton<IJobSourceExecutionLock, PostgresJobSourceExecutionLock>();
        services.AddSingleton<IExternalJobCreationLock, PostgresExternalJobCreationLock>();
        return services;
    }
}

public sealed class PostgresExternalJobCreationLock(IConfiguration configuration) : IExternalJobCreationLock, IExternalJobCreationLockRunFactory, IExternalJobCreationBatchLock
{
    private readonly string connectionString = PostgresAdvisorySession.ConnectionString(configuration);

    public IExternalJobCreationLockRun CreateRun() => new CreationRun(() => new NpgsqlConnection(connectionString));

    public async Task<IAsyncDisposable> AcquireBatchAsync(
        IReadOnlyCollection<(string? Url, string Fingerprint)> identities, CancellationToken cancellationToken = default)
    {
        var keys = identities.SelectMany(x => CreateKeys(x.Url, x.Fingerprint)).Distinct().Order().ToArray();
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            // Ordered, parameterized commands in one protocol batch. The exact same
            // keys/order coordinate with existing individual writers on other instances.
            await using var batch = new NpgsqlBatch(connection) { Timeout = 30 };
            foreach (var key in keys)
            {
                var command = new NpgsqlBatchCommand("SELECT pg_advisory_lock($1);");
                command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = key });
                batch.BatchCommands.Add(command);
            }
            if (keys.Length > 0) await batch.ExecuteNonQueryAsync(cancellationToken);
            // Pooling=false and Multiplexing=false: disposing physically closes this
            // pinned session, releasing every acquired lock even after partial acquisition.
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    internal sealed class CreationRun(Func<DbConnection> connections) : IExternalJobCreationLockRun
    {
        private DbConnection? connection;
        private int active;
        private bool disposed;
        public async Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintHash);
            if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                throw new InvalidOperationException("A creation run may hold only one item lease at a time.");
            try
            {
                if (connection is not null && connection.State != ConnectionState.Open)
                {
                    await connection.DisposeAsync();
                    connection = null;
                }
                connection ??= connections();
                var lease = (await PostgresAdvisorySession.AcquireAsync(connection, CreateKeys(canonicalUrl, fingerprintHash),
                    false, cancellationToken, reuseSession: true))!;
                return new ItemLease(lease, this);
            }
            catch { Interlocked.Exchange(ref active, 0); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            disposed = true;
            if (connection is not null) await connection.DisposeAsync();
            connection = null;
        }

        private sealed class ItemLease(IAsyncDisposable lease, CreationRun run) : IAsyncDisposable
        {
            private int released;
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref released, 1) != 0) return;
                try { await lease.DisposeAsync(); }
                finally { Interlocked.Exchange(ref run.active, 0); }
            }
        }
    }

    public async Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintHash);
        // URL coordinates changed metadata; fingerprint also coordinates different
        // URLs for the same identity. Sort all keys to prevent lock-order deadlocks.
        var keys = CreateKeys(canonicalUrl, fingerprintHash);
        return (await PostgresAdvisorySession.AcquireAsync(new NpgsqlConnection(connectionString), keys, false, cancellationToken))!;
    }

    internal static long[] CreateKeys(string? url, string fingerprint) =>
        (string.IsNullOrEmpty(url)
            ? new[] { PostgresAdvisorySession.Key("CareerHarbor:JobCreation:Fingerprint:v1:" + fingerprint) }
            : new[] { PostgresAdvisorySession.Key("CareerHarbor:JobCreation:Url:v1:" + url),
                PostgresAdvisorySession.Key("CareerHarbor:JobCreation:Fingerprint:v1:" + fingerprint) })
        .Distinct().Order().ToArray();
}

internal static class PostgresAdvisorySession
{
    internal static long Key(string identity) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

    internal static string ConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        // Advisory locks require a pinned session. Physical close is the last-resort
        // release after cancellation/ambiguous acquisition/unlock failure.
        var builder = new NpgsqlConnectionStringBuilder(configured) { Pooling = false, Multiplexing = false };
        if (builder.Host?.Contains("-pooler.", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Aggregation session locks require a direct PostgreSQL endpoint, not a transaction-pooled endpoint.");
        return builder.ConnectionString;
    }

    internal static async Task<IAsyncDisposable?> AcquireAsync(DbConnection connection, long[] keys, bool tryOnly, CancellationToken token,
        bool reuseSession = false)
    {
        try
        {
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(token);
            foreach (var key in keys)
            {
                await using var command = Command(connection, tryOnly ? "pg_try_advisory_lock" : "pg_advisory_lock", key, 30);
                var acquired = await command.ExecuteScalarAsync(token);
                if (tryOnly && acquired is not true)
                {
                    await connection.DisposeAsync();
                    return null;
                }
            }
            return new Lease(connection, keys, !reuseSession);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static DbCommand Command(DbConnection connection, string function, long key, int timeout)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT {function}(@key);";
        command.CommandTimeout = timeout;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = key;
        command.Parameters.Add(parameter);
        return command;
    }

    private sealed class Lease(DbConnection connection, long[] keys, bool disposeConnection) : IAsyncDisposable
    {
        private int disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            var unlocked = false;
            try
            {
                if (connection.State != ConnectionState.Open) return;
                foreach (var key in keys.Reverse())
                {
                    await using var command = Command(connection, "pg_advisory_unlock", key, 5);
                    if (await command.ExecuteScalarAsync(CancellationToken.None) is not true)
                        throw new InvalidOperationException("Advisory lock release was not confirmed.");
                }
                unlocked = true;
            }
            finally { if (disposeConnection || !unlocked) await connection.DisposeAsync(); }
        }
    }
}
