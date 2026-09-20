using JobPortal.Application.Abstractions.Jobs;

namespace JobPortal.Maintenance;

public sealed record BackfillOptions(bool Apply, int BatchSize)
{
    public static BackfillOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "job-url-hash-backfill")
            throw new ArgumentException("Expected command: job-url-hash-backfill. Use --help.");
        var apply = false;
        var size = 500;
        var seenSize = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) apply = true;
            else if (args[i] == "--batch-size" && !seenSize && ++i < args.Length &&
                int.TryParse(args[i], out size) && size is >= 1 and <= 5000) seenSize = true;
            else throw new ArgumentException("Unknown, repeated or invalid option. Batch size must be 1–5000. Use --help.");
        }
        return new(apply, size);
    }
}

public sealed record BackfillCandidate(Guid Id, string? ApplicationUrl);
public sealed record BackfillUpdate(Guid Id, string ApplicationUrl, string Hash);
public sealed record BackfillCounts(long Total, long NullHashes, long Populated, long ActiveNullHashes);
public sealed class BackfillReport
{
    public long Scanned { get; internal set; }
    public long Eligible { get; internal set; }
    public long Invalid { get; internal set; }
    public long Updated { get; internal set; }
    public long Conflicts { get; internal set; }
    public int Batches { get; internal set; }
    public int Failures { get; internal set; }
}

public interface IBackfillStore
{
    Task<BackfillCounts> CountAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BackfillCandidate>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken);
    Task<int> UpdateBatchAsync(IReadOnlyList<BackfillUpdate> updates, CancellationToken cancellationToken);
}

public static class Backfill
{
    public static async Task RunAsync(IBackfillStore store, BackfillOptions options, BackfillReport report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);
        if (options.BatchSize is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(options));
        Guid? after = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = await store.ReadAsync(after, options.BatchSize, cancellationToken);
                if (rows.Count == 0) break;
                var updates = new List<BackfillUpdate>(rows.Count);
                foreach (var row in rows)
                {
                    report.Scanned++;
                    var hash = ApplicationUrlIdentity.Hash(row.ApplicationUrl);
                    if (hash is null) report.Invalid++;
                    else { report.Eligible++; updates.Add(new(row.Id, row.ApplicationUrl!, hash)); }
                }
                if (options.Apply && updates.Count > 0)
                {
                    var affected = await store.UpdateBatchAsync(updates, cancellationToken);
                    report.Updated += affected;
                    report.Conflicts += updates.Count - affected;
                }
                report.Batches++;
                after = rows[^1].Id;
            }
        }
        catch { report.Failures++; throw; }
    }
}
