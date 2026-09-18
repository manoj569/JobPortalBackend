using JobPortal.Application.Features.Dashboard;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IDashboardRepository
{
    Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<SavedJobResponse> Items, int TotalCount)> GetSavedJobsAsync(Guid userId, DashboardQuery query, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> IsJobSavedAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    Task AddSavedJobAsync(SavedJob savedJob, CancellationToken cancellationToken = default);
    Task<SavedJob?> GetSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
    void RemoveSavedJob(SavedJob savedJob);
    Task<(IReadOnlyCollection<AppliedJobHistoryResponse> Items, int TotalCount)> GetAppliedJobsAsync(Guid userId, DashboardQuery query, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<NotificationResponse> Items, int TotalCount, int UnreadCount)> GetNotificationsAsync(Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default);
    Task<Notification?> GetNotificationAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task<int> MarkAllNotificationsReadAsync(Guid userId, DateTime readAtUtc, CancellationToken cancellationToken = default);
    Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<bool> NotificationExistsAsync(Guid userId, NotificationType type, Guid referralId, CancellationToken cancellationToken = default);
}
