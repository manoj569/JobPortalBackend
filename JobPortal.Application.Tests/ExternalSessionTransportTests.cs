using System.Reflection;
using JobPortal.API.Hubs;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalSessionTransportTests
{
    [Fact]
    public async Task CandidateCanAttachAndInteractOnlyWithOwnedActiveCapture()
    {
        await using var h = Harness();
        var capture = await h.Manager.StartAsync(h.User, Site(), null, default);
        await h.Service.AttachAsync(h.User, capture.Id, "owner", default);
        await h.Service.ClickAsync(h.User, capture.Id, "owner", 20, 30, default);
        var frame = await h.Service.RequestFrameAsync(h.User, capture.Id, "owner", default);
        Assert.Equal([1, 1, 1], frame.Data); Assert.Equal(1, h.Browser.Clicks);

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.AttachAsync(Guid.NewGuid(), capture.Id, "intruder", default));
        await h.Service.DetachConnectionAsync(h.User, "owner", default);
        await Assert.ThrowsAsync<ConflictException>(() => h.Service.RequestFrameAsync(h.User, capture.Id, "owner", default));
    }

    [Fact]
    public async Task InactiveEntitlementAndDisabledTransportFailClosed()
    {
        await using var inactive = Harness(access: false);
        var capture = await inactive.Manager.StartAsync(inactive.User, Site(), null, default);
        var denied = await Assert.ThrowsAsync<AppException>(() => inactive.Service.AttachAsync(inactive.User, capture.Id, "c", default));
        Assert.Equal(403, denied.StatusCode);

        await using var disabled = Harness(transportEnabled: false);
        var disabledCapture = await disabled.Manager.StartAsync(disabled.User, Site(), null, default);
        var unavailable = await Assert.ThrowsAsync<ConflictException>(() => disabled.Service.AttachAsync(disabled.User, disabledCapture.Id, "c", default));
        Assert.Equal("external_session_transport_unavailable", unavailable.Code);
    }

    [Theory]
    [InlineData(ExternalSessionCaptureStatus.Cancelled)]
    [InlineData(ExternalSessionCaptureStatus.Completed)]
    [InlineData(ExternalSessionCaptureStatus.Expired)]
    public async Task TerminalCaptureCannotBeAttached(ExternalSessionCaptureStatus status)
    {
        await using var h = Harness();
        var capture = await h.Manager.StartAsync(h.User, Site(), null, default);
        capture.Status = status;
        await Assert.ThrowsAsync<ConflictException>(() => h.Service.AttachAsync(h.User, capture.Id, "c", default));
    }

    [Fact]
    public async Task TypedInputIsValidatedAndNeverRetainedByCapture()
    {
        await using var h = Harness();
        var capture = await h.Manager.StartAsync(h.User, Site(), null, default);
        await h.Service.AttachAsync(h.User, capture.Id, "c", default);
        await h.Service.InsertTextAsync(h.User, capture.Id, "c", "TestPassword_DoNotLog_123", default);
        Assert.Equal(1, h.Browser.TextDispatches);
        Assert.DoesNotContain(capture.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            p => p.Name.Contains("Text", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Service.InsertTextAsync(h.User, capture.Id, "c", new string('x', 513), default));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Service.ClickAsync(h.User, capture.Id, "c", -1, 2, default));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Service.PressKeyAsync(h.User, capture.Id, "c", "Control+L", default));
        await Assert.ThrowsAsync<BadRequestException>(() => h.Service.ResizeAsync(h.User, capture.Id, "c", 5000, 5000, default));
    }

    [Fact]
    public async Task InputAndFrameRatesAreBoundedAndOversizedFramesRejected()
    {
        await using var h = Harness(frameBytes: 70_000);
        var capture = await h.Manager.StartAsync(h.User, Site(), null, default);
        await h.Service.AttachAsync(h.User, capture.Id, "c", default);
        var tooLarge = await Assert.ThrowsAsync<ConflictException>(() => h.Service.RequestFrameAsync(h.User, capture.Id, "c", default));
        Assert.Equal("external_session_frame_too_large", tooLarge.Code);
        for (var i = 0; i < 3; i++) await h.Service.ClickAsync(h.User, capture.Id, "c", i, i, default);
        var limited = await Assert.ThrowsAsync<ConflictException>(() => h.Service.ClickAsync(h.User, capture.Id, "c", 4, 4, default));
        Assert.Equal("external_session_transport_rate_limit", limited.Code);
    }

    [Fact]
    public async Task FramesAndInputNeverCrossCapturesOrUsers()
    {
        await using var a = Harness(frameMarker: 7);
        await using var b = Harness(frameMarker: 9);
        var ca = await a.Manager.StartAsync(a.User, Site(), null, default);
        var cb = await b.Manager.StartAsync(b.User, Site(), null, default);
        await a.Service.AttachAsync(a.User, ca.Id, "a", default);
        await b.Service.AttachAsync(b.User, cb.Id, "b", default);
        Assert.Equal(7, (await a.Service.RequestFrameAsync(a.User, ca.Id, "a", default)).Data[0]);
        Assert.Equal(9, (await b.Service.RequestFrameAsync(b.User, cb.Id, "b", default)).Data[0]);
        await Assert.ThrowsAsync<NotFoundException>(() => a.Service.AttachAsync(a.User, cb.Id, "wrong-instance", default));
        Assert.Equal(0, a.Transport.ExtraStarts); Assert.Equal(0, b.Transport.ExtraStarts);
    }

    [Fact]
    public async Task CaptureRecordsOpaqueOwnerInstanceButDtosCannotExposeIt()
    {
        await using var h = Harness();
        var capture = await h.Manager.StartAsync(h.User, Site(), null, default);
        var owner = capture.GetType().GetProperty("OwnerInstanceId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.False(string.IsNullOrWhiteSpace((string?)owner?.GetValue(capture)));
        Assert.DoesNotContain(typeof(ExternalSessionCaptureResponse).GetProperties(), p => p.Name.Contains("Instance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NavigateBackProducesIndependentlyObservablePreviousPageState()
    {
        await using var h = Harness();
        var capture = await h.Manager.StartAsync(h.User, Site(), null, default);
        await h.Service.AttachAsync(h.User, capture.Id, "owner", default);
        Assert.Equal("NAV_PAGE_TWO", h.Browser.CurrentMarker);

        await h.Service.GoBackAsync(h.User, capture.Id, "owner", default);

        Assert.Equal("NAV_PAGE_ONE", h.Browser.CurrentMarker);
        Assert.DoesNotContain("NAV_PAGE_TWO", h.Browser.CurrentMarker);
        Assert.Equal(new Uri("https://www.linkedin.com/login"), h.Browser.CurrentUri);
    }

    [Fact]
    public void HubIsCandidateAuthorizedAndHasNoBrowserInternalEscapeHatches()
    {
        Assert.Equal("Candidate", typeof(ExternalSessionCaptureHub).GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        string[] forbidden = ["JavaScript", "Url", "Cdp", "Cookie", "Storage", "Playwright", "Clipboard", "Download"];
        Assert.DoesNotContain(typeof(ExternalSessionCaptureHub).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => forbidden.Any(value => method.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ProductionTransportRequiresExplicitStickyAffinity()
    {
        var options = Options(true); options.Enabled = true; options.Browser.Enabled = true;
        options.ExternalSessions.Capture.Transport.StickyAffinityConfigured = false;
        var result = new AIApplyOptionsValidator(false).Validate(null, options);
        Assert.True(result.Failed); Assert.Contains(result.Failures!, x => x.Contains("sticky affinity", StringComparison.OrdinalIgnoreCase));
    }

    private static TestHarness Harness(bool access = true, bool transportEnabled = true, int frameBytes = 3, byte frameMarker = 1)
    {
        var options = Options(transportEnabled);
        var transport = new FakeTransport(new FakeBrowser(frameBytes, frameMarker));
        var manager = new ExternalSessionCaptureManager(transport, Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System);
        var service = new ExternalSessionCaptureInteractionService(manager, new Authorization(access), transport, Microsoft.Extensions.Options.Options.Create(options));
        return new(Guid.NewGuid(), manager, service, transport, transport.Browser);
    }

    private static AIApplyOptions Options(bool transportEnabled) => new()
    {
        ExternalSessions = new() { Capture = new() { MaximumDurationMinutes = 10, IdleTimeoutMinutes = 5,
            MaximumConcurrentPerUser = 1, MaximumConcurrentGlobal = 10,
            Transport = new() { Enabled = transportEnabled, MaxFramesPerSecond = 3, MaximumFrameBytes = 65_536,
                MaximumInputEventsPerSecond = 3, MaximumTextInputCharacters = 512 } } }
    };
    private static ExternalSessionSiteDescriptor Site() => new(JobSiteIdentifier.LinkedIn, "LinkedIn", new("https://www.linkedin.com/login"));

    private sealed record TestHarness(Guid User, ExternalSessionCaptureManager Manager, ExternalSessionCaptureInteractionService Service, FakeTransport Transport, FakeBrowser Browser) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Manager.DisposeAsync(); }
    private sealed class Authorization(bool allowed) : IAIApplyAuthorizationService
    {
        public Task<AIApplyAccess> GetAccessAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new AIApplyAccess(allowed, false, AIApplyPlanTier.Standard, allowed, false));
        public Task<AIApplyAccess> RequireAsync(Guid userId, bool pro = false, CancellationToken ct = default) => allowed ? GetAccessAsync(userId, ct) : throw new AppException("AI Apply access is required.", 403, "ai_apply_access_required");
    }
    private sealed class FakeTransport(FakeBrowser browser) : IExternalSessionCaptureTransport
    {
        public FakeBrowser Browser { get; } = browser; public int Starts { get; private set; } public int ExtraStarts => Math.Max(0, Starts - 1);
        public bool IsAvailable => true; public string InteractionMode => "SignalRRemoteBrowser";
        public Task<IExternalSessionCaptureBrowser> StartAsync(ExternalSessionSiteDescriptor site, CancellationToken ct) { Starts++; return Task.FromResult<IExternalSessionCaptureBrowser>(Browser); }
    }
    private sealed class FakeBrowser(int frameBytes, byte marker) : IExternalSessionCaptureBrowser
    {
        public int Clicks { get; private set; } public int TextDispatches { get; private set; }
        public string CurrentMarker { get; private set; } = "NAV_PAGE_TWO";
        public Uri CurrentUri { get; private set; } = new("https://www.linkedin.com/checkpoint");
        public Task<ExternalSessionBrowserFrame> CaptureFrameAsync(CancellationToken ct) => Task.FromResult(new ExternalSessionBrowserFrame(Enumerable.Repeat(marker, frameBytes).ToArray(), "image/jpeg", 1280, 800, DateTime.UtcNow));
        public Task ClickAsync(double x, double y, CancellationToken ct) { Clicks++; return Task.CompletedTask; }
        public Task InsertTextAsync(string text, CancellationToken ct) { TextDispatches++; return Task.CompletedTask; }
        public Task ScrollAsync(double deltaX, double deltaY, CancellationToken ct) => Task.CompletedTask;
        public Task PressKeyAsync(string key, CancellationToken ct) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken ct) { CurrentMarker = "NAV_PAGE_ONE"; CurrentUri = new("https://www.linkedin.com/login"); return Task.CompletedTask; }
        public Task ResizeAsync(int width, int height, CancellationToken ct) => Task.CompletedTask;
        public Task<ExternalSessionCaptureSnapshot> InspectAsync(CancellationToken ct) => Task.FromResult(new ExternalSessionCaptureSnapshot(new("https://www.linkedin.com/feed"), true, false, false));
        public Task<byte[]> CaptureStorageStateAsync(CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
