using Npgsql;
using NpgsqlTypes;

namespace JobPortal.Maintenance;

public sealed class PostgresBackfillStore(NpgsqlConnection connection) : IBackfillStore
{
    public const string UpdateSql = """
        UPDATE "Jobs" SET "CanonicalApplicationUrlHash" = @hash
        WHERE "Id" = @id AND "CanonicalApplicationUrlHash" IS NULL
          AND "IsDeleted" = FALSE AND "ApplicationUrl" = @url
        """;

    public async Task<BackfillCounts> CountAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT count(*), count(*) FILTER (WHERE "CanonicalApplicationUrlHash" IS NULL),
                count(*) FILTER (WHERE "CanonicalApplicationUrlHash" IS NOT NULL),
                count(*) FILTER (WHERE "CanonicalApplicationUrlHash" IS NULL AND "IsDeleted" = FALSE)
            FROM "Jobs"
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    public async Task<IReadOnlyList<BackfillCandidate>> ReadAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT "Id", "ApplicationUrl" FROM "Jobs"
            WHERE "CanonicalApplicationUrlHash" IS NULL AND "IsDeleted" = FALSE
              AND (@after IS NULL OR "Id" > @after)
            ORDER BY "Id" LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<BackfillCandidate>(limit);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return rows;
    }

    public async Task<int> UpdateBatchAsync(IReadOnlyList<BackfillUpdate> updates, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var affected = 0;
        foreach (var update in updates)
        {
            await using var command = new NpgsqlCommand(UpdateSql, connection, transaction);
            command.Parameters.AddWithValue("hash", update.Hash);
            command.Parameters.AddWithValue("id", update.Id);
            command.Parameters.AddWithValue("url", update.ApplicationUrl);
            affected += await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return affected;
        // Disposing an uncommitted transaction rolls back the entire failed batch.
    }
}
