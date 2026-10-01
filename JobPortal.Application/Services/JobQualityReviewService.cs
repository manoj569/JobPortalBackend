using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Services;

// Current-state review only. This neither publishes nor persists a historical assessment.
public sealed class JobQualityReviewService(IJobRepository jobs, IJobQualityGate gate, TimeProvider clock)
{
    public async Task<JobQualityResult> ReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await jobs.GetByIdAsync(id, includeDeleted: false, cancellationToken)
            ?? throw new NotFoundException("Job was not found.");
        return gate.Evaluate(job, clock.GetUtcNow().UtcDateTime);
    }
}
