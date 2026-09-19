using FluentValidation;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.JobAggregation;

public sealed class SaveJobSourceRequestValidator : AbstractValidator<SaveJobSourceRequest>
{
    public SaveJobSourceRequestValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.CareerPageUrl).NotEmpty().MaximumLength(2048)
            .Must(value => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo))
            .WithMessage("CareerPageUrl must be an absolute HTTP or HTTPS URL without credentials.");
        RuleFor(x => x.AtsType).IsInEnum();
        RuleFor(x => x.AtsIdentifier).MaximumLength(255);
        RuleFor(x => x.AtsIdentifier).NotEmpty()
            .When(x => x.AtsType is AtsType.Greenhouse or AtsType.Lever);
        RuleFor(x => x.ScanIntervalMinutes).InclusiveBetween(1, 10080);
    }
}

public sealed class JobSourceSearchQueryValidator : AbstractValidator<JobSourceSearchQuery>
{
    public JobSourceSearchQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.CompanyId).NotEqual(Guid.Empty).When(x => x.CompanyId.HasValue);
        RuleFor(x => x.AtsType).IsInEnum().When(x => x.AtsType.HasValue);
    }
}
