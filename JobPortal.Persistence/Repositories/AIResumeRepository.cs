using JobPortal.Application.Features.AIResume;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AIResumeRepository(JobPortalDbContext context) : IAIResumeRepository
{
    private static readonly SemaphoreSlim TestGate = new(1, 1);
    public async Task<T> WriteAsync<T>(Guid userId, Func<Task<T>> action, CancellationToken ct)
    {
        if (!context.Database.IsRelational())
        {
            await TestGate.WaitAsync(ct);
            try { var result = await action(); await context.SaveChangesAsync(ct); return result; }
            finally { TestGate.Release(); }
        }
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            // Transaction-scoped, feature-namespaced user lock: never held during provider/network I/O.
            var key = BitConverter.ToInt64(userId.ToByteArray(), 0) ^ 0x4149524553554D45L;
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            var result = await action();
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
    public Task<User?> CandidateAsync(Guid userId, CancellationToken ct) => context.Users.AsNoTracking()
        .Include(x => x.Role).Include(x => x.ResumeProfile)
        .SingleOrDefaultAsync(x => x.Id == userId && x.Status == UserStatus.Active && x.Role.Name == "Candidate", ct);
    public Task<Job?> JobAsync(Guid jobId, DateTime now, CancellationToken ct) => context.Jobs.Include(x => x.Company)
        .SingleOrDefaultAsync(x => x.Id == jobId && x.Status == JobStatus.Published && !x.IsHidden &&
            x.PublishedAtUtc != null && x.PublishedAtUtc <= now && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now), ct);
    public Task<AIResumeSession?> SessionAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        context.Set<AIResumeSession>().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == sessionId, ct);
    public Task<AIResumeCreditWallet?> WalletAsync(Guid userId, CancellationToken ct) =>
        context.Set<AIResumeCreditWallet>().SingleOrDefaultAsync(x => x.UserId == userId, ct);
    public Task<AIResumePurchase?> PurchaseAsync(string merchantOrderId, CancellationToken ct) =>
        context.Set<AIResumePurchase>().SingleOrDefaultAsync(x => x.MerchantOrderId == merchantOrderId, ct);
    public Task<AIResumePurchase?> PurchaseByKeyAsync(Guid userId, Guid key, CancellationToken ct) =>
        context.Set<AIResumePurchase>().SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == key, ct);
    public Task<AIResumePurchase?> PurchaseForOwnerAsync(Guid userId, string merchantOrderId, CancellationToken ct) =>
        context.Set<AIResumePurchase>().SingleOrDefaultAsync(x => x.UserId == userId && x.MerchantOrderId == merchantOrderId, ct);
    public Task<AIResumeGeneration?> GenerationByKeyAsync(Guid userId, Guid key, CancellationToken ct) =>
        context.Set<AIResumeGeneration>().SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == key, ct);
    public Task<AIResumeGeneration?> ActiveGenerationAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        context.Set<AIResumeGeneration>().SingleOrDefaultAsync(x => x.UserId == userId && x.SessionId == sessionId && x.Status == AIResumeGenerationStatus.Reserved, ct);
    public Task<TailoredResume?> ResumeAsync(Guid userId, Guid resumeId, CancellationToken ct) =>
        context.Set<TailoredResume>().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == resumeId, ct);
    public Task<TailoredResume?> LatestAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        context.Set<TailoredResume>().Where(x => x.UserId == userId && x.SessionId == sessionId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    public Task<TailoredResume?> ResumeForGenerationAsync(Guid userId, Guid generationId, CancellationToken ct) =>
        context.Set<TailoredResume>().SingleOrDefaultAsync(x => x.UserId == userId && x.GenerationId == generationId, ct);
    public async Task<IReadOnlyList<(TailoredResume Resume, AIResumeSession Session)>> HistoryAsync(Guid userId, int skip, int take, CancellationToken ct) =>
        await context.Set<TailoredResume>().AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take)
            .Join(context.Set<AIResumeSession>().AsNoTracking(), resume => resume.SessionId, session => session.Id,
                (resume, session) => new { Resume = resume, Session = session })
            .Where(x => x.Session.UserId == userId)
            .Select(x => new ValueTuple<TailoredResume, AIResumeSession>(x.Resume, x.Session)).ToListAsync(ct);
    public void Add(object entity) => context.Add(entity);
}
