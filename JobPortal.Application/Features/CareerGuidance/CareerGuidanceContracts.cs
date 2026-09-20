using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed record ConsultantProfileRequest(string DisplayName, string ProfessionalHeadline, string Bio,
    Guid? CompanyId, string CompanyName, string CurrentRole, decimal YearsOfExperience,
    CareerProfessionalType ProfessionalType, string LinkedInUrl, string[] Languages, string[] Expertise,
    bool AcceptIndependentGuidancePolicy, Guid? Revision = null);
public sealed record ConsultantServiceRequest(string ServiceType, string Title, string Description,
    int DurationMinutes, decimal Price, string Currency, bool IsActive, Guid Revision);
public enum ConsultantReviewAction { Approve = 1, Reject, Suspend, Reactivate }
public sealed record ConsultantReviewRequest(ConsultantReviewAction Action, string Reason, Guid Revision);
public sealed record ConsultantSearchQuery(int PageNumber = 1, int PageSize = 20, Guid? CompanyId = null,
    string? Company = null, string? Role = null, string? Search = null, CareerProfessionalType? ProfessionalType = null,
    string? Language = null, string? Expertise = null, string? ServiceType = null, string? Currency = null,
    decimal? MinPrice = null, decimal? MaxPrice = null, string Sort = "newest");
public sealed record ConsultantAdminQuery(int PageNumber = 1, int PageSize = 20, ConsultantVerificationStatus? Status = null);
public sealed record ConsultantServiceResponse(Guid Id, string ServiceType, string Title, string Description,
    int DurationMinutes, decimal Price, string Currency, bool IsActive);
public sealed record ConsultantPublicResponse(Guid Id, string DisplayName, string ProfessionalHeadline, string Bio,
    Guid? CompanyId, string CompanyName, string CurrentRole, decimal YearsOfExperience, CareerProfessionalType ProfessionalType,
    string[] Languages, string[] Expertise, bool IsAcceptingBookings, string GuidanceDisclaimer,
    IReadOnlyCollection<ConsultantServiceResponse> Services);
public sealed record ConsultantPrivateResponse(ConsultantPublicResponse Profile, Guid UserId, string LinkedInUrl,
    ConsultantVerificationStatus VerificationStatus, string? VerificationMethod, string? VerificationReason,
    Guid? ReviewedByUserId, DateTime? ReviewedAtUtc, DateTime? VerifiedAtUtc, DateTime TermsAcceptedAtUtc,
    string PolicyVersion, Guid Revision, IReadOnlyCollection<ConsultantServiceResponse> Services);

public interface ICareerGuidanceRepository
{
    Task<CareerConsultant?> FindAsync(Guid id, bool publicOnly, CancellationToken ct);
    Task<CareerConsultant?> FindByUserAsync(Guid userId, CancellationToken ct);
    Task<(IReadOnlyCollection<CareerConsultant> Items, int Total)> SearchAsync(ConsultantSearchQuery query, CancellationToken ct);
    Task<(IReadOnlyCollection<CareerConsultant> Items, int Total)> AdminSearchAsync(ConsultantAdminQuery query, CancellationToken ct);
    Task AddAsync(CareerConsultant profile, CancellationToken ct);
}

public interface ICareerGuidanceService
{
    Task<PagedResponse<ConsultantPublicResponse>> SearchAsync(ConsultantSearchQuery query, CancellationToken ct);
    Task<ConsultantPublicResponse> GetAsync(Guid id, CancellationToken ct);
    Task<ConsultantPrivateResponse> MineAsync(Guid actor, CancellationToken ct);
    Task<ConsultantPrivateResponse> ApplyAsync(Guid actor, ConsultantProfileRequest request, CancellationToken ct);
    Task<ConsultantPrivateResponse> UpdateAsync(Guid actor, ConsultantProfileRequest request, CancellationToken ct);
    Task<ConsultantPrivateResponse> SaveServiceAsync(Guid actor, Guid? id, ConsultantServiceRequest request, CancellationToken ct);
    Task<ConsultantPrivateResponse> DeleteServiceAsync(Guid actor, Guid id, Guid revision, CancellationToken ct);
    Task<PagedResponse<ConsultantPrivateResponse>> AdminSearchAsync(Guid actor, ConsultantAdminQuery query, CancellationToken ct);
    Task<ConsultantPrivateResponse> AdminGetAsync(Guid actor, Guid id, CancellationToken ct);
    Task<ConsultantPrivateResponse> ReviewAsync(Guid actor, Guid id, ConsultantReviewRequest request, CancellationToken ct);
}
