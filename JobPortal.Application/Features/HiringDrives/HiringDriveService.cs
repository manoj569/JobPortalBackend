using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Domain.Enums;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.HiringDrives;

public sealed class HiringDriveService(IHiringDriveRepository repository, IUnitOfWork unitOfWork,
    TimeProvider clock, IAuditWriter auditWriter, IValidator<SaveHiringDriveRequest> saveValidator,
    IValidator<HiringDriveQuery> queryValidator, IValidator<HiringDriveImpressionsRequest> impressionsValidator) : IHiringDriveService
{
    public async Task<IReadOnlyCollection<PublicHiringDriveResponse>> PublicAsync(CancellationToken cancellationToken) =>
        (await repository.PublicAsync(clock.GetUtcNow().UtcDateTime, cancellationToken)).Select(Public).ToArray();

    public async Task<PagedResponse<AdminHiringDriveResponse>> SearchAsync(HiringDriveQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.ValidateAndThrowAsync(query, cancellationToken);
        var (items, total) = await repository.SearchAsync(query, cancellationToken);
        return new(items.Select(Admin).ToArray(), query.PageNumber, query.PageSize, total);
    }
    public async Task<AdminHiringDriveResponse> GetAsync(Guid id, CancellationToken cancellationToken) => Admin(await Required(id, cancellationToken));

    public async Task<AdminHiringDriveResponse> SaveAsync(Guid actorId, Guid? id, SaveHiringDriveRequest request, CancellationToken cancellationToken)
    {
        await saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (request.CompanyId is { } company && !await repository.CompanyExistsAsync(company, cancellationToken))
            throw new ValidationException("CompanyId must reference an existing company.");
        if (request.JobId is { } job && !await repository.JobExistsAsync(job, cancellationToken))
            throw new ValidationException("JobId must reference an existing job.");
        var drive = id.HasValue ? await Required(id.Value, cancellationToken) : new HiringDrive { CreatedByUserId = actorId };
        drive.Title = request.Title.Trim();
        drive.Subtitle = request.Subtitle;
        drive.Description = request.Description;
        drive.CompanyId = request.CompanyId;
        drive.BadgeText = request.BadgeText;
        drive.BadgeType = request.BadgeType;
        drive.ImageUrl = request.ImageUrl;
        drive.LocationText = request.LocationText;
        drive.ExperienceText = request.ExperienceText;
        drive.JobId = request.JobId;
        drive.ApplyUrl = request.ApplyUrl;
        drive.StartAtUtc = request.StartAtUtc;
        drive.EndAtUtc = request.EndAtUtc;
        drive.DisplayOrder = request.DisplayOrder;
        drive.IsActive = request.IsActive;
        drive.IsSponsored = request.IsSponsored;
        drive.UpdatedByUserId = actorId;
        if (!id.HasValue) await repository.AddAsync(drive, cancellationToken);
        await auditWriter.AppendAsync(new(id.HasValue ? AuditAction.Update : AuditAction.Create, "HiringDrive",
            drive.Id.ToString(), Actor: new(actorId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Admin(drive);
    }
    public async Task DeleteAsync(Guid actorId, Guid id, CancellationToken cancellationToken)
    {
        var drive = await Required(id, cancellationToken);
        drive.IsActive = false;
        drive.IsDeleted = true;
        drive.DeletedAtUtc = clock.GetUtcNow().UtcDateTime;
        drive.UpdatedByUserId = actorId;
        await auditWriter.AppendAsync(new(AuditAction.Delete, "HiringDrive", drive.Id.ToString(),
            Actor: new(actorId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    public async Task<HiringDriveClickResponse> ClickAsync(Guid id, CancellationToken cancellationToken)
    {
        var drive = await repository.ClickAsync(id, clock.GetUtcNow().UtcDateTime, cancellationToken)
            ?? throw new NotFoundException("Hiring drive is unavailable.");
        var destination = Destination(drive);
        return new(destination.Type, destination.JobId, destination.JobSlug, destination.Url);
    }
    public async Task ImpressionsAsync(HiringDriveImpressionsRequest request, CancellationToken cancellationToken)
    {
        await impressionsValidator.ValidateAndThrowAsync(request, cancellationToken);
        await repository.ImpressionsAsync(request.DriveIds.Distinct().ToArray(), clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
    private async Task<HiringDrive> Required(Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Hiring drive was not found.");
    private static HiringDriveDestination Destination(HiringDrive d) => d.Job is { } job
        ? new("internalJob", job.Id, job.Slug)
        : d.JobId is { } jobId
            ? new("internalJob", jobId)
            : new("external", Url: d.ApplyUrl);
    private static PublicHiringDriveResponse Public(HiringDrive d) => new(d.Id, d.Title, d.Subtitle,
        d.BadgeText, d.BadgeType, d.Company is { } c ? new(c.Id, c.Name, c.LogoUrl) : null,
        d.ImageUrl, d.LocationText, d.ExperienceText, d.IsSponsored, Destination(d));
    private static AdminHiringDriveResponse Admin(HiringDrive d) => new(d.Id,
        new(d.Title, d.StartAtUtc, d.CompanyId, d.Subtitle, d.Description, d.BadgeText, d.BadgeType,
            d.ImageUrl, d.LocationText, d.ExperienceText, d.JobId, d.ApplyUrl, d.EndAtUtc, d.DisplayOrder, d.IsActive, d.IsSponsored),
        d.ImpressionCount, d.ClickCount, d.ImpressionCount == 0 ? 0 : (decimal)d.ClickCount / d.ImpressionCount * 100,
        d.CreatedAtUtc, d.UpdatedAtUtc, d.CreatedByUserId, d.UpdatedByUserId);
}
