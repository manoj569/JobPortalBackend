using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.JobAggregation;

public sealed record JobSourceSearchQuery(
    int PageNumber = 1, int PageSize = 20, Guid? CompanyId = null,
    AtsType? AtsType = null, bool? IsActive = null);

// Category mapping is operator configuration, not persisted by these endpoints.
public sealed record SaveJobSourceRequest(
    Guid CompanyId, string CareerPageUrl, AtsType AtsType, string? AtsIdentifier,
    bool IsActive = true, int ScanIntervalMinutes = 720);

public sealed record JobSourceResponse(
    Guid Id, Guid CompanyId, string CompanyName, string CareerPageUrl,
    AtsType AtsType, string? AtsIdentifier, bool IsActive, int ScanIntervalMinutes,
    DateTime? LastRunAtUtc, DateTime? LastSuccessfulRunAtUtc, string? LastError,
    int ConsecutiveFailures, Guid? CategoryId, string? CategoryName,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
