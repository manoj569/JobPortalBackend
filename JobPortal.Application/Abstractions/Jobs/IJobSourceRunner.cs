namespace JobPortal.Application.Abstractions.Jobs;

public interface IJobSourceRunner
{
    Task<JobSourceRunResult> RunAsync(
        Guid jobSourceId,
        CancellationToken cancellationToken = default);
}
