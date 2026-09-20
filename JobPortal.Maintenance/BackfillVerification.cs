using System.Data;
using JobPortal.Application.Abstractions.Jobs;
using Npgsql;

namespace JobPortal.Maintenance;

public sealed class VerificationReport
{
    public long Total { get; private set; }
    public long Active { get; private set; }
    public long Populated { get; private set; }
    public long ActivePopulated { get; private set; }
    public long ActiveNullInvalid { get; private set; }
    public long ActiveNullEligible { get; private set; }
    public long Mismatches { get; private set; }
    public long DuplicateValues { get; set; }
    public long JobsWithReferrals { get; set; }
    public long JobsWithContacts { get; set; }
    public long JobsWithApplications { get; set; }
    public long OrphanReferrals { get; set; }
    public long OrphanContacts { get; set; }
    public long OrphanApplications { get; set; }
    public SortedDictionary<int, long> StatusCounts { get; } = [];
    public bool Passed => ActiveNullEligible == 0 && Mismatches == 0 && DuplicateValues == 0 &&
        OrphanReferrals == 0 && OrphanContacts == 0 && OrphanApplications == 0;

    public void Observe(bool deleted, int status, string? url, string? storedHash)
    {
        Total++;
        if (!deleted) Active++;
        StatusCounts[status] = StatusCounts.GetValueOrDefault(status) + 1;
        var expected = ApplicationUrlIdentity.Hash(url);
        if (storedHash is not null)
        {
            Populated++;
            if (!deleted) ActivePopulated++;
            if (!string.Equals(storedHash, expected, StringComparison.Ordinal)) Mismatches++;
        }
        else if (!deleted)
        {
            if (expected is null) ActiveNullInvalid++;
            else ActiveNullEligible++;
        }
    }

    public void Write(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.WriteLine($"Total Jobs: {Total}\nActive Jobs: {Active}\nSoft-deleted Jobs: {Total - Active}\nJobs with hash populated: {Populated}\nActive Jobs with hash populated: {ActivePopulated}\nActive Jobs with NULL hash: {ActiveNullInvalid + ActiveNullEligible}\nActive NULL hashes, invalid/empty URLs: {ActiveNullInvalid}\nActive NULL hashes, eligible URLs: {ActiveNullEligible}\nPopulated hash mismatches (all Jobs): {Mismatches}\nActive duplicate hash values (groups): {DuplicateValues}");
        foreach (var status in StatusCounts) output.WriteLine($"Jobs with Status {status.Key}: {status.Value}");
        output.WriteLine($"Jobs with referrals: {JobsWithReferrals}\nJobs with recruiter contacts: {JobsWithContacts}\nJobs with applications: {JobsWithApplications}\nOrphan JobReferrals: {OrphanReferrals}\nOrphan JobRecruiterContacts: {OrphanContacts}\nOrphan JobApplications: {OrphanApplications}");
        output.WriteLine($"No eligible active NULL hashes remain: {Result(ActiveNullEligible == 0)}\nAll populated hashes match current canonicalization: {Result(Mismatches == 0)}\nNo unexpected duplicate active canonical hashes: {Result(DuplicateValues == 0)}\nRelational integrity checks: {Result(OrphanReferrals == 0 && OrphanContacts == 0 && OrphanApplications == 0)}\nOverall: {Result(Passed)}");
    }

    private static string Result(bool passed) => passed ? "PASS" : "FAIL";
}

public static class BackfillVerification
{
    public const string JobsSql = """
        SELECT "IsDeleted", "Status", "ApplicationUrl", "CanonicalApplicationUrlHash" FROM "Jobs"
        """;
    public const string IntegritySql = """
        SELECT
          (SELECT count(*) FROM (SELECT "CanonicalApplicationUrlHash" FROM "Jobs"
            WHERE "IsDeleted" = FALSE AND "CanonicalApplicationUrlHash" IS NOT NULL
            GROUP BY "CanonicalApplicationUrlHash" HAVING count(*) > 1) AS duplicates),
          (SELECT count(*) FROM "Jobs" j WHERE EXISTS (SELECT 1 FROM "JobReferrals" r WHERE r."JobId" = j."Id")),
          (SELECT count(*) FROM "Jobs" j WHERE EXISTS (SELECT 1 FROM "JobRecruiterContacts" r WHERE r."JobId" = j."Id")),
          (SELECT count(*) FROM "Jobs" j WHERE EXISTS (SELECT 1 FROM "JobApplications" r WHERE r."JobId" = j."Id")),
          (SELECT count(*) FROM "JobReferrals" r WHERE NOT EXISTS (SELECT 1 FROM "Jobs" j WHERE j."Id" = r."JobId")),
          (SELECT count(*) FROM "JobRecruiterContacts" r WHERE NOT EXISTS (SELECT 1 FROM "Jobs" j WHERE j."Id" = r."JobId")),
          (SELECT count(*) FROM "JobApplications" r WHERE NOT EXISTS (SELECT 1 FROM "Jobs" j WHERE j."Id" = r."JobId"))
        """;

    public static bool IsCommand(string[] args) => args is ["job-url-hash-backfill", "--verify"];

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
                CommandTimeout = 120
            };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellation.Token);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellation.Token);
            // row_security=off fails rather than silently returning a policy-filtered subset;
            // it does not grant permission to bypass row-level security.
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL row_security = off", connection, transaction))
                await readOnly.ExecuteNonQueryAsync(cancellation.Token);
            var report = new VerificationReport();
            // Streaming keeps client memory bounded; every count uses the same read-only snapshot.
            await using (var jobs = new NpgsqlCommand(JobsSql, connection, transaction))
            await using (var reader = await jobs.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellation.Token))
                while (await reader.ReadAsync(cancellation.Token))
                    report.Observe(reader.GetBoolean(0), reader.GetInt32(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
            await using (var integrity = new NpgsqlCommand(IntegritySql, connection, transaction))
            await using (var reader = await integrity.ExecuteReaderAsync(cancellation.Token))
            {
                await reader.ReadAsync(cancellation.Token);
                report.DuplicateValues = reader.GetInt64(0);
                report.JobsWithReferrals = reader.GetInt64(1);
                report.JobsWithContacts = reader.GetInt64(2);
                report.JobsWithApplications = reader.GetInt64(3);
                report.OrphanReferrals = reader.GetInt64(4);
                report.OrphanContacts = reader.GetInt64(5);
                report.OrphanApplications = reader.GetInt64(6);
            }
            report.Write(output);
            return report.Passed ? 0 : 1;
        }
        catch (OperationCanceledException) { await error.WriteLineAsync("FAIL: verification cancelled; no complete verification result."); return 130; }
        catch (Exception) { await error.WriteLineAsync("FAIL: verification incomplete. Check connection, table visibility, schema and SELECT permissions securely. Relational checks cannot be marked PASS."); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
