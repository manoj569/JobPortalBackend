using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerSessionService(ICareerSessionRepository repository, IUserRepository users,
    IAuditWriter audit, ICareerMeetingProvider provider, ICareerMeetingProtector protector,
    TimeProvider clock, IOptions<CareerSessionOptions> options)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private CareerSessionOptions Settings => options.Value;

    public async Task<CareerSessionResponse> ProvisionAsync(Guid actor, Guid bookingId, CareerSessionAudience audience, CancellationToken ct)
    {
        await Actor(actor, audience, ct);
        var payment = await repository.PaymentAsync(bookingId, ct) ?? throw new NotFoundException("Eligible booking not found.");
        Authorize(payment.Booking, actor, audience);
        var session = await repository.ForBookingAsync(bookingId, ct);
        if (session is not null) return Dto(session);
        RequireEligible(payment);
        if (Now >= payment.Booking.EndUtc) throw new ConflictException("Cannot provision an elapsed booking.");
        if (!Identifier(provider.Name, 30)) throw new ConflictException("Meeting provider identity is invalid.");
        session = new CareerGuidanceSession
        {
            BookingId = bookingId, Booking = payment.Booking, CandidateUserId = payment.CandidateUserId,
            ConsultantId = payment.ConsultantId, MeetingProvider = provider.Name,
            ScheduledStartUtc = payment.Booking.StartUtc, ScheduledEndUtc = payment.Booking.EndUtc,
            MeetingProvisioningAttemptedAtUtc = Now, EarningReleaseDelayHours = Settings.EarningReleaseDelayHours
        };
        foreach (var offset in Settings.ReminderOffsetsMinutes)
        {
            var due = session.ScheduledStartUtc.AddMinutes(-offset);
            if (due < Now) continue;
            foreach (var recipient in new[] { payment.CandidateUserId, payment.Booking.Consultant.UserId })
                session.Reminders.Add(new() { SessionId = session.Id, RecipientUserId = recipient, OffsetMinutes = offset, ScheduledForUtc = due });
        }
        repository.Add(session);
        TouchEligibility(payment);
        await Save(session, "session_provisioned", ct);
        // Persist the intent BEFORE external work. Retrying provisioning only returns that intent;
        // an administrator reconciles uncertain results through provider lookup, never another create.
        var meeting = await ProviderCall(() => provider.CreateAsync(session.Id, session.ScheduledStartUtc, session.ScheduledEndUtc, ct), ct);
        ApplyMeeting(session, meeting);
        TouchEligibility(payment);
        await Save(session, "meeting_created", ct);
        return Dto(session);
    }

    public async Task<CareerSessionResponse> GetAsync(Guid actor, Guid id, CareerSessionAudience audience, bool bookingId, CancellationToken ct) =>
        Dto(await Required(actor, id, audience, bookingId, ct));

    public async Task<PagedResponse<CareerSessionResponse>> ListAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct)
    {
        await Actor(actor, admin ? CareerSessionAudience.Administrator : CareerSessionAudience.Consultant, ct);
        if (query.PageNumber is < 1 or > 1000000 || query.PageSize is < 1 or > 100) throw new BadRequestException("Invalid page.");
        var result = await repository.ListAsync(actor, admin, query, ct);
        return new(result.Items.Select(Dto).ToArray(), result.PageNumber, result.PageSize, result.TotalCount);
    }

    public async Task<CareerSessionJoinResponse> JoinAsync(Guid actor, Guid id, CareerSessionAudience audience, bool bookingId, CancellationToken ct)
    {
        var session = await Required(actor, id, audience, bookingId, ct);
        if (audience == CareerSessionAudience.Administrator) return new(Dto(session), null, "metadata_only");
        var payment = await repository.PaymentAsync(session.BookingId, ct);
        if (payment is null || !Eligible(payment)) return new(Dto(session), null, "booking_not_eligible");
        if (!JoinWindow(session)) return new(Dto(session), null, "outside_join_window_or_terminal");
        var host = audience == CareerSessionAudience.Consultant && session.ProtectedHostUrl is not null;
        var encrypted = host ? session.ProtectedHostUrl : session.ProtectedParticipantUrl;
        return encrypted is null ? new(Dto(session), null, "meeting_not_configured") :
            new(Dto(session), protector.Unprotect(session.Id, host, encrypted), null);
    }

    public async Task<CareerSessionResponse> ConfigureAsync(Guid actor, Guid id, CareerManualMeetingRequest request, CancellationToken ct)
    {
        var session = await Required(actor, id, CareerSessionAudience.Administrator, false, ct);
        Revision(session, request.Revision);
        if (session.MeetingProvider != "Manual" || session.Status is not (CareerSessionStatus.Scheduled or CareerSessionStatus.Ready))
            throw new ConflictException("Manual links cannot be configured in this state.");
        var payment = await EligiblePayment(session, ct);
        ValidateUrl(request.ParticipantUrl);
        if (request.HostUrl is not null) ValidateUrl(request.HostUrl);
        session.ProtectedParticipantUrl = protector.Protect(id, false, request.ParticipantUrl);
        session.ProtectedHostUrl = request.HostUrl is null ? null : protector.Protect(id, true, request.HostUrl);
        session.Status = CareerSessionStatus.Ready;
        TouchEligibility(payment);
        await Save(session, "manual_meeting_configured", ct);
        return Dto(session);
    }

    public async Task<CareerSessionResponse> ReconcileAsync(Guid actor, Guid id, CareerSessionAction request, CancellationToken ct)
    {
        var session = await Required(actor, id, CareerSessionAudience.Administrator, false, ct);
        Revision(session, request.Revision);
        if (session.Status is not (CareerSessionStatus.Scheduled or CareerSessionStatus.Ready)) throw new ConflictException("Session is not reconcilable.");
        var payment = await EligiblePayment(session, ct);
        if (session.MeetingProvider != provider.Name) throw new ConflictException("Meeting provider is unavailable.");
        var meeting = await ProviderCall(() => provider.GetAsync(session.Id, ct), ct) ?? throw new ConflictException("Meeting outcome remains uncertain; do not create a duplicate.");
        ApplyMeeting(session, meeting);
        TouchEligibility(payment);
        await Save(session, "meeting_reconciled", ct);
        return Dto(session);
    }

    public async Task<CareerSessionResponse> StartAsync(Guid actor, Guid id, CareerSessionAction request, CancellationToken ct)
    {
        var session = await Required(actor, id, CareerSessionAudience.Consultant, false, ct);
        var payment = await EligiblePayment(session, ct);
        if (session.Status == CareerSessionStatus.InProgress) return Dto(session);
        Revision(session, request.Revision);
        if (session.Status != CareerSessionStatus.Ready || !JoinWindow(session)) throw new ConflictException("Session cannot start now.");
        session.Status = CareerSessionStatus.InProgress; session.StartedAtUtc = Now;
        TouchEligibility(payment);
        await Save(session, "session_started", ct);
        return Dto(session);
    }

    public async Task<CareerSessionResponse> CompleteAsync(Guid actor, Guid id, CareerSessionAction request, CancellationToken ct)
    {
        var session = await Required(actor, id, CareerSessionAudience.Consultant, false, ct);
        if (session.Status == CareerSessionStatus.Completed) return Dto(session);
        Revision(session, request.Revision);
        var payment = await EligiblePayment(session, ct);
        if (session.Status != CareerSessionStatus.InProgress || Now < session.ScheduledEndUtc)
            throw new ConflictException("Only an in-progress session after its scheduled end can complete.");
        session.Status = CareerSessionStatus.Completed; session.CompletedAtUtc = Now;
        payment.Booking.Status = CareerBookingStatus.Completed; payment.Booking.CompletedAtUtc = Now;
        payment.Earning!.AvailableAtUtc = Now.AddHours(session.EarningReleaseDelayHours);
        payment.Earning.Revision = Guid.NewGuid();
        TouchEligibility(payment); CancelReminders(session);
        await Save(session, "session_completed", ct);
        return Dto(session);
    }

    public async Task<CareerSessionResponse> NoShowAsync(Guid actor, Guid id, CareerSessionAction request, bool consultantNoShow, CancellationToken ct)
    {
        var session = await Required(actor, id, consultantNoShow ? CareerSessionAudience.Administrator : CareerSessionAudience.Consultant, false, ct);
        var target = consultantNoShow ? CareerSessionStatus.ConsultantNoShow : CareerSessionStatus.CandidateNoShow;
        if (session.Status == target) return Dto(session);
        Revision(session, request.Revision);
        var payment = consultantNoShow ? await ReviewPayment(session, ct) : await EligiblePayment(session, ct);
        RequireGrace(session);
        if (!consultantNoShow && session.CandidateJoinedAtUtc.HasValue) throw new ConflictException("Candidate attendance is recorded.");
        if (consultantNoShow && session.ConsultantJoinedAtUtc.HasValue) throw new ConflictException("Consultant attendance is recorded.");
        session.Status = target;
        if (consultantNoShow)
        {
            session.ConsultantNoShowMarkedAtUtc = Now; payment.RequiresRefundReview = true;
            payment.Booking.Status = CareerBookingStatus.NoShowConsultant;
        }
        else { session.CandidateNoShowMarkedAtUtc = Now; payment.Booking.Status = CareerBookingStatus.NoShowCandidate; }
        payment.Earning!.AvailableAtUtc = null; payment.Earning.Revision = Guid.NewGuid();
        TouchEligibility(payment); CancelReminders(session);
        await Save(session, consultantNoShow ? "consultant_no_show_confirmed" : "candidate_no_show_asserted", ct);
        return Dto(session);
    }

    public async Task<CareerSessionResponse> ReportAsync(Guid actor, Guid bookingId, CareerSessionAction request, CancellationToken ct)
    {
        var session = await Required(actor, bookingId, CareerSessionAudience.Candidate, true, ct);
        if (session.ConsultantNoShowReportedAtUtc.HasValue) return Dto(session);
        Revision(session, request.Revision); RequireGrace(session);
        var payment = await ReviewPayment(session, ct);
        session.ConsultantNoShowReportedAtUtc = Now;
        TouchEligibility(payment);
        await Save(session, "consultant_no_show_reported", ct);
        return Dto(session);
    }

    private void RequireGrace(CareerGuidanceSession session)
    {
        if (session.Status is not (CareerSessionStatus.Scheduled or CareerSessionStatus.Ready or CareerSessionStatus.InProgress) ||
            Now < session.ScheduledStartUtc.AddMinutes(Settings.NoShowGraceMinutes)) throw new ConflictException("No-show cannot be recorded now.");
    }
    private bool JoinWindow(CareerGuidanceSession session) => session.Status switch
    {
        CareerSessionStatus.Ready => Now >= session.ScheduledStartUtc.AddMinutes(-Settings.SessionJoinEarlyMinutes) &&
            Now <= session.ScheduledStartUtc.AddMinutes(Settings.SessionJoinLateMinutes) && Now < session.ScheduledEndUtc,
        CareerSessionStatus.InProgress => Now >= session.ScheduledStartUtc.AddMinutes(-Settings.SessionJoinEarlyMinutes) &&
            Now <= session.ScheduledEndUtc.AddMinutes(Settings.CompletionGraceMinutes),
        _ => false
    };
    private void ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            !Settings.AllowedMeetingHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new BadRequestException("Meeting link must use HTTPS and an allowed meeting host.");
    }
    private void ApplyMeeting(CareerGuidanceSession session, CareerMeeting meeting)
    {
        if (meeting.Provider != session.MeetingProvider || !Identifier(meeting.ExternalMeetingId, 200) ||
            (session.ProviderMeetingId is not null && session.ProviderMeetingId != meeting.ExternalMeetingId)) throw new ConflictException("Meeting identity mismatch.");
        session.ProviderMeetingId = meeting.ExternalMeetingId; session.MeetingCreatedAtUtc ??= Now;
        if (meeting.ParticipantJoinUrl is not null)
        {
            ValidateUrl(meeting.ParticipantJoinUrl);
            session.ProtectedParticipantUrl = protector.Protect(session.Id, false, meeting.ParticipantJoinUrl);
            if (meeting.HostJoinUrl is not null) ValidateUrl(meeting.HostJoinUrl);
            session.ProtectedHostUrl = meeting.HostJoinUrl is null ? null : protector.Protect(session.Id, true, meeting.HostJoinUrl);
            session.Status = CareerSessionStatus.Ready;
        }
    }
    private static async Task<T> ProviderCall<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try { return await call(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        // Provider exceptions can contain bearer links. Never pass them to global exception logging.
        catch (Exception) { throw new ConflictException("Meeting provider outcome is uncertain. Reconcile before retrying.", "meeting_outcome_uncertain"); }
    }
    private async Task<CareerGuidanceSession> Required(Guid actor, Guid id, CareerSessionAudience audience, bool bookingId, CancellationToken ct)
    {
        await Actor(actor, audience, ct);
        var session = (bookingId ? await repository.ForBookingAsync(id, ct) : await repository.GetAsync(id, ct)) ?? throw new NotFoundException("Session not found.");
        Authorize(session.Booking, actor, audience);
        return session;
    }
    private async Task Actor(Guid actor, CareerSessionAudience audience, CancellationToken ct)
    {
        var user = await users.GetByIdWithRoleAsync(actor, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active) throw new UnauthorizedException();
        if (audience == CareerSessionAudience.Administrator && user.Role.Name != "Administrator") throw new AppException("Administrator access required.", 403, "forbidden");
    }
    private static void Authorize(CareerGuidanceBooking booking, Guid actor, CareerSessionAudience audience)
    {
        if (booking.IsDeleted || (audience != CareerSessionAudience.Administrator &&
            (audience == CareerSessionAudience.Candidate ? booking.CandidateUserId != actor : booking.Consultant.UserId != actor)))
            throw new NotFoundException("Session not found.");
    }
    private static bool OpenPaidBooking(CareerGuidancePayment payment) => !payment.IsDeleted && payment.Status == CareerPaymentStatus.Captured &&
        payment.PaidAtUtc.HasValue && payment.Refund is null && payment.Earning is { IsDeleted: false, Status: CareerEarningStatus.Pending } &&
        !payment.Booking.IsDeleted && payment.Booking.Status == CareerBookingStatus.Confirmed;
    internal static bool Eligible(CareerGuidancePayment payment) => OpenPaidBooking(payment) && !payment.RequiresRefundReview &&
        payment.Booking.Candidate is { IsDeleted: false, Status: UserStatus.Active } &&
        payment.Booking.Consultant is { IsDeleted: false, VerificationStatus: ConsultantVerificationStatus.Verified, User.IsDeleted: false, User.Status: UserStatus.Active };
    private static void RequireEligible(CareerGuidancePayment payment)
    { if (!Eligible(payment)) throw new ConflictException("Session requires an active confirmed booking and captured payment without refund review."); }
    private async Task<CareerGuidancePayment> EligiblePayment(CareerGuidanceSession session, CancellationToken ct)
    {
        var payment = await repository.PaymentAsync(session.BookingId, ct) ?? throw new ConflictException("Captured payment not found.");
        RequireEligible(payment); return payment;
    }
    private async Task<CareerGuidancePayment> ReviewPayment(CareerGuidanceSession session, CancellationToken ct)
    {
        var payment = await repository.PaymentAsync(session.BookingId, ct);
        // No-show review must remain available when the absent consultant is suspended or
        // deactivated, or refund review is already flagged. It never makes the session joinable.
        if (payment is null || !OpenPaidBooking(payment)) throw new ConflictException("No-show review requires an open captured booking without a refund.");
        return payment;
    }
    private static bool Identifier(string? value, int maxLength) => value is { Length: > 0 } && value.Length <= maxLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    internal static void TouchEligibility(CareerGuidancePayment payment)
    { payment.Revision = Guid.NewGuid(); payment.Booking.Revision = Guid.NewGuid(); payment.Booking.Consultant.Revision = Guid.NewGuid(); }
    internal static void CancelReminders(CareerGuidanceSession session)
    {
        foreach (var reminder in session.Reminders.Where(r => r.Status == CareerReminderStatus.Pending))
        { reminder.Status = CareerReminderStatus.Cancelled; reminder.Revision = Guid.NewGuid(); }
    }
    private static void Revision(CareerGuidanceSession session, Guid revision)
    { if (revision == Guid.Empty || session.Revision != revision) throw new ConflictException("Session changed; reload and retry.", "session_concurrency"); }
    private async Task Save(CareerGuidanceSession session, string result, CancellationToken ct)
    {
        session.Revision = Guid.NewGuid();
        await audit.AppendAsync(new(AuditAction.Update, "CareerGuidanceSession", session.Id.ToString(), new Dictionary<string, string?> { ["result"] = result }), ct);
        await repository.SaveAsync(ct);
    }
    private static CareerSessionResponse Dto(CareerGuidanceSession s) => new(s.Id, s.BookingId, s.CandidateUserId, s.ConsultantId,
        s.MeetingProvider, s.ProviderMeetingId, s.ScheduledStartUtc, s.ScheduledEndUtc, s.Status, s.StartedAtUtc, s.CompletedAtUtc,
        s.ConsultantNoShowReportedAtUtc, s.MeetingCreatedAtUtc, s.ProtectedParticipantUrl is not null, s.Revision,
        s.CandidateJoinedAtUtc, s.ConsultantJoinedAtUtc, s.CandidateNoShowMarkedAtUtc, s.ConsultantNoShowMarkedAtUtc,
        s.MeetingProvisioningAttemptedAtUtc, s.EarningReleaseDelayHours);
}
