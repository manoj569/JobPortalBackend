using JobPortal.Application.Abstractions.Jobs;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace JobPortal.Persistence.Postgres;

public sealed class PostgresJobSourceExecutionLock : IJobSourceExecutionLock
{
    private const string KeyPrefix = "CareerHarbor:JobSourceExecution:v1:";
    private readonly string connectionString;

    public PostgresJobSourceExecutionLock(IConfiguration configuration)
    {
        connectionString = PostgresAdvisorySession.ConnectionString(configuration);
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid jobSourceId, CancellationToken cancellationToken = default) =>
        PostgresAdvisorySession.AcquireAsync(new NpgsqlConnection(connectionString),
            [CreateLockKey(jobSourceId)], true, cancellationToken);

    internal static long CreateLockKey(Guid jobSourceId) =>
        PostgresAdvisorySession.Key(KeyPrefix + jobSourceId.ToString("D"));
}
