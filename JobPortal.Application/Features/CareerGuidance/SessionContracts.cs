using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerSessionOptions
{
    public int SessionJoinEarlyMinutes { get; set; } = 10;
    public int SessionJoinLateMinutes { get; set; } = 30;
    public int NoShowGraceMinutes { get; set; } = 15;
    public int CompletionGraceMinutes { get; set; } = 15;
    public int EarningReleaseDelayHours { get; set; } = 48;
    public int[] ReminderOffsetsMinutes { get; set; } = [1440, 60, 10];
    public bool SessionRemindersEnabled { get; set; }
    public int SessionReminderPollSeconds { get; set; } = 60;
    public string[] AllowedMeetingHosts { get; set; } = ["meet.google.com", "zoom.us", "teams.microsoft.com", "meet.jit.si"];
    public bool IsValid() => SessionJoinEarlyMinutes is >= 0 and <= 120 && SessionJoinLateMinutes is >= 1 and <= 120 &&
        NoShowGraceMinutes is >= 1 and <= 120 && CompletionGraceMinutes is >= 0 and <= 120 &&
        EarningReleaseDelayHours is >= 24 and <= 2160 && SessionReminderPollSeconds is >= 10 and <= 3600 &&
        ReminderOffsetsMinutes is { Length: <= 6 } && ReminderOffsetsMinutes.All(x => x is > 0 and <= 10080) &&
        ReminderOffsetsMinutes.Distinct().Count() == ReminderOffsetsMinutes.Length &&
        AllowedMeetingHosts is { Length: > 0 } && AllowedMeetingHosts.All(h => Uri.CheckHostName(h) == UriHostNameType.Dns);
}
public enum CareerSessionAudience { Candidate, Consultant, Administrator }
public sealed record CareerSessionAction(Guid Revision);
public sealed record CareerManualMeetingRequest(Guid Revision, string ParticipantUrl, string? HostUrl);
public sealed record CareerSessionResponse(Guid Id, Guid BookingId, Guid CandidateUserId, Guid ConsultantId,
    string MeetingProvider, string? ProviderMeetingId, DateTime ScheduledStartUtc, DateTime ScheduledEndUtc,
    CareerSessionStatus Status, DateTime? StartedAtUtc, DateTime? CompletedAtUtc, DateTime? ConsultantNoShowReportedAtUtc,
    DateTime? MeetingCreatedAtUtc, bool MeetingConfigured, Guid Revision,
    DateTime? CandidateJoinedAtUtc, DateTime? ConsultantJoinedAtUtc,
    DateTime? CandidateNoShowMarkedAtUtc, DateTime? ConsultantNoShowMarkedAtUtc,
    DateTime MeetingProvisioningAttemptedAtUtc, int EarningReleaseDelayHours);
public sealed record CareerSessionJoinResponse(CareerSessionResponse Session, string? JoinUrl, string? WithheldReason);
public sealed record CareerMeeting(string Provider, string ExternalMeetingId, string? ParticipantJoinUrl, string? HostJoinUrl);
public interface ICareerMeetingProvider
{
    string Name { get; }
    Task<CareerMeeting> CreateAsync(Guid sessionId, DateTime startUtc, DateTime endUtc, CancellationToken ct);
    // Must reconcile using the SAME durable session identity; never blindly create on lookup failure.
    Task<CareerMeeting?> GetAsync(Guid sessionId, CancellationToken ct);
}
public interface ICareerMeetingProtector
{
    byte[] Protect(Guid sessionId, bool host, string value);
    string Unprotect(Guid sessionId, bool host, byte[] value);
}
public interface ICareerSessionRepository
{
    Task<CareerGuidanceSession?> GetAsync(Guid id, CancellationToken ct);
    Task<CareerGuidanceSession?> ForBookingAsync(Guid bookingId, CancellationToken ct);
    Task<CareerGuidancePayment?> PaymentAsync(Guid bookingId, CancellationToken ct);
    Task<PagedResponse<CareerGuidanceSession>> ListAsync(Guid actor, bool admin, FinanceQuery query, CancellationToken ct);
    Task<IReadOnlyList<CareerGuidanceSessionReminder>> DueAsync(DateTime now, CancellationToken ct);
    void Add(CareerGuidanceSession session);
    Task SaveAsync(CancellationToken ct);
}
