using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Referrals;

public sealed class ReferralMarketplaceService(IReferralMarketplaceRepository repository, NotificationOutbox outbox,
    IAuditWriter audit, IResumeStorage resumes, TimeProvider clock)
{
    public const int ConnectionLimit = 10;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ReferralRequestResponse> CreateAsync(Guid candidateId, Guid referralId, CreateReferralRequest input, CancellationToken ct)
    {
        var message = Bounded(input.CandidateMessage, 2000);
        return await repository.WriteAsync(candidateId, referralId, async () =>
        {
            await Candidate(candidateId, ct);
            var opportunity = await repository.OpportunityAsync(referralId, ct) ?? throw Missing();
            RequireOpportunity(opportunity);
            if (opportunity.ReferrerUserId == candidateId) throw new BadRequestException("You cannot request your own opportunity.", "INVALID_REFERRAL_REQUEST");
            var membership = await Access(candidateId, ct);
            if (await repository.ForOpportunityAsync(candidateId, referralId, ct) is not null)
                throw new ConflictException("This opportunity was already requested.", "REFERRAL_ALREADY_REQUESTED");
            await RequireCapacity(opportunity, membership, candidateId, ct);
            var now = Now;
            var request = new ReferralRequest { JobReferralId = referralId, ReferrerUserId = opportunity.ReferrerUserId,
                CandidateUserId = candidateId, RequestedAtUtc = now, ExpiresAtUtc = now.AddHours(48), CandidateMessage = message,
                JobReferral = opportunity };
            // Repository attaches the opportunity unchanged and inserts the new request.
            await repository.AddAsync(request, ct);
            await Event(request, NotificationSource.ReferralRequested, request.ReferrerUserId, "requested", ct);
            return Map(request, true);
        }, ct);
    }

    public async Task<MyReferralRequestsResponse> MineAsync(Guid candidateId, int page, int size, CancellationToken ct)
    {
        Page(page, size); await Candidate(candidateId, ct);
        var quota = await QuotaAsync(candidateId, ct);
        var rows = await repository.ListAsync(candidateId, null, null, false, page, size, Now, ct);
        return new(new(rows.Items.Select(x => Map(x, quota.HasActiveAccess)).ToArray(), page, size, rows.Total), quota);
    }
    public async Task<ReferralQuotaResponse> QuotaAsync(Guid candidateId, CancellationToken ct)
    {
        var membership = await repository.MembershipAsync(candidateId, Now, ct);
        if (membership is null) return new(ConnectionLimit, 0, 0, null, null, false);
        var (start, end) = Period(membership, Now);
        var count = await repository.AcceptedForPeriodAsync(candidateId, membership.Id, start, ct);
        return new(ConnectionLimit, count, Math.Max(0, ConnectionLimit - count), start, end, true);
    }
    public async Task<ReferralRequestResponse> CandidateDetailAsync(Guid candidateId, Guid id, CancellationToken ct)
    {
        await Candidate(candidateId, ct);
        var row = await repository.RequestAsync(id, candidateId, null, ct) ?? throw Missing();
        return Map(row, await repository.MembershipAsync(candidateId, Now, ct) is not null);
    }
    public async Task<PagedResponse<ReferrerRequestResponse>> InboxAsync(Guid referrerId, ReferralRequestStatus? status, int page, int size, CancellationToken ct)
    {
        Page(page, size); ValidateStatus(status); await Active(referrerId, ct);
        var rows = await repository.ListAsync(null, referrerId, status, false, page, size, Now, ct);
        return new(rows.Items.Select(Card).ToArray(), page, size, rows.Total);
    }
    public async Task<ReferrerRequestResponse> ReferrerDetailAsync(Guid referrerId, Guid id, CancellationToken ct)
    {
        await Active(referrerId, ct);
        return Card(await repository.RequestAsync(id, null, referrerId, ct) ?? throw Missing());
    }
    public async Task<PagedResponse<AdminReferralRequestResponse>> AdminListAsync(bool issuesOnly, ReferralRequestStatus? status, int page, int size, CancellationToken ct)
    {
        Page(page, size); ValidateStatus(status);
        var rows = await repository.ListAsync(null, null, status, issuesOnly, page, size, Now, ct);
        return new(rows.Items.Select(x => new AdminReferralRequestResponse(Card(x), x.CandidateUserId, x.ReferrerUserId)).ToArray(), page, size, rows.Total);
    }
    public async Task<AdminReferralRequestResponse> AdminDetailAsync(Guid id, CancellationToken ct)
    {
        var row = await repository.RequestAsync(id, null, null, ct) ?? throw Missing();
        return new(Card(row), row.CandidateUserId, row.ReferrerUserId);
    }

    public Task<ReferrerRequestResponse> AcceptAsync(Guid referrerId, Guid id, CancellationToken ct) => ReferrerMutation(referrerId, id, async row =>
    {
        if (row.IsSuccessful) return false;
        RequireRequested(row);
        RequireOpportunity(row.JobReferral); await Candidate(row.CandidateUserId, ct);
        var membership = await Access(row.CandidateUserId, ct);
        await RequireCapacity(row.JobReferral, membership, row.CandidateUserId, ct);
        var (start, end) = Period(membership, Now);
        return row.Accept(Now, membership.Id, start, end);
    }, NotificationSource.ReferralAccepted, "accepted", ct);
    public Task<ReferrerRequestResponse> RejectAsync(Guid referrerId, Guid id, RejectReferralRequest input, CancellationToken ct)
    {
        var reason = Bounded(input.Reason, 1000);
        return ReferrerMutation(referrerId, id, row => Task.FromResult(row.Reject(Now, reason)), NotificationSource.ReferralRequestRejected, "rejected", ct);
    }
    public Task<ReferrerRequestResponse> SubmitAsync(Guid referrerId, Guid id, SubmitReferralRequest input, CancellationToken ct)
    {
        var reference = Bounded(input.ReferralSubmissionReference, 250);
        return ReferrerMutation(referrerId, id, row => { RequireOpportunity(row.JobReferral); return Task.FromResult(row.Submit(Now, reference)); },
            NotificationSource.ReferralSubmitted, "submitted", ct);
    }
    public Task<ReferralRequestResponse> ConfirmAsync(Guid candidateId, Guid id, CancellationToken ct) =>
        CandidateMutation(candidateId, id, row => row.Confirm(Now), NotificationSource.ReferralConfirmed, "confirmed", ct);
    public Task<ReferralRequestResponse> NotReceivedAsync(Guid candidateId, Guid id, CancellationToken ct) =>
        CandidateMutation(candidateId, id, row => row.ReportNotReceived(Now), NotificationSource.ReferralNotReceived, "not-received", ct);

    public async Task<ReferrerContactDetailsResponse> ContactAsync(Guid candidateId, Guid id, CancellationToken ct)
    {
        await Candidate(candidateId, ct);
        var row = await repository.RequestAsync(id, candidateId, null, ct) ?? throw Missing();
        if (!row.IsSuccessful || row.ReferrerUserId != row.JobReferral.ReferrerUserId)
            throw new AppException("Acceptance is required before contact access.", 403, "REFERRAL_CONTACT_NOT_AVAILABLE");
        await Access(candidateId, ct); RequireOpportunity(row.JobReferral);
        var opportunity = row.JobReferral; var user = opportunity.ReferrerUser;
        return new("Employee Referrer", opportunity.ShowLinkedIn ? user.LinkedInUrl : null,
            opportunity.ShowEmail ? user.Email : null, opportunity.ShowPhone ? user.PhoneNumber : null);
    }
    public async Task<ReferrerContactDetailsResponse?> ContactForOpportunityAsync(Guid candidateId, Guid referralId, CancellationToken ct)
    {
        var row = await repository.ForOpportunityAsync(candidateId, referralId, ct);
        if (row is null || row.IsDeleted || !row.IsSuccessful) return null;
        return await ContactAsync(candidateId, row.Id, ct);
    }
    public async Task<ResumeDownload> ResumeAsync(Guid referrerId, Guid id, CancellationToken ct)
    {
        await Active(referrerId, ct);
        var row = await repository.RequestAsync(id, null, referrerId, ct) ?? throw Missing();
        RequireOpportunity(row.JobReferral);
        if (row.EffectiveStatus(Now) is ReferralRequestStatus.Rejected or ReferralRequestStatus.Expired) throw Missing();
        var user = await Candidate(row.CandidateUserId, ct);
        if (user.ResumeStorageKey is null || user.ResumeContentType is null) throw Missing();
        var stream = await resumes.OpenReadAsync(user.Id, user.ResumeStorageKey, null, user.ResumeFileName, user.ResumeContentType, ct) ?? throw Missing();
        return new(stream, user.ResumeFileName ?? "resume", user.ResumeContentType);
    }
    public async Task<ReferralMetricsResponse> MetricsAsync(Guid referrerId, CancellationToken ct)
    { await Active(referrerId, ct); return await repository.MetricsAsync(referrerId, ct); }

    private async Task<ReferrerRequestResponse> ReferrerMutation(Guid actor, Guid id, Func<ReferralRequest, Task<bool>> transition,
        NotificationSource source, string eventName, CancellationToken ct)
    {
        await Active(actor, ct);
        var identity = await repository.RequestAsync(id, null, actor, ct) ?? throw Missing();
        return await repository.WriteAsync(identity.CandidateUserId, identity.JobReferralId, async () =>
        {
            await Active(actor, ct);
            var row = await repository.RequestAsync(id, null, actor, ct) ?? throw Missing();
            bool changed;
            try { changed = await transition(row); }
            catch (ReferralTransitionException ex) { throw new ConflictException("Referral transition is not permitted.", ex.Code); }
            if (changed) await Event(row, source, row.CandidateUserId, eventName, ct);
            return Card(row);
        }, ct);
    }
    private async Task<ReferralRequestResponse> CandidateMutation(Guid actor, Guid id, Func<ReferralRequest, bool> transition,
        NotificationSource source, string eventName, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var identity = await repository.RequestAsync(id, actor, null, ct) ?? throw Missing();
        return await repository.WriteAsync(actor, identity.JobReferralId, async () =>
        {
            await Candidate(actor, ct);
            var row = await repository.RequestAsync(id, actor, null, ct) ?? throw Missing();
            bool changed;
            try { changed = transition(row); }
            catch (ReferralTransitionException ex) { throw new ConflictException("Referral transition is not permitted.", ex.Code); }
            if (changed) await Event(row, source, row.ReferrerUserId, eventName, ct);
            return Map(row, await repository.MembershipAsync(actor, Now, ct) is not null);
        }, ct);
    }
    private async Task Event(ReferralRequest row, NotificationSource source, Guid recipient, string name, CancellationToken ct)
    {
        var content = ReferralNotifications.Request(row, source, recipient);
        outbox.Enqueue(source, row.Id, Guid.Empty, recipient, $"referral-request:{row.Id:D}:{name}",
            content.Title, content.Message, content.Route);
        await audit.AppendAsync(new(AuditAction.Update, "ReferralRequest", row.Id.ToString(),
            new Dictionary<string, string?> { ["event"] = name }), ct);
    }
    private async Task RequireCapacity(JobReferral opportunity, Membership membership, Guid candidateId, CancellationToken ct)
    {
        if (await repository.AcceptedForOpportunityAsync(opportunity.Id, ct) >= opportunity.ReferralSlots)
            throw new ConflictException("No referral slots remain.", "REFERRAL_SLOTS_FULL");
        var (start, _) = Period(membership, Now);
        if (await repository.AcceptedForPeriodAsync(candidateId, membership.Id, start, ct) >= ConnectionLimit)
            throw new ConflictException("Referral connection limit reached.", "REFERRAL_CONNECTION_LIMIT_REACHED");
    }
    private async Task<User> Active(Guid id, CancellationToken ct) => await repository.UserAsync(id, ct)
        ?? throw new AppException("Active account required.", 403, "FORBIDDEN_REFERRAL_REQUEST");
    private async Task<User> Candidate(Guid id, CancellationToken ct)
    {
        var user = await Active(id, ct);
        if (user.Role.Name != "Candidate") throw new AppException("Candidate account required.", 403, "FORBIDDEN_REFERRAL_REQUEST");
        return user;
    }
    private async Task<Membership> Access(Guid id, CancellationToken ct) => await repository.MembershipAsync(id, Now, ct)
        ?? throw new AppException("Active Referral Access is required.", 403, "REFERRAL_ACCESS_REQUIRED");
    private static bool Eligible(JobReferral opportunity, DateTime now) => !opportunity.IsDeleted &&
        opportunity.ApprovalStatus == JobReferralApprovalStatus.Approved && !opportunity.Job.IsDeleted && !opportunity.Job.IsHidden &&
        opportunity.Job.Status == JobStatus.Published && (opportunity.Job.ExpiresAtUtc == null || opportunity.Job.ExpiresAtUtc > now) &&
        !opportunity.ReferrerUser.IsDeleted && opportunity.ReferrerUser.Status == UserStatus.Active;
    private void RequireOpportunity(JobReferral opportunity)
    { if (!Eligible(opportunity, Now)) throw new ConflictException("Referral opportunity is unavailable.", "REFERRAL_CONTACT_NOT_AVAILABLE"); }
    private void RequireRequested(ReferralRequest row)
    {
        var status = row.EffectiveStatus(Now);
        if (status != ReferralRequestStatus.Requested) throw new ConflictException("Referral transition is not permitted.",
            status == ReferralRequestStatus.Expired ? "REFERRAL_REQUEST_EXPIRED" : "INVALID_REFERRAL_STATUS");
    }
    // Renewals extend an existing term. Each consecutive 30-day window has its own ten connections.
    public static (DateTime Start, DateTime End) Period(Membership membership, DateTime now)
    {
        var index = (long)Math.Floor((now - membership.StartsAtUtc).TotalDays / 30);
        var start = membership.StartsAtUtc.AddDays(Math.Max(0, index) * 30);
        var end = start.AddDays(30);
        if (membership.EndsAtUtc is { } expiry && expiry < end) end = expiry;
        return (start, end);
    }
    private ReferralRequestResponse Map(ReferralRequest row, bool access) => new(row.Id, row.JobReferralId, row.JobReferral.JobId,
        row.JobReferral.Job.Title, row.JobReferral.Job.Company.Name, "Employee Referrer", row.EffectiveStatus(Now),
        row.RequestedAtUtc, row.ExpiresAtUtc, row.AcceptedAtUtc, row.RejectedAtUtc,
        row.EffectiveStatus(Now) == ReferralRequestStatus.Expired ? row.ExpiredAtUtc ?? row.ExpiresAtUtc : null,
        row.ReferralSubmittedAtUtc, row.CandidateConfirmedAtUtc, row.NotReceivedAtUtc, row.CandidateMessage,
        access && row.IsSuccessful && Eligible(row.JobReferral, Now));
    private ReferrerRequestResponse Card(ReferralRequest row)
    {
        var user = row.CandidateUser;
        return new(Map(row, false), new($"{user.FirstName} {user.LastName}".Trim(), user.YearsOfExperience,
            user.CandidateSkills.Where(x => !x.IsDeleted).Select(x => x.Name).ToArray(),
            user.CandidateExperiences.Where(x => !x.IsDeleted && x.IsCurrent).OrderByDescending(x => x.StartDate).FirstOrDefault()?.CompanyName,
            user.Location, user.AvailabilityToJoin?.ToString(), user.ResumeStorageKey is not null), row.RejectionReason, row.ReferralSubmissionReference);
    }
    private static NotFoundException Missing() => new("Referral request was not found.");
    private static string? Bounded(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text?.Length > max) throw new BadRequestException("Referral input exceeds the permitted length.", "validation_error");
        return text;
    }
    private static void Page(int page, int size)
    { if (page is < 1 or > 1000000 || size is < 1 or > 100) throw new BadRequestException("Invalid pagination."); }
    private static void ValidateStatus(ReferralRequestStatus? status)
    { if (status.HasValue && !Enum.IsDefined(status.Value)) throw new BadRequestException("Invalid referral status."); }
}
