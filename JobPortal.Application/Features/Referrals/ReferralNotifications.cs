using System.Globalization;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Referrals;

public sealed class ReferralNotificationOptions
{
    public string? AdminApprovalPath { get; set; }
    public bool AdminEmailEnabled { get; set; } = true;
    public bool RemindersEnabled { get; set; } = true;
    public int PendingReminderAgeHours { get; set; } = 24;
    public int ReminderIntervalHours { get; set; } = 24;
    public int SweepBatchSize { get; set; } = 20;
    public bool IsValid() => (AdminApprovalPath is null || ReferralNotifications.IsAdminPath(AdminApprovalPath)) &&
        PendingReminderAgeHours is >= 1 and <= 720 && ReminderIntervalHours is >= 1 and <= 720 &&
        SweepBatchSize is >= 1 and <= 100;
}

public interface IReferralNotificationScheduler
{
    Task EnqueueDueAsync(CancellationToken ct);
}

public static class ReferralNotifications
{
    // Free-form review notes have no public/private classification. Only these non-personal reasons are emailed verbatim.
    private static readonly HashSet<string> PublicReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Duplicate posting", "Job is no longer available", "Incomplete job details",
        "Not a referral opportunity", "Does not meet referral guidelines"
    };
    public static bool IsReferral(NotificationSource source) => source is
        NotificationSource.ReferralApproved or NotificationSource.ReferralRejected or NotificationSource.ReferralRequested or
        NotificationSource.ReferralAccepted or NotificationSource.ReferralRequestRejected or NotificationSource.ReferralSubmitted or
        NotificationSource.ReferralConfirmed or NotificationSource.ReferralNotReceived or NotificationSource.ReferralJobSubmitted or
        NotificationSource.ReferralExpired or NotificationSource.ReferralRequestReminder;

    public static bool IsReferral(Notification notification) => notification.Type is >= NotificationType.ReferralApproved and <= NotificationType.ReferralRequestReminder ||
        notification.BusinessKey?.StartsWith("referral:", StringComparison.Ordinal) == true ||
        notification.BusinessKey?.StartsWith("referral-request:", StringComparison.Ordinal) == true;

    // Configured admin paths cannot contain tokens, query strings, redirects, traversal or hosts.
    public static bool IsAdminPath(string path) => path.Length <= 200 &&
        (path.StartsWith("/admin/", StringComparison.Ordinal) || path.StartsWith("/dashboard/admin/", StringComparison.Ordinal)) &&
        !path.Contains("//", StringComparison.Ordinal) && path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_');

    public static NotificationType Type(NotificationSource source) => source switch
    {
        NotificationSource.ReferralApproved => NotificationType.ReferralApproved,
        NotificationSource.ReferralRejected => NotificationType.ReferralRejected,
        NotificationSource.ReferralJobSubmitted => NotificationType.ReferralJobSubmitted,
        NotificationSource.ReferralRequested => NotificationType.ReferralRequested,
        NotificationSource.ReferralAccepted => NotificationType.ReferralAccepted,
        NotificationSource.ReferralRequestRejected => NotificationType.ReferralRequestRejected,
        NotificationSource.ReferralSubmitted => NotificationType.ReferralSubmitted,
        NotificationSource.ReferralConfirmed => NotificationType.ReferralConfirmed,
        NotificationSource.ReferralNotReceived => NotificationType.ReferralNotReceived,
        NotificationSource.ReferralExpired => NotificationType.ReferralExpired,
        NotificationSource.ReferralRequestReminder => NotificationType.ReferralRequestReminder,
        _ => NotificationType.System
    };

    public static (string Title, string Message, string? Route) Job(JobReferral referral, Job job,
        NotificationSource source, string? adminPath = null)
    {
        var label = Label(job);
        return source switch
        {
            NotificationSource.ReferralJobSubmitted => ("Referral job awaiting approval",
                $"{label} was submitted for approval at {referral.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}. Sign in as an administrator to review it.", adminPath),
            NotificationSource.ReferralApproved => ("Referred job approved",
                $"Your referred job {label} is approved and available for referral requests.", "/dashboard/referrals"),
            NotificationSource.ReferralRejected => ("Referral job not approved",
                $"Your referred job {label} was not approved." + SafeReason(referral.RejectionReason) + " Sign in to review the outcome.", "/dashboard/referrals"),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    public static (string Title, string Message, string Route) Request(ReferralRequest request, NotificationSource source, Guid recipient)
    {
        var label = Label(request.JobReferral.Job);
        var candidate = recipient == request.CandidateUserId;
        var route = candidate ? "/dashboard/my-referral-requests" : "/dashboard/referral-requests";
        var content = source switch
        {
            NotificationSource.ReferralRequested => ("New referral request", $"You received a referral request for {label}. Sign in to review the candidate details."),
            NotificationSource.ReferralAccepted => ("Referral request accepted", $"The referrer accepted your request for {label}. The referral has not yet been marked as submitted to the employer."),
            NotificationSource.ReferralRequestRejected => ("Referral request declined", $"The referrer declined your request for {label}. Thank you for your interest. You can explore other referral opportunities."),
            NotificationSource.ReferralSubmitted => ("Referral marked as submitted", $"The referrer marked your referral for {label} as submitted to the employer. This does not confirm employer receipt or job selection."),
            NotificationSource.ReferralConfirmed => ("Candidate confirmed referral", $"The candidate confirmed receipt of the referral for {label}. This is a candidate confirmation, not employer confirmation or job selection."),
            NotificationSource.ReferralNotReceived => ("Referral receipt needs review", $"The candidate reported not receiving the referral for {label}. Sign in to review the request."),
            NotificationSource.ReferralExpired => ("Referral request expired", $"The pending referral request for {label} expired without acceptance."),
            NotificationSource.ReferralRequestReminder => ("Pending referral request reminder", $"A referral request for {label} is still awaiting your response. Sign in to review it before it expires."),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return (content.Item1, content.Item2, route);
    }

    private static string Label(Job job) => $"\"{Clean(job.Title, 180)}\" at {Clean(job.Company.Name, 160)}";
    private static string Clean(string text, int limit) => new string(text.Where(c => !char.IsControl(c)).Take(limit).ToArray()).Trim();
    private static string SafeReason(string? reason)
    {
        // Referrer request rejection notes remain private. Only a short plain administrator decision reason is eligible.
        if (string.IsNullOrWhiteSpace(reason) || !PublicReasons.Contains(reason.Trim())) return string.Empty;
        return $" Reason: {reason.Trim()}";
    }
}
