using JobPortal.Application.Features.Referrals;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class ReferralMarketplaceRepository(JobPortalDbContext db) : IReferralMarketplaceRepository
{
    private static readonly SemaphoreSlim TestGate = new(1, 1);
    public async Task<T> WriteAsync<T>(Guid candidateId, Guid referralId, Func<Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await TestGate.WaitAsync(ct);
            try { db.ChangeTracker.Clear(); var result = await action(); await db.SaveChangesAsync(ct); return result; }
            finally { TestGate.Release(); }
        }
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Candidate first, opportunity second, for every mutation. Locks work across application instances.
            var candidateKey = BitConverter.ToInt64(candidateId.ToByteArray(), 0) ^ 0x52454643414E4449L;
            var referralKey = BitConverter.ToInt64(referralId.ToByteArray(), 0) ^ 0x5245464F50504F52L;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({candidateKey})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({referralKey})", ct);
            // Coordinate also with existing membership renewal and opportunity moderation writes.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Memberships\" WHERE \"UserId\" = {candidateId} AND \"PlanCode\" = 'ReferralContactAccess' AND \"IsDeleted\" = FALSE FOR UPDATE", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"JobReferrals\" WHERE \"Id\" = {referralId} FOR UPDATE", ct);
            try
            {
                var result = await action(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { throw new ConflictException("This referral was already requested.", "REFERRAL_ALREADY_REQUESTED"); }
        });
    }
    public Task<User?> UserAsync(Guid id, CancellationToken ct) => db.Users.AsNoTracking().Include(x => x.Role)
        .SingleOrDefaultAsync(x => x.Id == id && x.Status == UserStatus.Active, ct);
    public Task<JobReferral?> OpportunityAsync(Guid id, CancellationToken ct) => db.JobReferrals.AsNoTracking()
        .Include(x => x.Job).ThenInclude(x => x.Company).Include(x => x.ReferrerUser)
        .SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Membership?> MembershipAsync(Guid candidateId, DateTime now, CancellationToken ct) => db.Memberships.AsNoTracking()
        .SingleOrDefaultAsync(x => x.UserId == candidateId && x.PlanCode == "ReferralContactAccess" && x.Status == MembershipStatus.Active &&
            x.StartsAtUtc <= now && (x.EndsAtUtc == null || x.EndsAtUtc > now), ct);
    private IQueryable<ReferralRequest> Requests() => db.Set<ReferralRequest>()
        .Include(x => x.JobReferral).ThenInclude(x => x.Job).ThenInclude(x => x.Company)
        .Include(x => x.JobReferral).ThenInclude(x => x.ReferrerUser)
        .Include(x => x.CandidateUser).ThenInclude(x => x.CandidateSkills)
        .Include(x => x.CandidateUser).ThenInclude(x => x.CandidateExperiences)
        .Include(x => x.CandidateUser).ThenInclude(x => x.ResumeProfile).AsSplitQuery();
    public Task<ReferralRequest?> RequestAsync(Guid id, Guid? candidateId, Guid? referrerId, CancellationToken ct) => Requests()
        .SingleOrDefaultAsync(x => x.Id == id && (candidateId == null || x.CandidateUserId == candidateId) &&
            (referrerId == null || x.ReferrerUserId == referrerId && x.JobReferral.ReferrerUserId == referrerId), ct);
    public Task<ReferralRequest?> ForOpportunityAsync(Guid candidateId, Guid referralId, CancellationToken ct) => Requests()
        .IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CandidateUserId == candidateId && x.JobReferralId == referralId, ct);
    public Task<int> AcceptedForOpportunityAsync(Guid referralId, CancellationToken ct) => db.Set<ReferralRequest>().IgnoreQueryFilters()
        .CountAsync(x => x.JobReferralId == referralId && x.AcceptedAtUtc != null, ct);
    public Task<int> AcceptedForPeriodAsync(Guid candidateId, Guid membershipId, DateTime start, CancellationToken ct) =>
        db.Set<ReferralRequest>().IgnoreQueryFilters().CountAsync(x => x.CandidateUserId == candidateId &&
            x.AcceptedMembershipId == membershipId && x.QuotaPeriodStartUtc == start && x.AcceptedAtUtc != null, ct);
    public async Task AddAsync(ReferralRequest request, CancellationToken ct)
    {
        db.Attach(request.JobReferral);
        await db.Set<ReferralRequest>().AddAsync(request, ct);
    }
    public async Task<(IReadOnlyCollection<ReferralRequest> Items, int Total)> ListAsync(Guid? candidateId, Guid? referrerId,
        ReferralRequestStatus? status, bool issuesOnly, int page, int size, DateTime now, CancellationToken ct)
    {
        var query = Requests().AsNoTracking().Where(x => (candidateId == null || x.CandidateUserId == candidateId) &&
            (referrerId == null || x.ReferrerUserId == referrerId && x.JobReferral.ReferrerUserId == referrerId) && (!issuesOnly || x.NotReceivedAtUtc != null));
        if (status == ReferralRequestStatus.Expired)
            query = query.Where(x => x.Status == ReferralRequestStatus.Expired || x.Status == ReferralRequestStatus.Requested && x.ExpiresAtUtc <= now);
        else if (status == ReferralRequestStatus.Requested)
            query = query.Where(x => x.Status == ReferralRequestStatus.Requested && x.ExpiresAtUtc > now);
        else if (status.HasValue) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        return (items, total);
    }
    public async Task<ReferralMetricsResponse> MetricsAsync(Guid referrerId, CancellationToken ct)
    {
        var counts = await db.Set<ReferralRequest>().IgnoreQueryFilters().Where(x => x.ReferrerUserId == referrerId)
            .GroupBy(x => x.ReferrerUserId).Select(g => new
            { Received = g.Count(), Accepted = g.Count(x => x.AcceptedAtUtc != null), Submitted = g.Count(x => x.ReferralSubmittedAtUtc != null), Confirmed = g.Count(x => x.CandidateConfirmedAtUtc != null) }).SingleOrDefaultAsync(ct);
        var jobs = await db.JobReferrals.IgnoreQueryFilters().CountAsync(x => x.ReferrerUserId == referrerId, ct);
        return new(jobs, counts?.Received ?? 0, counts?.Accepted ?? 0, counts?.Submitted ?? 0, counts?.Confirmed ?? 0,
            counts is { Submitted: > 0 } ? (decimal)counts.Confirmed / counts.Submitted : 0m);
    }
}
