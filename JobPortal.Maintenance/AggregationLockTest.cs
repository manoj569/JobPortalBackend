using System.Data.Common;
using JobPortal.Persistence.Postgres;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace JobPortal.Maintenance;

public static class AggregationLockTest
{
    public static bool IsCommand(string[] args) => args is ["job-aggregation-lock-test"];

    public static long TestKey(Guid runId, int identity) =>
        PostgresAdvisorySession.Key($"CareerHarbor:Maintenance:AdvisoryVerification:v1:{runId:D}:{identity}");

    public static async Task VerifyAsync(Func<DbConnection> connectionFactory, TextWriter output, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(output);
        var runId = Guid.NewGuid();
        var first = TestKey(runId, 1);
        var second = TestKey(runId, 2);
        if (first == second) throw new InvalidOperationException("Test identity collision.");

        await using (var a = await Acquire(first))
        {
            Require(a is not null, "First acquisition");
            await using var b = await Acquire(first);
            Require(b is null, "Same-lock exclusion");
            await using var different = await Acquire(second);
            Require(different is not null, "Different-lock concurrency");
        }
        await using (var reacquired = await Acquire(first))
            Require(reacquired is not null, "Release/reacquisition");

        // Close the owning physical connection WITHOUT calling the lease's unlock first.
        await using (var connection = connectionFactory())
        await using (var lease = await PostgresAdvisorySession.AcquireAsync(connection, [first], true, token))
        {
            if (lease is null) throw new InvalidOperationException("Disposal setup failed.");
            await connection.DisposeAsync();
            await using var afterClose = await Acquire(first);
            Require(afterClose is not null, "Connection-disposal release");
        }
        // Exercise production cleanup with a cancelled acquisition, then prove reacquisition.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var observedCancellation = false;
        try
        {
            await using var unexpected = await PostgresAdvisorySession.AcquireAsync(connectionFactory(), [first], true, cancelled.Token);
        }
        catch (OperationCanceledException) { observedCancellation = true; }
        await using (var afterCancellation = await Acquire(first))
            Require(observedCancellation && afterCancellation is not null, "Pre-cancelled acquisition cleanup");

        Task<IAsyncDisposable?> Acquire(long key) =>
            PostgresAdvisorySession.AcquireAsync(connectionFactory(), [key], true, token);
        void Require(bool passed, string label)
        {
            output.WriteLine($"{label}: {(passed ? "PASS" : "FAIL")}");
            if (!passed) throw new InvalidOperationException("Advisory verification failed.");
        }
    }

    public static async Task<int> RunAsync(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var value = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(value))
        {
            await error.WriteLineAsync("Missing ConnectionStrings__DefaultConnection. Overall advisory-lock verification: FAIL");
            return 2;
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = value }).Build();
            // Exact production endpoint validation, disabled pooling and multiplexing.
            var connectionString = PostgresAdvisorySession.ConnectionString(config);
            await VerifyAsync(() => new NpgsqlConnection(connectionString), output, cancellation.Token);
            await output.WriteLineAsync("Direct/session endpoint: PASS (production host guard and session checks passed)");
            await output.WriteLineAsync("Overall advisory-lock verification: PASS");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Overall advisory-lock verification: FAIL (cancelled/timed out)");
            return 130;
        }
        catch (Exception)
        {
            await error.WriteLineAsync("Overall advisory-lock verification: FAIL. Check direct endpoint, connectivity and advisory-lock permissions securely.");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
