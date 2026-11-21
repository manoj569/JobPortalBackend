using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorJourneySummaryStore(JobPortalDbContext db) : IAcceleratorJourneySummaryStore
{
    public async Task<JourneyMarketResolution> ResolveMarketAsync(Guid actor, Guid marketPathId, CancellationToken ct)
    {
        // Market career-card identity is MarketRole.Id. It is never treated as GoalId or LearningPathId.
        var market = await db.MarketRoles.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==marketPathId && !x.IsDeleted && x.Status==MarketConceptStatus.Active,ct);
        if (market?.AcceleratorRoleId is null) return new("MarketRoleNotMapped",market?.Name,null,null,null,null);
        var role = await db.AcceleratorCareerRoles.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==market.AcceleratorRoleId && !x.IsDeleted,ct);
        if (role is null) return new("MarketRoleNotMapped",market.Name,null,null,null,null);
        var enrollment = await db.AcceleratorPathEnrollments.AsNoTracking().Where(x=>x.UserId==actor && !x.IsDeleted && x.Status!=AcceleratorLearningEnrollmentStatus.Abandoned &&
            !x.LearningPath.IsDeleted && x.LearningPath.Status==AcceleratorLearningCatalogStatus.Published && x.LearningPath.CanonicalCareerRoleId==role.Id)
            .OrderByDescending(x=>x.LastActivityAtUtc).ThenBy(x=>x.Id).Select(x=>new {x.Id,x.LearningPathId}).FirstOrDefaultAsync(ct);
        if (enrollment is not null) return new("Ready",market.Name,role.Id,enrollment.LearningPathId,enrollment.Id,null);
        var goal = await db.AcceleratorCareerGoals.AsNoTracking().Where(x=>x.UserId==actor && x.RoleId==role.Id && !x.IsDeleted && x.Status==AcceleratorGoalStatus.Active)
            .OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        if (goal is not null) return new("Ready",market.Name,role.Id,null,null,goal.Id);
        var path = await db.AcceleratorLearningPaths.AsNoTracking().Where(x=>!x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published && x.CanonicalCareerRoleId==role.Id)
            .OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        return new(path is null ? "LearningPathNotConfigured" : "EnrollmentRequired",market.Name,role.Id,path?.Id,null,null);
    }

    public async Task<JourneyMarketResolution> ResolveMarketSkillAsync(Guid actor, Guid marketSkillPathId, CancellationToken ct)
    {
        // Skill-card IDs and career-card IDs belong to different namespaces and are resolved explicitly.
        var market=await db.MarketSkills.AsNoTracking().Include(x=>x.Skill).Include(x=>x.Competency)
            .SingleOrDefaultAsync(x=>x.Id==marketSkillPathId && !x.IsDeleted && x.Status==MarketConceptStatus.Active,ct);
        if (market is null || market.Skill.IsDeleted || !market.CompetencyId.HasValue)
            return new("MarketSkillNotMapped",market?.Skill.Name,null,null,null,null);
        var competency=market.Competency;
        if (competency is null || competency.IsDeleted || competency.SkillId!=market.SkillId)
            return new("CompetencyMappingIncomplete",market.Skill.Name,null,null,null,null);
        var enrollment=await db.AcceleratorPathEnrollments.AsNoTracking().Where(x=>x.UserId==actor && !x.IsDeleted && x.Status!=AcceleratorLearningEnrollmentStatus.Abandoned &&
            !x.LearningPath.IsDeleted && x.LearningPath.Status==AcceleratorLearningCatalogStatus.Published && x.LearningPath.Type==AcceleratorLearningPathType.Skill && x.LearningPath.CanonicalSkillId==market.SkillId)
            .OrderByDescending(x=>x.LastActivityAtUtc).ThenBy(x=>x.Id).Select(x=>new {x.Id,x.LearningPathId}).FirstOrDefaultAsync(ct);
        var pathId=enrollment?.LearningPathId ?? await db.AcceleratorLearningPaths.AsNoTracking().Where(x=>!x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published &&
            x.Type==AcceleratorLearningPathType.Skill && x.CanonicalSkillId==market.SkillId).OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (!pathId.HasValue) return new("SkillLearningPathNotConfigured",market.Skill.Name,null,null,null,null);
        var store=new AcceleratorJourneyStore(db);
        var requirements=await store.PathRequirementsAsync(pathId.Value,ct);
        var competencies=await store.CompetenciesAsync(requirements.Select(x=>x.CompetencyId).ToArray(),ct);
        var valid=requirements.Any(x=>x.CompetencyId==competency.Id) && requirements.All(x=>x.TargetProficiency is >=1 and <=5 && x.Weight>0) &&
            competencies.Count==requirements.Count && competencies.All(x=>x.TaxonomyId==competency.TaxonomyId);
        return new(!valid ? "CompetencyMappingIncomplete" : enrollment is null ? "EnrollmentRequired" : "Ready",market.Skill.Name,null,pathId,enrollment?.Id,null);
    }

    public async Task<JourneySummaryData> ReadAsync(Guid actor, AcceleratorJourneyWorkScope scope, DateTime now, CancellationToken ct)
    {
        var context=scope.Context; var phase2=new AcceleratorPhase2Store(db); var journeys=new AcceleratorJourneyStore(db);
        var enrollment=context.LearningPathEnrollmentId.HasValue ? await new AcceleratorLearningPathStore(db).EnrollmentAsync(actor,context.LearningPathEnrollmentId.Value,ct) : null;
        var goal=context.CareerGoalId.HasValue ? await db.AcceleratorCareerGoals.AsNoTracking().Include(x=>x.Role).SingleAsync(x=>x.Id==context.CareerGoalId && x.UserId==actor,ct) : null;
        var requirements=scope.Requirements.Select(x=>x.CompetencyId).ToArray();
        var template=enrollment is null ? (scope.RoleId.HasValue ? await phase2.ActiveTemplateAsync(scope.RoleId.Value,context.TaxonomyId,ct) : null)
            : (await journeys.AssessmentMappingAsync(enrollment.LearningPathId,AcceleratorLearningPathAssessmentPurpose.Baseline,ct)) is { } mapping
                ? await db.AcceleratorAssessmentTemplates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==mapping.AssessmentTemplateId && x.IsActive && !x.IsDeleted,ct) : null;
        var questions=await phase2.QuestionsAsync(context.TaxonomyId,requirements,ct);
        var templateTaxonomy=template?.TaxonomyId ?? (template?.RoleId.HasValue==true && template.RoleId==scope.RoleId ? context.TaxonomyId : (Guid?)null);
        var assessmentConfigured=template is not null && templateTaxonomy==context.TaxonomyId && (!template.RoleId.HasValue || template.RoleId==scope.RoleId);
        var attempts=await db.AcceleratorAssessmentAttempts.AsNoTracking().Where(x=>x.UserId==actor && !x.IsDeleted &&
            (x.JourneyContextId==context.Id || (context.CareerGoalId.HasValue && x.JourneyContextId==null && x.GoalId==context.CareerGoalId)))
            .Select(x=>new {x.Id,x.Status,x.Revision,x.StartedAtUtc,x.ExpiresAtUtc,x.CreatedAtUtc,
                QuestionCount=x.Answers.Count(a=>!a.IsDeleted),AnsweredCount=x.Answers.Count(a=>!a.IsDeleted && a.SubmittedAnswerJson!="{}")}).ToArrayAsync(ct);
        var activeAssessment=attempts.Where(x=>AcceleratorAssessmentRecoveryPolicy.CanResume(x.Status,x.ExpiresAtUtc,now)).OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).FirstOrDefault();
        var latestAssessment=activeAssessment ?? attempts.OrderByDescending(x=>x.CreatedAtUtc).ThenByDescending(x=>x.Id).FirstOrDefault();
        var assessmentRecovery=new JourneyAssessmentRecovery(latestAssessment?.Id,latestAssessment?.Status,latestAssessment?.Revision,latestAssessment?.StartedAtUtc,latestAssessment?.ExpiresAtUtc,
            latestAssessment?.QuestionCount ?? 0,latestAssessment?.AnsweredCount ?? 0,activeAssessment is not null,assessmentConfigured && questions.Any(x=>x.IsActive));
        var roadmap=scope.Roadmap ?? await db.AcceleratorRoadmaps.AsNoTracking().Where(x=>x.UserId==actor && x.JourneyContextId==context.Id && !x.IsDeleted && x.Status==AcceleratorRoadmapStatus.Completed).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct);
        var activities=roadmap is null ? [] : await phase2.ActivitiesAsync(actor,roadmap.Id,ct);
        var lessons=enrollment?.LearningPath.Modules.Where(x=>!x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published)
            .SelectMany(x=>x.Lessons).Where(x=>!x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published).ToArray() ?? [];
        var completeLessonIds=enrollment?.LessonProgress.Where(x=>x.Status==AcceleratorLessonProgressStatus.Completed).Select(x=>x.LessonId).ToHashSet() ?? [];
        var practice=await (from s in db.AcceleratorPracticeSessions.AsNoTracking()
            join a in db.AcceleratorPracticeAttempts on s.Id equals a.SessionId
            join e in db.AcceleratorPracticeEvaluations on a.Id equals e.AttemptId
            where s.UserId==actor && s.JourneyContextId==context.Id && a.UserId==actor && !s.IsDeleted && !a.IsDeleted && !e.IsDeleted && e.MaximumScore>0
            select 100m*e.Score/e.MaximumScore).ToArrayAsync(ct);
        var practiceCount=await db.AcceleratorPracticeSessions.CountAsync(x=>x.UserId==actor && x.JourneyContextId==context.Id && !x.IsDeleted,ct);
        var projects=await db.AcceleratorCandidateProjects.AsNoTracking().Where(x=>x.UserId==actor && x.JourneyContextId==context.Id && !x.IsDeleted).ToArrayAsync(ct);
        var interviews=await db.AcceleratorInterviewSessions.AsNoTracking().Where(x=>x.UserId==actor && x.JourneyContextId==context.Id && !x.IsDeleted).ToArrayAsync(ct);
        var activeProject=projects.Where(x=>x.Status!=AcceleratorCandidateProjectStatus.Completed && x.Status!=AcceleratorCandidateProjectStatus.Abandoned).OrderByDescending(x=>x.StartedAtUtc).ThenBy(x=>x.Id).FirstOrDefault();
        var activeInterview=interviews.Where(x=>x.Status==AcceleratorInterviewSessionStatus.InProgress).OrderByDescending(x=>x.StartedAtUtc).ThenBy(x=>x.Id).FirstOrDefault();
        var interviewQuestions=activeInterview is null ? 0 : await db.AcceleratorSessionQuestions.CountAsync(x=>x.SessionId==activeInterview.Id && !x.IsDeleted,ct);
        var interviewEvaluated=activeInterview is null ? 0 : await (from answer in db.AcceleratorInterviewAnswers
            join evaluation in db.AcceleratorInterviewEvaluations on answer.Id equals evaluation.AnswerId
            where answer.SessionId==activeInterview.Id && !answer.IsDeleted && !evaluation.IsDeleted
            select answer.SessionQuestionId).Distinct().CountAsync(ct);
        var projectVersions=await new AcceleratorPhase4Store(db).ActiveVersionsAsync(scope.RoleId,100,ct,requirements);
        var interviewVersions=await new AcceleratorPhase5Store(db).PublishedVersionsAsync(scope.RoleId,100,ct,requirements);
        var eligibleProjects=projectVersions.Count(v=>AcceleratorJourneyWorkService.Compatible(scope,v.TargetRoleId,v.Competencies.Select(x=>x.CompetencyId)) &&
            v.Competencies.Where(x=>x.IsPrerequisite).All(x=>scope.Evidence.Any(e=>e.CompetencyId==x.CompetencyId && (int)e.Proficiency>=x.RequiredProficiency)));
        var eligibleInterviews=interviewVersions.Count(v=>AcceleratorJourneyWorkService.Compatible(scope,v.Definition.TargetRoleId,v.Competencies.Select(x=>x.CompetencyId)));
        var skill=scope.Requirements.Count==0 ? 0 : 100m*scope.Requirements.Count(r=>scope.Evidence.Any(e=>e.CompetencyId==r.CompetencyId && (int)e.Proficiency>=r.TargetProficiency && e.Confidence>=.6m))/scope.Requirements.Count;
        var completedProjects=projects.Where(x=>x.Status==AcceleratorCandidateProjectStatus.Completed && x.FinalScore.HasValue).ToArray();
        var completedInterviews=interviews.Where(x=>x.Status==AcceleratorInterviewSessionStatus.Completed && x.OverallScore.HasValue).ToArray();
        var readiness=CareerReadinessPolicy.Calculate(skill,practice.Length==0 ? 0 : practice.Average(),completedProjects.Length==0 ? 0 : completedProjects.Average(x=>x.FinalScore!.Value),completedInterviews.Length==0 ? 0 : completedInterviews.Average(x=>x.OverallScore!.Value));
        var marketRoleId=scope.RoleId.HasValue ? await db.MarketRoles.Where(x=>x.AcceleratorRoleId==scope.RoleId && !x.IsDeleted && x.Status==MarketConceptStatus.Active).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct) : null;
        int? jobs=marketRoleId.HasValue ? await db.MarketJobRoles.Where(x=>x.MarketRoleId==marketRoleId && !x.IsDeleted && !x.Job.IsDeleted && x.Job.Status==JobStatus.Published && x.Job.PublishedAtUtc!=null && x.Job.PublishedAtUtc<=now && (x.Job.ExpiresAtUtc==null || x.Job.ExpiresAtUtc>now)).Select(x=>x.JobId).Distinct().CountAsync(ct) : null;
        // Applications/saved jobs are explicitly candidate-wide; no copied Accelerator application records.
        var applications=await db.JobApplications.CountAsync(x=>x.UserId==actor && !x.IsDeleted,ct);
        var saved=await db.SavedJobs.CountAsync(x=>x.UserId==actor && !x.IsDeleted,ct);
        var resume=await db.Users.Where(x=>x.Id==actor && !x.IsDeleted).Select(x=>x.ResumeStorageKey!=null && x.ResumeStorageKey!="").SingleAsync(ct);
        int? remaining=null;
        // An unselected project/interview has no authoritative remaining duration, even if learning alone is complete.
        if (!(eligibleProjects>0 && completedProjects.Length==0) && !(eligibleInterviews>0 && completedInterviews.Length==0) &&
            !projects.Any(x=>x.Status!=AcceleratorCandidateProjectStatus.Completed && x.Status!=AcceleratorCandidateProjectStatus.Abandoned) && !interviews.Any(x=>x.Status==AcceleratorInterviewSessionStatus.InProgress))
        {
            if (lessons.Length>0 && lessons.All(x=>x.EstimatedMinutes>0)) remaining=lessons.Where(x=>!completeLessonIds.Contains(x.Id)).Sum(x=>x.EstimatedMinutes);
            else if (activities.Count>0 && activities.All(x=>x.EstimatedMinutes>0)) remaining=activities.Where(x=>x.Status!=AcceleratorNodeStatus.Completed).Sum(x=>x.EstimatedMinutes);
        }
        return new(enrollment?.LearningPath.Title ?? goal?.Role.Name ?? "Journey",enrollment?.LearningPathId,enrollment?.Id,assessmentConfigured,
            questions.Any(x=>x.IsActive),activeAssessment?.Id,
            attempts.Count(x=>x.Status==AcceleratorAssessmentStatus.Completed),lessons.Count(x=>completeLessonIds.Contains(x.Id)),lessons.Length,activities.Count(x=>x.Status==AcceleratorNodeStatus.Completed),activities.Count,
            practiceCount,practice.Length,projects.Length,completedProjects.Length,activeProject?.Id,eligibleProjects,
            interviews.Length,completedInterviews.Length,activeInterview?.Id,completedInterviews.OrderByDescending(x=>x.CompletedAtUtc).Select(x=>(Guid?)x.Id).FirstOrDefault(),eligibleInterviews,
            readiness,remaining,jobs,applications,saved,resume,questions.Any(x=>x.IsActive && x.Type is AcceleratorQuestionType.MultipleChoice or AcceleratorQuestionType.MultipleSelect or AcceleratorQuestionType.TrueFalse),
            roadmap?.Id,roadmap?.Status,latestAssessment?.Status,assessmentRecovery,activeProject?.ProgressPercent,interviewQuestions,interviewEvaluated);
    }
}
