using System.Collections.Concurrent;
using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.JobAggregation;

// Registered once per process. Entries exist only while a source is in use.
public sealed class JobSourceRunGuard
{
    private readonly ConcurrentDictionary<Guid, byte> active = new();

    public IDisposable Acquire(Guid sourceId)
    {
        if (!active.TryAdd(sourceId, 0))
            throw new ConflictException("This job source is already running or being updated.", "job_source_busy");
        return new Lease(active, sourceId);
    }

    private sealed class Lease(ConcurrentDictionary<Guid, byte> active, Guid sourceId) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                active.TryRemove(sourceId, out _);
        }
    }
}
