using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

public enum AcceleratorNextAction { Unavailable, EnrollLearningPath, StartAssessment, ResumeAssessment, GenerateRoadmap, ContinueLearning, StartPractice, StartProject, ContinueProject, StartInterview, ContinueInterview, ViewInterviewReport, ViewJobs, ViewApplications }
public sealed class AcceleratorJourneySummaryOptions
{
    // Explicit aggregate policy; readiness component weights remain CareerReadinessPolicy's existing 40/10/20/30.
    public decimal MinimumJobReadyScore { get; set; } = 70;
}
public sealed record JourneyMarketResolution(string State, string? Title, Guid? RoleId, Guid? LearningPathId, Guid? EnrollmentId, Guid? GoalId);
public sealed record JourneyChecklistItem(string Code, bool Complete);
public sealed record JourneyPhaseStatus(string Code, bool Unlocked, bool Complete, int? ProgressPercent = null, string ProgressBasis = "DiscreteCompletion");
public sealed record JourneyAssessmentRecovery(Guid? AttemptId, AcceleratorAssessmentStatus? Status, Guid? Revision, DateTime? StartedAtUtc,
    DateTime? ExpiresAtUtc, int QuestionCount, int AnsweredQuestionCount, bool CanResume, bool CanStart);
public sealed record JourneySummaryData(string Title, Guid? LearningPathId, Guid? EnrollmentId, bool AssessmentConfigured, bool QuestionsAvailable,
    Guid? ActiveAssessmentId, int CompletedAssessments, int CompletedLessons, int TotalLessons, int CompletedActivities, int TotalActivities,
    int PracticeSessions, int PracticeEvaluations, int Projects, int CompletedProjects, Guid? ActiveProjectId, int EligibleProjects,
    int Interviews, int CompletedInterviews, Guid? ActiveInterviewId, Guid? LatestReportId, int EligibleInterviews,
    decimal Readiness, int? EstimatedMinutesRemaining, int? RelevantJobCount, int ApplicationCount, int SavedJobCount, bool HasResume, bool PracticeAvailable = false,
    Guid? RoadmapId = null, AcceleratorRoadmapStatus? RoadmapStatus = null, AcceleratorAssessmentStatus? AssessmentStatus = null,
    JourneyAssessmentRecovery? Assessment = null, int? ActiveProjectProgressPercent = null, int InterviewQuestionCount = 0, int InterviewEvaluatedQuestionCount = 0);
public sealed record AcceleratorJourneySummaryResponse(string State, string PolicyVersion, Guid? JourneyContextId, Guid? GoalId, Guid? LearningPathId, Guid? EnrollmentId,
    string? Title, AcceleratorEntitlementResponse Entitlement, JourneySummaryData? Summary, Guid? RoadmapId, int? OverallProgressPercent,
    int? EstimatedMinutesRemaining, string? CurrentPhase, AcceleratorNextAction NextAction, Guid? NextResourceId,
    IReadOnlyList<JourneyChecklistItem> Checklist, IReadOnlyList<JourneyPhaseStatus> Phases, AcceleratorRoadmapStatus? RoadmapStatus = null, AcceleratorAssessmentStatus? AssessmentStatus = null,
    JourneyAssessmentRecovery? Assessment = null, string PhaseProgressPolicyVersion = AcceleratorPhaseProgressPolicy.Version);
