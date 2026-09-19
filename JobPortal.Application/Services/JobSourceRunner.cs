using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;

namespace JobPortal.Application.Services;

public sealed class JobSourceRunner(
    IJobSourceRepository sources,
    IEnumerable<IExternalJobProvider> providers,
    IJobIngestionService ingestionService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IJobSourceCategoryResolver categoryResolver) : IJobSourceRunner
{
    public async Task<JobSourceRunResult> RunAsync(
        Guid jobSourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var source = await sources.GetByIdAsync(
            jobSourceId,
            cancellationToken);

        if (source is null)
        {
            return new JobSourceRunResult
            {
                JobSourceId = jobSourceId,
                Succeeded = false,
                Error = "Job source was not found."
            };
        }

        if (!source.IsActive)
        {
            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = "Job source is inactive."
            };
        }

        var provider = providers.FirstOrDefault(
            x => x.AtsType == source.AtsType);

        if (provider is null)
        {
            // Unsupported configurations are failed attempts too: automatic polling
            // must respect their ScanIntervalMinutes rather than retry every poll.
            source.LastRunAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            source.LastError = $"No provider is registered for ATS type '{source.AtsType}'.";
            source.ConsecutiveFailures++;
            sources.Update(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = source.LastError
            };
        }

        var previousSuccessfulRun = source.LastSuccessfulRunAtUtc;
        var previousFailures = source.ConsecutiveFailures;

        try
        {
            var rawJobs = await provider.FetchJobsAsync(
                source,
                cancellationToken);
            var categoryId = await categoryResolver.ResolveCategoryIdAsync(source, cancellationToken);

            var created = 0;
            var matched = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var rawJob in rawJobs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await ingestionService.IngestAsync(
                        rawJob with { CategoryId = categoryId },
                        cancellationToken);

                    switch (result.Outcome)
                    {
                        case JobIngestionOutcome.Created:
                            created++;
                            break;

                        case JobIngestionOutcome.MatchedByUrl:
                        case JobIngestionOutcome.MatchedByFingerprint:
                        case JobIngestionOutcome.MatchedByFuzzy:
                            matched++;
                            break;

                        case JobIngestionOutcome.CompanyNotFound:
                        case JobIngestionOutcome.Invalid:
                            skipped++;
                            break;

                        default:
                            failed++;
                            break;
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A malformed/problematic individual record must not
                    // abort processing of the remaining provider records.
                    unitOfWork.ResetAfterFailure();
                    failed++;
                }
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            source.LastRunAtUtc = now;
            source.LastSuccessfulRunAtUtc = now;
            source.LastError = null;
            source.ConsecutiveFailures = 0;

            sources.Update(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                TotalReceived = rawJobs.Count,
                Created = created,
                Matched = matched,
                Skipped = skipped,
                Failed = failed,
                Succeeded = true
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            unitOfWork.ResetAfterFailure();

            var now = timeProvider.GetUtcNow().UtcDateTime;

            source.LastRunAtUtc = now;
            // Exception messages can contain response bodies, URLs or credentials.
            source.LastError = "External job source run failed.";
            source.LastSuccessfulRunAtUtc = previousSuccessfulRun;
            source.ConsecutiveFailures = previousFailures + 1;

            sources.Update(source);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new JobSourceRunResult
            {
                JobSourceId = source.Id,
                Succeeded = false,
                Error = source.LastError
            };
        }
    }
}
