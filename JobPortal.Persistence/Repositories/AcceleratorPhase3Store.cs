using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorPhase3Store(JobPortalDbContext context) : IAcceleratorPhase3Store
{
    public Task<AcceleratorTutorConversation?> ConversationAsync(Guid userId, Guid id, CancellationToken ct) => context.AcceleratorTutorConversations.Include(x => x.Messages).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorTutorConversation?> ConversationByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => context.AcceleratorTutorConversations.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorTutorConversation>> ConversationsAsync(Guid userId, int skip, int take, CancellationToken ct) => await context.AcceleratorTutorConversations.Where(x => x.UserId == userId && !x.IsDeleted).OrderByDescending(x => x.LastMessageAtUtc).Skip(skip).Take(take).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorTutorMessage>> MessagesAsync(Guid userId, Guid conversationId, int take, CancellationToken ct) => await context.AcceleratorTutorMessages.Where(x => x.ConversationId == conversationId && x.Conversation.UserId == userId && !x.IsDeleted).OrderByDescending(x => x.CreatedAtUtc).Take(take).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
    public Task<AcceleratorPracticeSession?> PracticeSessionAsync(Guid userId, Guid id, CancellationToken ct) => context.AcceleratorPracticeSessions.Include(x => x.Questions).SingleOrDefaultAsync(x => x.UserId == userId && x.Id == id && !x.IsDeleted, ct);
    public Task<AcceleratorPracticeSession?> PracticeSessionByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => context.AcceleratorPracticeSessions.Include(x => x.Questions).SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public Task<AcceleratorPracticeQuestion?> PracticeQuestionAsync(Guid userId, Guid sessionId, Guid questionId, CancellationToken ct) => context.AcceleratorPracticeQuestions.SingleOrDefaultAsync(x => x.SessionId == sessionId && x.Id == questionId && x.Session.UserId == userId && !x.IsDeleted, ct);
    public Task<AcceleratorPracticeAttempt?> PracticeAttemptByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => context.AcceleratorPracticeAttempts.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public async Task<IReadOnlyList<AcceleratorPracticeEvaluation>> PracticeEvaluationsAsync(Guid userId, Guid sessionId, CancellationToken ct) => await context.AcceleratorPracticeEvaluations.Where(x => x.Attempt.UserId == userId && x.Attempt.SessionId == sessionId && !x.IsDeleted).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorAiDispatchRecord>> FailedDispatchesAsync(int skip, int take, CancellationToken ct) => await context.AcceleratorAiDispatchRecords.Where(x => x.Status is AcceleratorAiDispatchStatus.Failed or AcceleratorAiDispatchStatus.Uncertain).OrderByDescending(x => x.CreatedAtUtc).Skip(skip).Take(take).ToListAsync(ct);
    public async Task<IReadOnlyList<AcceleratorAiDispatchRecord>> EligibleReconciliationDispatchesAsync(DateTime now, int take, CancellationToken ct) => await context.AcceleratorAiDispatchRecords.Where(x => x.Status == AcceleratorAiDispatchStatus.Uncertain && !x.RequiresManualReview && x.NextReconciliationAtUtc <= now && (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now) && !x.IsDeleted).OrderBy(x => x.NextReconciliationAtUtc).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
    public async Task<AcceleratorAiDispatchRecord?> ClaimReconciliationAsync(Guid dispatchId, Guid claimId, DateTime now, DateTime expiresAt, CancellationToken ct)
    {
        var claimed = await context.AcceleratorAiDispatchRecords.Where(x => x.Id == dispatchId && x.Status == AcceleratorAiDispatchStatus.Uncertain && !x.RequiresManualReview && !x.IsDeleted && (x.ReconciliationClaimExpiresAtUtc == null || x.ReconciliationClaimExpiresAtUtc <= now)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReconciliationClaimId, claimId).SetProperty(x => x.ReconciliationClaimExpiresAtUtc, expiresAt).SetProperty(x => x.LastReconciliationAtUtc, now).SetProperty(x => x.ReconciliationAttemptCount, x => x.ReconciliationAttemptCount + 1).SetProperty(x => x.Revision, Guid.NewGuid()).SetProperty(x => x.UpdatedAtUtc, now), ct);
        return claimed == 0 ? null : await context.AcceleratorAiDispatchRecords.SingleOrDefaultAsync(x => x.Id == dispatchId && x.ReconciliationClaimId == claimId && !x.IsDeleted, ct);
    }
    public Task<AcceleratorAiDispatchRecord?> DispatchByKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => context.AcceleratorAiDispatchRecords.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);
    public void Add(object entity) => context.Add(entity);
}