public interface IAcceleratorJourneySummaryStore
{
    Task<JourneyMarketResolution> ResolveMarketAsync(Guid actor, Guid marketPathId, CancellationToken ct);
    Task<JourneyMarketResolution> ResolveMarketSkillAsync(Guid actor, Guid marketSkillPathId, CancellationToken ct);
    Task<JourneySummaryData> ReadAsync(Guid actor, AcceleratorJourneyWorkScope scope, DateTime now, CancellationToken ct);
}
public static class AcceleratorJourneyPhasePolicy
{
    public const string Version = "journey-checklist-v1";
    // Progress is the equally weighted completion ratio of these disclosed applicable checkpoints, not a second readiness score.
    public static AcceleratorJourneySummaryResponse Calculate(AcceleratorJourneyWorkScope scope, JourneySummaryData data,
        AcceleratorEntitlementResponse entitlement, decimal minimumJobReadyScore)
    {
        if (minimumJobReadyScore is < 0 or > 100) throw new InvalidOperationException("Journey readiness threshold must be a percentage.");
        var foundation = data.CompletedAssessments > 0 && scope.Requirements.Count > 0;
        var roadmapId = scope.Roadmap?.Id ?? data.RoadmapId;
        var learned = data.CompletedLessons + data.CompletedActivities + data.PracticeEvaluations > 0;
        var checklist = new List<JourneyChecklistItem> { new("AssessmentCompleted", foundation), new("RoadmapCreated", roadmapId.HasValue), new("LearningEvidence", learned) };
        if (data.PracticeAvailable) checklist.Add(new("PracticeEvaluated", data.PracticeEvaluations > 0));
        if (data.EligibleProjects > 0 || data.Projects > 0) checklist.Add(new("ProjectCompleted", data.CompletedProjects > 0));
        if (data.EligibleInterviews > 0 || data.Interviews > 0) checklist.Add(new("InterviewCompleted", data.CompletedInterviews > 0));
        checklist.Add(new("ApplicationSubmitted", data.ApplicationCount > 0));
        var phases = new JourneyPhaseStatus[] {
            new("Foundation",true,foundation), new("SkillBuild",foundation,roadmapId.HasValue && learned),
            new("BuildAndShowcase",foundation && roadmapId.HasValue,data.CompletedProjects > 0),
            new("InterviewReadiness",foundation && learned,data.CompletedInterviews > 0),
            new("JobApplicationAndTracking",foundation && data.Readiness >= minimumJobReadyScore,data.ApplicationCount > 0) };
        phases=phases.Select(phase=>AcceleratorPhaseProgressPolicy.Apply(phase,data)).ToArray();
        var action = AcceleratorNextAction.ContinueLearning; Guid? resource = roadmapId;
        var state = "Ready";
        if (data.ActiveAssessmentId.HasValue) { action=AcceleratorNextAction.ResumeAssessment; resource=data.ActiveAssessmentId; }
        else if (!foundation) { action=data.AssessmentConfigured && data.QuestionsAvailable ? AcceleratorNextAction.StartAssessment : AcceleratorNextAction.Unavailable; state=!data.AssessmentConfigured ? "AssessmentNotConfigured" : !data.QuestionsAvailable ? "QuestionsUnavailable" : "Ready"; resource=null; }
        else if (!roadmapId.HasValue) { action=AcceleratorNextAction.GenerateRoadmap; resource=null; }
        else if (data.ActiveProjectId.HasValue) { action=AcceleratorNextAction.ContinueProject; resource=data.ActiveProjectId; }
        else if (data.ActiveInterviewId.HasValue) { action=AcceleratorNextAction.ContinueInterview; resource=data.ActiveInterviewId; }
        else if (!learned) action=AcceleratorNextAction.ContinueLearning;
        else if (data.PracticeEvaluations==0 && data.PracticeAvailable) { action=AcceleratorNextAction.StartPractice; resource=null; }
        else if (foundation && data.Readiness>=minimumJobReadyScore) { action=data.ApplicationCount>0 ? AcceleratorNextAction.ViewApplications : AcceleratorNextAction.ViewJobs; resource=null; }
        else if (data.CompletedProjects==0 && data.EligibleProjects>0) { action=AcceleratorNextAction.StartProject; resource=null; }
        else if (data.CompletedInterviews==0 && data.EligibleInterviews>0) { action=AcceleratorNextAction.StartInterview; resource=null; }
        else if (data.LatestReportId.HasValue) { action=AcceleratorNextAction.ViewInterviewReport; resource=data.LatestReportId; }
        return new(state,Version,scope.Context.Id,scope.Context.CareerGoalId,data.LearningPathId,data.EnrollmentId,data.Title,entitlement,data,roadmapId,
            (int)decimal.Round(checklist.Count(x=>x.Complete)*100m/checklist.Count),data.EstimatedMinutesRemaining,
            phases.FirstOrDefault(x=>x.Unlocked && !x.Complete)?.Code ?? phases[^1].Code,action,resource,checklist,phases,scope.Roadmap?.Status ?? data.RoadmapStatus,data.AssessmentStatus,data.Assessment);
    }
}
public sealed class AcceleratorJourneySummaryService(IAcceleratorJourneySummaryStore store, AcceleratorJourneyWorkService work,
    AcceleratorService access, TimeProvider clock, IOptions<AcceleratorJourneySummaryOptions> options)
{
    public async Task<AcceleratorJourneySummaryResponse> GetAsync(Guid actor, Guid? marketPathId, Guid? contextId, CancellationToken ct, Guid? marketSkillPathId = null)
    {
        await access.Candidate(actor,ct);
        if ((marketPathId.HasValue ? 1 : 0)+(contextId.HasValue ? 1 : 0)+(marketSkillPathId.HasValue ? 1 : 0)!=1)
            throw new BadRequestException("Specify exactly one marketPathId, marketSkillPathId or journeyContextId.");
        var entitlement=await access.EntitlementAsync(actor,ct);
        AcceleratorJourneyWorkScope scope;
        if (contextId.HasValue) scope=await work.OwnedAsync(actor,contextId.Value,ct);
        else
        {
            var mapping=marketSkillPathId.HasValue ? await store.ResolveMarketSkillAsync(actor,marketSkillPathId.Value,ct)
                : await store.ResolveMarketAsync(actor,marketPathId!.Value,ct);
            if (mapping.State!="Ready") return new(mapping.State,AcceleratorJourneyPhasePolicy.Version,null,mapping.GoalId,mapping.LearningPathId,mapping.EnrollmentId,mapping.Title,entitlement,null,null,null,null,null,
                mapping.State=="EnrollmentRequired" ? AcceleratorNextAction.EnrollLearningPath : AcceleratorNextAction.Unavailable,mapping.LearningPathId,[],[]);
            scope=await work.ResolveAsync(actor,mapping.GoalId ?? Guid.Empty,mapping.EnrollmentId,ct);
        }
        return AcceleratorJourneyPhasePolicy.Calculate(scope,await store.ReadAsync(actor,scope,clock.GetUtcNow().UtcDateTime,ct),entitlement,options.Value.MinimumJobReadyScore);
    }
}
