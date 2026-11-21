namespace JobPortal.Application.Features.CareerAccelerator;

public static class AcceleratorPhaseProgressPolicy
{
    public const string Version="journey-phase-progress-v1";
    // Percentages describe submitted/completed work, never mastery. Overall weights and phase unlock rules are unchanged.
    // Existing completion/unlock flags keep their original semantics; each percentage names its precise work basis.
    // Absent continuous evidence uses explicit discrete completion (0/100).
    public static JourneyPhaseStatus Apply(JourneyPhaseStatus phase,JourneySummaryData data)
    {
        var percent=phase.Complete ? 100 : 0; var basis="DiscreteCompletion";
        switch (phase.Code)
        {
            case "Foundation" when data.Assessment is { QuestionCount: >0 } assessment:
                percent=phase.Complete ? 100 : Ratio(assessment.AnsweredQuestionCount,assessment.QuestionCount);
                basis="AssessmentAnsweredQuestions"; break;
            case "SkillBuild" when data.TotalActivities>0:
                percent=Ratio(data.CompletedActivities,data.TotalActivities); basis="CompletedLearningActivities"; break;
            case "SkillBuild" when data.TotalLessons>0:
                percent=Ratio(data.CompletedLessons,data.TotalLessons); basis="CompletedLessons"; break;
            case "BuildAndShowcase" when data.ActiveProjectProgressPercent.HasValue:
                percent=phase.Complete ? 100 : Math.Clamp(data.ActiveProjectProgressPercent.Value,0,100); basis="AuthoritativeProjectProgress"; break;
            case "InterviewReadiness" when data.InterviewQuestionCount>0:
                percent=phase.Complete ? 100 : Ratio(data.InterviewEvaluatedQuestionCount,data.InterviewQuestionCount); basis="EvaluatedInterviewQuestions"; break;
        }
        return phase with { ProgressPercent=percent,ProgressBasis=basis };
    }
    private static int Ratio(int completed,int total) => (int)decimal.Round(Math.Clamp(completed,0,total)*100m/total,0,MidpointRounding.AwayFromZero);
}
