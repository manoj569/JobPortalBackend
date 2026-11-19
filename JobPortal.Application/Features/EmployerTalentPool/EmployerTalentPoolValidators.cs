using FluentValidation;

namespace JobPortal.Application.Features.EmployerTalentPool;

public sealed class EmployerTalentPoolQueryValidator
    : AbstractValidator<EmployerTalentPoolQuery>
{
    public EmployerTalentPoolQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Search)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Search));

        RuleFor(x => x.Skill)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Skill));

        RuleFor(x => x.Location)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Location));
    }
}

public sealed class AddEmployerTalentPoolCandidateRequestValidator
    : AbstractValidator<AddEmployerTalentPoolCandidateRequest>
{
    public AddEmployerTalentPoolCandidateRequestValidator()
    {
        RuleFor(x => x.CandidateUserId)
            .NotEmpty();

        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}

public sealed class UpdateEmployerTalentPoolNotesRequestValidator
    : AbstractValidator<UpdateEmployerTalentPoolNotesRequest>
{
    public UpdateEmployerTalentPoolNotesRequestValidator()
    {
        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}
