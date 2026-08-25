using FluentValidation;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Candidates;

public sealed class UpdateCandidateProfileRequestValidator : AbstractValidator<UpdateCandidateProfileRequest>
{
    public UpdateCandidateProfileRequestValidator()
    {
        RuleFor(x => x.Headline).MaximumLength(180);
        RuleFor(x => x.Bio).MaximumLength(2000);
        RuleFor(x => x.Location).MaximumLength(250);
        RuleFor(x => x.LinkedInUrl).MaximumLength(2048).Must(OptionalUrl);
        RuleFor(x => x.PortfolioUrl).MaximumLength(2048).Must(OptionalUrl);
        RuleFor(x => x.Skills).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 50);
        RuleForEach(x => x.Skills).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Education).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 20);
        RuleForEach(x => x.Education).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Experience).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 30);
        RuleForEach(x => x.Experience).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.PreferredJobTypes).Cascade(CascadeMode.Stop).NotNull().Must(x => x.Count <= 10);
        RuleForEach(x => x.PreferredJobTypes).IsInEnum();
    }

    private static bool OptionalUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}

public sealed class UpdateCandidateOnboardingRequestValidator :
    AbstractValidator<UpdateCandidateOnboardingRequest>
{
    public UpdateCandidateOnboardingRequestValidator(TimeProvider timeProvider)
    {
        var currentYear = timeProvider.GetUtcNow().Year;

        RuleFor(x => x.CareerStage).IsInEnum();
        RuleFor(x => x.DesiredOpportunities)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(values => values.Count is >= 1 and <= 3)
            .WithMessage("DesiredOpportunities must contain between 1 and 3 values.")
            .Must(BeUnique)
            .WithMessage("DesiredOpportunities must not contain duplicates.");
        RuleForEach(x => x.DesiredOpportunities).IsInEnum();
        RuleFor(x => x.City)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(150)
            .Must(BeSafeText)
            .WithMessage("City contains invalid characters.");
        RuleFor(x => x.Skills)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(values => values.Count is >= 1 and <= 20)
            .WithMessage("Skills must contain between 1 and 20 values.")
            .Must(BeUniqueTrimmedStrings)
            .WithMessage("Skills must be unique and non-empty.");
        RuleForEach(x => x.Skills)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(100)
            .Must(BeSafeText)
            .WithMessage("Skills contain invalid characters.");
        RuleFor(x => x.WorkPreferences)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(values => values.Count <= 3)
            .WithMessage("WorkPreferences cannot contain more than 3 values.")
            .Must(BeUnique)
            .WithMessage("WorkPreferences must not contain duplicates.");
        RuleForEach(x => x.WorkPreferences).IsInEnum();
        RuleFor(x => x.College)
            .MaximumLength(200)
            .Must(BeOptionalSafeText)
            .WithMessage("College contains invalid characters.");
        RuleFor(x => x.Degree)
            .MaximumLength(200)
            .Must(BeOptionalSafeText)
            .WithMessage("Degree contains invalid characters.");
        RuleFor(x => x.GraduationYear)
            .InclusiveBetween(currentYear - 80, currentYear + 10)
            .When(x => x.GraduationYear.HasValue);
        RuleFor(x => x.YearsOfExperience)
            .InclusiveBetween(0, 50)
            .When(x => x.YearsOfExperience.HasValue);
        RuleFor(x => x.YearsOfExperience)
            .NotNull()
            .When(x => x.CareerStage == CareerStage.Experienced)
            .WithMessage("YearsOfExperience is required for Experienced candidates.");
    }

    private static bool BeSafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static bool BeOptionalSafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || !value.Any(char.IsControl);

    private static bool BeUnique<T>(IReadOnlyCollection<T> values)
        where T : struct, Enum =>
        values.Distinct().Count() == values.Count;

    private static bool BeUniqueTrimmedStrings(IReadOnlyCollection<string> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        return normalized.Length == values.Count &&
            normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;
    }
}

public sealed class CandidatePageQueryValidator : AbstractValidator<CandidatePageQuery>
{
    public CandidatePageQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class JobApplicationQueryValidator : AbstractValidator<JobApplicationQuery>
{
    public JobApplicationQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

public sealed class CreateJobApplicationRequestValidator : AbstractValidator<CreateJobApplicationRequest>
{
    public CreateJobApplicationRequestValidator()
    {
        RuleFor(x => x.CoverLetter).MaximumLength(5000);
        RuleFor(x => x.ApplicationMethod).IsInEnum();
    }
}

public sealed class UpdateCandidateAboutRequestValidator : AbstractValidator<UpdateCandidateAboutRequest>
{
    public UpdateCandidateAboutRequestValidator()
    {
        RuleFor(x => x.ResumeHeadline).MaximumLength(180).Must(BeSafeOptionalText);
        RuleFor(x => x.ProfileSummary).MaximumLength(2000).Must(BeSafeOptionalText);
    }

    private static bool BeSafeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) || !value.Any(char.IsControl);
}

public sealed class UpsertCandidateSkillRequestValidator : AbstractValidator<UpsertCandidateSkillRequest>
{
    public UpsertCandidateSkillRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(100)
            .Must(value => !value.Any(char.IsControl))
            .WithMessage("Skill name contains invalid characters.");
        RuleFor(x => x.Proficiency).IsInEnum().When(x => x.Proficiency.HasValue);
        RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 50)
            .When(x => x.YearsOfExperience.HasValue);
    }
}

