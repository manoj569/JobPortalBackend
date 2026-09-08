using System.Reflection;
using System.Text;
using System.Net;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalSessionCaptureTests
{
    [Fact]
    public async Task SupportedCandidateStartsOpaqueAwaitingCapture()
    {
        await using var harness = Harness();
        var result = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        Assert.NotEqual(Guid.Empty, result.CaptureId); Assert.NotEqual(harness.User, result.CaptureId);
        Assert.Equal(ExternalSessionCaptureStatus.AwaitingCandidate, result.Status);
        Assert.True(result.InteractionAvailable); Assert.True(result.RequiresCandidateAction);
    }

    [Fact]
    public async Task InactiveEntitlementIsRejectedBeforeTransportStarts()
    {
        await using var harness = Harness(access: false);
        var error = await Assert.ThrowsAsync<AppException>(() => harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(0, harness.Transport.StartCount);
    }

    [Theory]
    [InlineData(false, true, true, "external_sessions_disabled")]
    [InlineData(true, false, true, "external_session_capture_disabled")]
    [InlineData(true, true, false, "external_session_capture_unavailable")]
    public async Task DisabledBoundariesFailClosed(bool sessionsEnabled, bool captureEnabled, bool transportAvailable, string code)
    {
        await using var harness = Harness(sessionsEnabled: sessionsEnabled, captureEnabled: captureEnabled, transportAvailable: transportAvailable);
        var error = await Assert.ThrowsAsync<ConflictException>(() => harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task UnsupportedSiteAndArbitraryUrlHaveNoApiSurface()
    {
        await using var harness = Harness(siteSupported: false);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => harness.Service.StartAsync(harness.User, JobSiteIdentifier.Generic, new(), default));
        Assert.Equal("external_session_site_unsupported", error.Code);
        Assert.DoesNotContain(typeof(StartExternalSessionCaptureRequest).GetProperties(), x => x.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CandidateCannotReadCompleteOrCancelAnotherCapture()
    {
        await using var harness = Harness();
        var capture = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.GetAsync(other, capture.CaptureId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.CompleteAsync(other, capture.CaptureId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.CancelAsync(other, capture.CaptureId, default));
    }

    [Fact]
    public async Task ValidCompletionPersistsOnceDisposesAndIsIdempotent()
    {
        await using var harness = Harness();
        var capture = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        var completed = await harness.Service.CompleteAsync(harness.User, capture.CaptureId, default);
        Assert.Equal(ExternalSessionCaptureStatus.Completed, completed.Capture.Status); Assert.NotNull(completed.Session);
        Assert.Equal(1, harness.Sessions.SaveCount); Assert.True(harness.Transport.LastBrowser!.Disposed);
        var repeated = await harness.Service.CompleteAsync(harness.User, capture.CaptureId, default);
        Assert.Equal(ExternalSessionCaptureStatus.Completed, repeated.Capture.Status); Assert.Equal(1, harness.Sessions.SaveCount);
    }

    [Fact]
    public async Task ConcurrentCompletionPersistsOnlyOnce()
    {
        await using var harness = Harness();
        var capture = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        var results = await Task.WhenAll(
            harness.Service.CompleteAsync(harness.User, capture.CaptureId, default),
            harness.Service.CompleteAsync(harness.User, capture.CaptureId, default));
        Assert.All(results, x => Assert.Equal(ExternalSessionCaptureStatus.Completed, x.Capture.Status));
        Assert.Equal(1, harness.Sessions.SaveCount);
    }

    [Theory]
    [InlineData(ExternalSessionValidationStatus.RequiresLogin)]
    [InlineData(ExternalSessionValidationStatus.RequiresHumanVerification)]
    [InlineData(ExternalSessionValidationStatus.ValidationFailed)]
    [InlineData(ExternalSessionValidationStatus.Unsupported)]
    [InlineData(ExternalSessionValidationStatus.TemporarilyUnavailable)]
    public async Task UnprovenAuthenticationNeverPersists(ExternalSessionValidationStatus validation)
    {
        await using var harness = Harness(validation: validation);
        var capture = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        var completed = await harness.Service.CompleteAsync(harness.User, capture.CaptureId, default);
        Assert.Equal(ExternalSessionCaptureStatus.Failed, completed.Capture.Status); Assert.Null(completed.Session);
        Assert.Equal(0, harness.Sessions.SaveCount); Assert.True(harness.Transport.LastBrowser!.Disposed);
    }

    [Fact]
    public async Task CancellationIsOwnedIdempotentAndDisposesContext()
    {
        await using var harness = Harness();
        var capture = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        await harness.Service.CancelAsync(harness.User, capture.CaptureId, default);
        await harness.Service.CancelAsync(harness.User, capture.CaptureId, default);
        Assert.True(harness.Transport.LastBrowser!.Disposed);
        await Assert.ThrowsAsync<ConflictException>(() => harness.Service.CompleteAsync(harness.User, capture.CaptureId, default));
    }

    [Fact]
    public async Task CapacityAndExplicitReconnectAreEnforced()
    {
        await using var harness = Harness();
        await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        var limit = await Assert.ThrowsAsync<ConflictException>(() => harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default));
        Assert.Equal("external_session_capture_limit", limit.Code);

        await using var active = Harness(activeSession: true);
        await Assert.ThrowsAsync<ConflictException>(() => active.Service.StartAsync(active.User, JobSiteIdentifier.LinkedIn, new(), default));
        var reconnect = await active.Service.StartAsync(active.User, JobSiteIdentifier.LinkedIn, new(true), default);
        Assert.Equal(ExternalSessionCaptureStatus.AwaitingCandidate, reconnect.Status);
    }

    [Fact]
    public async Task IdleCleanupExpiresAndDisposesAbandonedCapture()
    {
        var clock = new MutableClock(new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero));
        await using var harness = Harness(clock: clock);
        var capture = await harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True(await harness.Manager.CleanupAsync(default) > 0);
        Assert.True(harness.Transport.LastBrowser!.Disposed);
        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.GetAsync(harness.User, capture.CaptureId, default));
    }

    [Fact]
    public async Task InitializationFailureLeavesNoCaptureOrBrowser()
    {
        await using var harness = Harness(startFailure: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.StartAsync(harness.User, JobSiteIdentifier.LinkedIn, new(), default));
        Assert.Equal(0, await harness.Manager.CleanupAsync(default));
    }

    [Fact]
    public void ApiDtosNeverContainCredentialsBrowserStateOrTransportEndpoints()
    {
        var forbidden = new[] { "Password", "Username", "Otp", "Mfa", "Totp", "Recovery", "Cookie", "StorageState", "Token", "BrowserContext", "Page", "Cdp", "WebSocket", "Endpoint" };
        foreach (var type in new[] { typeof(StartExternalSessionCaptureRequest), typeof(ExternalSessionCaptureResponse), typeof(CompleteExternalSessionCaptureResponse) })
            Assert.DoesNotContain(type.GetProperties(), property => forbidden.Any(value => property.Name.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CaptureRoutesAreCandidateControllerActionsAndRateLimited()
    {
        var methods = typeof(AIApplyController).GetMethods().Where(x => x.Name.Contains("Capture", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, methods.Length);
        Assert.All(methods, method => Assert.NotNull(method.GetCustomAttribute<EnableRateLimitingAttribute>()));
        Assert.Contains(methods, method => method.GetCustomAttribute<HttpPostAttribute>()?.Template == "external-sessions/{site}/capture");
        Assert.Contains(methods, method => method.GetCustomAttribute<HttpPostAttribute>()?.Template == "external-sessions/captures/{captureId:guid}/complete");
    }

    [Theory]
    [InlineData(JobSiteIdentifier.Workday, "jobs.myworkdayjobs.com")]
    [InlineData(JobSiteIdentifier.LinkedIn, "www.linkedin.com")]
    [InlineData(JobSiteIdentifier.Naukri, "www.naukri.com")]
    [InlineData(JobSiteIdentifier.Indeed, "secure.indeed.com")]
    [InlineData(JobSiteIdentifier.Foundit, "www.foundit.in")]
    [InlineData(JobSiteIdentifier.Wellfound, "wellfound.com")]
    public async Task SiteValidatorsRequireKnownPublicHostAndExplicitAuthenticatedSignal(JobSiteIdentifier site, string host)
    {
        var navigation = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()), new FakeDns(IPAddress.Parse("203.0.113.10")));
        var validator = Validator(site, navigation);
        var valid = await validator.ValidateAsync(new SnapshotBrowser(new(new Uri($"https://{host}/account"), true, false, false)), default);
        Assert.Equal(ExternalSessionValidationStatus.Valid, valid.Status);
        var uncertain = await validator.ValidateAsync(new SnapshotBrowser(new(new Uri($"https://{host}/account"), false, false, false)), default);
        Assert.Equal(ExternalSessionValidationStatus.ValidationFailed, uncertain.Status);
    }

    [Theory]
    [InlineData("https://linkedin.example.com/login")]
    [InlineData("https://www.linkedin.com.evil.test/login")]
    public async Task ValidatorRejectsLookalikeHosts(string url)
    {
        var navigation = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()), new FakeDns(IPAddress.Parse("203.0.113.10")));
        var result = await new LinkedInExternalSessionValidator(navigation).ValidateAsync(
            new SnapshotBrowser(new(new Uri(url), true, false, false)), default);
        Assert.Equal(ExternalSessionValidationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task ValidatorRejectsPrivateAddressAndPrioritizesHumanChallenge()
    {
        var privateNavigation = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()), new FakeDns(IPAddress.Parse("10.0.0.8")));
        var rejected = await new LinkedInExternalSessionValidator(privateNavigation).ValidateAsync(
            new SnapshotBrowser(new(new("https://www.linkedin.com/feed"), true, false, false)), default);
        Assert.Equal(ExternalSessionValidationStatus.ValidationFailed, rejected.Status);
        var publicNavigation = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()), new FakeDns(IPAddress.Parse("203.0.113.10")));
        var challenge = await new LinkedInExternalSessionValidator(publicNavigation).ValidateAsync(
            new SnapshotBrowser(new(new("https://www.linkedin.com/checkpoint"), true, false, true)), default);
        Assert.Equal(ExternalSessionValidationStatus.RequiresHumanVerification, challenge.Status);
    }

    private static IExternalJobSiteSessionValidator Validator(JobSiteIdentifier site, ExternalNavigationPolicy navigation) => site switch
    {
        JobSiteIdentifier.Workday => new WorkdayExternalSessionValidator(navigation),
        JobSiteIdentifier.LinkedIn => new LinkedInExternalSessionValidator(navigation),
        JobSiteIdentifier.Naukri => new NaukriExternalSessionValidator(navigation),
        JobSiteIdentifier.Indeed => new IndeedExternalSessionValidator(navigation),
        JobSiteIdentifier.Foundit => new FounditExternalSessionValidator(navigation),
        JobSiteIdentifier.Wellfound => new WellfoundExternalSessionValidator(navigation),
        _ => throw new ArgumentOutOfRangeException(nameof(site))
    };

    private static CaptureHarness Harness(bool access = true, bool sessionsEnabled = true, bool captureEnabled = true,
        bool transportAvailable = true, bool siteSupported = true, bool activeSession = false,
        ExternalSessionValidationStatus validation = ExternalSessionValidationStatus.Valid, MutableClock? clock = null, bool startFailure = false)
    {
        var options = new AIApplyOptions
        {
            Enabled = true,
            ExternalSessions = new()
            {
                Enabled = sessionsEnabled, MaximumLifetimeDays = 30, MaximumStorageStateBytes = 262144,
                Capture = new() { Enabled = captureEnabled, MaximumDurationMinutes = 10, IdleTimeoutMinutes = 5, MaximumConcurrentPerUser = 1, MaximumConcurrentGlobal = 10 }
            }
        };
        var transport = new FakeTransport(transportAvailable, startFailure);
        var actualClock = clock ?? new MutableClock(new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero));
        var manager = new ExternalSessionCaptureManager(transport, Options.Create(options), actualClock);
        var sessions = new FakeSessions(activeSession, actualClock.GetUtcNow().UtcDateTime);
        var service = new ExternalSessionCaptureService(manager, transport, new FakeSites(siteSupported),
            [new FakeValidator(validation)], sessions, new FakeAuthorization(access), new FakeAudit(), Options.Create(options), actualClock);
        return new(Guid.NewGuid(), service, manager, transport, sessions);
    }

    private sealed record CaptureHarness(Guid User, ExternalSessionCaptureService Service, ExternalSessionCaptureManager Manager, FakeTransport Transport, FakeSessions Sessions) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Manager.DisposeAsync(); }
    private sealed class FakeAuthorization(bool allowed) : IAIApplyAuthorizationService
    {
        public Task<AIApplyAccess> GetAccessAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new AIApplyAccess(allowed, false, AIApplyPlanTier.Standard, allowed, false));
        public Task<AIApplyAccess> RequireAsync(Guid userId, bool pro = false, CancellationToken ct = default) => allowed ? GetAccessAsync(userId, ct) : throw new AppException("AI Apply access is required.", 403, "ai_apply_access_required");
    }
    private sealed class FakeAudit : IAuditWriter { public readonly List<AuditEvent> Events = []; public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) { Events.Add(auditEvent); return Task.CompletedTask; } }
    private sealed class FakeSites(bool supported) : IExternalSessionSiteRegistry
    { public Task<ExternalSessionSiteDescriptor?> GetAsync(JobSiteIdentifier site, CancellationToken ct) => Task.FromResult(supported && site == JobSiteIdentifier.LinkedIn ? new ExternalSessionSiteDescriptor(site, "LinkedIn", new("https://www.linkedin.com/login")) : null); }
    private sealed class FakeValidator(ExternalSessionValidationStatus status) : IExternalJobSiteSessionValidator
    { public JobSiteIdentifier Site => JobSiteIdentifier.LinkedIn; public Task<ExternalSessionValidationResult> ValidateAsync(IExternalSessionCaptureBrowser browser, CancellationToken ct) => Task.FromResult(new ExternalSessionValidationResult(status, status switch { ExternalSessionValidationStatus.RequiresLogin => "login_required", ExternalSessionValidationStatus.RequiresHumanVerification => "human_verification_required", _ => "validation_failed" })); }
    private sealed class FakeTransport(bool available, bool startFailure) : IExternalSessionCaptureTransport
    {
        public bool IsAvailable => available; public string InteractionMode => "DeterministicTest"; public int StartCount { get; private set; } public FakeBrowser? LastBrowser { get; private set; }
        public Task<IExternalSessionCaptureBrowser> StartAsync(ExternalSessionSiteDescriptor site, CancellationToken ct) { StartCount++; if (startFailure) throw new InvalidOperationException("controlled initialization failure"); LastBrowser = new(); return Task.FromResult<IExternalSessionCaptureBrowser>(LastBrowser); }
    }
    private sealed class FakeBrowser : IExternalSessionCaptureBrowser
    {
        public bool Disposed { get; private set; }
        public Task<ExternalSessionCaptureSnapshot> InspectAsync(CancellationToken ct) => Task.FromResult(new ExternalSessionCaptureSnapshot(new("https://www.linkedin.com/feed"), true, false, false));
        public Task<byte[]> CaptureStorageStateAsync(CancellationToken ct) => Task.FromResult(Encoding.UTF8.GetBytes("{\"cookies\":[],\"origins\":[]}"));
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class SnapshotBrowser(ExternalSessionCaptureSnapshot snapshot) : IExternalSessionCaptureBrowser
    {
        public Task<ExternalSessionCaptureSnapshot> InspectAsync(CancellationToken ct) => Task.FromResult(snapshot);
        public Task<byte[]> CaptureStorageStateAsync(CancellationToken ct) => Task.FromResult(Encoding.UTF8.GetBytes("{\"cookies\":[],\"origins\":[]}"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeDns(IPAddress address) : IExternalHostAddressResolver
    { public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(new[] { address }); }
    private sealed class FakeSessions : IExternalJobSiteSessionStore
    {
        private ExternalJobSiteSessionMetadata? session; public int SaveCount { get; private set; }
        public FakeSessions(bool active, DateTime now) { if (active) session = new(Guid.NewGuid(), Guid.Empty, JobSiteIdentifier.LinkedIn, ExternalJobSiteSessionStatus.Active, now, now, now.AddDays(1), now, false, 1); }
        public Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct) => Task.FromResult(session is null ? null : session with { UserId = userId });
        public Task<ExternalJobSiteSessionMetadata?> GetMetadataAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult(session?.Id == sessionId ? session with { UserId = userId } : null);
        public Task<UsableExternalJobSiteSession?> GetActiveAsync(Guid userId, JobSiteIdentifier site, CancellationToken ct) => Task.FromResult<UsableExternalJobSiteSession?>(null);
        public Task<ExternalJobSiteSessionMetadata> SaveAsync(Guid userId, JobSiteIdentifier site, ReadOnlyMemory<byte> storageState, DateTime expiresAtUtc, long? expectedVersion, CancellationToken ct, bool allowRevokedReplacement = false) { SaveCount++; var now = DateTime.UtcNow; session = new(Guid.NewGuid(), userId, site, ExternalJobSiteSessionStatus.Active, now, now, expiresAtUtc, now, false, (expectedVersion ?? 0) + 1); return Task.FromResult(session); }
        public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> RequireReauthenticationAsync(Guid userId, Guid sessionId, long expectedVersion, CancellationToken ct) => Task.FromResult(false);
    }
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider { private DateTimeOffset current = now; public override DateTimeOffset GetUtcNow() => current; public void Advance(TimeSpan value) => current += value; }
}
