using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Domain.Common;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class InterviewInsightRepository(JobPortalDbContext db) : IInterviewInsightRepository
{
    public Task<bool> IsCandidateAsync(Guid candidateId, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(x => x.Id == candidateId && x.Status == UserStatus.Active &&
            x.RoleId == SystemRoleIds.Candidate, ct);
    public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken ct) => db.Companies.AsNoTracking().AnyAsync(x => x.Id == companyId, ct);
    public Task<bool> JobBelongsToCompanyAsync(Guid jobId, Guid companyId, CancellationToken ct) =>
        db.Jobs.AsNoTracking().AnyAsync(x => x.Id == jobId && x.CompanyId == companyId, ct);
    public Task<bool> HasApplicationAtCompanyAsync(Guid candidateId, Guid companyId, CancellationToken ct) =>
        db.JobApplications.AsNoTracking().AnyAsync(x => x.UserId == candidateId && x.Job.CompanyId == companyId, ct);
    public Task<bool> HasScheduleAtCompanyAsync(Guid candidateId, Guid companyId, CancellationToken ct) =>
        db.CandidateInterviewSchedules.AsNoTracking().AnyAsync(x => x.CandidateId == candidateId &&
            x.CompanyId == companyId && x.Status != InterviewScheduleStatus.Cancelled, ct);
    public Task<bool> HasPastScheduleAtCompanyAsync(Guid candidateId, Guid companyId, DateTime now, CancellationToken ct) =>
        db.CandidateInterviewSchedules.AsNoTracking().AnyAsync(x => x.CandidateId == candidateId &&
            x.CompanyId == companyId && x.Status != InterviewScheduleStatus.Cancelled && x.InterviewAtUtc <= now, ct);
    public Task<int> CountInsightsSinceAsync(Guid candidateId, DateTime since, CancellationToken ct) =>
        db.InterviewInsights.CountAsync(x => x.AuthorCandidateId == candidateId && x.CreatedAtUtc >= since, ct);
    public Task<int> CountFeedbackSinceAsync(Guid candidateId, DateTime since, CancellationToken ct) =>
        db.InsightHelpfulnessFeedback.CountAsync(x => x.CandidateId == candidateId && x.CreatedAtUtc >= since, ct);
    public Task AddInsightAsync(InterviewInsight insight, CancellationToken ct) => db.InterviewInsights.AddAsync(insight, ct).AsTask();
    public async Task<InterviewInsight?> GetInsightAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = db.InterviewInsights.Include(x => x.Company).Include(x => x.AuthorCandidate)
            .Include(x => x.Rounds.OrderBy(r => r.Sequence)).Where(x => x.Id == id);
        return await (tracking ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct);
    }
    public async Task<(IReadOnlyCollection<InterviewInsightCardResponse>, int)> SearchPublishedAsync(Guid candidateId, InterviewInsightQuery query, DateOnly? fromMonth, CancellationToken ct)
    {
        var q = db.InterviewInsights.AsNoTracking().Where(x => x.Status == InterviewInsightStatus.Published &&
            !x.Reports.Any(r => r.Status == InsightReportStatus.Actioned));
        if (query.CompanyId.HasValue) q = q.Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Company)) q = db.Database.IsNpgsql()
            ? q.Where(x => EF.Functions.ILike(x.Company.Name, $"%{query.Company.Trim()}%"))
            : q.Where(x => x.Company.Name.Contains(query.Company.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Role)) q = db.Database.IsNpgsql()
            ? q.Where(x => EF.Functions.ILike(x.RoleTitle, $"%{query.Role.Trim()}%"))
            : q.Where(x => x.RoleTitle.Contains(query.Role.Trim(), StringComparison.OrdinalIgnoreCase));
        if (query.Difficulty.HasValue) q = q.Where(x => x.OverallDifficulty == query.Difficulty);
        if (query.RoundType.HasValue) q = q.Where(x => x.Rounds.Any(r => r.RoundType == query.RoundType));
        if (!string.IsNullOrWhiteSpace(query.ExperienceLevel)) q = db.Database.IsNpgsql()
            ? q.Where(x => x.ExperienceLevel != null && EF.Functions.ILike(x.ExperienceLevel, query.ExperienceLevel.Trim()))
            : q.Where(x => x.ExperienceLevel != null && x.ExperienceLevel.Equals(query.ExperienceLevel.Trim(), StringComparison.OrdinalIgnoreCase));
        if (query.Outcome.HasValue) q = q.Where(x => x.Outcome == query.Outcome);
        if (query.InterviewFormat.HasValue) q = q.Where(x => x.InterviewFormat == query.InterviewFormat);
        if (fromMonth.HasValue) q = q.Where(x => x.InterviewDateMonth >= fromMonth);
        var total = await q.CountAsync(ct);
        q = query.Sort.Equals(nameof(InterviewInsightSort.Newest), StringComparison.OrdinalIgnoreCase)
            ? q.OrderByDescending(x => x.PublishedAtUtc).ThenByDescending(x => x.Id)
            : query.Sort.Equals(nameof(InterviewInsightSort.MostRounds), StringComparison.OrdinalIgnoreCase)
                ? q.OrderByDescending(x => x.Rounds.Count).ThenByDescending(x => x.PublishedAtUtc).ThenByDescending(x => x.Id)
                : q.OrderByDescending(x => x.HelpfulConfirmedCount).ThenByDescending(x => x.PublishedAtUtc).ThenByDescending(x => x.Id);
        var items = await q.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new InterviewInsightCardResponse(x.Id, x.CompanyId, x.Company.Name, x.RoleTitle,
                x.InterviewDateMonth.Month, x.InterviewDateMonth.Year, x.OverallDifficulty, x.Outcome,
                x.ExperienceLevel, x.InterviewFormat, x.Rounds.Count,
                x.Rounds.OrderBy(r => r.Sequence).Select(r => r.RoundType).Distinct().ToArray(),
                x.Rounds.Where(r => r.RoundTitle != null).OrderBy(r => r.Sequence)
                    .Select(r => r.RoundTitle!).Distinct().Take(6).ToArray(),
                x.ProcessSummary.Length <= 240 ? x.ProcessSummary : x.ProcessSummary.Substring(0, 240),
                x.HelpfulConfirmedCount,
                db.JobApplications.Any(a => a.UserId == candidateId && a.Job.CompanyId == x.CompanyId) ||
                    db.CandidateInterviewSchedules.Any(s => s.CandidateId == candidateId && s.CompanyId == x.CompanyId && s.Status != InterviewScheduleStatus.Cancelled),
                x.AuthorCandidateId != candidateId && (db.JobApplications.Any(a => a.UserId == candidateId && a.Job.CompanyId == x.CompanyId) ||
                    db.CandidateInterviewSchedules.Any(s => s.CandidateId == candidateId && s.CompanyId == x.CompanyId && s.Status != InterviewScheduleStatus.Cancelled)),
                x.IsAnonymous, x.IsAnonymous ? null : (x.AuthorCandidate.FirstName + " " + x.AuthorCandidate.LastName).Trim()))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyCollection<InterviewInsightCompanyResponse>> SearchCompaniesAsync(string query, int limit, CancellationToken ct)
    {
        var companies = db.Companies.AsNoTracking();
        companies = db.Database.IsNpgsql()
            ? companies.Where(x => EF.Functions.ILike(x.Name, $"%{query}%"))
            : companies.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        return await companies
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(limit)
            .Select(x => new InterviewInsightCompanyResponse(x.Id, x.Name)).ToListAsync(ct);
    }
    public async Task<(IReadOnlyCollection<InterviewInsight>, int)> SearchAdminAsync(AdminInterviewInsightQuery query, CancellationToken ct)
    {
        var q = db.InterviewInsights.AsNoTracking().Include(x => x.Company).Include(x => x.AuthorCandidate)
            .Include(x => x.Rounds.OrderBy(r => r.Sequence)).AsQueryable();
        if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status);
        var total = await q.CountAsync(ct);
        return (await q.OrderByDescending(x => x.CreatedAtUtc).Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize).ToListAsync(ct), total);
    }
    public Task AddScheduleAsync(CandidateInterviewSchedule schedule, CancellationToken ct) => db.CandidateInterviewSchedules.AddAsync(schedule, ct).AsTask();
    public async Task<CandidateInterviewSchedule?> GetScheduleAsync(Guid candidateId, Guid id, bool tracking, CancellationToken ct)
    {
        var q = db.CandidateInterviewSchedules.Include(x => x.Company).Where(x => x.Id == id && x.CandidateId == candidateId);
        return await (tracking ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyCollection<CandidateInterviewSchedule>> GetSchedulesAsync(Guid candidateId, CancellationToken ct) =>
        await db.CandidateInterviewSchedules.AsNoTracking().Include(x => x.Company).Where(x => x.CandidateId == candidateId)
            .OrderByDescending(x => x.InterviewAtUtc).ToListAsync(ct);
    public Task<bool> FeedbackExistsAsync(Guid candidateId, Guid insightId, CancellationToken ct) =>
        db.InsightHelpfulnessFeedback.AsNoTracking().AnyAsync(x => x.CandidateId == candidateId && x.InsightId == insightId, ct);
    public Task AddFeedbackAsync(InsightHelpfulnessFeedback feedback, CancellationToken ct) => db.InsightHelpfulnessFeedback.AddAsync(feedback, ct).AsTask();
    public Task<bool> ReportExistsAsync(Guid candidateId, Guid insightId, CancellationToken ct) =>
        db.InsightReports.AsNoTracking().AnyAsync(x => x.ReporterCandidateId == candidateId && x.InsightId == insightId, ct);
    public Task AddReportAsync(InsightReport report, CancellationToken ct) => db.InsightReports.AddAsync(report, ct).AsTask();
    public async Task<(int, int, int, int, int, IReadOnlyCollection<MyInterviewContributionCardResponse>)> ContributionsAsync(Guid candidateId, CancellationToken ct)
    {
        var q = db.InterviewInsights.AsNoTracking().Where(x => x.AuthorCandidateId == candidateId);
        var published = await q.CountAsync(x => x.Status == InterviewInsightStatus.Published, ct);
        var pending = await q.CountAsync(x => x.Status == InterviewInsightStatus.PendingReview, ct);
        var needsChanges = await q.CountAsync(x => x.Status == InterviewInsightStatus.Rejected, ct);
        var helped = await q.Where(x => x.Status == InterviewInsightStatus.Published).SumAsync(x => x.HelpfulConfirmedCount, ct);
        var score = await q.Where(x => x.Status == InterviewInsightStatus.Published).SumAsync(x => x.QualityScore, ct);
        var items = await q.OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .Select(x => new MyInterviewContributionCardResponse(x.Id, x.Company.Name, x.RoleTitle,
                x.InterviewDateMonth.Month, x.InterviewDateMonth.Year, x.Status,
                x.HelpfulConfirmedCount, x.QualityScore, x.UpdatedAtUtc ?? x.CreatedAtUtc,
                x.Status == InterviewInsightStatus.Rejected ? x.ModerationReason : null)).ToListAsync(ct);
        return (published, pending, needsChanges, helped, score, items);
    }
    public async Task<(string, int, int)> CompanySummaryAsync(Guid companyId, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        if (company is null) return (string.Empty, 0, 0);
        var q = db.InterviewInsights.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == InterviewInsightStatus.Published);
        return (company, await q.CountAsync(ct), await q.SumAsync(x => x.HelpfulConfirmedCount, ct));
    }
    public async Task<(IReadOnlyCollection<InsightReport>, int)> SearchReportsAsync(AdminInsightReportQuery query, CancellationToken ct)
    {
        var q = db.InsightReports.AsNoTracking().AsQueryable();
        if (query.Status.HasValue) q = q.Where(x => x.Status == query.Status);
        var total = await q.CountAsync(ct);
        return (await q.OrderByDescending(x => x.CreatedAtUtc).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct), total);
    }
    public Task<InsightReport?> GetReportAsync(Guid id, CancellationToken ct) => db.InsightReports.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddNotificationAsync(Notification notification, CancellationToken ct) => db.Notifications.AddAsync(notification, ct).AsTask();
    public async Task<int> CreateDueScheduleNotificationsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var ids = await db.CandidateInterviewSchedules.AsNoTracking()
            .Where(x => x.Status != InterviewScheduleStatus.Cancelled && x.FeedbackNotificationSentAtUtc == null &&
                x.ConfirmFeedbackAvailableAtUtc <= nowUtc)
            .OrderBy(x => x.ConfirmFeedbackAvailableAtUtc).Select(x => x.Id).Take(50).ToListAsync(ct);
        var created = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var claimed = await db.CandidateInterviewSchedules.Where(x => x.Id == id && x.FeedbackNotificationSentAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FeedbackNotificationSentAtUtc, nowUtc), ct);
            if (claimed == 1)
            {
                var schedule = await db.CandidateInterviewSchedules.AsNoTracking().Include(x => x.Company).SingleAsync(x => x.Id == id, ct);
                await db.Notifications.AddAsync(new Notification
                {
                    UserId = schedule.CandidateId, Type = NotificationType.Profile,
                    Title = "How did your interview go?",
                    Message = $"Tell the community whether the {schedule.Company.Name} insights helped.",
                    ActionUrl = "/dashboard/interview-insights"
                }, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                created++;
            }
            else await transaction.RollbackAsync(ct);
        }
        return created;
    }
    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new JobPortal.Application.Common.Exceptions.UniqueConstraintException(
                "An Interview Insights uniqueness constraint was violated.", exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new JobPortal.Application.Common.Exceptions.ConflictException(
                "Interview Insight data changed concurrently. Please retry.", "interview_insight_concurrency");
        }
    }
}
