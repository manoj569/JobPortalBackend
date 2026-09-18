using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class DashboardRepository(
    JobPortalDbContext context,
    TimeProvider timeProvider) : IDashboardRepository
{
    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task<(IReadOnlyCollection<SavedJobResponse> Items, int TotalCount)> GetSavedJobsAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var source = context.SavedJobs.AsNoTracking().Where(x => x.UserId == userId &&
            x.Job.Status == JobStatus.Published && !x.Job.IsHidden && !x.Job.IsDeleted &&
            x.Job.PublishedAtUtc.HasValue &&
            (!x.Job.ExpiresAtUtc.HasValue || x.Job.ExpiresAtUtc > utcNow));
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(DashboardProjections.SavedJob)
            .ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public Task<bool> IsAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return context.Jobs.AsNoTracking().AnyAsync(x => x.Id == jobId &&
            x.Status == JobStatus.Published && !x.IsHidden && !x.IsDeleted &&
            x.PublishedAtUtc.HasValue &&
            (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > utcNow), cancellationToken);
    }

    public Task<bool> IsJobSavedAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
        context.SavedJobs.AnyAsync(x => x.UserId == userId && x.JobId == jobId, cancellationToken);
    public Task AddSavedJobAsync(SavedJob savedJob, CancellationToken cancellationToken = default) =>
        context.SavedJobs.AddAsync(savedJob, cancellationToken).AsTask();
    public Task<SavedJob?> GetSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
        context.SavedJobs.SingleOrDefaultAsync(x => x.UserId == userId && x.JobId == jobId, cancellationToken);
    public void RemoveSavedJob(SavedJob savedJob) => context.SavedJobs.Remove(savedJob);

    public async Task<(IReadOnlyCollection<AppliedJobHistoryResponse> Items, int TotalCount)> GetAppliedJobsAsync(
        Guid userId, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.UserJobHistories.AsNoTracking()
            .Where(x => x.UserId == userId && x.Action == JobHistoryAction.Applied);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new AppliedJobHistoryResponse(
                x.Id, x.JobId, x.Job.Title, x.Job.Slug, x.Job.Company.Name,
                x.Action, x.OccurredAtUtc, x.Notes))
            .ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public async Task<(IReadOnlyCollection<NotificationResponse> Items, int TotalCount, int UnreadCount)> GetNotificationsAsync(
        Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default)
    {
        var all = context.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        var unreadCount = await all.CountAsync(x => !x.IsRead, cancellationToken);
        var source = isRead.HasValue ? all.Where(x => x.IsRead == isRead) : all;
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new NotificationResponse(x.Id, x.Title, x.Message, x.Type,
                x.ActionUrl, x.IsRead, x.ReadAtUtc, x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return (items, totalCount, unreadCount);
    }

    public Task<Notification?> GetNotificationAsync(
        Guid userId, Guid notificationId, CancellationToken cancellationToken = default) =>
        context.Notifications.SingleOrDefaultAsync(
            x => x.Id == notificationId && x.UserId == userId, cancellationToken);
    public async Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await context.Notifications.AddAsync(notification, cancellationToken);
    }
    public Task<bool> NotificationExistsAsync(Guid userId, NotificationType type, Guid referralId, CancellationToken cancellationToken = default) =>
        context.Notifications.AnyAsync(x => x.UserId == userId && x.Type == type && x.ReferralId == referralId, cancellationToken);
    public Task<int> MarkAllNotificationsReadAsync(
        Guid userId, DateTime readAtUtc, CancellationToken cancellationToken = default) =>
        context.Notifications.Where(x => x.UserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAtUtc, readAtUtc)
                .SetProperty(x => x.UpdatedAtUtc, readAtUtc), cancellationToken);
}
