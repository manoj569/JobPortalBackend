using FluentValidation;

namespace JobPortal.Application.Features.InterviewInsights;

internal static class InsightContentRules
{
    private static readonly string[] Prohibited =
        ["password", "credential", "salary slip", "internal url", "answer key", "leaked assessment", "nda answer"];
    public static bool IsSafe(string? value) => string.IsNullOrWhiteSpace(value) ||
        !Prohibited.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed class InterviewRoundRequestValidator : AbstractValidator<InterviewRoundRequest>
{
    public InterviewRoundRequestValidator()
    {
        RuleFor(x => x.RoundType).IsInEnum();
        RuleFor(x => x.RoundTitle).MaximumLength(160);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(1, 1440).When(x => x.DurationMinutes.HasValue);
        RuleFor(x => x.QuestionsOrTopics).NotEmpty().MaximumLength(3000).Must(InsightContentRules.IsSafe)
            .WithMessage("Share paraphrased topics only; confidential or leaked assessment material is not allowed.");
        RuleFor(x => x.CandidateAdvice).MaximumLength(2000).Must(InsightContentRules.IsSafe);
    }
}

public sealed class CreateInterviewInsightRequestValidator : AbstractValidator<CreateInterviewInsightRequest>
{
    public CreateInterviewInsightRequestValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.RoleTitle).NotEmpty().MaximumLength(160);
        RuleFor(x => x.ExperienceLevel).MaximumLength(80);
        RuleFor(x => x.InterviewDateMonth).Must(x => x.Day == 1).WithMessage("InterviewDateMonth must be the first day of its month.");
        RuleFor(x => x.OverallDifficulty).IsInEnum();
        RuleFor(x => x.ProcessSummary).NotEmpty().MaximumLength(3000).Must(InsightContentRules.IsSafe);
        RuleFor(x => x.PreparationTips).NotEmpty().MaximumLength(3000).Must(InsightContentRules.IsSafe);
        RuleFor(x => x.Outcome).IsInEnum().When(x => x.Outcome.HasValue);
        RuleFor(x => x.ContentGuidelinesAccepted).Equal(true)
            .WithMessage("You must confirm that no confidential material, personal data, assessment leaks, or NDA-protected content is included.");
        RuleFor(x => x.Rounds).NotEmpty().Must(x => x.Count <= 12);
        RuleForEach(x => x.Rounds).SetValidator(new InterviewRoundRequestValidator());
    }
}

public sealed class UpdateInterviewInsightRequestValidator : AbstractValidator<UpdateInterviewInsightRequest>
{
    public UpdateInterviewInsightRequestValidator()
    {
        Include(new UpdateRules());
    }
    private sealed class UpdateRules : AbstractValidator<UpdateInterviewInsightRequest>
    {
        public UpdateRules()
        {
            RuleFor(x => x.RoleTitle).NotEmpty().MaximumLength(160);
            RuleFor(x => x.ExperienceLevel).MaximumLength(80);
            RuleFor(x => x.InterviewDateMonth).Must(x => x.Day == 1);
            RuleFor(x => x.OverallDifficulty).IsInEnum();
            RuleFor(x => x.ProcessSummary).NotEmpty().MaximumLength(3000).Must(InsightContentRules.IsSafe);
            RuleFor(x => x.PreparationTips).NotEmpty().MaximumLength(3000).Must(InsightContentRules.IsSafe);
            RuleFor(x => x.ContentGuidelinesAccepted).Equal(true);
            RuleFor(x => x.Rounds).NotEmpty().Must(x => x.Count <= 12);
            RuleForEach(x => x.Rounds).SetValidator(new InterviewRoundRequestValidator());
        }
    }
}

public sealed class CreateInterviewScheduleRequestValidator : AbstractValidator<CreateInterviewScheduleRequest>
{
    public CreateInterviewScheduleRequestValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.RoleTitle).MaximumLength(160);
        RuleFor(x => x.InterviewAtUtc).NotEmpty();
    }
}
public sealed class UpdateInterviewScheduleRequestValidator : AbstractValidator<UpdateInterviewScheduleRequest>
{
    public UpdateInterviewScheduleRequestValidator()
    {
        RuleFor(x => x.RoleTitle).MaximumLength(160);
        RuleFor(x => x.InterviewAtUtc).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}
public sealed class CreateInsightFeedbackRequestValidator : AbstractValidator<CreateInsightFeedbackRequest>
{
    public CreateInsightFeedbackRequestValidator()
    {
        RuleFor(x => x.CandidateInterviewScheduleId).NotEmpty();
        RuleFor(x => x.Helpfulness).IsInEnum();
        RuleFor(x => x.InterviewMatch).IsInEnum();
        RuleFor(x => x.Feedback).MaximumLength(500).Must(InsightContentRules.IsSafe);
    }
}
public sealed class CreateInsightReportRequestValidator : AbstractValidator<CreateInsightReportRequest>
{
    public CreateInsightReportRequestValidator()
    {
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Details).MaximumLength(500);
    }
}
