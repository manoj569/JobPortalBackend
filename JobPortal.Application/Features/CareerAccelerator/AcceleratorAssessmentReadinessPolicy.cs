using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record AcceleratorAssessmentCoverage(int RequiredCompetencyCount,int CoveredCompetencyCount,
    decimal ObjectiveQuestionCoveragePercent,int EligibleObjectiveQuestionCount,int SelectedQuestionCount,
    bool AssessmentReady,IReadOnlyList<Guid> MissingCompetencyIds,int BaselineCoveredCompetencyCount,Guid TaxonomyId);

// Ephemeral server-side validation result. Never request-bound, returned to candidates or persisted.
public sealed record AcceleratorQuestionAvailability(Guid Id,Guid TaxonomyId,Guid CompetencyId,int Difficulty,bool Active,bool Eligible)
{
    public static AcceleratorQuestionAvailability From(AcceleratorQuestion question) => new(question.Id,question.TaxonomyId,question.CompetencyId,question.Difficulty,
        question.IsActive && !question.IsDeleted,AcceleratorAssessmentReadinessPolicy.IsEligible(question));
}

public static class AcceleratorAssessmentReadinessPolicy
{
    public const int MinimumCompetencyCoveragePercent=50;
    public const int MaximumQuestions=100;
    // Reuse the existing objective-bank validator. No separate SQL-only approximation of validity.
    public static bool IsEligible(AcceleratorQuestion question) => AcceleratorJourneyProvisioningPolicy.ValidQuestion(question);
    public static bool SufficientCoverage(int required,int covered) => required>0 && covered>0 && covered<=required &&
        covered*100m>=required*MinimumCompetencyCoveragePercent;
    public static AcceleratorQuestion[] SelectQuestions(IEnumerable<AcceleratorQuestion> questions,string? strategy,int maximum) =>
        SelectCore(questions.Where(x=>strategy!=AcceleratorJourneyProvisioningPolicy.ObjectiveSelection || IsEligible(x)),x=>x.CompetencyId,x=>x.Difficulty,x=>x.Id,maximum);
    public static AcceleratorAssessmentCoverage Evaluate(Guid taxonomyId,IEnumerable<Guid> requiredCompetencyIds,
        IEnumerable<AcceleratorQuestion> questions,int maximum=MaximumQuestions,string strategy=AcceleratorJourneyProvisioningPolicy.ObjectiveSelection) =>
        EvaluateValidated(taxonomyId,requiredCompetencyIds,questions.Select(AcceleratorQuestionAvailability.From),maximum,strategy);
    public static AcceleratorAssessmentCoverage EvaluateValidated(Guid taxonomyId,IEnumerable<Guid> requiredCompetencyIds,
        IEnumerable<AcceleratorQuestionAvailability> questions,int maximum=MaximumQuestions,string strategy=AcceleratorJourneyProvisioningPolicy.ObjectiveSelection)
    {
        var required=requiredCompetencyIds.Distinct().Order().ToArray(); var ids=required.ToHashSet();
        var bank=questions.Where(x=>x.TaxonomyId==taxonomyId && ids.Contains(x.CompetencyId) && x.Active).ToArray();
        var valid=bank.Where(x=>x.Eligible).ToArray();
        var selected=SelectCore(bank.Where(x=>strategy!=AcceleratorJourneyProvisioningPolicy.ObjectiveSelection || x.Eligible),x=>x.CompetencyId,x=>x.Difficulty,x=>x.Id,maximum);
        var bankCovered=valid.Select(x=>x.CompetencyId).Distinct().ToArray();
        var covered=selected.Where(x=>x.Eligible).Select(x=>x.CompetencyId).Distinct().ToArray();
        return new(required.Length,bankCovered.Length,required.Length==0 ? 0 : decimal.Round(bankCovered.Length*100m/required.Length,2),
            valid.Select(x=>x.Id).Distinct().Count(),selected.Length,SufficientCoverage(required.Length,covered.Length) && selected.All(x=>x.Eligible),required.Except(bankCovered).ToArray(),covered.Length,taxonomyId);
    }
    private static T[] SelectCore<T>(IEnumerable<T> questions,Func<T,Guid> competency,Func<T,int> difficulty,Func<T,Guid> id,int maximum) =>
        questions.GroupBy(competency).Select(x=>x.OrderBy(difficulty).ThenBy(id).First()).OrderBy(competency).Take(Math.Clamp(maximum,0,MaximumQuestions)).ToArray();
}
