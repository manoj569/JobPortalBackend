using FluentValidation;

namespace JobPortal.Application.Features.EmployerPipeline;

public sealed class EmployerPipelineQueryValidator
    : AbstractValidator<EmployerPipelineQuery>
{
    public EmployerPipelineQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Stage)
            .IsInEnum()
            .When(x => x.Stage.HasValue);

        RuleFor(x => x.ScreeningStatus)
            .IsInEnum()
            .When(x => x.ScreeningStatus.HasValue);
    }
}

public sealed class UpdateEmployerPipelineStageRequestValidator
    : AbstractValidator<UpdateEmployerPipelineStageRequest>
{
    public UpdateEmployerPipelineStageRequestValidator()
    {
        RuleFor(x => x.Stage)
            .IsInEnum();
    }
}

public sealed class StartEmployerScreeningRequestValidator
    : AbstractValidator<StartEmployerScreeningRequest>
{
    public StartEmployerScreeningRequestValidator()
    {
        RuleFor(x => x.Notes)
            .MaximumLength(2000);
    }
}

public sealed class UpdateEmployerScreeningRequestValidator
    : AbstractValidator<UpdateEmployerScreeningRequest>
{
    public UpdateEmployerScreeningRequestValidator()
    {
        RuleFor(x => x.Score)
            .InclusiveBetween(0m, 100m)
            .When(x => x.Score.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(2000);
    }
}

public sealed class CompleteEmployerScreeningRequestValidator
    : AbstractValidator<CompleteEmployerScreeningRequest>
{
    public CompleteEmployerScreeningRequestValidator()
    {
        RuleFor(x => x.Score)
            .InclusiveBetween(0m, 100m)
            .When(x => x.Score.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(2000);
    }
}
