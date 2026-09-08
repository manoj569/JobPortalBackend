using System.Security.Cryptography;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.AIApply;

public sealed class ExternalSessionCaptureService(
    ExternalSessionCaptureManager manager,
    IExternalSessionCaptureTransport transport,
    IExternalSessionSiteRegistry sites,
    IEnumerable<IExternalJobSiteSessionValidator> validators,
    IExternalJobSiteSessionStore sessions,
    IAIApplyAuthorizationService authorization,
    IAuditWriter audit,
    IOptions<AIApplyOptions> options,
    TimeProvider clock) : IExternalSessionCaptureService
{
    public async Task<ExternalSessionCaptureResponse> StartAsync(Guid userId, JobSiteIdentifier site, StartExternalSessionCaptureRequest request, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        if (!options.Value.ExternalSessions.Enabled) throw new ConflictException("Persistent external sessions are disabled.", "external_sessions_disabled");
        if (!options.Value.ExternalSessions.Capture.Enabled) throw new ConflictException("External login capture is disabled.", "external_session_capture_disabled");
        if (!transport.IsAvailable) throw new ConflictException("Interactive external login is not currently available.", "external_session_capture_unavailable");
        var descriptor = await sites.GetAsync(site, ct)
            ?? throw new BadRequestException("This external site is not enabled for secure session capture.", "external_session_site_unsupported");
        var existing = await sessions.GetMetadataAsync(userId, site, ct);
        if (existing?.Status == ExternalJobSiteSessionStatus.Active && !request.Reconnect)
            throw new ConflictException("An active external session already exists. Explicit reconnect is required.", "external_session_already_active");
        var capture = await manager.StartAsync(userId, descriptor, existing?.Version, ct);
        await AuditAsync(AuditAction.Create, capture, "external_session_capture_started", ct);
        return Map(capture);
    }

    public async Task<ExternalSessionCaptureResponse> GetAsync(Guid userId, Guid captureId, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        return Map(await OwnedAsync(userId, captureId, ct));
    }

    public async Task<CompleteExternalSessionCaptureResponse> CompleteAsync(Guid userId, Guid captureId, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        var capture = await OwnedAsync(userId, captureId, ct);
        if (!await manager.BeginValidationAsync(capture, ct))
        {
            var completed = await sessions.GetMetadataAsync(userId, capture.Site.Site, ct);
            return new(Map(capture), completed is null ? null : Map(completed));
        }
        byte[]? state = null;
        try
        {
            var validator = validators.SingleOrDefault(x => x.Site == capture.Site.Site);
            if (validator is null || capture.Browser is null)
                return await FailAsync(capture, "session_validator_unavailable", ct);
            var validation = await validator.ValidateAsync(capture.Browser, ct);
            if (validation.Status != ExternalSessionValidationStatus.Valid)
                return await FailAsync(capture, validation.ReasonCode, ct);
            state = await capture.Browser.CaptureStorageStateAsync(ct);
            var session = await sessions.SaveAsync(userId, capture.Site.Site, state,
                clock.GetUtcNow().UtcDateTime.AddDays(options.Value.ExternalSessions.MaximumLifetimeDays),
                capture.BaselineVersion, ct, allowRevokedReplacement: true);
            await manager.FinishAsync(capture, ExternalSessionCaptureStatus.Completed);
            await AuditAsync(AuditAction.Create, capture, "external_session_capture_completed", ct);
            return new(Map(capture), Map(session));
        }
        catch
        {
            if (capture.Status == ExternalSessionCaptureStatus.Validating)
            {
                await manager.FinishAsync(capture, ExternalSessionCaptureStatus.Failed);
                await AuditAsync(AuditAction.Update, capture, "external_session_capture_failed", ct);
            }
            throw;
        }
        finally { if (state is not null) CryptographicOperations.ZeroMemory(state); }
    }

    public async Task CancelAsync(Guid userId, Guid captureId, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        var capture = await OwnedAsync(userId, captureId, ct);
        if (!await manager.CancelAsync(capture, ct))
            throw new ConflictException("Completed capture cannot be cancelled.", "external_session_capture_completed");
        await AuditAsync(AuditAction.Delete, capture, "external_session_capture_cancelled", ct);
    }

    private async Task<CompleteExternalSessionCaptureResponse> FailAsync(ExternalSessionCaptureManager.Capture capture, string reason, CancellationToken ct)
    {
        await manager.FinishAsync(capture, ExternalSessionCaptureStatus.Failed);
        await AuditAsync(AuditAction.Update, capture, reason, ct);
        return new(Map(capture) with { Message = SafeValidationMessage(reason) }, null);
    }
    private async Task<ExternalSessionCaptureManager.Capture> OwnedAsync(Guid userId, Guid id, CancellationToken ct) =>
        await manager.GetOwnedAsync(userId, id, ct) ?? throw new NotFoundException("External login capture was not found.");
    private ExternalSessionCaptureResponse Map(ExternalSessionCaptureManager.Capture x)
    {
        var interactionAvailable = transport.IsAvailable && x.Status == ExternalSessionCaptureStatus.AwaitingCandidate && x.Browser is not null;
        return new(x.Id, x.Site.Site, x.Site.DisplayName, x.Status, x.ExpiresAtUtc,
            x.Status == ExternalSessionCaptureStatus.AwaitingCandidate, interactionAvailable,
            interactionAvailable ? transport.InteractionMode : "Unavailable", Message(x.Status));
    }
    private static ExternalJobSiteSessionResponse Map(ExternalJobSiteSessionMetadata x) => new(x.Id, x.Site, x.Status, x.ExpiresAtUtc,
        x.LastValidatedAtUtc, x.RequiresReauthentication, x.Status is ExternalJobSiteSessionStatus.Active or ExternalJobSiteSessionStatus.RequiresReauthentication);
    private static string Message(ExternalSessionCaptureStatus status) => status switch
    {
        ExternalSessionCaptureStatus.AwaitingCandidate => "Authenticate directly with the external site, then confirm completion.",
        ExternalSessionCaptureStatus.Validating => "The external session is being validated.",
        ExternalSessionCaptureStatus.Completed => "The external session was connected securely.",
        ExternalSessionCaptureStatus.Cancelled => "The external login capture was cancelled.",
        ExternalSessionCaptureStatus.Expired => "The external login capture expired.",
        ExternalSessionCaptureStatus.Failed => "The external session could not be safely validated.",
        _ => "The external login capture is starting."
    };
    private static string SafeValidationMessage(string reason) => reason == "human_verification_required"
        ? "Complete the external site's verification and start a new secure connection."
        : reason == "login_required" ? "The external site did not confirm that you are signed in." : "The external session could not be safely validated.";
    private Task AuditAsync(AuditAction action, ExternalSessionCaptureManager.Capture capture, string result, CancellationToken ct) =>
        audit.AppendAsync(new(action, "ExternalSessionCapture", capture.Id.ToString(),
            new Dictionary<string, string?> { ["site"] = capture.Site.Site.ToString(), ["result"] = result },
            new(capture.UserId, "Candidate")), ct);
}
