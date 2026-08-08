using FluentValidation;

namespace JobPortal.Application.Features.PublicJobs;

public sealed class PublicJobQueryValidator : AbstractValidator<PublicJobQuery>
{
    private static readonly int[] SupportedFreshnessDays = [1, 3, 7, 15, 30];
    private static readonly int[] SupportedDurations = [1, 2, 3, 6];

    public PublicJobQueryValidator()
    {
        RuleFor(x => x.EffectivePageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
        RuleFor(x => x.Keyword).MaximumLength(250);
        RuleFor(x => x.Location).MaximumLength(250);
        RuleFor(x => x.CompanyName).MaximumLength(250);
        RuleFor(x => x.CategoryName).MaximumLength(250);
        RuleFor(x => x.MinimumSalary).InclusiveBetween(0, 1_000_000_000);
        RuleFor(x => x.MaximumSalary).InclusiveBetween(0, 1_000_000_000);
        RuleFor(x => x.MaximumSalary).GreaterThanOrEqualTo(x => x.MinimumSalary)
            .When(x => x.MinimumSalary.HasValue && x.MaximumSalary.HasValue);
        RuleFor(x => x.MinAmount).InclusiveBetween(0, 1_000_000_000);
        RuleFor(x => x.MaxAmount).InclusiveBetween(0, 1_000_000_000);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(x => x.MinAmount)
            .When(x => x.MinAmount.HasValue && x.MaxAmount.HasValue);
        RuleFor(x => x.MinExperienceYears).InclusiveBetween(0, 60);
        RuleFor(x => x.MaxExperienceYears).InclusiveBetween(0, 60);
        RuleFor(x => x.MaxExperienceYears).GreaterThanOrEqualTo(x => x.MinExperienceYears)
            .When(x => x.MinExperienceYears.HasValue && x.MaxExperienceYears.HasValue);
        RuleFor(x => x.FreshnessDays)
            .Must(value => !value.HasValue || SupportedFreshnessDays.Contains(value.Value))
            .WithMessage("FreshnessDays must be one of: 1, 3, 7, 15, 30.");
        RuleFor(x => x.SortBy).IsInEnum();
        RuleFor(x => x.SortDirection)
            .Must(value => value.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");

        ValidateStrings(x => x.Locations, 25, 250, "Locations");
        ValidateStrings(x => x.Departments, 25, 150, "Departments");
        ValidateStrings(x => x.RoleCategories, 25, 150, "RoleCategories");
        ValidateStrings(x => x.EducationRequirements, 25, 200, "EducationRequirements");
        ValidateStrings(x => x.Industries, 25, 150, "Industries");

        RuleFor(x => x.CompanyIds).Must(values => values is null || values.Length <= 50)
            .WithMessage("CompanyIds cannot contain more than 50 values.");
        RuleFor(x => x.CompanyIds)
            .Must(values => values is null || values.All(value => value != Guid.Empty))
            .WithMessage("CompanyIds cannot contain an empty identifier.");
        ValidateEnums(x => x.WorkModes, "WorkModes");
        ValidateEnums(x => x.EmploymentTypes, "EmploymentTypes");
        ValidateEnums(x => x.CompanyTypes, "CompanyTypes");
        ValidateEnums(x => x.PostedByTypes, "PostedByTypes");
        RuleFor(x => x.InternshipDurationMonths)
            .Must(values => values is null || values.Length <= SupportedDurations.Length)
            .WithMessage("InternshipDurationMonths contains too many values.");
        RuleFor(x => x.InternshipDurationMonths)
            .Must(values => values is null || values.All(SupportedDurations.Contains))
            .WithMessage("Internship duration must be 1, 2, 3, or 6 months.");
    }

    private void ValidateStrings(
        System.Linq.Expressions.Expression<Func<PublicJobQuery, string[]?>> expression,
        int maximumCount,
        int maximumLength,
        string name)
    {
        RuleFor(expression).Must(values => values is null || values.Length <= maximumCount)
            .WithMessage($"{name} cannot contain more than {maximumCount} values.");
        RuleFor(expression)
            .Must(values => values is null || values.All(value =>
                !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength))
            .WithMessage($"Each {name} value is required and cannot exceed {maximumLength} characters.");
    }

    private void ValidateEnums<TEnum>(
        System.Linq.Expressions.Expression<Func<PublicJobQuery, TEnum[]?>> expression,
        string name)
        where TEnum : struct, Enum
    {
        RuleFor(expression).Must(values => values is null || values.Length <= 20)
            .WithMessage($"{name} cannot contain more than 20 values.");
        RuleFor(expression)
            .Must(values => values is null || values.All(Enum.IsDefined))
            .WithMessage($"{name} contains an invalid value.");
    }
}
