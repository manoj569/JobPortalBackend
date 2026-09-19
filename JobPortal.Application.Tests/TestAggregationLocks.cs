using System.Collections.Concurrent;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Persistence.Postgres;

namespace JobPortal.Application.Tests;

internal sealed class TestAggregationLocks : IJobSourceExecutionLock, IExternalJobCreationLock
{
    private readonly ConcurrentDictionary<Guid, byte> sources = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> creations = new();
    public bool Busy { get; set; }
    public int SourceReleases;
    public int CreationAcquisitions;
    public int CreationReleases;
    public Action? BeforeCreation { get; set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid jobSourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Busy || !sources.TryAdd(jobSourceId, 0)) return Task.FromResult<IAsyncDisposable?>(null);
        return Task.FromResult<IAsyncDisposable?>(new Lease(() => { sources.TryRemove(jobSourceId, out _); Interlocked.Increment(ref SourceReleases); }));
    }

    public async Task<IAsyncDisposable> AcquireAsync(string? canonicalUrl, string fingerprintHash, CancellationToken cancellationToken = default)
    {
        var held = new List<SemaphoreSlim>();
        try
        {
            foreach (var key in PostgresExternalJobCreationLock.CreateKeys(canonicalUrl, fingerprintHash))
            {
                var gate = creations.GetOrAdd(key, _ => new(1, 1));
                await gate.WaitAsync(cancellationToken);
                held.Add(gate);
            }
            Interlocked.Increment(ref CreationAcquisitions);
            BeforeCreation?.Invoke();
            return new Lease(() => { foreach (var gate in held) gate.Release(); Interlocked.Increment(ref CreationReleases); });
        }
        catch { foreach (var gate in held) gate.Release(); throw; }
    }

    private sealed class Lease(Action release) : IAsyncDisposable
    {
        private int disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) release();
            return ValueTask.CompletedTask;
        }
    }
}
