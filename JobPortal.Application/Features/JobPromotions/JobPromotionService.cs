using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.JobPromotions;

public sealed class SaveJobPromotionValidator : AbstractValidator<SaveJobPromotionRequest>
{
    public SaveJobPromotionValidator()
    {
        RuleFor(x => x.DisplayPriority).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => !x.StartsAtUtc.HasValue || !x.EndsAtUtc.HasValue || x.EndsAtUtc > x.StartsAtUtc)
            .WithMessage("EndsAtUtc must be later than StartsAtUtc.");
    }
}

public sealed class JobPromotionService(
    IJobPromotionRepository repository,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    TimeProvider clock,
    IValidator<SaveJobPromotionRequest> saveValidator,
    IValidator<JobPromotionQuery> queryValidator) : IJobPromotionService
{
    private const int PublicLimit = 24;

    public async Task<JobPromotionConfigurationResponse> SaveAsync(Guid actorId, Guid jobId,
        SaveJobPromotionRequest request, CancellationToken cancellationToken)
    {
        await saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (!await repository.JobExistsAsync(jobId, cancellationToken))
            throw new NotFoundException("Job was not found.");
        if (request.IsEnabled && !await repository.IsJobEligibleAsync(jobId, now, cancellationToken))
            throw new ValidationException("Only publicly available jobs can be promoted.");

        var existing = await repository.GetByJobIdAsync(jobId, cancellationToken);
        var promotion = existing
            ?? new JobPromotion { JobId = jobId, CreatedByUserId = actorId };
        promotion.IsEnabled = request.IsEnabled;
        promotion.StartsAtUtc = request.StartsAtUtc?.UtcDateTime;
        promotion.EndsAtUtc = request.EndsAtUtc?.UtcDateTime;
        promotion.DisplayPriority = request.DisplayPriority;
        promotion.UpdatedByUserId = actorId;
        await repository.UpsertAsync(promotion, actorId, cancellationToken);
        await auditWriter.AppendAsync(new(existing is null ? AuditAction.Create : AuditAction.Update, "JobPromotion", jobId.ToString(),
            Actor: new(actorId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var saved = await repository.GetByJobIdAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("The saved job promotion could not be reloaded.");
        return ToConfiguration(saved, await repository.IsJobEligibleAsync(jobId, now, cancellationToken));
    }

    public async Task<JobPromotionConfigurationResponse> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var promotion = await repository.GetByJobIdAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Job promotion was not found.");
        return ToConfiguration(promotion, await repository.IsJobEligibleAsync(jobId,
            clock.GetUtcNow().UtcDateTime, cancellationToken));
    }

    public async Task<PagedResponse<JobPromotionConfigurationResponse>> SearchAsync(
        JobPromotionQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.ValidateAndThrowAsync(query, cancellationToken);
        var (items, total) = await repository.SearchAsync(query, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return new(items.Select(ToConfiguration).ToArray(),
            query.PageNumber, query.PageSize, total);
    }

    public async Task<JobPromotionConfigurationResponse> DisableAsync(Guid actorId, Guid jobId,
        CancellationToken cancellationToken)
    {
        var promotion = await repository.GetByJobIdAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Job promotion was not found.");
        promotion.IsEnabled = false;
        promotion.UpdatedByUserId = actorId;
        await repository.UpsertAsync(promotion, actorId, cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Update, "JobPromotion", jobId.ToString(),
            Actor: new(actorId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var saved = await repository.GetByJobIdAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("The saved job promotion could not be reloaded.");
        return ToConfiguration(saved, await repository.IsJobEligibleAsync(jobId,
            clock.GetUtcNow().UtcDateTime, cancellationToken));
    }

    public async Task<JobPromotionConfigurationResponse> UploadBannerAsync(Guid actorId, Guid jobId,
        JobPromotionBannerUpload upload, CancellationToken cancellationToken)
    {
        var promotion = await repository.GetByJobIdAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Create the promotion configuration before uploading a banner.");
        var (content, extension) = await JobPortal.Application.Features.Support.SupportScreenshotValidation.ReadAsync(
            new JobPortal.Application.Features.Support.SupportScreenshotUpload(upload.Content, upload.Length, upload.FileName, upload.ContentType),
            cancellationToken);
        promotion.BannerImageContent = content;
        promotion.BannerContentType = JobPortal.Application.Features.Support.SupportScreenshotValidation.ContentType(extension);
        promotion.BannerImageUrl = $"/api/job-promotions/{jobId:D}/banner";
        promotion.UpdatedByUserId = actorId;
        await repository.UpsertAsync(promotion, actorId, cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Update, "JobPromotionBanner", jobId.ToString(),
            Actor: new(actorId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var saved = await repository.GetByJobIdAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("The saved job promotion could not be reloaded.");
        return ToConfiguration(saved, await repository.IsJobEligibleAsync(jobId,
            clock.GetUtcNow().UtcDateTime, cancellationToken));
    }

    public async Task<IReadOnlyCollection<PromotedJobResponse>> ActiveAsync(CancellationToken cancellationToken)
    {
        var promotions = await repository.ActiveAsync(clock.GetUtcNow().UtcDateTime, PublicLimit, cancellationToken);
        return promotions.Select(x => new PromotedJobResponse(
            x.JobId, x.JobSlug, x.Title, x.CompanyName, x.CompanyLogoUrl,
            x.BannerImageUrl, x.JobCategory, x.Location,
            x.WorkplaceType, x.EmploymentType, x.MinimumExperienceYears,
            x.MaximumExperienceYears, x.EducationRequirement, x.MinimumSalary,
            x.MaximumSalary, x.CurrencyCode, x.Skills,
            x.PostedAtUtc, $"/dashboard/jobs/{Uri.EscapeDataString(x.JobSlug)}"))
            .ToArray();
    }

    public async Task<JobPromotionBanner> BannerAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var promotion = await repository.GetActiveBannerAsync(jobId, clock.GetUtcNow().UtcDateTime, cancellationToken)
            ?? throw new NotFoundException("Promotion banner was not found.");
        return new(promotion.BannerImageContent!, promotion.BannerContentType!);
    }

    private static JobPromotionConfigurationResponse ToConfiguration(JobPromotion promotion, bool eligible) =>
        new(promotion.JobId, promotion.Job.Slug, promotion.Job.Title, promotion.IsEnabled, eligible,
            promotion.BannerImageUrl, promotion.StartsAtUtc, promotion.EndsAtUtc, promotion.DisplayPriority,
            promotion.CreatedAtUtc, promotion.UpdatedAtUtc, promotion.CreatedByUserId, promotion.UpdatedByUserId);

    private static JobPromotionConfigurationResponse ToConfiguration(JobPromotionAdminItem promotion) =>
        new(promotion.JobId, promotion.JobSlug, promotion.JobTitle, promotion.IsEnabled, promotion.IsEligible,
            promotion.BannerImageUrl, promotion.StartsAtUtc, promotion.EndsAtUtc, promotion.DisplayPriority,
            promotion.CreatedAtUtc, promotion.UpdatedAtUtc, promotion.CreatedByUserId, promotion.UpdatedByUserId);
}

public sealed class JobPromotionQueryValidator : AbstractValidator<JobPromotionQuery>
{
    public JobPromotionQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
