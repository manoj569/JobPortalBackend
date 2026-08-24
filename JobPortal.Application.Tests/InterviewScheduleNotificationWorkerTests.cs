using JobPortal.API.HostedServices;
using JobPortal.Application.Abstractions.InterviewInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class InterviewScheduleNotificationWorkerTests
{
    [Fact]
    public void ClaimTransactionRunsInsideConfiguredExecutionStrategy()
    {
        var source = File.ReadAllText(SourcePath("JobPortal.Persistence", "Repositories", "InterviewInsightRepository.cs"));
        var strategy = source.IndexOf("Database.CreateExecutionStrategy()", StringComparison.Ordinal);
        var execute = source.IndexOf("strategy.ExecuteAsync", strategy, StringComparison.Ordinal);
        var transaction = source.IndexOf("Database.BeginTransactionAsync", execute, StringComparison.Ordinal);
        var selection = source.IndexOf("CandidateInterviewSchedules.AsNoTracking()", transaction, StringComparison.Ordinal);
        var claim = source.IndexOf("ExecuteUpdateAsync", selection, StringComparison.Ordinal);
        var save = source.IndexOf("SaveChangesAsync", claim, StringComparison.Ordinal);
        var commit = source.IndexOf("CommitAsync", save, StringComparison.Ordinal);
        Assert.True(strategy >= 0 && strategy < execute);
        Assert.True(execute < transaction && transaction < selection && selection < claim && claim < save && save < commit);
    }

    [Fact]
    public async Task FailedIterationIsRetriedWithoutTerminatingWorker()
    {
        var processor = new ScriptedProcessor(async (call, token) =>
        {
            if (call == 1) throw new InvalidOperationException("synthetic failure");
            return await Task.FromResult(0);
        });
        await using var provider = Services(processor);
        var worker = Worker(provider, TimeSpan.Zero);
        await worker.StartAsync(CancellationToken.None);
        await processor.WaitForCallsAsync(2);
        await worker.StopAsync(CancellationToken.None);
        Assert.True(processor.CallCount >= 2);
    }

    [Fact]
    public async Task RetriedAmbiguousAttemptDoesNotDuplicateNotification()
    {
        var notifications = 0;
        var claimed = false;
        var processor = new ScriptedProcessor((call, token) =>
        {
            if (!claimed)
            {
                claimed = true;
                notifications++;
                throw new TimeoutException("synthetic post-commit failure");
            }
            return Task.FromResult(0);
        });
        await using var provider = Services(processor);
        var worker = Worker(provider, TimeSpan.Zero);
        await worker.StartAsync(CancellationToken.None);
        await processor.WaitForCallsAsync(2);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task CancellationStopsWorkerCleanly()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ScriptedProcessor(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });
        await using var provider = Services(processor);
        var worker = Worker(provider, TimeSpan.Zero);
        await worker.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ServiceProvider Services(IInterviewScheduleNotificationProcessor processor) =>
        new ServiceCollection().AddSingleton(processor).BuildServiceProvider();

    private static InterviewScheduleNotificationHostedService Worker(ServiceProvider provider, TimeSpan retryDelay) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<InterviewScheduleNotificationHostedService>.Instance, retryDelay);

    private static string SourcePath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine([root, .. parts]);
    }

    private sealed class ScriptedProcessor(Func<int, CancellationToken, Task<int>> action) : IInterviewScheduleNotificationProcessor
    {
        private readonly TaskCompletionSource<int> calls = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);
        public async Task<int> CreateDueScheduleNotificationsAsync(DateTime nowUtc, CancellationToken ct)
        {
            var current = Interlocked.Increment(ref callCount);
            calls.TrySetResult(current);
            return await action(current, ct);
        }
        public async Task WaitForCallsAsync(int expected)
        {
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (CallCount < expected && DateTime.UtcNow < timeout)
                await Task.Delay(10);
            Assert.True(CallCount >= expected, $"Expected {expected} calls but observed {CallCount}.");
        }
    }
}
