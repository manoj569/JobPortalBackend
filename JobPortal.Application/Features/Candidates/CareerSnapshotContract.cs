using System.Text.Json.Serialization;
using FluentValidation;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Candidates;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCareerSnapshotRequest(
    CareerStage CareerStage,
    string PrimaryRole,
    IReadOnlyCollection<string> Skills,
    IReadOnlyCollection<WorkPreference> WorkPreferences,
    decimal? YearsOfExperience = null,
    string? City = null);

public sealed class UpdateCareerSnapshotRequestValidator : AbstractValidator<UpdateCareerSnapshotRequest>
{
    public UpdateCareerSnapshotRequestValidator()
    {
        RuleFor(x => x.CareerStage).IsInEnum();
        RuleFor(x => x.PrimaryRole).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(100)
            .Must(SafeText).Must(x => !x.Contains(',', StringComparison.Ordinal)).Must(x => !string.Equals(x.Trim(), "Other", StringComparison.OrdinalIgnoreCase))
            .WithMessage("PrimaryRole must be a specific career role; supply the custom role instead of Other.");
        RuleFor(x => x.Skills).Cascade(CascadeMode.Stop).NotNull()
            .Must(x => x.Count is >= 1 and <= 20);
        RuleForEach(x => x.Skills).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(100).Must(SafeText);
        RuleFor(x => x.WorkPreferences).Cascade(CascadeMode.Stop).NotNull()
            .Must(x => x.Count <= 3 && x.Distinct().Count() == x.Count);
        RuleForEach(x => x.WorkPreferences).IsInEnum();
        RuleFor(x => x.City).NotEmpty().MaximumLength(150).Must(x => x is null || SafeText(x))
            .When(x => x.City is not null);
        RuleFor(x => x.YearsOfExperience).NotNull()
            .When(x => x.CareerStage == CareerStage.Experienced);
        // Match the existing numeric(4,1) storage without silently rounding input.
        RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 50)
            .Must(x => !x.HasValue || decimal.Round(x.Value, 1) == x.Value)
            .When(x => x.YearsOfExperience.HasValue);
    }

    private static bool SafeText(string value) => !value.Any(char.IsControl);
}
