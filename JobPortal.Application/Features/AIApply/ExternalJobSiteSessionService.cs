using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.AIApply;

public sealed class ExternalJobSiteSessionService(
    IExternalJobSiteSessionStore sessions,
    IAIApplyAuthorizationService authorization,
    IAuditWriter audit) : IExternalJobSiteSessionService
{
    public async Task<ExternalJobSiteSessionResponse> GetAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        var session = await sessions.GetMetadataAsync(userId, site, ct)
            ?? throw new NotFoundException("External session was not found.");
        return Map(session);
    }

    public async Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        var session = await sessions.GetMetadataAsync(userId, sessionId, ct)
            ?? throw new NotFoundException("External session was not found.");
        if (!await sessions.RevokeAsync(userId, sessionId, ct))
            throw new NotFoundException("External session was not found.");
        await audit.AppendAsync(new(AuditAction.Delete, "ExternalJobSiteSession", sessionId.ToString(),
            new Dictionary<string, string?> { ["site"] = session.Site.ToString(), ["operation"] = "revoke" },
            new(userId, "Candidate")), ct);
    }

    private static ExternalJobSiteSessionResponse Map(ExternalJobSiteSessionMetadata x) => new(
        x.Id, x.Site, x.Status, x.ExpiresAtUtc, x.LastValidatedAtUtc, x.RequiresReauthentication,
        x.Status is ExternalJobSiteSessionStatus.Active or ExternalJobSiteSessionStatus.RequiresReauthentication);
}
