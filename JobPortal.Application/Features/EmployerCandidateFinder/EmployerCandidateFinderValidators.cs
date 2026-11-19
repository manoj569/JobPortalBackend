using FluentValidation;

namespace JobPortal.Application.Features.EmployerCandidateFinder;

public sealed class EmployerCandidateFinderQueryValidator
    : AbstractValidator<EmployerCandidateFinderQuery>
{
    public EmployerCandidateFinderQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.MinimumMatchScore)
            .InclusiveBetween(0, 100);

        RuleFor(x => x.Search)
            .MaximumLength(200)
            .When(x => !string.IsNullOrWhiteSpace(x.Search));

        RuleFor(x => x.Location)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Location));

        RuleFor(x => x.Skill)
            .MaximumLength(120)
            .When(x => !string.IsNullOrWhiteSpace(x.Skill));

        RuleFor(x => x.MinimumExperienceYears)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinimumExperienceYears.HasValue);

        RuleFor(x => x.MaximumExperienceYears)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MaximumExperienceYears.HasValue);

        RuleFor(x => x)
            .Must(x =>
                !x.MinimumExperienceYears.HasValue ||
                !x.MaximumExperienceYears.HasValue ||
                x.MinimumExperienceYears <= x.MaximumExperienceYears)
            .WithMessage(
                "MinimumExperienceYears cannot exceed MaximumExperienceYears.");
    }
}