public sealed class UpdateCandidateBasicDetailsRequestValidator :
    AbstractValidator<UpdateCandidateBasicDetailsRequest>
{
    public UpdateCandidateBasicDetailsRequestValidator()
    {
        RuleFor(x => x.WorkStatus).NotNull().WithMessage("WorkStatus is required.")
            .IsInEnum();
        RuleFor(x => x.IsOutsideIndia).NotNull().WithMessage("IsOutsideIndia is required.");
        RuleFor(x => x.CurrentCountry).NotEmpty().MaximumLength(100).Must(ProfileText.Safe);
        RuleFor(x => x.CurrentCity).NotEmpty().MaximumLength(150).Must(ProfileText.Safe);
        RuleFor(x => x.CurrentArea).MaximumLength(150).Must(ProfileText.OptionalSafe);
        RuleFor(x => x.AvailabilityToJoin).IsInEnum().When(x => x.AvailabilityToJoin.HasValue);
        RuleFor(x => x.NoticePeriod).IsInEnum()
            .Must(value => !value.HasValue || value is
                CandidateAvailability.ImmediateJoiner or
                CandidateAvailability.FifteenDaysOrLess or
                CandidateAvailability.OneMonth or
                CandidateAvailability.ThreeMonths or
                CandidateAvailability.Other)
            .WithMessage("NoticePeriod must be ImmediateJoiner, FifteenDaysOrLess, OneMonth, ThreeMonths, or Other.")
            .When(x => x.NoticePeriod.HasValue);
        RuleFor(x => x.CurrentAnnualSalary).GreaterThanOrEqualTo(0).When(x => x.CurrentAnnualSalary.HasValue);
        RuleFor(x => x.CurrentFixedAnnualSalary).GreaterThanOrEqualTo(0).When(x => x.CurrentFixedAnnualSalary.HasValue);
        RuleFor(x => x.CurrentVariableAnnualSalary).GreaterThanOrEqualTo(0).When(x => x.CurrentVariableAnnualSalary.HasValue);
        RuleFor(x => x.MobileNumber)
            .Must(value => string.IsNullOrWhiteSpace(value) ||
                IndianMobileNumber.TryNormalizeTenDigit(value, out _))
            .WithMessage("Mobile number must be a valid 10-digit Indian mobile number.");
        RuleFor(x => x).Must(x => x.WorkStatus == CandidateWorkStatus.Experienced ||
            x.CurrentAnnualSalary is null && x.CurrentFixedAnnualSalary is null &&
            x.CurrentVariableAnnualSalary is null)
            .WithMessage("Salary is available only for experienced candidates.");
        RuleFor(x => x).Must(x => !x.CurrentFixedAnnualSalary.HasValue ||
            !x.CurrentVariableAnnualSalary.HasValue || !x.CurrentAnnualSalary.HasValue ||
            x.CurrentFixedAnnualSalary.Value + x.CurrentVariableAnnualSalary.Value <= x.CurrentAnnualSalary.Value)
            .WithMessage("Fixed and variable salary cannot exceed total annual salary.");
    }
}

public sealed class UpdateCandidateCareerPreferencesRequestValidator :
    AbstractValidator<UpdateCandidateCareerPreferencesRequest>
{
    public UpdateCandidateCareerPreferencesRequestValidator()
    {
        RuleFor(x => x.PreferredJobRoles).NotNull().Must(x => x.Count <= 3)
            .Must(ProfileText.Unique).WithMessage("Preferred job roles must be unique and cannot exceed 3.");
        RuleForEach(x => x.PreferredJobRoles).NotEmpty().MaximumLength(100).Must(ProfileText.Safe);
        RuleFor(x => x.PreferredCities).NotNull().Must(x => x.Count <= 5)
            .Must(ProfileText.Unique).WithMessage("Preferred cities must be unique and cannot exceed 5.");
        RuleForEach(x => x.PreferredCities).NotEmpty().MaximumLength(150).Must(ProfileText.Safe);
        RuleFor(x => x.ExpectedAnnualSalary).GreaterThanOrEqualTo(0).When(x => x.ExpectedAnnualSalary.HasValue);
        RuleFor(x => x.JobTypes).NotNull().Must(ProfileText.UniqueEnums);
        RuleForEach(x => x.JobTypes).IsInEnum();
        RuleFor(x => x.EmploymentTypes).NotNull().Must(ProfileText.UniqueEnums);
        RuleForEach(x => x.EmploymentTypes).IsInEnum();
        RuleFor(x => x.PreferredShifts).NotNull().Must(ProfileText.UniqueEnums);
        RuleForEach(x => x.PreferredShifts).IsInEnum();
    }
}

file static class ProfileText
{
    public static bool Safe(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(char.IsControl) && !value.Contains('<') && !value.Contains('>');
    public static bool OptionalSafe(string? value) => string.IsNullOrWhiteSpace(value) || Safe(value);
    public static bool Unique(IReadOnlyCollection<string> values) => values.All(x => !string.IsNullOrWhiteSpace(x)) &&
        values.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count;
    public static bool UniqueEnums<T>(IReadOnlyCollection<T> values) where T : struct, Enum =>
        values.Distinct().Count() == values.Count;
}
