using FluentValidation;

namespace JobPortal.Application.Features.EmployerCompanies;

public sealed class EmployerCompanySetupRequestValidator
    : AbstractValidator<EmployerCompanySetupRequest>
{
    public EmployerCompanySetupRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(160);

        RuleFor(x => x.WebsiteUrl)
            .Must(BeValidHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl))
            .WithMessage(
                "WebsiteUrl must be a valid HTTP or HTTPS URL.");

        RuleFor(x => x.Description)
            .MaximumLength(5000);

        RuleFor(x => x.Industry)
            .MaximumLength(150);

        RuleFor(x => x.Location)
            .MaximumLength(250);

        RuleFor(x => x.EmployeeCount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.EmployeeCount.HasValue);
    }

    private static bool BeValidHttpUrl(string? value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }
}

public sealed class EmployerCompanyUpdateRequestValidator
    : AbstractValidator<EmployerCompanyUpdateRequest>
{
    public EmployerCompanyUpdateRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(160);

        RuleFor(x => x.WebsiteUrl)
            .Must(BeValidHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.WebsiteUrl))
            .WithMessage(
                "WebsiteUrl must be a valid HTTP or HTTPS URL.");

        RuleFor(x => x.Description)
            .MaximumLength(5000);

        RuleFor(x => x.Industry)
            .MaximumLength(150);

        RuleFor(x => x.Location)
            .MaximumLength(250);

        RuleFor(x => x.EmployeeCount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.EmployeeCount.HasValue);
    }

    private static bool BeValidHttpUrl(string? value)
    {
        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }
}
