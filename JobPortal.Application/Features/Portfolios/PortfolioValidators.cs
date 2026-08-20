using FluentValidation;
using JobPortal.Application.Common.Validation;

namespace JobPortal.Application.Features.Portfolios;

internal static class PortfolioValidation
{
    public static bool PlainText(string? value) =>
        string.IsNullOrEmpty(value) || !value.Any(char.IsControl) &&
        !value.Contains('<', StringComparison.Ordinal) && !value.Contains('>', StringComparison.Ordinal);
    public static bool Order(int value) => value is >= 0 and <= 1000;
}

public sealed class CreatePortfolioRequestValidator : AbstractValidator<CreatePortfolioRequest>
{
    public CreatePortfolioRequestValidator()
    {
        RuleFor(x => x.RequestedSlug).MaximumLength(80);
        RuleFor(x => x.Template).IsInEnum();
    }
}
public sealed class UpdatePortfolioSettingsRequestValidator : AbstractValidator<UpdatePortfolioSettingsRequest>
{
    public UpdatePortfolioSettingsRequestValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Template).IsInEnum();
        RuleFor(x => x.Sections).NotNull().Must(x => x.Count == 9)
            .WithMessage("All supported portfolio section settings are required.")
            .Must(x => x.Select(y => y.SectionType).Distinct().Count() == 9)
            .WithMessage("Portfolio section settings must be unique.");
        RuleForEach(x => x.Sections).ChildRules(section =>
        {
            section.RuleFor(x => x.SectionType).IsInEnum();
            section.RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
        });
    }
}
public sealed class ExperienceRequestValidator : AbstractValidator<ExperienceRequest>
{
    public ExperienceRequestValidator()
    {
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Location).MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.EmploymentType).IsInEnum().When(x => x.EmploymentType.HasValue);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).Null().When(x => x.IsCurrent).WithMessage("Current experience cannot have an end date.");
        RuleFor(x => x.EndDate).NotNull().When(x => !x.IsCurrent).WithMessage("Previous experience requires an end date.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate.HasValue && !x.IsCurrent);
        RuleFor(x => x.Description).MaximumLength(4000).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.AnnualSalary).GreaterThanOrEqualTo(0).When(x => x.AnnualSalary.HasValue);
        RuleFor(x => x.NoticePeriod).IsInEnum().When(x => x.NoticePeriod.HasValue);
        RuleFor(x => x.NoticePeriod).Null().When(x => !x.IsCurrent)
            .WithMessage("Notice period is available only for current employment.");
        RuleFor(x => x.SkillsUsed).Must(x => x is null || x.Count <= 30);
        RuleForEach(x => x.SkillsUsed).NotEmpty().MaximumLength(100).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.SkillsUsed).Must(x => x is null || x.Select(y => y.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == x.Count)
            .WithMessage("Skills used must not contain duplicates.");
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
public sealed class EducationRequestValidator : AbstractValidator<EducationRequest>
{
    public EducationRequestValidator(TimeProvider timeProvider)
    {
        var maximumYear = timeProvider.GetUtcNow().Year + 10;
        RuleFor(x => x.Qualification).NotEmpty().MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Institution).NotEmpty().MaximumLength(250).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.FieldOfStudy).MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.StartYear).InclusiveBetween(1900, maximumYear).When(x => x.StartYear.HasValue);
        RuleFor(x => x.EndYear).InclusiveBetween(1900, maximumYear).When(x => x.EndYear.HasValue);
        RuleFor(x => x.EndYear).GreaterThanOrEqualTo(x => x.StartYear!.Value).When(x => x.StartYear.HasValue && x.EndYear.HasValue);
        RuleFor(x => x.EndYear).Null().When(x => x.IsCurrentlyStudying)
            .WithMessage("Currently studying education cannot have an ending year.");
        RuleFor(x => x.CourseType).IsInEnum().When(x => x.CourseType.HasValue);
        RuleFor(x => x.GradingSystem).MaximumLength(100).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Grade).MaximumLength(100).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Description).MaximumLength(4000).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
public sealed class ProjectRequestValidator : AbstractValidator<ProjectRequest>
{
    public ProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Role).MaximumLength(150).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(4000).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Technologies).NotNull().Must(x => x.Count <= 30);
        RuleForEach(x => x.Technologies).NotEmpty().MaximumLength(100).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.SourceUrl).Must(x => SafeHttpsUrl.IsValid(x));
        RuleFor(x => x.LiveUrl).Must(x => SafeHttpsUrl.IsValid(x));
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate!.Value).When(x => x.StartDate.HasValue && x.EndDate.HasValue);
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
public sealed class CertificationRequestValidator : AbstractValidator<CertificationRequest>
{
    public CertificationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Issuer).MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.ExpiryDate).Null().When(x => x.DoesNotExpire).WithMessage("A non-expiring certification cannot have an expiry date.");
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.IssuedDate!.Value).When(x => x.IssuedDate.HasValue && x.ExpiryDate.HasValue);
        RuleFor(x => x.CredentialId).MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.CredentialUrl).Must(x => SafeHttpsUrl.IsValid(x));
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
public sealed class ProfessionalLinkRequestValidator : AbstractValidator<ProfessionalLinkRequest>
{
    public ProfessionalLinkRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Label).MaximumLength(100).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Url).NotEmpty().MaximumLength(2048).Must(x => SafeHttpsUrl.IsValid(x, false));
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
public sealed class CustomSectionRequestValidator : AbstractValidator<CustomSectionRequest>
{
    public CustomSectionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
public sealed class CustomItemRequestValidator : AbstractValidator<CustomItemRequest>
{
    public CustomItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Description).MaximumLength(4000).Must(PortfolioValidation.PlainText);
        RuleFor(x => x.Url).Must(x => SafeHttpsUrl.IsValid(x));
        RuleFor(x => x.DisplayOrder).Must(PortfolioValidation.Order);
    }
}
