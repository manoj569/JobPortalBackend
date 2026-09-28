using System.Security.Claims;
using JobPortal.Application.Abstractions.AIApply;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace JobPortal.API.Hubs;

[Authorize]
public sealed class ExternalSessionCaptureHub(IExternalSessionCaptureInteractionService interactions) : Hub
{
    private Guid UserId => Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new HubException("Authentication is required.");

    [Authorize(Roles = "Candidate")]
    public Task AttachCapture(Guid captureId) => interactions.AttachAsync(UserId, captureId, Context.ConnectionId, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task DetachCapture(Guid captureId) => interactions.DetachAsync(UserId, captureId, Context.ConnectionId, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task<ExternalSessionBrowserFrame> RequestFrame(Guid captureId) => interactions.RequestFrameAsync(UserId, captureId, Context.ConnectionId, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task SendPointerClick(Guid captureId, double x, double y) => interactions.ClickAsync(UserId, captureId, Context.ConnectionId, x, y, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task SendScroll(Guid captureId, double deltaX, double deltaY) => interactions.ScrollAsync(UserId, captureId, Context.ConnectionId, deltaX, deltaY, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task SendTextInput(Guid captureId, string text) => interactions.InsertTextAsync(UserId, captureId, Context.ConnectionId, text, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task SendKeyPress(Guid captureId, string key) => interactions.PressKeyAsync(UserId, captureId, Context.ConnectionId, key, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task NavigateBack(Guid captureId) => interactions.GoBackAsync(UserId, captureId, Context.ConnectionId, Context.ConnectionAborted);
    [Authorize(Roles = "Candidate")]
    public Task ResizeViewport(Guid captureId, int width, int height) => interactions.ResizeAsync(UserId, captureId, Context.ConnectionId, width, height, Context.ConnectionAborted);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.User?.IsInRole("Candidate") == true && Context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } value && Guid.TryParse(value, out var userId))
            await interactions.DetachConnectionAsync(userId, Context.ConnectionId, CancellationToken.None);
        await base.OnDisconnectedAsync(exception);
    }
}
