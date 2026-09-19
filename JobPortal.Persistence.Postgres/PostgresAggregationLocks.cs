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

public sealed class PostgresExternalJobCreationLock(IConfiguration configuration) : IExternalJobCreationLock
{
    private readonly string connectionString = PostgresAdvisorySession.ConnectionString(configuration);

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

    internal static async Task<IAsyncDisposable?> AcquireAsync(DbConnection connection, long[] keys, bool tryOnly, CancellationToken token)
    {
        try
        {
            await connection.OpenAsync(token);
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
            return new Lease(connection, keys);
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

    private sealed class Lease(DbConnection connection, long[] keys) : IAsyncDisposable
    {
        private int disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try
            {
                if (connection.State != ConnectionState.Open) return;
                foreach (var key in keys.Reverse())
                {
                    await using var command = Command(connection, "pg_advisory_unlock", key, 5);
                    await command.ExecuteScalarAsync(CancellationToken.None);
                }
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
