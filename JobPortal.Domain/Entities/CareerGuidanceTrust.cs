using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public enum CareerReviewStatus { Pending = 1, Approved, Rejected, Hidden }
public enum CareerDisputeCategory { ConsultantNoShow = 1, SessionQuality, Misrepresentation, InappropriateConduct, ConfidentialInformationRequest, TechnicalFailure, BillingIssue, Other }
public enum CareerDisputeStatus { Open = 1, UnderReview, AwaitingCandidate, AwaitingConsultant, Resolved, Rejected, Cancelled }
public enum CareerDisputeResolution { None = 1, NoAction, WarningIssued, RefundApproved, RefundDenied, ConsultantRestricted, ReviewHidden, Other }
public enum CareerEvidenceType { Text = 1, Response, AdminNote }

public sealed class CareerGuidanceReview : BaseEntity
{
    public Guid BookingId { get; set; }
    public CareerGuidanceBooking Booking { get; set; } = null!;
    public Guid SessionId { get; set; }
    public CareerGuidanceSession Session { get; set; } = null!;
    public Guid PaymentId { get; set; }
    public CareerGuidancePayment Payment { get; set; } = null!;
    public Guid ConsultantId { get; set; }
    public Guid CandidateUserId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Comment { get; set; }
    public bool IsPublished { get; set; }
    public CareerReviewStatus ModerationStatus { get; set; } = CareerReviewStatus.Pending;
    public string? ModerationReason { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class CareerGuidanceDispute : BaseEntity
{
    public Guid BookingId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid PaymentId { get; set; }
    public CareerGuidancePayment Payment { get; set; } = null!;
    public Guid CandidateUserId { get; set; }
    public Guid ConsultantId { get; set; }
    public CareerDisputeCategory Category { get; set; }
    public string Description { get; set; } = "";
    public bool RequestedRefund { get; set; }
    public CareerDisputeStatus Status { get; set; } = CareerDisputeStatus.Open;
    public CareerDisputeResolution Resolution { get; set; } = CareerDisputeResolution.None;
    public string? AdminNotes { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public ICollection<CareerGuidanceDisputeEvidence> Evidence { get; set; } = new List<CareerGuidanceDisputeEvidence>();
}

public sealed class CareerGuidanceDisputeEvidence : BaseEntity
{
    public Guid DisputeId { get; set; }
    public CareerGuidanceDispute Dispute { get; set; } = null!;
    public Guid SubmittedByUserId { get; set; }
    public Guid RequestId { get; set; }
    public CareerEvidenceType EvidenceType { get; set; }
    public string Description { get; set; } = "";
    public bool IsPrivateToAdmin { get; set; }
}
