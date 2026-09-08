using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.AIApply;

public sealed class ExternalSessionCaptureInteractionService(
    ExternalSessionCaptureManager manager,
    IAIApplyAuthorizationService authorization,
    IExternalSessionCaptureTransport transport,
    IOptions<AIApplyOptions> options) : IExternalSessionCaptureInteractionService
{
    public async Task AttachAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct); await manager.AttachAsync(userId, captureId, connectionId, ct);
    }
    public async Task DetachAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct); await manager.DetachAsync(userId, captureId, connectionId);
    }
    public async Task DetachConnectionAsync(Guid userId, string connectionId, CancellationToken ct)
    { await authorization.RequireAsync(userId, ct: ct); await manager.DetachConnectionAsync(userId, connectionId); }
    public async Task<ExternalSessionBrowserFrame> RequestFrameAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct);
        var frame = await manager.InteractAsync(userId, captureId, connectionId, "frame", browser => browser.CaptureFrameAsync(ct), ct);
        if (frame.Data.Length > options.Value.ExternalSessions.Capture.Transport.MaximumFrameBytes)
            throw new ConflictException("Rendered browser frame exceeds the configured limit.", "external_session_frame_too_large");
        return frame;
    }
    public async Task ClickAsync(Guid userId, Guid captureId, string connectionId, double x, double y, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct); Bounds(x, y);
        await Run(userId, captureId, connectionId, browser => browser.ClickAsync(x, y, ct), ct);
    }
    public async Task ScrollAsync(Guid userId, Guid captureId, string connectionId, double deltaX, double deltaY, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct);
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || Math.Abs(deltaX) > 5000 || Math.Abs(deltaY) > 5000) throw InvalidInput();
        await Run(userId, captureId, connectionId, browser => browser.ScrollAsync(deltaX, deltaY, ct), ct);
    }
    public async Task InsertTextAsync(Guid userId, Guid captureId, string connectionId, string text, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct);
        if (string.IsNullOrEmpty(text) || text.Length > options.Value.ExternalSessions.Capture.Transport.MaximumTextInputCharacters || text.Any(char.IsControl)) throw InvalidInput();
        await Run(userId, captureId, connectionId, browser => browser.InsertTextAsync(text, ct), ct);
    }
    public async Task PressKeyAsync(Guid userId, Guid captureId, string connectionId, string key, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct);
        string[] allowed = ["Enter", "Tab", "Escape", "Backspace", "Delete", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown"];
        if (!allowed.Contains(key, StringComparer.Ordinal)) throw InvalidInput();
        await Run(userId, captureId, connectionId, browser => browser.PressKeyAsync(key, ct), ct);
    }
    public async Task GoBackAsync(Guid userId, Guid captureId, string connectionId, CancellationToken ct)
    { await AuthorizeAsync(userId, ct); await Run(userId, captureId, connectionId, browser => browser.GoBackAsync(ct), ct); }
    public async Task ResizeAsync(Guid userId, Guid captureId, string connectionId, int width, int height, CancellationToken ct)
    {
        await AuthorizeAsync(userId, ct);
        if (width is < 640 || height is < 480 || width > options.Value.ExternalSessions.Capture.Transport.MaximumViewportWidth || height > options.Value.ExternalSessions.Capture.Transport.MaximumViewportHeight) throw InvalidInput();
        await Run(userId, captureId, connectionId, browser => browser.ResizeAsync(width, height, ct), ct);
    }
    private async Task AuthorizeAsync(Guid userId, CancellationToken ct)
    {
        await authorization.RequireAsync(userId, ct: ct);
        if (!options.Value.ExternalSessions.Capture.Transport.Enabled || !transport.IsAvailable)
            throw new ConflictException("Interactive browser transport is unavailable.", "external_session_transport_unavailable");
    }
    private Task<bool> Run(Guid user, Guid capture, string connection, Func<IExternalSessionCaptureBrowser, Task> action, CancellationToken ct) =>
        manager.InteractAsync(user, capture, connection, "input", async browser => { await action(browser); return true; }, ct);
    private void Bounds(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x > options.Value.ExternalSessions.Capture.Transport.MaximumViewportWidth || y > options.Value.ExternalSessions.Capture.Transport.MaximumViewportHeight) throw InvalidInput();
    }
    private static BadRequestException InvalidInput() => new("Interactive browser input is invalid.", "external_session_input_invalid");
}
