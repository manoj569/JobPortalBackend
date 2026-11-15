using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed record CandidateMailboxView(Guid Id, string Provider, string EmailAddress,
    DateTime ConnectedAtUtc, bool IsConnected);

public sealed partial class AcceleratorTalentService
{
    public async Task<IReadOnlyList<CandidateMailboxView>> MailboxesAsync(Guid actor,
        CancellationToken ct)
    {
        await Candidate(actor, ct);
        return await db.CandidateMailboxConnections.AsNoTracking().Where(x =>
            x.CandidateId == actor && !x.IsDeleted).OrderByDescending(x => x.ConnectedAtUtc)
            .Select(x => new CandidateMailboxView(x.Id, x.Provider, x.EmailAddress,
                x.ConnectedAtUtc, x.RevokedAtUtc == null)).Take(10).ToArrayAsync(ct);
    }

    public async Task DisconnectMailboxAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var connection = await db.CandidateMailboxConnections.SingleOrDefaultAsync(x =>
            x.Id == id && x.CandidateId == actor && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Mailbox connection not found.");
        if (connection.RevokedAtUtc.HasValue) return;
        connection.RevokedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Update, "CandidateMailboxDisconnect", id, "Candidate", ct);
    }

    public string BeginMailboxConnect(Guid actor, string provider)
    {
        _ = actor;
        if (provider is not ("google" or "microsoft"))
            throw new BadRequestException("Unsupported mailbox provider.");
        if (provider == "google" && !Outreach.GoogleMailboxEnabled ||
            provider == "microsoft" && !Outreach.MicrosoftMailboxEnabled)
            throw new AppException("Mailbox connection is disabled.", 503, "mailbox_disabled");
        // No OAuth implementation or token vault is registered. Never invent an auth URL.
        throw new AppException("Mailbox OAuth is not configured.", 503, "mailbox_unavailable");
    }
}
