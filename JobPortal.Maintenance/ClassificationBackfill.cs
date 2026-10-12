using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using Npgsql;
using NpgsqlTypes;

namespace JobPortal.Maintenance;

public sealed record ClassificationBackfillOptions(bool Apply, int BatchSize, int MaxBatches, string Checkpoint)
{
    public static ClassificationBackfillOptions Parse(string[] args)
    {
        var apply = false; var size = 100; var batches = 10; string? checkpoint = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            var option = args[i];
            if (!seen.Add(option)) throw new ArgumentException("Repeated option.");
            if (option == "--apply") { apply = true; continue; }
            if (++i >= args.Length) throw new ArgumentException("Missing option value.");
            switch (option)
            {
                case "--batch-size" when int.TryParse(args[i], out size) && size is >= 1 and <= 500: break;
                case "--max-batches" when int.TryParse(args[i], out batches) && batches is >= 1 and <= 1000: break;
                case "--checkpoint": checkpoint = Path.GetFullPath(args[i]); break;
                default: throw new ArgumentException("Unknown or invalid option.");
            }
        }
        if (checkpoint is null) throw new ArgumentException("--checkpoint <local path> is required. Dry-run and apply need separate files.");
        return new(apply, size, batches, checkpoint);
    }
}

public sealed class ClassificationProgress
{
    public string Scope { get; set; } = "";
    public DateTime SnapshotAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? Cursor { get; set; }
    public Guid? Upper { get; set; }
    public long Total { get; set; }
    public long Analyzed { get; set; }
    public long Proposed { get; set; }
    public long Applied { get; set; }
    public long Unchanged { get; set; }
    public long Conflicts { get; set; }
    public long Failures { get; set; }
    public HashSet<Guid> RetryIds { get; set; } = [];
    public HashSet<Guid> UncountedRetryAnalyses { get; set; } = [];
    public long Remaining => Math.Max(0, Total - Analyzed) + RetryIds.Count;
}
public sealed record ClassificationRow(Job Job, string Revision)
{
    public string OriginalValues { get; } = ClassificationBackfill.Values(Job);
    public string? OriginalClassification { get; } = Job.ClassificationJson;
}
public interface IClassificationBackfillStore
{
    Task<(long Total, Guid? Upper)> InventoryAsync(CancellationToken token);
    Task<IReadOnlyList<ClassificationRow>> ReadAsync(Guid? after, Guid upper, int size, CancellationToken token);
    Task<ClassificationRow?> ReadOneAsync(Guid id, CancellationToken token);
    Task<bool> UpdateAsync(ClassificationRow row, CancellationToken token);
}

public static class ClassificationBackfill
{
    public static string Values(Job job) => JsonSerializer.Serialize(new
    { job.EmploymentType, job.WorkplaceType, job.MinimumExperienceYears, job.MaximumExperienceYears,
        job.MinimumSalary, job.MaximumSalary, job.CurrencyCode, job.SalaryPeriod });

