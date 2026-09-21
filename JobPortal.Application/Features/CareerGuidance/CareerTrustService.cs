using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerTrustService(ICareerTrustRepository repository, ICareerGuidanceRepository profiles,
    IUserRepository users, IAuditWriter audit, TimeProvider clock, IOptions<CareerTrustOptions> options,
    IOptions<CareerSessionOptions> sessionOptions)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<CareerReviewResponse> SubmitReviewAsync(Guid actor, Guid bookingId, CareerReviewRequest request, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Candidate, ct);
        var p = await OwnedPayment(actor, bookingId, ct);
        ReviewInput(request);
        var existing = await repository.ReviewAsync(bookingId, true, ct);
        if (existing is not null)
        {
            if (!existing.IsDeleted && Same(existing, request)) return ReviewDto(existing, false);
            throw new ConflictException("A review already exists for this booking.");
        }
        var s = await ReviewEligible(p, ct);
        var r = new CareerGuidanceReview { BookingId = bookingId, Booking = p.Booking, SessionId = s.Id, Session = s,
            PaymentId = p.Id, Payment = p, ConsultantId = p.ConsultantId, CandidateUserId = actor,
            Rating = request.Rating, Title = Text(request.Title), Comment = Text(request.Comment) };
        repository.Add(r); Touch(p);
        await Save(r.Id, "review_submitted", ct); return ReviewDto(r, false);
    }
    public async Task<CareerReviewResponse> GetReviewAsync(Guid actor, Guid id, bool admin, CancellationToken ct)
    {
        await Actor(actor, admin ? CareerSessionAudience.Administrator : CareerSessionAudience.Candidate, ct);
        var r = await RequiredReview(actor, id, admin, ct); return ReviewDto(r, admin);
    }
    public async Task<CareerReviewResponse> EditReviewAsync(Guid actor, Guid bookingId, CareerReviewRequest request, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Candidate, ct);
        var r = await RequiredReview(actor, bookingId, false, ct);
        ReviewInput(request); Revision(r.Revision, request.Revision);
        if (r.IsDeleted || r.ModerationStatus is CareerReviewStatus.Hidden or CareerReviewStatus.Rejected || Now > r.CreatedAtUtc.AddDays(options.Value.ReviewEditWindowDays))
            throw new ConflictException("Review editing is closed.");
        var p = await OwnedPayment(actor, bookingId, ct); await ReviewEligible(p, ct);
        if (Same(r, request)) return ReviewDto(r, false);
        r.Rating = request.Rating; r.Title = Text(request.Title); r.Comment = Text(request.Comment);
        r.ModerationStatus = CareerReviewStatus.Pending; r.IsPublished = false; r.ModerationReason = null; r.Revision = Guid.NewGuid();
        Touch(p); await Save(r.Id, "review_edited", ct); return ReviewDto(r, false);
    }
    public async Task<CareerReviewResponse> WithdrawReviewAsync(Guid actor, Guid bookingId, Guid revision, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Candidate, ct);
        var r = await RequiredReview(actor, bookingId, false, ct);
        if (r.IsDeleted) return ReviewDto(r, false);
        Revision(r.Revision, revision);
        r.IsDeleted = true; r.DeletedAtUtc = Now; r.IsPublished = false; r.Revision = Guid.NewGuid();
        await Save(r.Id, "review_withdrawn", ct); return ReviewDto(r, false);
    }
    public async Task<CareerReviewResponse> ModerateAsync(Guid actor, Guid id, CareerReviewModerationRequest request, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Administrator, ct);
        if (!Enum.IsDefined(request.Status) || request.Status == CareerReviewStatus.Pending) throw new BadRequestException("Invalid moderation action.");
        Plain(request.Reason, 1000, true);
        var r = await RequiredReview(actor, id, true, ct);
        var p = await repository.PaymentAsync(r.BookingId, ct) ?? throw new NotFoundException("Payment not found.");
        IndependentAdmin(actor, p);
        if (r.IsDeleted) throw new ConflictException("A withdrawn review cannot be published or moderated.");
        if (r.ModerationStatus == request.Status && r.ModerationReason == request.Reason.Trim()) return ReviewDto(r, true);
        Revision(r.Revision, request.Revision);
        if (request.Status == CareerReviewStatus.Approved) await ReviewEligible(p, ct);
        r.ModerationStatus = request.Status; r.IsPublished = request.Status == CareerReviewStatus.Approved;
        r.ModerationReason = request.Reason.Trim(); r.Revision = Guid.NewGuid(); Touch(p);
        await Save(r.Id, "review_moderated", ct); return ReviewDto(r, true);
    }
    public async Task<PagedResponse<CareerReviewResponse>> ReviewsAsync(Guid actor, CareerTrustQuery query, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Administrator, ct); Page(query);
        var rows = await repository.ReviewsAsync(query, ct);
        return new(rows.Items.Select(r => ReviewDto(r, true)).ToArray(), rows.PageNumber, rows.PageSize, rows.TotalCount);
    }
    public async Task<PagedResponse<CareerPublicReview>> PublicReviewsAsync(Guid consultantId, CareerTrustQuery query, CancellationToken ct)
    {
        Page(query);
        if (await profiles.FindAsync(consultantId, true, ct) is null) throw new NotFoundException("Consultant not found.");
        return await repository.PublicReviewsAsync(consultantId, query, ct);
    }
    public async Task<CareerDisputeResponse> OpenDisputeAsync(Guid actor, Guid bookingId, CareerDisputeRequest request, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Candidate, ct);
        var p = await OwnedPayment(actor, bookingId, ct);
        if (!Enum.IsDefined(request.Category)) throw new BadRequestException("Invalid dispute category.");
        Plain(request.Description, 4000, true);
        // One lifetime case per booking prevents repeated claims from continually restarting holds.
        var existing = await repository.DisputeAsync(bookingId, true, ct);
        if (existing is not null)
        {
            if (existing.Category == request.Category && existing.Description == request.Description.Trim() && existing.RequestedRefund == request.RequestedRefund)
                return DisputeDto(existing, CareerSessionAudience.Candidate);
            throw new ConflictException("A dispute already exists for this booking; add evidence to that case.");
        }
        if (p.IsDeleted || !p.PaidAtUtc.HasValue || p.Booking.IsDeleted || p.Earning is null) throw new ConflictException("Only paid bookings can be disputed.");
        var s = await repository.SessionAsync(bookingId, ct);
        var end = s?.CompletedAtUtc is { } completed && completed > p.Booking.EndUtc ? completed : p.Booking.EndUtc;
        if (Now > end.AddHours(options.Value.DisputeOpenWindowHours) ||
            request.Category != CareerDisputeCategory.BillingIssue && Now < p.Booking.StartUtc ||
            request.Category == CareerDisputeCategory.ConsultantNoShow && Now < p.Booking.StartUtc.AddMinutes(sessionOptions.Value.NoShowGraceMinutes))
            throw new ConflictException("This dispute is outside the permitted time window.");
        var d = new CareerGuidanceDispute { BookingId = bookingId, SessionId = s?.Id, PaymentId = p.Id, Payment = p,
            CandidateUserId = actor, ConsultantId = p.ConsultantId, Category = request.Category, Description = request.Description.Trim(),
            RequestedRefund = request.RequestedRefund, SubmittedAtUtc = Now };
        repository.Add(d);
        if (p.Earning.Status == CareerEarningStatus.Pending) { p.Earning.AvailableAtUtc = null; p.Earning.Revision = Guid.NewGuid(); }
        Touch(p); await Save(d.Id, "dispute_opened", ct); return DisputeDto(d, CareerSessionAudience.Candidate);
    }
    public async Task<CareerDisputeResponse> GetDisputeAsync(Guid actor, Guid id, CareerSessionAudience audience, bool byBooking, CancellationToken ct)
    {
        await Actor(actor, audience, ct);
        return DisputeDto(await RequiredDispute(actor, id, audience, byBooking, ct), audience);
    }
    public async Task<PagedResponse<CareerDisputeResponse>> DisputesAsync(Guid actor, CareerSessionAudience audience, CareerTrustQuery query, CancellationToken ct)
    {
        await Actor(actor, audience, ct); Page(query);
        var rows = await repository.DisputesAsync(actor, audience, query, ct);
        return new(rows.Items.Select(d => DisputeDto(d, audience)).ToArray(), rows.PageNumber, rows.PageSize, rows.TotalCount);
    }
    public async Task<CareerDisputeResponse> EvidenceAsync(Guid actor, Guid id, CareerSessionAudience audience, CareerEvidenceRequest request, CancellationToken ct)
    {
        await Actor(actor, audience, ct);
        var d = await RequiredDispute(actor, id, audience, false, ct);
        Plain(request.Description, 4000, true);
        if (request.RequestId == Guid.Empty || !Enum.IsDefined(request.EvidenceType)) throw new BadRequestException("Invalid evidence request.");
        if (audience != CareerSessionAudience.Administrator && (request.IsPrivateToAdmin || request.EvidenceType == CareerEvidenceType.AdminNote))
            throw new AppException("Only administrators may submit private evidence.", 403, "forbidden");
        var privateEvidence = request.IsPrivateToAdmin || request.EvidenceType == CareerEvidenceType.AdminNote;
        var existing = d.Evidence.SingleOrDefault(e => e.RequestId == request.RequestId);
        if (existing is not null)
        {
            if (existing.SubmittedByUserId == actor && existing.Description == request.Description.Trim() && existing.EvidenceType == request.EvidenceType && existing.IsPrivateToAdmin == privateEvidence)
                return DisputeDto(d, audience);
            throw new ConflictException("Evidence request identity already used.");
        }
        Revision(d.Revision, request.Revision);
        if (!Active(d) || d.Evidence.Count >= 50) throw new ConflictException("Evidence submission is closed or its limit was reached.");
        d.Evidence.Add(new() { DisputeId = d.Id, SubmittedByUserId = actor, RequestId = request.RequestId, EvidenceType = request.EvidenceType,
            Description = request.Description.Trim(), IsPrivateToAdmin = privateEvidence });
        d.Revision = Guid.NewGuid(); await Save(d.Id, "dispute_evidence_added", ct); return DisputeDto(d, audience);
    }
    public async Task<CareerDisputeResponse> StatusAsync(Guid actor, Guid id, CareerDisputeStatusRequest request, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Administrator, ct); Plain(request.AdminNotes, 2000, true);
        if (request.Status is not (CareerDisputeStatus.UnderReview or CareerDisputeStatus.AwaitingCandidate or CareerDisputeStatus.AwaitingConsultant))
            throw new BadRequestException("Use resolution for terminal actions.");
        var d = await RequiredDispute(actor, id, CareerSessionAudience.Administrator, false, ct);
        IndependentAdmin(actor, d.Payment);
        if (d.Status == request.Status && d.AdminNotes == request.AdminNotes.Trim()) return DisputeDto(d, CareerSessionAudience.Administrator);
        Revision(d.Revision, request.Revision);
        if (!Active(d)) throw new ConflictException("Dispute is terminal.");
        d.Status = request.Status; d.AdminNotes = request.AdminNotes.Trim(); d.Revision = Guid.NewGuid();
        await Save(d.Id, "dispute_status_changed", ct); return DisputeDto(d, CareerSessionAudience.Administrator);
    }
    public async Task<CareerDisputeResponse> ResolveAsync(Guid actor, Guid id, CareerDisputeResolveRequest request, CancellationToken ct)
    {
        await Actor(actor, CareerSessionAudience.Administrator, ct); Plain(request.AdminNotes, 2000, true);
        if (!Enum.IsDefined(request.Resolution) || request.Resolution == CareerDisputeResolution.None) throw new BadRequestException("Invalid resolution.");
        var d = await RequiredDispute(actor, id, CareerSessionAudience.Administrator, false, ct);
        IndependentAdmin(actor, d.Payment);
        if (!Active(d))
        {
            if (d.Resolution == request.Resolution && d.AdminNotes == request.AdminNotes.Trim()) return DisputeDto(d, CareerSessionAudience.Administrator);
            throw new ConflictException("Dispute is already resolved.");
        }
        Revision(d.Revision, request.Revision);
        var p = d.Payment;
        var s = await repository.SessionAsync(d.BookingId, ct);
        if (request.Resolution == CareerDisputeResolution.RefundApproved)
        {
            if (p.Earning is null || p.Earning.Status is CareerEarningStatus.Payable or CareerEarningStatus.Settled)
                throw new ConflictException("Settlement reconciliation is required before refund approval.");
            if (p.Status != CareerPaymentStatus.Refunded) p.RequiresRefundReview = true;
        }
        if (request.Resolution == CareerDisputeResolution.ConsultantRestricted)
        {
            if (p.Consultant.UserId == actor) throw new AppException("Self-moderation is not allowed.", 403, "forbidden");
            if (p.Consultant.VerificationStatus is not (ConsultantVerificationStatus.Verified or ConsultantVerificationStatus.Suspended))
                throw new ConflictException("Use the consultant verification workflow for this profile state.");
            p.Consultant.VerificationStatus = ConsultantVerificationStatus.Suspended;
            p.Consultant.ReviewedAtUtc = Now; p.Consultant.ReviewedByUserId = actor;
            p.Consultant.VerificationReason = "Administrator restriction following dispute review."; p.Consultant.Revision = Guid.NewGuid();
        }
        if (request.Resolution == CareerDisputeResolution.ReviewHidden)
        {
            var r = await repository.ReviewAsync(d.BookingId, true, ct) ?? throw new ConflictException("No review exists for this booking.");
            r.IsPublished = false; r.ModerationStatus = CareerReviewStatus.Hidden; r.ModerationReason = "Hidden following administrator dispute review."; r.Revision = Guid.NewGuid();
        }
        d.Status = CareerDisputeStatus.Resolved; d.Resolution = request.Resolution; d.AdminNotes = request.AdminNotes.Trim();
        d.ResolvedAtUtc = Now; d.ResolvedByUserId = actor; d.Revision = Guid.NewGuid();
        if (p.Earning is { Status: CareerEarningStatus.Pending } earning)
        {
            earning.AvailableAtUtc = request.Resolution != CareerDisputeResolution.RefundApproved && !p.IsDeleted && p.Status == CareerPaymentStatus.Captured &&
                p.Refund is null && !p.RequiresRefundReview && s is { IsDeleted: false, Status: CareerSessionStatus.Completed } &&
                !p.Booking.IsDeleted && p.Booking.Status == CareerBookingStatus.Completed && !p.Consultant.IsDeleted &&
                p.Consultant.VerificationStatus == ConsultantVerificationStatus.Verified && !p.Consultant.User.IsDeleted && p.Consultant.User.Status == UserStatus.Active &&
                !p.Booking.Candidate.IsDeleted && p.Booking.Candidate.Status == UserStatus.Active
                ? Now.AddHours(s.EarningReleaseDelayHours) : null;
            earning.Revision = Guid.NewGuid();
            if (earning.AvailableAtUtc.HasValue) p.Consultant.Revision = Guid.NewGuid();
        }
        Touch(p); await Save(d.Id, "dispute_resolved", ct); return DisputeDto(d, CareerSessionAudience.Administrator);
    }

    public static bool Active(CareerGuidanceDispute d) => d.Status is >= CareerDisputeStatus.Open and <= CareerDisputeStatus.AwaitingConsultant;
    private async Task<CareerGuidancePayment> OwnedPayment(Guid actor, Guid bookingId, CancellationToken ct)
    {
        var p = await repository.PaymentAsync(bookingId, ct);
        if (p is null || p.CandidateUserId != actor || p.Booking.CandidateUserId != actor || p.Consultant.UserId == actor)
            throw new NotFoundException("Booking not found.");
        return p;
    }
    private async Task<CareerGuidanceSession> ReviewEligible(CareerGuidancePayment p, CancellationToken ct)
    {
        var s = await repository.SessionAsync(p.BookingId, ct);
        if (p.IsDeleted || p.Status != CareerPaymentStatus.Captured || !p.PaidAtUtc.HasValue || p.Refund is not null ||
            p.Booking.IsDeleted || p.Booking.Status != CareerBookingStatus.Completed || s is not { IsDeleted: false, Status: CareerSessionStatus.Completed } ||
            s.CandidateUserId != p.CandidateUserId || s.ConsultantId != p.ConsultantId || p.Consultant.UserId == p.CandidateUserId ||
            p.Booking.Candidate.IsDeleted || p.Booking.Candidate.Status != UserStatus.Active) throw new ConflictException("Only completed paid, non-refunded sessions permit reviews.");
        return s;
    }
    private async Task<CareerGuidanceReview> RequiredReview(Guid actor, Guid id, bool admin, CancellationToken ct)
    {
        var r = await repository.ReviewAsync(id, !admin, ct);
        if (r is null || !admin && r.CandidateUserId != actor) throw new NotFoundException("Review not found."); return r;
    }
    private async Task<CareerGuidanceDispute> RequiredDispute(Guid actor, Guid id, CareerSessionAudience audience, bool byBooking, CancellationToken ct)
    {
        var d = await repository.DisputeAsync(id, byBooking, ct);
        if (d is null || d.IsDeleted || audience != CareerSessionAudience.Administrator &&
            (audience == CareerSessionAudience.Candidate ? d.CandidateUserId != actor : d.Payment.Consultant.UserId != actor)) throw new NotFoundException("Dispute not found.");
        return d;
    }
    private async Task Actor(Guid actor, CareerSessionAudience audience, CancellationToken ct)
    {
        var user = await users.GetByIdWithRoleAsync(actor, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active) throw new UnauthorizedException();
        if (audience == CareerSessionAudience.Administrator && user.Role.Name != "Administrator" || audience == CareerSessionAudience.Candidate && user.Role.Name != "Candidate")
            throw new AppException("Access denied.", 403, "forbidden");
    }
    private static void Page(CareerTrustQuery q)
    {
        if (q.PageNumber is < 1 or > 1000000 || q.PageSize is < 1 or > 100 || q.Status.HasValue && !Enum.IsDefined(q.Status.Value) ||
            q.ModerationStatus.HasValue && !Enum.IsDefined(q.ModerationStatus.Value)) throw new BadRequestException("Invalid query.");
    }
    private static void ReviewInput(CareerReviewRequest r)
    { if (r.Rating is < 1 or > 5) throw new BadRequestException("Rating must be between 1 and 5."); Plain(r.Title, 120, false); Plain(r.Comment, 2000, false); }
    private static void Plain(string? value, int max, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value is not null && (value.Length > max || value.Any(c => c is '<' or '>' || char.IsControl(c) && c is not ('\r' or '\n' or '\t'))))
            throw new BadRequestException("Text must be bounded plain text without HTML or control characters.");
    }
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Same(CareerGuidanceReview r, CareerReviewRequest q) => r.Rating == q.Rating && r.Title == Text(q.Title) && r.Comment == Text(q.Comment);
    private static void Revision(Guid actual, Guid? expected)
    { if (!expected.HasValue || expected == Guid.Empty || actual != expected) throw new ConflictException("Trust data changed. Reload before retrying.", "trust_concurrency"); }
    private static void Touch(CareerGuidancePayment p)
    { p.Revision = Guid.NewGuid(); p.Booking.Revision = Guid.NewGuid(); }
    private static void IndependentAdmin(Guid actor, CareerGuidancePayment p)
    { if (actor == p.CandidateUserId || actor == p.Consultant.UserId) throw new AppException("An independent administrator must moderate this case.", 403, "self_moderation"); }
    private async Task Save(Guid id, string result, CancellationToken ct)
    {
        await audit.AppendAsync(new(AuditAction.Update, "CareerGuidanceTrust", id.ToString(), new Dictionary<string, string?> { ["result"] = result }), ct);
        await repository.SaveAsync(ct);
    }
    private static CareerReviewResponse ReviewDto(CareerGuidanceReview r, bool admin) => new(r.Id, r.BookingId, r.SessionId, r.ConsultantId,
        r.Rating, r.Title, r.Comment, r.IsPublished, r.ModerationStatus, admin ? r.ModerationReason : null, r.IsDeleted, r.CreatedAtUtc, r.Revision);
    private static CareerDisputeResponse DisputeDto(CareerGuidanceDispute d, CareerSessionAudience audience) => new(d.Id, d.BookingId, d.SessionId,
        d.Category, d.Description, d.RequestedRefund, d.Status, d.Resolution, audience == CareerSessionAudience.Administrator ? d.AdminNotes : null,
        d.SubmittedAtUtc, d.ResolvedAtUtc, d.Revision, d.Evidence.Where(e => !e.IsDeleted && (!e.IsPrivateToAdmin || audience == CareerSessionAudience.Administrator))
            .OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Id).Select(e => new CareerEvidenceResponse(e.Id, e.EvidenceType, e.Description, e.IsPrivateToAdmin, e.CreatedAtUtc)).ToArray());
}
