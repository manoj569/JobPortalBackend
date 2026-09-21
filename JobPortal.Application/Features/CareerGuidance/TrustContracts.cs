using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerTrustOptions
{
    public int ReviewEditWindowDays { get; set; } = 7;
    public int DisputeOpenWindowHours { get; set; } = 72;
    public bool IsValid() => ReviewEditWindowDays is >= 1 and <= 30 && DisputeOpenWindowHours is >= 24 and <= 720;
}
public sealed record CareerReviewRequest(int Rating, string? Title, string? Comment, Guid? Revision = null);
public sealed record CareerReviewModerationRequest(Guid Revision, CareerReviewStatus Status, string Reason);
public sealed record CareerDisputeRequest(CareerDisputeCategory Category, string Description, bool RequestedRefund = false);
public sealed record CareerEvidenceRequest(Guid Revision, Guid RequestId, CareerEvidenceType EvidenceType, string Description, bool IsPrivateToAdmin = false);
public sealed record CareerDisputeStatusRequest(Guid Revision, CareerDisputeStatus Status, string AdminNotes);
public sealed record CareerDisputeResolveRequest(Guid Revision, CareerDisputeResolution Resolution, string AdminNotes);
public sealed record CareerTrustQuery(int PageNumber = 1, int PageSize = 20, CareerDisputeStatus? Status = null, CareerReviewStatus? ModerationStatus = null);
public sealed record CareerRatingSummary(Guid ConsultantId, decimal? AverageRating, int ReviewCount);
public sealed record CareerPublicReview(Guid Id, int Rating, string? Title, string? Comment, string CandidateDisplayName, DateTime CreatedAtUtc);
public sealed record CareerReviewResponse(Guid Id, Guid BookingId, Guid SessionId, Guid ConsultantId, int Rating, string? Title,
    string? Comment, bool IsPublished, CareerReviewStatus ModerationStatus, string? ModerationReason, bool IsDeleted, DateTime CreatedAtUtc, Guid Revision);
public sealed record CareerEvidenceResponse(Guid Id, CareerEvidenceType EvidenceType, string Description, bool IsPrivateToAdmin, DateTime CreatedAtUtc);
public sealed record CareerDisputeResponse(Guid Id, Guid BookingId, Guid? SessionId, CareerDisputeCategory Category, string Description,
    bool RequestedRefund, CareerDisputeStatus Status, CareerDisputeResolution Resolution, string? AdminNotes, DateTime SubmittedAtUtc,
    DateTime? ResolvedAtUtc, Guid Revision, IReadOnlyCollection<CareerEvidenceResponse> Evidence);

public interface ICareerTrustRepository
{
    Task<CareerGuidancePayment?> PaymentAsync(Guid bookingId, CancellationToken ct);
    Task<CareerGuidanceSession?> SessionAsync(Guid bookingId, CancellationToken ct);
    Task<CareerGuidanceReview?> ReviewAsync(Guid id, bool byBooking, CancellationToken ct);
    Task<CareerGuidanceDispute?> DisputeAsync(Guid id, bool byBooking, CancellationToken ct);
    Task<PagedResponse<CareerGuidanceReview>> ReviewsAsync(CareerTrustQuery query, CancellationToken ct);
    Task<PagedResponse<CareerPublicReview>> PublicReviewsAsync(Guid consultantId, CareerTrustQuery query, CancellationToken ct);
    Task<PagedResponse<CareerGuidanceDispute>> DisputesAsync(Guid actor, CareerSessionAudience audience, CareerTrustQuery query, CancellationToken ct);
    void Add(CareerGuidanceReview review);
    void Add(CareerGuidanceDispute dispute);
    Task SaveAsync(CancellationToken ct);
}
