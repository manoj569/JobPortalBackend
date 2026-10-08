using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Referrals;

public sealed record CreateReferralRequest(string? CandidateMessage);
public sealed record RejectReferralRequest(string? Reason);
public sealed record SubmitReferralRequest(string? ReferralSubmissionReference);
public sealed record ReferralRequestResponse(Guid Id, Guid ReferralId, Guid JobId, string JobTitle, string CompanyName,
    string ReferrerLabel, ReferralRequestStatus Status, DateTime RequestedAtUtc, DateTime ExpiresAtUtc,
    DateTime? AcceptedAtUtc, DateTime? RejectedAtUtc, DateTime? ExpiredAtUtc, DateTime? ReferralSubmittedAtUtc,
    DateTime? CandidateConfirmedAtUtc, DateTime? NotReceivedAtUtc, string? CandidateMessage,
    bool CanViewContact);
public sealed record ReferralCandidateCard(string DisplayName, decimal? YearsOfExperience, IReadOnlyCollection<string> Skills,
    string? CurrentCompany, string? Location, string? Availability, bool ResumeAvailable);
public sealed record ReferrerRequestResponse(ReferralRequestResponse Request, ReferralCandidateCard Candidate,
    string? RejectionReason, string? ReferralSubmissionReference);
public sealed record AdminReferralRequestResponse(ReferrerRequestResponse Details, Guid CandidateUserId, Guid ReferrerUserId);
public sealed record ReferralQuotaResponse(int Limit, int AcceptedConnections, int RemainingConnections,
    DateTime? PeriodStartUtc, DateTime? PeriodEndUtc, bool HasActiveAccess);
public sealed record MyReferralRequestsResponse(PagedResponse<ReferralRequestResponse> Requests, ReferralQuotaResponse Quota);
public sealed record ReferralMetricsResponse(int JobsPosted, int RequestsReceived, int RequestsAccepted,
    int ReferralsSubmitted, int CandidateConfirmedReferrals, decimal CompletionRate);

public interface IReferralMarketplaceRepository
{
    Task<T> WriteAsync<T>(Guid candidateId, Guid referralId, Func<Task<T>> action, CancellationToken ct);
    Task<User?> UserAsync(Guid id, CancellationToken ct);
    Task<JobReferral?> OpportunityAsync(Guid id, CancellationToken ct);
    Task<Membership?> MembershipAsync(Guid candidateId, DateTime now, CancellationToken ct);
    Task<ReferralRequest?> RequestAsync(Guid id, Guid? candidateId, Guid? referrerId, CancellationToken ct);
    Task<ReferralRequest?> ForOpportunityAsync(Guid candidateId, Guid referralId, CancellationToken ct);
    Task<int> AcceptedForOpportunityAsync(Guid referralId, CancellationToken ct);
    Task<int> AcceptedForPeriodAsync(Guid candidateId, Guid membershipId, DateTime start, CancellationToken ct);
    Task AddAsync(ReferralRequest request, CancellationToken ct);
    Task<(IReadOnlyCollection<ReferralRequest> Items, int Total)> ListAsync(Guid? candidateId, Guid? referrerId,
        ReferralRequestStatus? status, bool issuesOnly, int page, int size, DateTime now, CancellationToken ct);
    Task<ReferralMetricsResponse> MetricsAsync(Guid referrerId, CancellationToken ct);
}
