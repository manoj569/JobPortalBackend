using JobPortal.API.HostedServices;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class RegistrationEmailHostedServiceTests
{
    [Fact]
    public async Task FailedIterationIsContainedAndNextIterationCanSucceed()
    {
        var queue = new ControlledOutbox { ThrowOnClaim = true };
        await using var provider = Services(queue);
        var logger = new CapturingLogger<RegistrationEmailHostedService>();
        var worker = new RegistrationEmailHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            logger, TimeSpan.FromMilliseconds(1));

        Assert.False(await worker.RunIterationAsync(default));
        queue.ThrowOnClaim = false;
        Assert.True(await worker.RunIterationAsync(default));
        Assert.Contains(logger.Events, item => item.EventId == 4102 && item.Level == LogLevel.Error);
    }

    [Fact]
    public async Task CancellationStopsIterationCleanly()
    {
        await using var provider = Services(new ControlledOutbox());
        var worker = new RegistrationEmailHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            new CapturingLogger<RegistrationEmailHostedService>(), TimeSpan.FromMilliseconds(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            worker.RunIterationAsync(cancellation.Token));
    }

    private static ServiceProvider Services(ControlledOutbox queue)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRegistrationEmailOutbox>(queue);
        services.AddSingleton<IEmailService, DisabledEmail>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ILogger<RegistrationEmailDispatcher>, CapturingLogger<RegistrationEmailDispatcher>>();
        services.AddScoped<RegistrationEmailDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed class ControlledOutbox : IRegistrationEmailOutbox
    {
        public bool ThrowOnClaim { get; set; }
        public Task EnqueueAsync(RegistrationEmailRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RegistrationEmailRequest?> ClaimDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnClaim) throw new InvalidOperationException("synthetic database outage");
            return Task.FromResult<RegistrationEmailRequest?>(null);
        }
        public Task MarkSentAsync(Guid requestId, DateTime sentAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid requestId, DateTime nextAttemptAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DisabledEmail : IEmailService
    {
        public Task<EmailDeliveryResult> SendPasswordResetAsync(User user, string rawToken, CancellationToken cancellationToken = default) => Task.FromResult(EmailDeliveryResult.Disabled);
        public Task<EmailDeliveryResult> SendApplicationStatusAsync(User user, string jobTitle, JobApplicationStatus status, CancellationToken cancellationToken = default) => Task.FromResult(EmailDeliveryResult.Disabled);
        public Task<EmailDeliveryResult> SendRegistrationVerificationAsync(User user, string rawToken, CancellationToken cancellationToken = default) => Task.FromResult(EmailDeliveryResult.Disabled);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, int EventId)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Add((logLevel, eventId.Id));
    }
}
