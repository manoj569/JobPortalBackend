using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;
using System.Text.Json.Serialization;

namespace JobPortal.Application.Features.HiringDrives;

public sealed record SaveHiringDriveRequest(
    string Title, DateTime StartAtUtc, Guid? CompanyId = null, string? Subtitle = null,
    string? Description = null, string? BadgeText = null, string? BadgeType = null,
    string? ImageUrl = null, string? LocationText = null, string? ExperienceText = null,
    Guid? JobId = null, string? ApplyUrl = null, DateTime? EndAtUtc = null,
    int DisplayOrder = 0, bool IsActive = false, bool IsSponsored = false, bool ShowOnHome = false);
public sealed record HiringDriveQuery(int PageNumber = 1, int PageSize = 20);
public sealed record HiringDriveImpressionsRequest(IReadOnlyCollection<Guid> DriveIds);
public sealed record HiringDriveCompany(Guid Id, string Name, string? LogoUrl);
public sealed record HiringDriveDestination(string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? JobId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JobSlug = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null);
public sealed record HiringDriveClickResponse(string DestinationType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? JobId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JobSlug = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null);
public sealed record PublicHiringDriveResponse(Guid Id, string Title, string? Subtitle,
    string? BadgeText, string? BadgeType, HiringDriveCompany? Company, string? ImageUrl,
    string? LocationText, string? ExperienceText, bool IsSponsored, HiringDriveDestination Destination,
    string? Description = null, DateTime? StartAtUtc = null, DateTime? EndAtUtc = null);
public sealed record AdminHiringDriveResponse(Guid Id, SaveHiringDriveRequest Campaign,
    long ImpressionCount, long ClickCount, decimal Ctr, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc, Guid? CreatedByUserId, Guid? UpdatedByUserId);

public interface IHiringDriveRepository
{
    Task<IReadOnlyCollection<HiringDrive>> PublicAsync(DateTime now, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<HiringDrive>> HomepageAsync(DateTime now, CancellationToken cancellationToken);
    Task<(IReadOnlyCollection<HiringDrive> Items, int Total)> SearchAsync(HiringDriveQuery query, CancellationToken cancellationToken);
    Task<HiringDrive?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CompanyExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> JobExistsAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(HiringDrive drive, CancellationToken cancellationToken);
    Task<HiringDrive?> ClickAsync(Guid id, DateTime now, CancellationToken cancellationToken);
    Task ImpressionsAsync(IReadOnlyCollection<Guid> ids, DateTime now, CancellationToken cancellationToken);
}
public interface IHiringDriveService
{
    Task<IReadOnlyCollection<PublicHiringDriveResponse>> PublicAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PublicHiringDriveResponse>> HomepageAsync(CancellationToken cancellationToken);
    Task<PagedResponse<AdminHiringDriveResponse>> SearchAsync(HiringDriveQuery query, CancellationToken cancellationToken);
    Task<AdminHiringDriveResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<AdminHiringDriveResponse> SaveAsync(Guid actorId, Guid? id, SaveHiringDriveRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid actorId, Guid id, CancellationToken cancellationToken);
    Task<HiringDriveClickResponse> ClickAsync(Guid id, CancellationToken cancellationToken);
    Task ImpressionsAsync(HiringDriveImpressionsRequest request, CancellationToken cancellationToken);
}
