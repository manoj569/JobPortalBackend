namespace JobPortal.Application.Abstractions.Jobs;

public interface IBulkJobIngestionService
{
    Task PrepareRunAsync(
        IReadOnlyCollection<RawExternalJob> rawJobs,
        CancellationToken cancellationToken = default);

    void ResetRunAfterFailure() { }
    Task CompleteRunAsync() => Task.CompletedTask;
    JobIngestionRunMetrics? RunMetrics => null;
}

public sealed record JobIngestionRunMetrics(int SaveCalls, double SaveMilliseconds, int PreloadedOwnedJobs,
    double PreloadMilliseconds = 0);

public interface IBatchedJobIngestionService
{
    // Only non-publishable Workday batches use this path. Other providers retain item commits.
    // Empty results for nonempty input select the caller's existing item-processing path.
    Task<IReadOnlyList<JobIngestionResult>> IngestBatchAsync(IReadOnlyList<RawExternalJob> rawJobs,
        CancellationToken cancellationToken = default);
}
