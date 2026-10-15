using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using JobPortal.Persistence.Context;

namespace JobPortal.Persistence.Repositories;

internal static class PersistenceLengthDiagnostics
{
    public static void Report(JobPortalDbContext context, DbUpdateException exception, ILogger? logger)
    {
        var violations = new List<string>();
        foreach (var entry in context.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var table = entry.Metadata.GetTableName();
            if (table is null) continue;
            var store = StoreObjectIdentifier.Table(table, entry.Metadata.GetSchema());
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.GetMaxLength() is not { } limit || property.CurrentValue is not string value) continue;
                // PostgreSQL varchar limits count Unicode characters, not UTF-16 code units.
                var length = value.EnumerateRunes().Count();
                if (length <= limit) continue;
                var column = property.Metadata.GetColumnName(store);
                violations.Add($"{table}.{column} ({entry.Metadata.ClrType.Name}.{property.Metadata.Name}): {length}/{limit}");
                // Never log values, identifiers, URLs, SQL, or the database exception.
                logger?.LogWarning(new EventId(22001, "PersistenceLengthViolation"),
                    "PersistenceLengthViolation Table={Table} Column={Column} Property={Property} ObservedLength={ObservedLength} MaxLength={MaxLength}",
                    table, column, property.Metadata.Name, length, limit);
            }
        }
        exception.Data["PersistenceLengthViolations"] = string.Join("; ", violations);
    }
}
