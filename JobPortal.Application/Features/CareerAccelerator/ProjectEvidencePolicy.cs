using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

// Project work is useful evidence, but a single evaluated assignment never establishes mastery.
public static class ProjectEvidencePolicy
{
    public static void PreserveStrongerVerifiedProficiency(IReadOnlyList<AcceleratorSkillEvidence> projectEvidence,
        IReadOnlyList<AcceleratorSkillEvidence> existing)
    {
        foreach (var item in projectEvidence)
        {
            var stronger = existing.Where(x => x.CompetencyId == item.CompetencyId && x.Proficiency > item.Proficiency &&
                    (x.Source is AcceleratorEvidenceSource.AssessmentDemonstrated or AcceleratorEvidenceSource.VirtualProject or
                        AcceleratorEvidenceSource.MockInterview) &&
                    x.Confidence >= .4m)
                .OrderByDescending(x => x.Confidence).FirstOrDefault();
            if (stronger is not null && item.Confidence >= stronger.Confidence)
                item.Confidence = Math.Max(0, stronger.Confidence - .0001m);
        }
    }

    public static IReadOnlyList<AcceleratorSkillEvidence> Create(AcceleratorCandidateProject project,
        AcceleratorProjectSubmission submission, AcceleratorProjectEvaluation evaluation,
        AcceleratorProjectRubric rubric)
    {
        var criteria = rubric.Criteria.ToDictionary(x => x.Id);
        return evaluation.Scores
            .Where(x => criteria.TryGetValue(x.CriterionId, out var criterion) && criterion.CompetencyId.HasValue)
            .GroupBy(x => criteria[x.CriterionId].CompetencyId!.Value)
            .Select(group =>
            {
                var weightedMaximum = group.Sum(x => criteria[x.CriterionId].Weight);
                var weightedScore = group.Sum(x => criteria[x.CriterionId].Weight * x.Score / criteria[x.CriterionId].MaximumScore);
                var percent = decimal.Round(100m * weightedScore / weightedMaximum, 2);
                var proficiency = percent >= 90m ? AcceleratorProficiency.Intermediate :
                    percent >= 70m ? AcceleratorProficiency.Developing : AcceleratorProficiency.Beginner;
                var confidence = percent >= 90m ? .6m : percent >= 70m ? .45m : .2m;
                return new AcceleratorSkillEvidence
                {
                    UserId = project.UserId,
                    GoalId = project.GoalId,
                    CompetencyId = group.Key,
                    Source = AcceleratorEvidenceSource.VirtualProject,
                    Proficiency = proficiency,
                    Confidence = confidence,
                    ScoringVersion = "project-rubric-v1",
                    CandidateProjectId = project.Id,
                    ProjectVersionId = project.ProjectVersionId,
                    AssignmentId = submission.AssignmentId,
                    SubmissionId = submission.Id,
                    ProjectEvaluationId = evaluation.Id,
                    Score = percent,
                    EvaluatedAtUtc = evaluation.EvaluatedAtUtc
                };
            }).ToArray();
    }
}
