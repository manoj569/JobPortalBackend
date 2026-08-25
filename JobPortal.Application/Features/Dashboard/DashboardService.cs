using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Dashboard;
using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Common.Validation;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Dashboard;

public sealed class DashboardService(
    IDashboardRepository dashboard,
    ICandidateRepository candidates,
    IMembershipService membershipService,
    IPaymentService paymentService,
    IAuthService authService,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<UpdateUserProfileRequest> profileValidator,
    TimeProvider timeProvider) : IDashboardService
{
    public async Task<UserProfileResponse> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredUserAsync(userId, cancellationToken);
        return MapProfile(user, await CompletionAsync(user, cancellationToken));
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        await profileValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredUserAsync(userId, cancellationToken);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            user.PhoneNumber = null;
            user.NormalizedPhoneNumber = null;
        }
        else
        {
            _ = IndianMobileNumber.TryNormalize(
                request.PhoneNumber, out var normalizedPhoneNumber);
            user.PhoneNumber = normalizedPhoneNumber;
            user.NormalizedPhoneNumber = normalizedPhoneNumber;
        }
        user.ProfileImageUrl = TextNormalizer.TrimOrNull(request.ProfileImageUrl);
        user.Headline = TextNormalizer.TrimOrNull(request.Headline);
        user.Bio = TextNormalizer.TrimOrNull(request.Bio);
        await auditWriter.AppendAsync(new(
            JobPortal.Domain.Enums.AuditAction.Update,
            "UserProfile",
            user.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapProfile(user, await CompletionAsync(user, cancellationToken));
    }

    public Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        membershipService.GetMyMembershipsAsync(userId, cancellationToken);

    public Task<PagedResponse<PaymentResponse>> GetPaymentHistoryAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        return paymentService.GetPaymentsAsync(
            userId, new HistoryQuery(query.PageNumber, query.PageSize), cancellationToken);
    }

    public async Task<PagedResponse<SavedJobResponse>> GetSavedJobsAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await dashboard.GetSavedJobsAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task SaveJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!await dashboard.IsAvailableJobAsync(jobId, cancellationToken))
            throw new NotFoundException("Job was not found.");
        if (await dashboard.IsJobSavedAsync(userId, jobId, cancellationToken)) return;
        var savedJob = new SavedJob { UserId = userId, JobId = jobId };
        await dashboard.AddSavedJobAsync(savedJob, cancellationToken);

        // 🚀 ADDED: Generate a Notification for saving the job
        await CreateNotificationAsync(
            userId,
            "Job Saved",
            "You have successfully saved this job to your list.",
            JobPortal.Domain.Enums.NotificationType.Profile,
            "/dashboard/saved-jobs",
            cancellationToken
        );

        await auditWriter.AppendAsync(new(
            JobPortal.Domain.Enums.AuditAction.Create,
            "SavedJob",
            savedJob.Id.ToString(),
            new Dictionary<string, string?> { ["jobId"] = jobId.ToString() }),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var savedJob = await dashboard.GetSavedJobAsync(userId, jobId, cancellationToken);
        if (savedJob is null) return;
        dashboard.RemoveSavedJob(savedJob);
        await auditWriter.AppendAsync(new(
            JobPortal.Domain.Enums.AuditAction.Delete,
            "SavedJob",
            savedJob.Id.ToString(),
            new Dictionary<string, string?> { ["jobId"] = jobId.ToString() }),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<AppliedJobHistoryResponse>> GetAppliedJobsAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await dashboard.GetAppliedJobsAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<(PagedResponse<NotificationResponse> Page, int UnreadCount)> GetNotificationsAsync(
        Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await dashboard.GetNotificationsAsync(userId, query, isRead, cancellationToken);
        return (new(result.Items, query.PageNumber, query.PageSize, result.TotalCount), result.UnreadCount);
    }

    public async Task MarkNotificationReadAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await dashboard.GetNotificationAsync(userId, notificationId, cancellationToken)
            ?? throw new NotFoundException("Notification was not found.");
        if (notification.IsRead) return;
        notification.IsRead = true;
        notification.ReadAtUtc = UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllNotificationsReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _ = await dashboard.MarkAllNotificationsReadAsync(userId, UtcNow, cancellationToken);

    public Task ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        authService.ChangePasswordAsync(userId, request, cancellationToken);

    // 🚀 ADDED: Helper method to create new notifications
    private async Task CreateNotificationAsync(
        Guid userId,
        string title,
        string message,
        JobPortal.Domain.Enums.NotificationType type,
        string? actionUrl = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAtUtc = UtcNow
        };

        await dashboard.AddNotificationAsync(notification, cancellationToken);
    }

    private async Task<User> RequiredUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dashboard.GetUserAsync(userId, cancellationToken) ?? throw new UnauthorizedException();
    private async Task<CandidateProfileCompletionResponse?> CompletionAsync(
        User user, CancellationToken cancellationToken)
    {
        if (user.RoleId != SystemRoleIds.Candidate) return null;
        var skills = await candidates.GetSkillsAsync(user.Id, cancellationToken);
        var records = await candidates.GetProfileRecordPresenceAsync(user.Id, cancellationToken);
        return CandidateProfileCompletionProjection.Create(user,
            skills.Count > 0 || HasLegacySkills(user.SkillsJson),
            records.HasEducation, records.HasEmployment);
    }

    private static bool HasLegacySkills(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json)?.Length > 0; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private static UserProfileResponse MapProfile(
        User x, CandidateProfileCompletionResponse? completion) => new(
        x.Id, x.Email, x.FirstName, x.LastName, x.PhoneNumber, x.ProfileImageUrl,
        x.Headline, x.Bio, x.EmailConfirmed, x.CreatedAtUtc, x.LastLoginAtUtc,
        x.AvailabilityToJoin, completion);
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
