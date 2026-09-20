using JobPortal.Maintenance;
using Npgsql;

if (args is ["--help"] or ["job-url-hash-backfill", "--help"])
{
    Console.WriteLine("job-aggregation-lock-test (isolated advisory-lock verification; no business writes)");
    Console.WriteLine("job-url-hash-backfill --verify (read-only post-backfill verification; exclusive option)");
    Console.WriteLine("job-url-hash-backfill [--apply] [--batch-size 1..5000]\njob-url-hash-backfill --check-triggers (read-only; cannot combine with other options)\nDefault: read-only dry-run, batch size 500. --apply explicitly permits hash-only writes.\nRequires ConnectionStrings__DefaultConnection and a direct PostgreSQL endpoint.");
    return 0;
}

if (TriggerInspection.IsCommand(args))
    return await TriggerInspection.RunAsync(Console.Out, Console.Error);

if (BackfillVerification.IsCommand(args))
    return await BackfillVerification.RunAsync(Console.Out, Console.Error);

if (AggregationLockTest.IsCommand(args))
    return await AggregationLockTest.RunAsync(Console.Out, Console.Error);

BackfillOptions options;
try { options = BackfillOptions.Parse(args); }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing ConnectionStrings__DefaultConnection.");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var report = new BackfillReport();
try
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        Pooling = false, Multiplexing = false, IncludeErrorDetail = false, LogParameters = false
    };
    if (builder.Host?.Contains("-pooler.", StringComparison.OrdinalIgnoreCase) == true)
    {
        Console.Error.WriteLine("Use a direct PostgreSQL endpoint, not a Neon pooler endpoint.");
        return 2;
    }
    await using var connection = new NpgsqlConnection(builder.ConnectionString);
    await connection.OpenAsync(cancellation.Token);
    if (!options.Apply)
    {
        // Server-side defense in depth; the dry-run code path never calls UpdateBatchAsync.
        await using var readOnly = new NpgsqlCommand("SET default_transaction_read_only = on", connection);
        await readOnly.ExecuteNonQueryAsync(cancellation.Token);
    }
    var store = new PostgresBackfillStore(connection);
    var counts = await store.CountAsync(cancellation.Token);
    Console.WriteLine($"Mode: {(options.Apply ? "APPLY" : "DRY-RUN")}\nTotal Jobs: {counts.Total}\nNULL hashes (all Jobs): {counts.NullHashes}\nAlready populated: {counts.Populated}\nActive NULL hashes targeted: {counts.ActiveNullHashes}\nNot targeted: {counts.Total - counts.ActiveNullHashes}");
    await Backfill.RunAsync(store, options, report, cancellation.Token);
    return 0;
}
catch (OperationCanceledException) { report.Failures = Math.Max(1, report.Failures); Console.Error.WriteLine("Cancelled. Any uncommitted batch was rolled back; rerun to finish."); return 130; }
catch (Exception) { report.Failures = Math.Max(1, report.Failures); Console.Error.WriteLine("Backfill failed. Any uncommitted batch was rolled back. Check configuration, permissions and schema securely; rerun after correction."); return 1; }
finally
{
    Console.WriteLine($"Scanned: {report.Scanned}\nEligible URLs: {report.Eligible}\nWould update: {(options.Apply ? 0 : report.Eligible)}\nUpdated (committed): {report.Updated}\nInvalid/empty skipped: {report.Invalid}\nConcurrent changes skipped: {report.Conflicts}\nBatches completed: {report.Batches}\nFailures: {report.Failures}");
}
