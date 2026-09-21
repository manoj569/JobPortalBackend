using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed record CareerAnalyticsQuery(DateTime? FromUtc = null, DateTime? ToUtc = null)
{
    public (DateTime From, DateTime To) Normalize(DateTime nowUtc)
    {
        var to = ToUtc ?? nowUtc;
        if (!FromUtc.HasValue && to.Ticks < TimeSpan.FromDays(30).Ticks)
            throw new ArgumentException("Analytics UTC range must be positive and no longer than 366 days.");
        var from = FromUtc ?? to.AddDays(-30);
        if (from.Kind != DateTimeKind.Utc || to.Kind != DateTimeKind.Utc || from >= to || (to - from).TotalDays > 366)
            throw new ArgumentException("Analytics UTC range must be positive and no longer than 366 days.");
        return (from, to);
    }
}
public sealed record CareerGuidanceAdminAnalytics(DateTime FromUtc, DateTime ToUtc, int Applications, int PendingVerification, int VerifiedConsultants, int RejectedConsultants, int SuspendedConsultants, int ActiveVerifiedConsultants, int TotalBookings, int PendingBookings, int ConfirmedBookings, int CompletedBookings, int CancelledBookings, int CandidateNoShows, int ConsultantNoShows, decimal GrossPaymentVolume, int CapturedPaymentCount, int FailedPaymentCount, decimal RefundedAmount, int RefundCount, decimal PlatformCommission, decimal ConsultantNetEarnings, int ScheduledSessions, int ReadySessions, int InProgressSessions, int CompletedSessions, int CancelledSessions, int SessionNoShows, int ReviewCount, int PublishedReviewCount, decimal? AverageRating, int OpenDisputes, int ResolvedDisputes, int RefundApprovedDisputes);
public sealed record CareerGuidanceConsultantAnalytics(Guid ConsultantId, DateTime FromUtc, DateTime ToUtc, int TotalBookings, int ConfirmedBookings, int CompletedSessions, int Cancellations, int CandidateNoShows, int ConsultantNoShows, decimal? AverageRating, int PublishedReviewCount, decimal GrossBookedValue, decimal ConsultantEarnings, decimal PendingEarnings, decimal HeldEarnings, int RefundedBookings, int OpenDisputes, DateTime? NextUpcomingSession);
public sealed record CareerGuidanceCandidateSummary(Guid CandidateUserId, int UpcomingBookings, int CompletedBookings, int CancelledBookings, int PendingReviewCount, int ActiveDisputeCount, DateTime? NextUpcomingSession);
