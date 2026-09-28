using System.Reflection;
using System.Security.Claims;
using JobPortal.API.Hubs;
using JobPortal.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class NotificationRealtimeTests
{
    [Theory]
    [InlineData("Candidate", true)]
    [InlineData("Consultant", false)]
    [InlineData("Referrer", false)]
    public async Task HubConnectsAuthenticatedRolesButBrowserMethodsRemainCandidateOnly(string role, bool allowed)
    {
        using var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) }, "test"));
        var hubAttributes = typeof(ExternalSessionCaptureHub).GetCustomAttributes<AuthorizeAttribute>().ToArray();
        var hubPolicy = (await AuthorizationPolicy.CombineAsync(provider, hubAttributes))!;
        Assert.True((await authorization.AuthorizeAsync(user, null, hubPolicy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(), null, hubPolicy)).Succeeded);
        foreach (var method in typeof(ExternalSessionCaptureHub).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                     .Where(m => m.Name != nameof(ExternalSessionCaptureHub.OnDisconnectedAsync)))
        {
            var policy = (await AuthorizationPolicy.CombineAsync(provider, hubAttributes.Concat(method.GetCustomAttributes<AuthorizeAttribute>())))!;
            Assert.Equal(allowed, (await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserIdentityComesFromAuthenticatedClaimsAndIsCanonical(bool authenticated)
    {
        var id = Guid.NewGuid();
        await using var connection = new DefaultConnectionContext();
        connection.Features.Set<IConnectionUserFeature>(new UserFeature
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString("D").ToUpperInvariant()) }, authenticated ? "test" : null))
        });
        var context = new HubConnectionContext(connection, new HubConnectionContextOptions(), NullLoggerFactory.Instance);
        Assert.Equal(authenticated ? id.ToString("D") : null, new NotificationUserIdProvider().GetUserId(context));
    }

    [Fact]
    public async Task RealtimeTargetsOnlyDurableRecipientAndSafeInboxDto()
    {
        var hub = new HubContext();
        var notification = new Notification { UserId = Guid.NewGuid(), Title = "Safe title", Message = "Safe body", BusinessKey = "internal-key" };
        await new NotificationRealtime(hub).PublishAsync(notification, default);
        Assert.Equal(notification.UserId.ToString("D"), hub.Client.Target);
        Assert.Equal("NotificationReceived", hub.Client.Method);
        var dto = Assert.IsType<JobPortal.Application.Features.Dashboard.NotificationResponse>(Assert.Single(hub.Client.Args!));
        Assert.Equal(notification.Id, dto.Id);
        Assert.Null(dto.GetType().GetProperty("UserId"));
        Assert.Null(dto.GetType().GetProperty("BusinessKey"));
    }

    private sealed class UserFeature : IConnectionUserFeature { public ClaimsPrincipal? User { get; set; } = new(); }
    private sealed class HubContext : IHubContext<ExternalSessionCaptureHub>
    {
        public RealtimeClients Client { get; } = new();
        public IHubClients Clients => Client;
        public IGroupManager Groups => throw new NotSupportedException();
    }
    private sealed class RealtimeClients : IHubClients, IClientProxy
    {
        public string? Target { get; private set; }
        public string? Method { get; private set; }
        public object?[]? Args { get; private set; }
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        { Method = method; Args = args; return Task.CompletedTask; }
        public IClientProxy User(string userId) { Target = userId; return this; }
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
