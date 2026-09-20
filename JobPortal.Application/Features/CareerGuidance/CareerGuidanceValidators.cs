using FluentValidation;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class ConsultantProfileRequestValidator : AbstractValidator<ConsultantProfileRequest>
{
    public ConsultantProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ProfessionalHeadline).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Bio).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.CompanyName).MaximumLength(200).NotNull();
        RuleFor(x => x.CurrentRole).NotEmpty().MaximumLength(160);
        RuleFor(x => x.CompanyId).NotEqual(Guid.Empty).When(x => x.CompanyId.HasValue);
        RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 70).PrecisionScale(4, 1, true);
        RuleFor(x => x.ProfessionalType).IsInEnum();
        RuleFor(x => x.LinkedInUrl).NotEmpty().MaximumLength(500).Must(url =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.Host is "linkedin.com" or "www.linkedin.com" && uri.AbsolutePath.StartsWith("/in/", StringComparison.Ordinal) &&
            uri.AbsolutePath.Length > 4 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            .WithMessage("Use an HTTPS LinkedIn profile URL without credentials, query or fragment.");
        RuleFor(x => x.Languages).NotNull().Must(x => x is { Length: >= 1 and <= 10 });
        RuleForEach(x => x.Languages).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Expertise).NotNull().Must(x => x is { Length: >= 1 and <= 20 });
        RuleForEach(x => x.Expertise).NotEmpty().MaximumLength(60);
        RuleFor(x => x.AcceptIndependentGuidancePolicy).Equal(true);
    }
}

public sealed class ConsultantServiceRequestValidator : AbstractValidator<ConsultantServiceRequest>
{
    public ConsultantServiceRequestValidator()
    {
        RuleFor(x => x.ServiceType).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(3000);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 180);
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(1000000).PrecisionScale(18, 2, true);
        RuleFor(x => x.Currency).Must(x => x is "INR" or "USD" or "EUR" or "GBP");
        RuleFor(x => x.Revision).NotEmpty();
    }
}

public sealed class ConsultantSearchQueryValidator : AbstractValidator<ConsultantSearchQuery>
{
    public ConsultantSearchQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.CompanyId).NotEqual(Guid.Empty).When(x => x.CompanyId.HasValue);
        RuleFor(x => x.Company).MaximumLength(200);
        RuleFor(x => x.Role).MaximumLength(160);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Language).MaximumLength(60);
        RuleFor(x => x.Expertise).MaximumLength(60);
        RuleFor(x => x.ServiceType).MaximumLength(60);
        RuleFor(x => x.ProfessionalType).IsInEnum().When(x => x.ProfessionalType.HasValue);
        RuleFor(x => x.Currency).Must(x => x is null or "INR" or "USD" or "EUR" or "GBP");
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000000);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).LessThanOrEqualTo(1000000);
        RuleFor(x => x).Must(x => !x.MinPrice.HasValue || !x.MaxPrice.HasValue || x.MinPrice <= x.MaxPrice)
            .WithMessage("MinPrice cannot exceed MaxPrice.");
        RuleFor(x => x.Sort).Must(x => x is "newest" or "experience" or "price-asc" or "price-desc");
        RuleFor(x => x.Currency).NotEmpty().When(x => x.MinPrice.HasValue || x.MaxPrice.HasValue || x.Sort?.StartsWith("price-", StringComparison.Ordinal) == true);
    }
}

public sealed class ConsultantReviewRequestValidator : AbstractValidator<ConsultantReviewRequest>
{
    public ConsultantReviewRequestValidator()
    {
        RuleFor(x => x.Action).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Revision).NotEmpty();
    }
}

public sealed class ConsultantAdminQueryValidator : AbstractValidator<ConsultantAdminQuery>
{
    public ConsultantAdminQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1000000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}
