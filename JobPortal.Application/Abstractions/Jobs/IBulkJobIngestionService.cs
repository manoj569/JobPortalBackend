namespace JobPortal.Application.Abstractions.Jobs;

public interface IBulkJobIngestionService
{
    Task PrepareRunAsync(
        IReadOnlyCollection<RawExternalJob> rawJobs,
        CancellationToken cancellationToken = default);
}
