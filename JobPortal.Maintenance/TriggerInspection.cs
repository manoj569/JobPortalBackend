using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;

namespace JobPortal.Maintenance;

public static partial class TriggerInspection
{
    public const string QuerySql = """
        SELECT t.tgname,
            CASE WHEN (t.tgtype & 2) <> 0 THEN 'BEFORE'
                 WHEN (t.tgtype & 64) <> 0 THEN 'INSTEAD OF' ELSE 'AFTER' END,
            concat_ws(', ', CASE WHEN (t.tgtype & 4) <> 0 THEN 'INSERT' END,
                CASE WHEN (t.tgtype & 8) <> 0 THEN 'DELETE' END,
                CASE WHEN (t.tgtype & 16) <> 0 THEN 'UPDATE' END,
                CASE WHEN (t.tgtype & 32) <> 0 THEN 'TRUNCATE' END),
            CASE t.tgenabled WHEN 'O' THEN 'Enabled (origin/local)'
                WHEN 'D' THEN 'Disabled' WHEN 'R' THEN 'Enabled (replica)'
                WHEN 'A' THEN 'Enabled (always)' ELSE 'Unknown' END,
            pg_catalog.pg_get_triggerdef(t.oid, true)
        FROM pg_catalog.pg_trigger AS t
        WHERE t.tgrelid = @table AND NOT t.tgisinternal
        ORDER BY t.tgname
        """;

    public static bool IsCommand(string[] args) => args is ["job-url-hash-backfill", "--check-triggers"];

    // Trigger arguments/WHEN literals can contain secrets. Preserve SQL structure, not literal values.
    [GeneratedRegex("'(?:''|\\\\.|[^'\\\\])*'", RegexOptions.CultureInvariant)]
    private static partial Regex SqlLiterals();

    public static string Format(string name, string timing, string events, string enabled, string definition) =>
        $"Trigger: {name}\nTiming/events: {timing} {events}\nState: {enabled}\nDefinition (string literals redacted): {SqlLiterals().Replace(definition, "'[REDACTED]'")}\n";

    public static void WriteEmpty(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.WriteLine("No user-defined triggers found on Jobs");
    }

    public static async Task<int> RunAsync(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var value = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(value))
        {
            await error.WriteLineAsync("Missing ConnectionStrings__DefaultConnection.");
            return 2;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value)
            {
                Pooling = false, Multiplexing = false, IncludeErrorDetail = false, LogParameters = false,
                CommandTimeout = 30
            };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellation.Token);
            // Only SELECT statements; no backfill store, writes, DDL or session SET commands.
            await using var resolve = new NpgsqlCommand("SELECT pg_catalog.to_regclass('\"Jobs\"')::oid", connection);
            var table = await resolve.ExecuteScalarAsync(cancellation.Token);
            if (table is null or DBNull)
            {
                await error.WriteLineAsync("Jobs table not found on the current search path; trigger inspection failed.");
                return 1;
            }
            await using var query = new NpgsqlCommand(QuerySql, connection);
            query.Parameters.AddWithValue("table", NpgsqlTypes.NpgsqlDbType.Oid, Convert.ToUInt32(table, CultureInfo.InvariantCulture));
            await using var reader = await query.ExecuteReaderAsync(cancellation.Token);
            var found = false;
            while (await reader.ReadAsync(cancellation.Token))
            {
                found = true;
                await output.WriteLineAsync(Format(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
            if (!found) WriteEmpty(output);
            return 0;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("Trigger inspection cancelled."); return 130; }
        catch (Exception) { await error.WriteLineAsync("Trigger inspection failed. Check connection, catalog permissions and schema securely."); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
