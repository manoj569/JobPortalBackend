using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerCompanies;

public sealed record EmployerCompanySetupRequest(
    string Name,
    string? WebsiteUrl,
    string? Description,
    string? Industry,
    string? Location,
    int? EmployeeCount,
    CompanyType? CompanyType);

public sealed record EmployerCompanyUpdateRequest(
    string Name,
    string? WebsiteUrl,
    string? Description,
    string? Industry,
    string? Location,
    int? EmployeeCount,
    CompanyType? CompanyType);

public sealed record EmployerCompanyResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? WebsiteUrl,
    string? LogoUrl,
    string? Industry,
    string? Location,
    int? EmployeeCount,
    CompanyType? CompanyType,
    bool IsVerified,
    Guid OwnerUserId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
