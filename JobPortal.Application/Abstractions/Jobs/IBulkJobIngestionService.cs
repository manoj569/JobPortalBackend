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

public sealed record JobIngestionRunMetrics(int SaveCalls, double SaveMilliseconds, int PreloadedOwnedJobs);
