using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.AdminDashboard;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AdminDashboardRepository(JobPortalDbContext context) : IAdminDashboardRepository
{
    public async Task<AdminDashboardStatistics> GetStatisticsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var revenueRows = await context.Payments.AsNoTracking()
    .Where(x => x.Status == PaymentStatus.Paid)
    .GroupBy(x => x.CurrencyCode)
    .Select(group => new
    {
        CurrencyCode = group.Key,
        Total = group.Sum(x => x.Amount),
        ThisMonth = group.Where(x => x.PaidAtUtc >= monthStart).Sum(x => x.Amount)
    })
    .OrderBy(x => x.CurrencyCode)
    .ToArrayAsync(cancellationToken);

        var revenue = revenueRows
            .Select(x => new RevenueTotal(x.CurrencyCode, x.Total, x.ThisMonth))
            .ToArray();
        var users = await context.Users.AsNoTracking().GroupBy(_ => 1)
            .Select(group => new { Total = group.Count() })
            .SingleOrDefaultAsync(cancellationToken);
        var paidUsers = await context.Payments.AsNoTracking()
            .Where(x => x.Status == PaymentStatus.Paid)
            .Select(x => x.UserId).Distinct().CountAsync(cancellationToken);
        var jobs = await context.Jobs.AsNoTracking().GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Published = group.Count(x => x.Status == JobStatus.Published &&
                    x.PublishedAtUtc.HasValue &&
                    x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc > utcNow),
                Featured = group.Count(x => x.IsFeatured && x.Status == JobStatus.Published &&
                    !x.IsHidden && x.PublishedAtUtc.HasValue &&
                    x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc > utcNow),
                Expired = group.Count(x => x.Status == JobStatus.Expired ||
                    (x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc <= utcNow))
            }).SingleOrDefaultAsync(cancellationToken);
        var categories = await context.Categories.AsNoTracking().CountAsync(cancellationToken);
        var companies = await context.Companies.AsNoTracking().CountAsync(cancellationToken);

        return new AdminDashboardStatistics(
            revenue, users?.Total ?? 0, paidUsers, jobs?.Total ?? 0,
            jobs?.Published ?? 0, jobs?.Featured ?? 0, jobs?.Expired ?? 0,
            categories, companies, utcNow);
    }

    public async Task<IReadOnlyCollection<RecentPaymentResponse>> GetRecentPaymentsAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await context.Payments.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new RecentPaymentResponse(
                x.Id, x.UserId, x.User.FirstName + " " + x.User.LastName, x.User.Email,
                x.Amount, x.CurrencyCode, x.Status, x.Provider, x.ProviderPaymentId,
                x.PaidAtUtc, x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<RecentUserResponse>> GetRecentUsersAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await context.Users.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new RecentUserResponse(
                x.Id, x.Email, x.FirstName, x.LastName, x.Status,
                x.EmailConfirmed, x.CreatedAtUtc, x.LastLoginAtUtc))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<RevenueChartPoint>> GetRevenueChartAsync(
        DateTime fromUtc, DateTime toUtc, ChartInterval interval,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Payments.AsNoTracking()
            .Where(x => x.Status == PaymentStatus.Paid &&
                x.PaidAtUtc >= fromUtc && x.PaidAtUtc < toUtc)
            .GroupBy(x => new
            {
                Year = x.PaidAtUtc!.Value.Year,
                Month = x.PaidAtUtc.Value.Month,
                Day = interval == ChartInterval.Day ? x.PaidAtUtc.Value.Day : 1,
                x.CurrencyCode
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                group.Key.CurrencyCode,
                Revenue = group.Sum(x => x.Amount),
                Payments = group.Count(),
                PaidUsers = group.Select(x => x.UserId).Distinct().Count()
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.CurrencyCode)
            .ToArrayAsync(cancellationToken);
        return rows.Select(x => new RevenueChartPoint(
            new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc),
            x.CurrencyCode, x.Revenue, x.Payments, x.PaidUsers)).ToArray();
    }

    public Task<IReadOnlyCollection<CountChartPoint>> GetUserChartAsync(
        DateTime fromUtc, DateTime toUtc, ChartInterval interval,
        CancellationToken cancellationToken = default) =>
        GetCountChartAsync(context.Users.AsNoTracking().Where(x =>
            x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc).Select(x => x.CreatedAtUtc),
            interval, cancellationToken);

    public Task<IReadOnlyCollection<CountChartPoint>> GetJobChartAsync(
        DateTime fromUtc, DateTime toUtc, ChartInterval interval,
        CancellationToken cancellationToken = default) =>
        GetCountChartAsync(context.Jobs.AsNoTracking().Where(x =>
            x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc).Select(x => x.CreatedAtUtc),
            interval, cancellationToken);

    public async Task<IReadOnlyCollection<DistributionChartPoint>> GetCategoryDistributionAsync(
    int limit, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var rows = await context.Categories.AsNoTracking()
            .Select(x => new
            {
                x.Id,
                Label = x.Name,
                Value = x.Jobs.Count(job => job.Status == JobStatus.Published &&
                    !job.IsHidden &&
                    job.PublishedAtUtc.HasValue &&
                    job.ExpiresAtUtc.HasValue &&
                    job.ExpiresAtUtc > utcNow)
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(x => new DistributionChartPoint(x.Id, x.Label, x.Value))
            .ToArray();
    }
    public async Task<IReadOnlyCollection<DistributionChartPoint>> GetCompanyDistributionAsync(
       int limit, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var rows = await context.Companies.AsNoTracking()
            .Select(x => new
            {
                x.Id,
                Label = x.Name,
                Value = x.Jobs.Count(job => job.Status == JobStatus.Published &&
                    !job.IsHidden &&
                    job.PublishedAtUtc.HasValue &&
                    job.ExpiresAtUtc.HasValue &&
                    job.ExpiresAtUtc > utcNow)
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(x => new DistributionChartPoint(x.Id, x.Label, x.Value))
            .ToArray();
    }

    private static async Task<IReadOnlyCollection<CountChartPoint>> GetCountChartAsync(
        IQueryable<DateTime> dates, ChartInterval interval, CancellationToken cancellationToken)
    {
        var rows = await dates.GroupBy(date => new
        {
            date.Year,
            date.Month,
            Day = interval == ChartInterval.Day ? date.Day : 1
        })
            .Select(group => new { group.Key.Year, group.Key.Month, group.Key.Day, Count = group.Count() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
            .ToArrayAsync(cancellationToken);
        return rows.Select(x => new CountChartPoint(
            new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc), x.Count)).ToArray();
    }
}
