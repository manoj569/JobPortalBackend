using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Services;

public sealed class JobIngestionService(
    IJobRepository jobs,
    ICompanyManagementRepository companies,
    ICategoryManagementRepository categories,
    IJobDeduplicationService deduplicationService,
    IJobFingerprintService fingerprintService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IExternalJobCreationLock creationLock,
    IUrlCanonicalizer canonicalizer) : IJobIngestionService
{
    public async Task<JobIngestionResult> IngestAsync(
        RawExternalJob rawJob,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawJob);
        cancellationToken.ThrowIfCancellationRequested();

        var title = TextNormalizer.TrimOrNull(rawJob.Title);
        var companyName = TextNormalizer.TrimOrNull(rawJob.CompanyName);

        if (title is null)
        {
            return Invalid("Job title is required.");
        }

        if (title.Length > 250)
        {
            return Invalid("Job title cannot exceed 250 characters.");
        }

        if (companyName is null)
        {
            return Invalid("Company name is required.");
        }

        if (rawJob.SalaryMin < 0 ||
            rawJob.SalaryMax < 0 ||
            rawJob.SalaryMin > rawJob.SalaryMax)
        {
            return Invalid("Salary range is invalid.");
        }

        // Phase 2:
        // Reject invalid experience ranges instead of storing bad filter data.
        if (rawJob.MinimumExperienceYears < 0 ||
            rawJob.MaximumExperienceYears < 0 ||
            rawJob.MinimumExperienceYears > rawJob.MaximumExperienceYears)
        {
            return Invalid("Experience range is invalid.");
        }

        var location = TextNormalizer.TrimOrNull(rawJob.Location);
        var applicationUrl = TextNormalizer.TrimOrNull(rawJob.ApplicationUrl);

        if (applicationUrl is not null &&
            (!Uri.TryCreate(applicationUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https")))
        {
            return Invalid(
                "ApplicationUrl must be an absolute HTTP or HTTPS URL.");
        }

        var companySlug = SlugGenerator.Generate(companyName);

        var company = await companies.FindByNameOrSlugAsync(
            companyName,
            companySlug,
            cancellationToken);

        if (company is null)
        {
            return new JobIngestionResult
            {
                Outcome = JobIngestionOutcome.CompanyNotFound,
                Message = $"Company '{companyName}' was not found.",
                ExplicitReasonCode =
                    JobIngestionReasonCode.CompanyNotFound
            };
        }

        var duplicate = await deduplicationService.FindDuplicateAsync(
            title,
            company.Name,
            location,
            applicationUrl,
            company.Id,
            cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        await using var creationLease =
            duplicate.IsDuplicate
                ? null
                : await creationLock.AcquireAsync(
                    applicationUrl is null
                        ? null
                        : canonicalizer.Canonicalize(applicationUrl),
                    fingerprintService.GenerateFingerprint(
                        title,
                        company.Name,
                        location),
                    cancellationToken);

        if (creationLease is not null)
        {
            // Another instance may have committed between the initial
            // read and lock.
            duplicate =
                await deduplicationService.FindDuplicateAsync(
                    title,
                    company.Name,
                    location,
                    applicationUrl,
                    company.Id,
                    cancellationToken);

            now = timeProvider.GetUtcNow().UtcDateTime;
        }

        if (duplicate.IsDuplicate &&
            duplicate.MatchedJob is not null)
        {
            // Dedup queries return detached snapshots. Reload the tracked
            // canonical entity so SaveChanges updates only changed
            // metadata, not its graph.
            var matchedJob = await jobs.GetByIdAsync(
                duplicate.MatchedJob.Id,
                cancellationToken: cancellationToken);

            if (matchedJob is null)
            {
                return Invalid(
                    "Matched job is no longer available.");
            }

            // External ingestion must not overwrite curated job data.
            // We only refresh LastSeenAtUtc and fill aggregation metadata
            // when it is currently missing.
            matchedJob.LastSeenAtUtc = now;

            if (matchedJob.FirstSeenAtUtc is null)
            {
                matchedJob.FirstSeenAtUtc = now;
            }

            if (string.IsNullOrWhiteSpace(
                    matchedJob.FingerprintHash))
            {
                matchedJob.FingerprintHash =
                    fingerprintService.GenerateFingerprint(
                        matchedJob.Title,
                        matchedJob.Company.Name,
                        matchedJob.Location);
            }

            await unitOfWork.SaveChangesAsync(
                cancellationToken);

            return new JobIngestionResult
            {
                ExplicitReasonCode =
                    ToReasonCode(duplicate.MatchTypeEnum),

                Outcome =
                    ToOutcome(duplicate.MatchTypeEnum),

                JobId = matchedJob.Id,

                Message =
                    duplicate.MatchTypeEnum ==
                    JobPortal.Application.Abstractions.Jobs
                        .MatchType.Fuzzy &&
                    duplicate.SimilarityScore.HasValue
                        ? $"Matched existing job by fuzzy similarity ({duplicate.SimilarityScore.Value:F3})."
                        : $"Matched existing job by {duplicate.MatchTypeEnum}."
            };
        }

        // Category is required only when creating a brand-new
        // canonical Job.
        if (!rawJob.CategoryId.HasValue ||
            rawJob.CategoryId.Value == Guid.Empty)
        {
            return Invalid(
                "CategoryId is required when creating a new external job.");
        }

        if (!await categories.ExistsAsync(
                rawJob.CategoryId.Value,
                cancellationToken))
        {
            return Invalid(
                $"Category '{rawJob.CategoryId.Value}' does not exist.");
        }

        var id = Guid.NewGuid();

        var job = new Job
        {
            Id = id,

            ReferenceNumber =
                $"JOB-{now:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",

            Title = title,

            Slug =
                $"{SlugGenerator.Generate(title, 240)}-{id.ToString("N")[..8]}",

            Description =
                TextNormalizer.TrimOrNull(
                    rawJob.Description) ?? string.Empty,

            Responsibilities =
                TextNormalizer.TrimOrNull(
                    rawJob.Responsibilities),

            Requirements =
                TextNormalizer.TrimOrNull(
                    rawJob.Requirements),

            Benefits =
                TextNormalizer.TrimOrNull(
                    rawJob.Benefits),

            ApplicationUrl =
                applicationUrl ?? string.Empty,

            Location = location,

            MinimumSalary =
                rawJob.SalaryMin,

            MaximumSalary =
                rawJob.SalaryMax,

            EmploymentType =
                rawJob.EmploymentType ?? default,

            WorkplaceType =
                rawJob.WorkplaceType ?? default,

            // Phase 2 structured filter data.
            // Values remain unknown/default when the external source
            // did not provide reliable information.
            ExperienceLevel =
                rawJob.ExperienceLevel ?? default,

            MinimumExperienceYears =
                rawJob.MinimumExperienceYears,

            MaximumExperienceYears =
                rawJob.MaximumExperienceYears,

            EducationRequirement =
                TextNormalizer.TrimOrNull(
                    rawJob.EducationRequirement),

            CompanyId =
                company.Id,

            Company =
                company,

            CategoryId =
                rawJob.CategoryId.Value,

            // External ingestion never publishes automatically.
            Status =
                JobStatus.Draft,

            FingerprintHash =
                fingerprintService.GenerateFingerprint(
                    title,
                    company.Name,
                    location),

            FirstSeenAtUtc = now,
            LastSeenAtUtc = now
        };

        await jobs.AddAsync(
            job,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return new JobIngestionResult
        {
            Outcome =
                JobIngestionOutcome.Created,

            JobId =
                job.Id,

            Message =
                "External job created as Draft.",

            ExplicitReasonCode =
                JobIngestionReasonCode.None
        };
    }

    private static JobIngestionResult Invalid(
        string message) =>
        new()
        {
            Outcome =
                JobIngestionOutcome.Invalid,

            Message =
                message,

            // Machine-readable classification of the existing
            // validation paths. ApplicationUrl-specific messages
            // are InvalidApplicationUrl; all other source-data
            // validation failures are InvalidSourceData.
            ExplicitReasonCode =
                JobIngestionReasonCodes.FromOutcome(
                    JobIngestionOutcome.Invalid,
                    message)
        };

    private static JobIngestionReasonCode ToReasonCode(
        JobPortal.Application.Abstractions.Jobs.MatchType
            matchType) =>
        matchType switch
        {
            JobPortal.Application.Abstractions.Jobs
                .MatchType.SourceUrl =>
                JobIngestionReasonCode
                    .DuplicateCanonicalUrl,

            JobPortal.Application.Abstractions.Jobs
                .MatchType.Fingerprint =>
                JobIngestionReasonCode
                    .DuplicateFingerprint,

            JobPortal.Application.Abstractions.Jobs
                .MatchType.Fuzzy =>
                JobIngestionReasonCode
                    .DuplicateFuzzyMatch,

            _ =>
                JobIngestionReasonCode.Unknown
        };

    private static JobIngestionOutcome ToOutcome(
        JobPortal.Application.Abstractions.Jobs.MatchType
            matchType) =>
        matchType switch
        {
            JobPortal.Application.Abstractions.Jobs
                .MatchType.SourceUrl =>
                JobIngestionOutcome.MatchedByUrl,

            JobPortal.Application.Abstractions.Jobs
                .MatchType.Fingerprint =>
                JobIngestionOutcome.MatchedByFingerprint,

            JobPortal.Application.Abstractions.Jobs
                .MatchType.Fuzzy =>
                JobIngestionOutcome.MatchedByFuzzy,

            _ =>
                JobIngestionOutcome.Failed
        };
}
