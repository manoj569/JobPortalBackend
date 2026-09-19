namespace JobPortal.Application.Abstractions.Jobs;

public interface IJobIngestionService
{
    Task<JobIngestionResult> IngestAsync(
        RawExternalJob rawJob,
        CancellationToken cancellationToken = default);
}
