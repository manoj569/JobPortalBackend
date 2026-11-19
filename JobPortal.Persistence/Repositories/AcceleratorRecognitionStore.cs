using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorRecognitionStore(JobPortalDbContext db) : IAcceleratorRecognitionStore
{
    public Task<AcceleratorSkillProficiencyPolicy?> PolicyAsync(Guid? pathId, CancellationToken ct) =>
        db.AcceleratorSkillProficiencyPolicies.AsNoTracking().Include(x => x.Bands).Where(x => !x.IsDeleted && x.IsActive && (x.LearningPathId == pathId || x.LearningPathId == null))
            .OrderByDescending(x => x.LearningPathId != null).ThenByDescending(x => x.Version).ThenBy(x => x.Code).FirstOrDefaultAsync(ct);
    public async Task<RecognitionSignals> SignalsAsync(AcceleratorJourneyContext context, CancellationToken ct)
    {
        var scores = await db.AcceleratorAssessmentAttempts.AsNoTracking().Where(x => !x.IsDeleted && x.UserId == context.UserId && x.JourneyContextId == context.Id && x.Status == AcceleratorAssessmentStatus.Completed && x.Score != null)
            .Select(x => x.Score!.Value).ToArrayAsync(ct);
        var total = 0; var complete = 0;
        if (context.LearningPathEnrollmentId.HasValue)
        {
            var enrollment = await new AcceleratorLearningPathStore(db).EnrollmentAsync(context.UserId, context.LearningPathEnrollmentId.Value, ct);
            if (enrollment is not null)
            {
                var modules = enrollment.LearningPath.Modules.Where(x => !x.IsDeleted && x.Status == AcceleratorLearningCatalogStatus.Published && x.IsRequired).ToArray();
                total = modules.Length;
                complete = modules.Count(m => !m.AssessmentTemplateId.HasValue && !m.ProjectVersionId.HasValue && !m.InterviewVersionId.HasValue &&
                    m.Lessons.Any(l => !l.IsDeleted && l.Status == AcceleratorLearningCatalogStatus.Published && l.IsRequired) &&
                    m.Lessons.Where(l => !l.IsDeleted && l.Status == AcceleratorLearningCatalogStatus.Published && l.IsRequired)
                        .All(l => enrollment.LessonProgress.Any(p => p.LessonId == l.Id && p.Status == AcceleratorLessonProgressStatus.Completed)));
            }
        }
        // Attribution is inherited from the owned session, never from a caller-supplied goal.
        var practice = await (from session in db.AcceleratorPracticeSessions.AsNoTracking()
            join attempt in db.AcceleratorPracticeAttempts on session.Id equals attempt.SessionId
            join question in db.AcceleratorPracticeQuestions on attempt.QuestionId equals question.Id
            join evaluation in db.AcceleratorPracticeEvaluations on attempt.Id equals evaluation.AttemptId
            where session.UserId == context.UserId && session.JourneyContextId == context.Id && !session.IsDeleted &&
                attempt.UserId == context.UserId && !attempt.IsDeleted && question.SessionId == session.Id && !question.IsDeleted && question.IsQualityApproved &&
                !evaluation.IsDeleted && evaluation.Method == AcceleratorPracticeEvaluationMethod.Deterministic && evaluation.Confidence == 1 && evaluation.MaximumScore > 0
            select new { evaluation.Score, evaluation.MaximumScore }).ToArrayAsync(ct);
        decimal? practiceScore = practice.Length == 0 ? null : decimal.Round(practice.Sum(x => x.Score) * 100m / practice.Sum(x => x.MaximumScore), 2);
        return new(scores.Length == 0 ? null : decimal.Round(scores.Average(),2), scores.Length, complete, total, practiceScore);
    }
    public async Task<IReadOnlyList<AcceleratorAchievementDefinition>> DefinitionsAsync(CancellationToken ct) => await db.AcceleratorAchievementDefinitions.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted).OrderBy(x => x.Code).ToArrayAsync(ct);
    public async Task<IReadOnlyList<AcceleratorAchievementDefinition>> DefinitionsByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.AcceleratorAchievementDefinitions.AsNoTracking().Where(x=>ids.Contains(x.Id)).ToArrayAsync(ct);
    public async Task<IReadOnlyList<CandidateAcceleratorAchievement>> AwardsAsync(Guid actor, Guid contextId, CancellationToken ct) => await db.CandidateAcceleratorAchievements.AsNoTracking().Where(x => x.UserId == actor && x.JourneyContextId == contextId && !x.IsDeleted).OrderBy(x => x.AwardedAtUtc).ToArrayAsync(ct);
    public Task<AcceleratorEnrollmentCertificate?> CertificateAsync(Guid actor, Guid contextId, CancellationToken ct) => db.AcceleratorEnrollmentCertificates.SingleOrDefaultAsync(x => x.UserId == actor && x.JourneyContextId == contextId && !x.IsDeleted,ct);
    public void Add(object entity) => db.Add(entity);
}