    public static async Task RunAsync(IClassificationBackfillStore store, ClassificationBackfillOptions options,
        ClassificationProgress progress, Func<object, Task> audit, Func<Task> checkpoint, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress); ArgumentNullException.ThrowIfNull(audit); ArgumentNullException.ThrowIfNull(checkpoint);
        if (progress.Upper is null && progress.Total == 0)
        { var inventory = await store.InventoryAsync(token); progress.Total = inventory.Total; progress.Upper = inventory.Upper; await checkpoint(); }
        async Task Process(ClassificationRow row, bool retry)
        {
            token.ThrowIfCancellationRequested();
            var before = Values(row.Job); var metadataBefore = row.Job.ClassificationJson;
            var analysisCounted = false;
            try
            {
                var result = JobClassificationPolicy.Analyze(row.Job);
                JobClassificationPolicy.Apply(row.Job, result, historical: true);
                var after = Values(row.Job);
                var proposed = before != after;
                var reviewFields = JsonSerializer.Deserialize<JobClassificationResult>(row.Job.ClassificationJson!)!.ReviewFields;
                var hasReview = reviewFields.Count > 0;
                if (!retry || progress.UncountedRetryAnalyses.Contains(row.Job.Id))
                {
                    if (proposed) progress.Proposed++; else progress.Unchanged++;
                    if (hasReview) progress.Conflicts++;
                    progress.UncountedRetryAnalyses.Remove(row.Job.Id);
                }
                analysisCounted = true;
                var applied = false;
                var oldFields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(before)!;
                var newFields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(after)!;
                var changedFields = oldFields.Keys.Where(key => oldFields[key].GetRawText() != newFields[key].GetRawText()).ToArray();
                var attemptId = Guid.NewGuid();
                // Record original values before a database write; interrupted outcomes can be reconciled by attempt ID.
                await audit(new { AttemptId = attemptId, Phase = "Intent", row.Job.Id, row.Job.JobSourceId, row.Job.ExternalJobId,
                    Mode = options.Apply ? "Apply" : "DryRun", Before = JsonSerializer.Deserialize<JsonElement>(before),
                    After = JsonSerializer.Deserialize<JsonElement>(after), ChangedFields = changedFields,
                    Proposed = proposed, result.Evidence, result.Conflicts, ReviewFields = reviewFields });
                if (options.Apply && (proposed || metadataBefore != row.Job.ClassificationJson))
                {
                    applied = await store.UpdateAsync(row, token);
                    if (applied && proposed) progress.Applied++;
                    if (!applied) { progress.RetryIds.Add(row.Job.Id); progress.Conflicts++; }
                }
                if (!options.Apply || applied || !proposed && metadataBefore == row.Job.ClassificationJson) progress.RetryIds.Remove(row.Job.Id);
                await audit(new { AttemptId = attemptId, Phase = "Outcome", row.Job.Id, row.Job.JobSourceId, row.Job.ExternalJobId, Mode = options.Apply ? "Apply" : "DryRun",
                    Before = JsonSerializer.Deserialize<JsonElement>(before), After = JsonSerializer.Deserialize<JsonElement>(after), ChangedFields = changedFields,
                    Proposed = proposed, Applied = applied, result.Evidence, result.Conflicts, Review = hasReview, ReviewFields = reviewFields });
                if (!retry) progress.Analyzed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                progress.Failures++; progress.RetryIds.Add(row.Job.Id);
                if (!retry && !analysisCounted) progress.UncountedRetryAnalyses.Add(row.Job.Id);
                if (!retry) progress.Analyzed++;
                await audit(new { row.Job.Id, Failure = ex.GetType().Name });
            }
        }
        foreach (var id in progress.RetryIds.Take(options.BatchSize).ToArray())
        {
            var row = await store.ReadOneAsync(id, token);
            if (row is null) { progress.RetryIds.Remove(id); progress.UncountedRetryAnalyses.Remove(id); } else await Process(row, true);
            await checkpoint();
        }
        if (progress.Upper is null) return;
        for (var batch = 0; batch < options.MaxBatches; batch++)
        {
            var rows = await store.ReadAsync(progress.Cursor, progress.Upper.Value, options.BatchSize, token);
            if (rows.Count == 0) break;
            foreach (var row in rows)
            {
                await Process(row, false);
                progress.Cursor = row.Job.Id;
                await checkpoint();
            }
        }
    }
}

