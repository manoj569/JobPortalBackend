using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Context;

internal static class AcceleratorPersistencePolicy
{
    public static void Enforce(JobPortalDbContext context)
    {
        var tracker = context.ChangeTracker;
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.State == EntityState.Added)
            {
                var addedVersionId = entry.Entity switch
                {
                    AcceleratorProjectCompetency competency => competency.ProjectVersionId,
                    AcceleratorProjectMilestone milestone => milestone.ProjectVersionId,
                    AcceleratorProjectRubric rubric => rubric.ProjectVersionId,
                    AcceleratorProjectAssignment assignment => tracker.Entries<AcceleratorProjectMilestone>()
                        .FirstOrDefault(x => x.Entity.Id == assignment.MilestoneId)?.Entity.ProjectVersionId
                        ?? context.AcceleratorProjectMilestones.AsNoTracking().Where(x => x.Id == assignment.MilestoneId)
                            .Select(x => x.ProjectVersionId).SingleOrDefault(),
                    AcceleratorProjectRubricCriterion criterion => tracker.Entries<AcceleratorProjectRubric>()
                        .FirstOrDefault(x => x.Entity.Id == criterion.RubricId)?.Entity.ProjectVersionId
                        ?? context.AcceleratorProjectRubrics.AsNoTracking().Where(x => x.Id == criterion.RubricId)
                            .Select(x => x.ProjectVersionId).SingleOrDefault(),
                    _ => Guid.Empty
                };
                if (addedVersionId != Guid.Empty) EnsureDraftUnreferenced(context, addedVersionId);
                var addedInterviewVersionId = entry.Entity switch
                {
                    AcceleratorInterviewCompetency competency => competency.InterviewVersionId,
                    AcceleratorInterviewSection section => section.InterviewVersionId,
                    AcceleratorInterviewRubricCriterion criterion => criterion.InterviewVersionId,
                    AcceleratorInterviewQuestion question => tracker.Entries<AcceleratorInterviewSection>()
                        .FirstOrDefault(x => x.Entity.Id == question.SectionId)?.Entity.InterviewVersionId
                        ?? context.AcceleratorInterviewSections.AsNoTracking().Where(x => x.Id == question.SectionId)
                            .Select(x => x.InterviewVersionId).SingleOrDefault(),
                    _ => Guid.Empty
                };
                if (addedInterviewVersionId != Guid.Empty) EnsureInterviewDraftUnreferenced(context, addedInterviewVersionId);
                continue;
            }
            if (entry.Entity is AcceleratorJourneyProvisioningReceipt or CandidateAcceleratorAchievement or AcceleratorSkillProficiencyPolicy or AcceleratorProficiencyBand or AcceleratorAchievementDefinition or
                AcceleratorTaxonomy or AcceleratorCareerRole or AcceleratorCompetency or AcceleratorPrerequisite or
                AcceleratorRoleRequirement or AcceleratorBillingCycle or AcceleratorAllowance or AcceleratorUsageEvent or
                AcceleratorProgressEvent or AcceleratorSkillEvidence or AcceleratorTutorMessage or AcceleratorPracticeAttempt or
                AcceleratorPracticeEvaluation or AcceleratorProjectSubmission or AcceleratorProjectEvaluation or AcceleratorProjectEvaluationScore or
                AcceleratorSessionQuestion or AcceleratorInterviewAnswer or AcceleratorInterviewEvaluation or
                AcceleratorInterviewCriterionScore or AcceleratorReadinessSnapshot)
                throw new InvalidOperationException("Published Accelerator taxonomy, billing allocations and usage events are immutable.");
            if (entry.Entity is TalentSubmissionEvent or TalentOutreachRecipient)
                throw new InvalidOperationException("Talent status history and recipient snapshots are append-only.");
            if (entry.Entity is TalentShareLink link &&
                (entry.State == EntityState.Deleted || entry.Property(nameof(link.CandidateId)).IsModified ||
                 entry.Property(nameof(link.GoalId)).IsModified ||
                 entry.Property(nameof(link.TokenHash)).IsModified ||
                 entry.Property(nameof(link.JobId)).IsModified ||
                 entry.Property(nameof(link.CompanyId)).IsModified))
                throw new InvalidOperationException("Talent share identity is immutable.");
            if (entry.Entity is TalentSubmission submission &&
                (entry.State == EntityState.Deleted || entry.Property(nameof(submission.CandidateId)).IsModified ||
                 entry.Property(nameof(submission.JobId)).IsModified ||
                 entry.Property(nameof(submission.CompanyId)).IsModified ||
                 entry.Property(nameof(submission.GoalId)).IsModified))
                throw new InvalidOperationException("Talent submission identity is immutable.");
            if (entry.Entity is TalentOutreachDraft draft &&
                (entry.State == EntityState.Deleted || entry.Property(nameof(draft.CandidateId)).IsModified ||
                 entry.Property(nameof(draft.JobId)).IsModified ||
                 entry.Property(nameof(draft.CompanyId)).IsModified ||
                 (entry.Property(nameof(draft.Status)).OriginalValue is TalentOutreachStatus.Sent &&
                  entry.Properties.Any(p => p.IsModified && p.Metadata.Name is
                      nameof(draft.Subject) or nameof(draft.Body) or nameof(draft.ProviderMessageId) or
                      nameof(draft.FailureEvidenceReference) or
                      nameof(draft.SendIdempotencyKey)))))
                throw new InvalidOperationException("Sent outreach and draft identity are immutable.");
            var versionId = entry.Entity switch
            {
                AcceleratorProjectCompetency competency => competency.ProjectVersionId,
                AcceleratorProjectMilestone milestone => milestone.ProjectVersionId,
                AcceleratorProjectRubric rubric => rubric.ProjectVersionId,
                AcceleratorProjectAssignment assignment => tracker.Entries<AcceleratorProjectMilestone>()
                    .FirstOrDefault(x => x.Entity.Id == assignment.MilestoneId)?.Entity.ProjectVersionId
                    ?? context.AcceleratorProjectMilestones.AsNoTracking().Where(x => x.Id == assignment.MilestoneId).Select(x => x.ProjectVersionId).SingleOrDefault(),
                AcceleratorProjectRubricCriterion criterion => tracker.Entries<AcceleratorProjectRubric>()
                    .FirstOrDefault(x => x.Entity.Id == criterion.RubricId)?.Entity.ProjectVersionId
                    ?? context.AcceleratorProjectRubrics.AsNoTracking().Where(x => x.Id == criterion.RubricId).Select(x => x.ProjectVersionId).SingleOrDefault(),
                _ => Guid.Empty
            };
            if (versionId != Guid.Empty)
            {
                if (entry.Properties.Any(p => p.IsModified && p.Metadata.IsForeignKey()))
                    throw new InvalidOperationException("Project catalog children cannot be moved between parents.");
                EnsureDraftUnreferenced(context, versionId);
            }
            var interviewVersionId = entry.Entity switch
            {
                AcceleratorInterviewCompetency competency => competency.InterviewVersionId,
                AcceleratorInterviewSection section => section.InterviewVersionId,
                AcceleratorInterviewRubricCriterion criterion => criterion.InterviewVersionId,
                AcceleratorInterviewQuestion question => tracker.Entries<AcceleratorInterviewSection>()
                    .FirstOrDefault(x => x.Entity.Id == question.SectionId)?.Entity.InterviewVersionId
                    ?? context.AcceleratorInterviewSections.AsNoTracking().Where(x => x.Id == question.SectionId)
                        .Select(x => x.InterviewVersionId).SingleOrDefault(),
                _ => Guid.Empty
            };
            if (interviewVersionId != Guid.Empty)
            {
                if (entry.Properties.Any(p => p.IsModified && p.Metadata.IsForeignKey()))
                    throw new InvalidOperationException("Interview catalog children cannot be moved between parents.");
                EnsureInterviewDraftUnreferenced(context, interviewVersionId);
            }
            if (entry.Entity is AcceleratorInterviewVersion interviewVersion)
            {
                if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Interview versions cannot be deleted.");
                var original = (AcceleratorInterviewCatalogStatus)entry.Property(nameof(interviewVersion.Status)).OriginalValue!;
                if (original != AcceleratorInterviewCatalogStatus.Draft &&
                    !(original == AcceleratorInterviewCatalogStatus.Published &&
                      interviewVersion.Status == AcceleratorInterviewCatalogStatus.Retired &&
                      entry.Properties.Where(p => p.IsModified).All(p => p.Metadata.Name is
                        nameof(interviewVersion.Status) or nameof(interviewVersion.RetiredAtUtc) or
                        nameof(interviewVersion.Revision) or nameof(interviewVersion.UpdatedAtUtc))))
                    throw new InvalidOperationException("Published or retired interviews are immutable.");
                if (original == AcceleratorInterviewCatalogStatus.Draft &&
                    interviewVersion.Status == AcceleratorInterviewCatalogStatus.Retired)
                    throw new InvalidOperationException("Draft interviews cannot be retired.");
                if (original == AcceleratorInterviewCatalogStatus.Draft) EnsureInterviewUnreferenced(context, interviewVersion.Id);
            }
            if (entry.Entity is AcceleratorInterviewSession interviewSession)
            {
                if (entry.State == EntityState.Deleted ||
                    (AcceleratorInterviewSessionStatus)entry.Property(nameof(interviewSession.Status)).OriginalValue! is
                        AcceleratorInterviewSessionStatus.Completed or AcceleratorInterviewSessionStatus.Abandoned ||
                    entry.Properties.Any(p => p.IsModified && p.Metadata.Name is
                        nameof(interviewSession.UserId) or nameof(interviewSession.GoalId) or
                        nameof(interviewSession.InterviewVersionId) or nameof(interviewSession.RequestKey) or
                        nameof(interviewSession.RequestHash)))
                    throw new InvalidOperationException("Interview identity and completed sessions are immutable.");
            }
            if (entry.Entity is AcceleratorPassportShare share &&
                (entry.State == EntityState.Deleted || entry.Property(nameof(share.UserId)).IsModified ||
                 entry.Property(nameof(share.GoalId)).IsModified))
                throw new InvalidOperationException("Passport share ownership is immutable.");
            if (entry.Entity is AcceleratorProjectVersion version)
            {
                if (entry.State == EntityState.Deleted)
                    throw new InvalidOperationException("Project catalog versions cannot be deleted.");
                var original = (AcceleratorProjectCatalogStatus)entry.Property(nameof(version.CatalogStatus)).OriginalValue!;
                if (original != AcceleratorProjectCatalogStatus.Draft &&
                    !(original == AcceleratorProjectCatalogStatus.Published && version.CatalogStatus == AcceleratorProjectCatalogStatus.Retired &&
                      entry.Properties.Where(p => p.IsModified).All(p => p.Metadata.Name is nameof(version.CatalogStatus) or nameof(version.IsActive) or nameof(version.UpdatedAtUtc))))
                    throw new InvalidOperationException("Published or retired project versions are immutable.");
                if (original == AcceleratorProjectCatalogStatus.Draft && version.CatalogStatus == AcceleratorProjectCatalogStatus.Retired)
                    throw new InvalidOperationException("A draft cannot be retired before publication.");
                if (original == AcceleratorProjectCatalogStatus.Draft) EnsureUnreferenced(context, version.Id);
            }
            if (entry.Entity is AcceleratorUsageReservation)
            {
                if (entry.State == EntityState.Deleted || entry.Property("IsDeleted").IsModified)
                    throw new InvalidOperationException("Accelerator usage accounting cannot be deleted.");
                foreach (var property in entry.Properties.Where(p => p.IsModified))
                {
                    if (property.Metadata.Name is not ("State" or "Revision" or "FinishedAtUtc" or "ActualCostInr" or
                        "InputTokens" or "OutputTokens" or "PolicyViolation" or "UpdatedAtUtc"))
                        throw new InvalidOperationException("Accelerator usage reservation identity and estimate are immutable.");
                }
            }
            if (entry.Entity is AcceleratorAiDispatchRecord dispatch)
            {
                if (entry.State == EntityState.Deleted ||
                    ((AcceleratorAiDispatchStatus)entry.Property(nameof(dispatch.Status)).OriginalValue! is
                        AcceleratorAiDispatchStatus.Reconciled or AcceleratorAiDispatchStatus.Failed) ||
                    entry.Properties.Any(p => p.IsModified && (p.Metadata.Name is
                        nameof(dispatch.UserId) or nameof(dispatch.ConversationId) or nameof(dispatch.CandidateProjectId) or
                        nameof(dispatch.ProjectSubmissionId) or nameof(dispatch.RequestKey) or nameof(dispatch.ReservationId) or
                        nameof(dispatch.InterviewSessionId) or nameof(dispatch.InterviewAnswerId) or
                        nameof(dispatch.InterviewSectionId) or nameof(dispatch.GenerationSlot) or
                        nameof(dispatch.Operation) or nameof(dispatch.Provider) or nameof(dispatch.RequestHash))) ||
                    (entry.Property(nameof(dispatch.ResultJson)).OriginalValue is not null &&
                        entry.Property(nameof(dispatch.ResultJson)).IsModified))
                    throw new InvalidOperationException("AI dispatch identity, verified output and terminal state are immutable.");
            }
            if (entry.Entity is AcceleratorCareerGoal && (entry.Property("UserId").IsModified || entry.Property("RoleId").IsModified || entry.Property("TaxonomyId").IsModified))
                throw new InvalidOperationException("Career goal identity and taxonomy history are immutable.");
        }
    }

    private static void EnsureDraftUnreferenced(JobPortalDbContext context, Guid versionId)
    {
        var tracked = context.ChangeTracker.Entries<AcceleratorProjectVersion>()
            .FirstOrDefault(x => x.Entity.Id == versionId);
        var status = tracked is null
            ? context.AcceleratorProjectVersions.AsNoTracking().Where(x => x.Id == versionId)
                .Select(x => x.CatalogStatus).SingleOrDefault()
            : tracked.State == EntityState.Added ? tracked.Entity.CatalogStatus
                : (AcceleratorProjectCatalogStatus)tracked.Property(nameof(AcceleratorProjectVersion.CatalogStatus)).OriginalValue!;
        if (status != AcceleratorProjectCatalogStatus.Draft)
            throw new InvalidOperationException("Published or retired project catalog children are immutable.");
        EnsureUnreferenced(context, versionId);
    }

    private static void EnsureUnreferenced(JobPortalDbContext context, Guid versionId)
    {
        if (context.ChangeTracker.Entries<AcceleratorCandidateProject>()
            .Any(x => x.Entity.ProjectVersionId == versionId))
            throw new InvalidOperationException("Candidate-referenced project versions are immutable.");
        if (context.AcceleratorCandidateProjects.AsNoTracking().Any(x => x.ProjectVersionId == versionId))
            throw new InvalidOperationException("Candidate-referenced project versions are immutable.");
    }

    private static void EnsureInterviewDraftUnreferenced(JobPortalDbContext context, Guid versionId)
    {
        var tracked = context.ChangeTracker.Entries<AcceleratorInterviewVersion>()
            .FirstOrDefault(x => x.Entity.Id == versionId);
        var status = tracked is null
            ? context.AcceleratorInterviewVersions.AsNoTracking().Where(x => x.Id == versionId)
                .Select(x => x.Status).SingleOrDefault()
            : tracked.State == EntityState.Added ? tracked.Entity.Status
                : (AcceleratorInterviewCatalogStatus)tracked.Property(nameof(AcceleratorInterviewVersion.Status)).OriginalValue!;
        if (status != AcceleratorInterviewCatalogStatus.Draft)
            throw new InvalidOperationException("Published or retired interview catalog children are immutable.");
        EnsureInterviewUnreferenced(context, versionId);
    }

    private static void EnsureInterviewUnreferenced(JobPortalDbContext context, Guid versionId)
    {
        if (context.ChangeTracker.Entries<AcceleratorInterviewSession>()
            .Any(x => x.Entity.InterviewVersionId == versionId) ||
            context.AcceleratorInterviewSessions.AsNoTracking().Any(x => x.InterviewVersionId == versionId))
            throw new InvalidOperationException("Candidate-referenced interview versions are immutable.");
    }
}
