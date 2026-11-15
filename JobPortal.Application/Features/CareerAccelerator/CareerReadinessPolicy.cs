using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.CareerAccelerator;

public static class CareerReadinessPolicy
{
    public const string Version = "readiness-v1";
    public const decimal SkillWeight = .4m;
    public const decimal PracticeWeight = .1m;
    public const decimal ProjectWeight = .2m;
    public const decimal InterviewWeight = .3m;

    public static decimal Calculate(decimal skill, decimal practice, decimal project, decimal interview)
    {
        if (new[] { skill, practice, project, interview }.Any(x => x is < 0 or > 100))
            throw new BadRequestException("Readiness components must be percentages.", "accelerator_readiness_invalid");
        return decimal.Round(skill * SkillWeight + practice * PracticeWeight +
            project * ProjectWeight + interview * InterviewWeight, 2);
    }
}