public sealed class PostgresClassificationBackfillStore(NpgsqlConnection connection, DateTime snapshotAtUtc) : IClassificationBackfillStore
{
    public async Task<(long Total, Guid? Upper)> InventoryAsync(CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT count(*), (SELECT \"Id\" FROM \"Jobs\" WHERE \"CreatedAtUtc\" <= @snapshot ORDER BY \"Id\" DESC LIMIT 1) FROM \"Jobs\" WHERE \"CreatedAtUtc\" <= @snapshot", connection);
        command.Parameters.AddWithValue("snapshot", snapshotAtUtc);
        await using var reader = await command.ExecuteReaderAsync(token); await reader.ReadAsync(token);
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
    public async Task<IReadOnlyList<ClassificationRow>> ReadAsync(Guid? after, Guid upper, int size, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT (to_jsonb(j) || jsonb_build_object('SourceMetadataJson', j.\"SourceMetadataJson\"::text))::text, md5(row_to_json(j)::text) FROM \"Jobs\" j WHERE (@after IS NULL OR j.\"Id\" > @after) AND j.\"Id\" <= @upper AND j.\"CreatedAtUtc\" <= @snapshot ORDER BY j.\"Id\" LIMIT @size", connection);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("snapshot", snapshotAtUtc); command.Parameters.AddWithValue("upper", upper); command.Parameters.AddWithValue("size", size);
        return await Read(command, token);
    }
    public async Task<ClassificationRow?> ReadOneAsync(Guid id, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT (to_jsonb(j) || jsonb_build_object('SourceMetadataJson', j.\"SourceMetadataJson\"::text))::text, md5(row_to_json(j)::text) FROM \"Jobs\" j WHERE j.\"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", id); return (await Read(command, token)).SingleOrDefault();
    }
    private static async Task<IReadOnlyList<ClassificationRow>> Read(NpgsqlCommand command, CancellationToken token)
    {
        var rows = new List<ClassificationRow>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(JsonSerializer.Deserialize<Job>(reader.GetString(0))!, reader.GetString(1)));
        return rows;
    }
    public async Task<bool> UpdateAsync(ClassificationRow row, CancellationToken token)
    {
        // Hard-coded field whitelist. Only changed classification columns are updated.
        var job = row.Job;
        var original = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.OriginalValues)!;
        var fields = new (string Name, NpgsqlDbType Type, object? Value)[]
        {
            ("EmploymentType", NpgsqlDbType.Integer, (int)job.EmploymentType),
            ("WorkplaceType", NpgsqlDbType.Integer, (int)job.WorkplaceType),
            ("MinimumExperienceYears", NpgsqlDbType.Integer, job.MinimumExperienceYears),
            ("MaximumExperienceYears", NpgsqlDbType.Integer, job.MaximumExperienceYears),
            ("MinimumSalary", NpgsqlDbType.Numeric, job.MinimumSalary),
            ("MaximumSalary", NpgsqlDbType.Numeric, job.MaximumSalary),
            ("CurrencyCode", NpgsqlDbType.Text, job.CurrencyCode),
            ("SalaryPeriod", NpgsqlDbType.Integer, job.SalaryPeriod is null ? null : (int)job.SalaryPeriod),
            ("ClassificationJson", NpgsqlDbType.Text, job.ClassificationJson)
        };
        await using var command = new NpgsqlCommand { Connection = connection };
        var assignments = new List<string>();
        foreach (var field in fields)
        {
            var changed = field.Name == "ClassificationJson" ? row.OriginalClassification != job.ClassificationJson :
                original[field.Name].GetRawText() != JsonSerializer.Serialize(field.Value);
            if (!changed) continue;
            var parameter = "p" + assignments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            assignments.Add("\"" + field.Name + "\"=@" + parameter);
            command.Parameters.AddWithValue(parameter, field.Type, field.Value ?? DBNull.Value);
        }
        if (assignments.Count == 0) return true;
        command.CommandText = "UPDATE \"Jobs\" j SET " + string.Join(",", assignments) +
            " WHERE \"Id\"=@id AND md5(row_to_json(j)::text)=@revision";
        command.Parameters.AddWithValue("id", job.Id); command.Parameters.AddWithValue("revision", row.Revision);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }
}

public static class ClassificationBackfillCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is [_, "--help"])
        { Console.WriteLine("job-classification-backfill --checkpoint <local file> [--apply] [--batch-size 1..500] [--max-batches 1..1000]\nDefault: dry-run; 100 jobs/batch, 10 batches. Resume using the same checkpoint. Use separate dry-run/apply files. Requires an explicitly configured ConnectionStrings__DefaultConnection. Does not run ingestion or migrations."); return 0; }
        try
        {
            var options = ClassificationBackfillOptions.Parse(args);
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? throw new ArgumentException("Missing explicit database connection.");
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { IncludeErrorDetail = false, LogParameters = false };
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            // An exclusive local lease prevents two processes sharing a cursor.
            await using var lease = new FileStream(options.Checkpoint + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellation.Token);
            await using var relation = new NpgsqlCommand("SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.oid=to_regclass('\"Jobs\"')", connection);
            var table = await relation.ExecuteScalarAsync(cancellation.Token) as string ?? throw new ArgumentException("Jobs table is missing.");
            var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{builder.Host}|{builder.Port}|{builder.Database}|{table}|{options.Apply}|jd-rules-v1|backfill-v2")));
            var progress = File.Exists(options.Checkpoint) ? JsonSerializer.Deserialize<ClassificationProgress>(await File.ReadAllTextAsync(options.Checkpoint, cancellation.Token))! : new ClassificationProgress { Scope = scope };
            if (progress.Scope != scope) throw new ArgumentException("Checkpoint database/table/mode/rules version mismatch.");
            if (!options.Apply)
            { await using var readOnly = new NpgsqlCommand("SET default_transaction_read_only = on", connection); await readOnly.ExecuteNonQueryAsync(cancellation.Token); }
            async Task Save()
            {
                await File.WriteAllTextAsync(options.Checkpoint + ".tmp", JsonSerializer.Serialize(progress), cancellation.Token);
                File.Move(options.Checkpoint + ".tmp", options.Checkpoint, overwrite: true);
            }
            async Task Audit(object item)
            {
                await using var stream = new FileStream(options.Checkpoint + ".audit.jsonl", FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(item).AsMemory(), cancellation.Token);
                await writer.FlushAsync(cancellation.Token);
                stream.Flush(flushToDisk: true);
            }
            await ClassificationBackfill.RunAsync(new PostgresClassificationBackfillStore(connection, progress.SnapshotAtUtc), options, progress,
                Audit, Save, cancellation.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { TotalJobs = progress.Total, AnalyzedJobs = progress.Analyzed, ProposedCorrections = progress.Proposed,
                AppliedCorrections = progress.Applied, UnchangedJobs = progress.Unchanged, progress.Conflicts, progress.Failures, RemainingJobs = progress.Remaining,
                progress.Cursor, RetryJobs = progress.RetryIds.Count })); return progress.RetryIds.Count == 0 ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Interrupted; rerun with the same checkpoint. Last uncheckpointed item may repeat safely."); return 130; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { Console.Error.WriteLine("Classification backfill failed (" + ex.GetType().Name + "). Check arguments, schema and local file permissions securely. No connection details are logged."); return 1; }
    }
}
