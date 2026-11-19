using FluentValidation;

namespace JobPortal.Application.Features.EmployerOffers;

public sealed class EmployerOfferQueryValidator
    : AbstractValidator<EmployerOfferQuery>
{
    public EmployerOfferQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Status)
            .IsInEnum()
            .When(x => x.Status.HasValue);
    }
}

public sealed class CreateEmployerOfferRequestValidator
    : AbstractValidator<CreateEmployerOfferRequest>
{
    public CreateEmployerOfferRequestValidator()
    {
        RuleFor(x => x.JobTitle)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.AnnualSalary)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AnnualSalary.HasValue);

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3);

        RuleFor(x => x.WorkLocation)
            .MaximumLength(300);

        RuleFor(x => x.Notes)
            .MaximumLength(4000);

        RuleFor(x => x.OfferLetterUrl)
            .MaximumLength(2048);

        RuleFor(x => x)
            .Must(x =>
                !x.JoiningDateUtc.HasValue ||
                !x.ExpiresAtUtc.HasValue ||
                x.JoiningDateUtc.Value >
                x.ExpiresAtUtc.Value)
            .WithMessage(
                "JoiningDateUtc must be after ExpiresAtUtc when both are provided.");
    }
}

public sealed class UpdateEmployerOfferRequestValidator
    : AbstractValidator<UpdateEmployerOfferRequest>
{
    public UpdateEmployerOfferRequestValidator()
    {
        RuleFor(x => x.JobTitle)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.AnnualSalary)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AnnualSalary.HasValue);

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3);

        RuleFor(x => x.WorkLocation)
            .MaximumLength(300);

        RuleFor(x => x.Notes)
            .MaximumLength(4000);

        RuleFor(x => x.OfferLetterUrl)
            .MaximumLength(2048);

        RuleFor(x => x)
            .Must(x =>
                !x.JoiningDateUtc.HasValue ||
                !x.ExpiresAtUtc.HasValue ||
                x.JoiningDateUtc.Value >
                x.ExpiresAtUtc.Value)
            .WithMessage(
                "JoiningDateUtc must be after ExpiresAtUtc when both are provided.");
    }
}

public sealed class WithdrawEmployerOfferRequestValidator
    : AbstractValidator<WithdrawEmployerOfferRequest>
{
    public WithdrawEmployerOfferRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(2000);
    }
}

public sealed class DeclineEmployerOfferRequestValidator
    : AbstractValidator<DeclineEmployerOfferRequest>
{
    public DeclineEmployerOfferRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(2000);
    }
}
