using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

#pragma warning disable CA1725

public sealed class AIApplyRepository(JobPortalDbContext context) : IAIApplyRepository
{
    public Task<User?> GetUserProfileAsync(Guid userId, CancellationToken ct = default) => context.Users.Include(x => x.CandidateSkills).Include(x => x.CandidateCertifications).Include(x => x.CandidateExperiences).SingleOrDefaultAsync(x => x.Id == userId && x.Status == UserStatus.Active, ct);
    public Task<Membership?> GetMembershipAsync(Guid userId, CancellationToken ct = default) => context.Memberships.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public Task<Job?> GetJobAsync(Guid id, CancellationToken ct = default) => context.Jobs.Include(x => x.Company).Include(x => x.JobSkills).ThenInclude(x => x.Skill).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Job>> GetJobsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => await context.Jobs.Include(x => x.Company).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
    public Task<AIApplyProfile?> GetProfileAsync(Guid userId, CancellationToken ct = default) => context.AIApplyProfiles.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public Task<AIApplyPreference?> GetPreferencesAsync(Guid userId, CancellationToken ct = default) => context.AIApplyPreferences.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public Task<AIApplySetting?> GetSettingsAsync(Guid userId, CancellationToken ct = default) => context.AIApplySettings.SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public async Task<IReadOnlyList<AIApplyRule>> GetRulesAsync(Guid userId, CancellationToken ct = default) => await context.AIApplyRules.Where(x => x.UserId == userId).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
    public Task<AIApplyRule?> GetRuleAsync(Guid userId, Guid id, CancellationToken ct = default) => context.AIApplyRules.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public Task<AIApplyApplication?> GetApplicationAsync(Guid userId, Guid id, CancellationToken ct = default) => context.AIApplyApplications.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public async Task<IReadOnlyList<AIApplyApplication>> GetApplicationsAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default) { var q = context.AIApplyApplications.Where(x => x.UserId == userId); if (from.HasValue) q = q.Where(x => x.CreatedAtUtc >= from); if (to.HasValue) q = q.Where(x => x.CreatedAtUtc < to); return await q.OrderByDescending(x => x.CreatedAtUtc).Take(1000).ToListAsync(ct); }
    public Task<bool> IsDuplicateAsync(Guid userId, Guid jobId, string normalizedUrl, CancellationToken ct = default) => context.AIApplyApplications.AnyAsync(x => x.UserId == userId && (x.JobId == jobId || x.NormalizedApplicationUrl == normalizedUrl), ct);
    public async Task<bool> TryContinueCandidateActionAsync(Guid userId, Guid applicationId, AIApplyFailureKind expectedFailureKind, DateTime nowUtc, CancellationToken ct = default)
    {
        if (!context.Database.IsRelational())
        {
            var item = await context.AIApplyApplications.SingleOrDefaultAsync(x => x.Id == applicationId && x.UserId == userId, ct);
            if (item is null || item.Status != AIApplyRunStatus.Failed || item.FailureKind != expectedFailureKind || item.SubmissionAttemptedAtUtc.HasValue) return false;
            AIApplyStateMachine.Transition(item, AIApplyRunStatus.Queued, nowUtc); ResetCandidateAction(item, nowUtc);
            context.AIApplyExecutionLogs.Add(ContinueLog(item, nowUtc)); await context.SaveChangesAsync(ct); return true;
        }

        var changed = false; var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            foreach (var entry in context.ChangeTracker.Entries<AIApplyApplication>().Where(x => x.Entity.Id == applicationId).ToList()) entry.State = EntityState.Detached;
            var affected = await context.AIApplyApplications
                .Where(x => x.Id == applicationId && x.UserId == userId && x.Status == AIApplyRunStatus.Failed && x.FailureKind == expectedFailureKind && x.SubmissionAttemptedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AIApplyRunStatus.Queued)
                    .SetProperty(x => x.ScheduledAtUtc, nowUtc)
                    .SetProperty(x => x.RequiresUserInput, false)
                    .SetProperty(x => x.FailureKind, AIApplyFailureKind.None)
                    .SetProperty(x => x.LastErrorCode, (string?)null)
                    .SetProperty(x => x.FailureClassification, (string?)null)
                    .SetProperty(x => x.CompletedAtUtc, (DateTime?)null)
                    .SetProperty(x => x.LeaseOwner, (string?)null)
                    .SetProperty(x => x.ClaimedAtUtc, (DateTime?)null)
                    .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);
            if (affected == 1)
            {
                context.AIApplyExecutionLogs.Add(new AIApplyExecutionLog { ApplicationId = applicationId, EventType = AIApplyExecutionEvent.CandidateContinueRequested, Status = AIApplyRunStatus.Queued, MessageCode = "candidate_continue_requested", StartedAtUtc = nowUtc, CompletedAtUtc = nowUtc });
                await context.SaveChangesAsync(ct); changed = true;
            }
            await transaction.CommitAsync(ct);
        });
        return changed;
    }

    private static void ResetCandidateAction(AIApplyApplication item, DateTime nowUtc) { item.ScheduledAtUtc = nowUtc; item.RequiresUserInput = false; item.FailureKind = AIApplyFailureKind.None; item.LastErrorCode = null; item.FailureClassification = null; item.CompletedAtUtc = null; item.LeaseOwner = null; item.ClaimedAtUtc = null; item.LeaseExpiresAtUtc = null; }
    private static AIApplyExecutionLog ContinueLog(AIApplyApplication item, DateTime nowUtc) => new() { ApplicationId = item.Id, EventType = AIApplyExecutionEvent.CandidateContinueRequested, Status = AIApplyRunStatus.Queued, MessageCode = "candidate_continue_requested", StartedAtUtc = nowUtc, CompletedAtUtc = nowUtc };
    public Task<AIApplyQuestion?> GetQuestionAsync(Guid userId, Guid id, CancellationToken ct = default) => context.AIApplyQuestions.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public async Task<IReadOnlyList<AIApplyQuestion>> GetQuestionsAsync(Guid userId, AIApplyQuestionStatus? status, CancellationToken ct = default) { var q = context.AIApplyQuestions.Where(x => x.UserId == userId); if (status.HasValue) q = q.Where(x => x.Status == status); return await q.OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(ct); }
    public Task<UserApplicationAnswer?> GetAnswerAsync(Guid userId, Guid id, CancellationToken ct = default) => context.UserApplicationAnswers.SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public Task<UserApplicationAnswer?> FindAnswerAsync(Guid userId, string normalizedQuestion, CancellationToken ct = default) => context.UserApplicationAnswers.SingleOrDefaultAsync(x => x.UserId == userId && x.NormalizedQuestion == normalizedQuestion && x.IsActive, ct);
    public async Task<IReadOnlyList<UserApplicationAnswer>> GetAnswersAsync(Guid userId, CancellationToken ct = default) => await context.UserApplicationAnswers.Where(x => x.UserId == userId).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(500).ToListAsync(ct);
    public async Task<AIApplyApplication?> ClaimNextAsync(DateTime now, int perUserConcurrency, string workerId, DateTime leaseExpiresAtUtc, CancellationToken ct = default)
    {
        AIApplyApplication? claimed = null; var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var limit = Math.Clamp(perUserConcurrency, 1, 5);
            var claimUserId = await context.Database.SqlQuery<Guid>($"SELECT owner.\"Id\" AS \"Value\" FROM \"Users\" owner WHERE EXISTS (SELECT 1 FROM \"AIApplyApplications\" queued WHERE queued.\"UserId\" = owner.\"Id\" AND queued.\"IsDeleted\" = FALSE AND queued.\"Status\" = {(int)AIApplyRunStatus.Queued} AND queued.\"ScheduledAtUtc\" <= {now}) AND (SELECT COUNT(*) FROM \"AIApplyApplications\" active WHERE active.\"IsDeleted\" = FALSE AND active.\"UserId\" = owner.\"Id\" AND active.\"Status\" = {(int)AIApplyRunStatus.Processing}) < {limit} ORDER BY (SELECT MAX(queued.\"Priority\") FROM \"AIApplyApplications\" queued WHERE queued.\"UserId\" = owner.\"Id\" AND queued.\"IsDeleted\" = FALSE AND queued.\"Status\" = {(int)AIApplyRunStatus.Queued} AND queued.\"ScheduledAtUtc\" <= {now}) DESC, (SELECT MIN(queued.\"ScheduledAtUtc\") FROM \"AIApplyApplications\" queued WHERE queued.\"UserId\" = owner.\"Id\" AND queued.\"IsDeleted\" = FALSE AND queued.\"Status\" = {(int)AIApplyRunStatus.Queued} AND queued.\"ScheduledAtUtc\" <= {now}) FOR UPDATE OF owner SKIP LOCKED LIMIT 1").SingleOrDefaultAsync(ct);
            if (claimUserId != Guid.Empty)
                claimed = await context.AIApplyApplications.FromSqlInterpolated($"SELECT candidate.* FROM \"AIApplyApplications\" candidate WHERE candidate.\"UserId\" = {claimUserId} AND candidate.\"IsDeleted\" = FALSE AND candidate.\"Status\" = {(int)AIApplyRunStatus.Queued} AND candidate.\"ScheduledAtUtc\" <= {now} AND (SELECT COUNT(*) FROM \"AIApplyApplications\" active WHERE active.\"IsDeleted\" = FALSE AND active.\"UserId\" = {claimUserId} AND active.\"Status\" = {(int)AIApplyRunStatus.Processing}) < {limit} ORDER BY candidate.\"Priority\" DESC, candidate.\"ScheduledAtUtc\" FOR UPDATE SKIP LOCKED LIMIT 1").SingleOrDefaultAsync(ct);
            if (claimed is not null) { claimed.Status = AIApplyRunStatus.Processing; claimed.StartedAtUtc ??= now; claimed.ClaimedAtUtc = now; claimed.LeaseOwner = workerId; claimed.LeaseExpiresAtUtc = leaseExpiresAtUtc; await context.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
        }); return claimed;
    }
    public async Task<int> RecoverExpiredLeasesAsync(DateTime now, int maximum, CancellationToken ct = default)
    {
        if (!context.Database.IsRelational())
        {
            var testItems = await context.AIApplyApplications.Where(x => x.Status == AIApplyRunStatus.Processing && x.LeaseExpiresAtUtc < now).OrderBy(x => x.LeaseExpiresAtUtc).Take(Math.Clamp(maximum, 1, 100)).ToListAsync(ct);
            Recover(testItems, now);
            if (testItems.Count > 0) await context.SaveChangesAsync(ct);
            return testItems.Count;
        }
        var recovered = 0;
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var stale = await context.AIApplyApplications.FromSqlInterpolated($"SELECT candidate.* FROM \"AIApplyApplications\" candidate WHERE candidate.\"IsDeleted\" = FALSE AND candidate.\"Status\" = {(int)AIApplyRunStatus.Processing} AND candidate.\"LeaseExpiresAtUtc\" < {now} ORDER BY candidate.\"LeaseExpiresAtUtc\" FOR UPDATE SKIP LOCKED LIMIT {Math.Clamp(maximum, 1, 100)}").ToListAsync(ct);
            Recover(stale, now);
            if (stale.Count > 0) { await context.SaveChangesAsync(ct); recovered = stale.Count; }
            await transaction.CommitAsync(ct);
        });
        return recovered;
    }
    private static void Recover(IEnumerable<AIApplyApplication> stale, DateTime now)
    {
        foreach (var item in stale)
        {
            item.LeaseOwner = null; item.LeaseExpiresAtUtc = null;
            if (item.SubmissionAttemptedAtUtc.HasValue) { item.Status = AIApplyRunStatus.NeedsReview; item.FailureKind = AIApplyFailureKind.SubmissionUnconfirmed; item.LastErrorCode = "worker_recovery_submission_uncertain"; item.FailureClassification = AIApplyFailureClassification.SubmissionUnconfirmed.ToString(); }
            else { item.Status = AIApplyRunStatus.Queued; item.ScheduledAtUtc = now; item.RetryCount++; item.LastErrorCode = "worker_recovery"; }
        }
    }
    public Task<bool> MarkSubmissionAttemptedAsync(Guid applicationId, DateTime now, CancellationToken ct = default) => MarkSubmissionAsync(applicationId, now, false, ct);
    public Task<bool> MarkSubmissionConfirmedAsync(Guid applicationId, DateTime now, CancellationToken ct = default) => MarkSubmissionAsync(applicationId, now, true, ct);
    private async Task<bool> MarkSubmissionAsync(Guid id, DateTime now, bool confirmed, CancellationToken ct) { var item = await context.AIApplyApplications.SingleOrDefaultAsync(x => x.Id == id && x.Status == AIApplyRunStatus.Processing, ct); if (item is null) return false; if (confirmed) item.SubmissionConfirmedAtUtc = now; else item.SubmissionAttemptedAtUtc ??= now; await context.SaveChangesAsync(ct); return true; }
    public Task AddAsync<T>(T entity, CancellationToken ct = default) where T : class => context.AddAsync(entity, ct).AsTask();
    public void Remove<T>(T entity) where T : class => context.Remove(entity);
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
