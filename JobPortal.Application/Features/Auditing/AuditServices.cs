using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Auditing;

public sealed class AuditWriter(
    IAuditLogRepository auditLogs,
    IAuditContextAccessor context) : IAuditWriter
{
    public Task AppendAsync(
        AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var actor = auditEvent.Actor;
        var actorRole = Normalize(actor?.Role ?? context.ActorRole, 50) ?? "System";
        var correlationId = Normalize(context.CorrelationId, 64)
            ?? $"system-{Guid.NewGuid():N}";
        var entityType = NormalizeRequired(auditEvent.EntityType, 200, "entity type");
        var entityId = NormalizeRequired(auditEvent.EntityId, 64, "entity ID");
        return auditLogs.AddAsync(new AuditLog
        {
            Action = auditEvent.Action,
            EntityName = entityType,
            EntityId = entityId,
            ChangesJson = AuditMetadataPolicy.Serialize(auditEvent.Metadata),
            ActorRole = actorRole,
            CorrelationId = correlationId,
            UserId = actor?.UserId ?? context.ActorUserId
        }, cancellationToken);
    }

    private static string NormalizeRequired(string value, int maximumLength, string name) =>
        Normalize(value, maximumLength)
        ?? throw new ArgumentException($"Audit {name} is required.", nameof(value));

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}

public sealed class AuditLogService(
    IAuditLogRepository auditLogs,
    IValidator<AuditLogQuery> validator) : IAuditLogService
{
    public async Task<PagedResponse<AuditLogResponse>> SearchAsync(
        AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await auditLogs.SearchAsync(query, cancellationToken);
        return new(
            result.Items.Select(log => new AuditLogResponse(
                log.Id,
                log.UserId,
                log.ActorRole,
                log.Action,
                log.EntityName,
                log.EntityId,
                log.CreatedAtUtc,
                log.CorrelationId,
                AuditMetadataPolicy.Deserialize(log.ChangesJson))).ToArray(),
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }
}

public sealed class AuditLogQueryValidator : AbstractValidator<AuditLogQuery>
{
    public AuditLogQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Action).IsInEnum().When(x => x.Action.HasValue);
        RuleFor(x => x.EntityType).MaximumLength(200);
        RuleFor(x => x.EntityId).MaximumLength(64);
        RuleFor(x => x.CorrelationId).MaximumLength(64);
        RuleFor(x => x).Must(x =>
                !x.FromUtc.HasValue || !x.ToUtc.HasValue || x.FromUtc <= x.ToUtc)
            .WithMessage("FromUtc must be earlier than or equal to ToUtc.");
    }
}

internal static class AuditMetadataPolicy
{
    private static readonly HashSet<string> AllowedKeys =
        new(StringComparer.Ordinal)
        {
            "amount",
            "autoPublishEnabled",
            "autoPublishEnabledBefore",
            "autoPublishEnabledAfter",
            "categoryId",
            "changedFields",
            "completed",
            "companyId",
            "currency",
            "duplicateRows",
            "fileType",
            "importedRows",
            "importType",
            "invalidRows",
            "isFeatured",
            "jobId",
            "membershipStatus",
            "newStatus",
            "previousStatus",
            "provider",
            "result",
            "sizeBytes",
            "source",
            "status",
            "skippedRows",
            "totalRows",
            "validRows"
        };

    public static string? Serialize(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;
        var safe = metadata
            .Where(pair => AllowedKeys.Contains(pair.Key))
            .Take(16)
            .ToDictionary(
                pair => pair.Key,
                pair => NormalizeValue(pair.Value),
                StringComparer.Ordinal);
        return safe.Count == 0 ? null : JsonSerializer.Serialize(safe);
    }

    public static IReadOnlyDictionary<string, string?> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string?>();
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (values is null)
                return new Dictionary<string, string?>();
            return values
                .Where(pair => AllowedKeys.Contains(pair.Key))
                .Take(16)
                .ToDictionary(
                    pair => pair.Key,
                    pair => NormalizeValue(pair.Value),
                    StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
    }

    private static string? NormalizeValue(string? value)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }
}
