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
        RuleFor(x => x.MissingCompleteSnapshotThreshold).InclusiveBetween(2, 100);
        RuleFor(x => x.ScanIntervalMinutes).InclusiveBetween(1, 10080);
        RuleFor(x => x).Custom((request, context) =>
        {
            if (request.AtsType == AtsType.RippleHire)
            {
                if (!RippleHireSourceConfiguration.IsPublicDiscoveryUrl(request.CareerPageUrl, out _))
                    context.AddFailure(nameof(request.CareerPageUrl), "RippleHire discovery requires a public HTTPS career URL on port 443 without credentials.");
            }
            else if (request.AtsType is not AtsType.OracleRecruitingCloud and not AtsType.Custom)
            {
                var result = JobSourceConfiguration.Resolve(request.AtsType, request.CareerPageUrl, request.AtsIdentifier);
                if (!result.IsValid)
                    context.AddFailure(new FluentValidation.Results.ValidationFailure(nameof(request.AtsIdentifier), $"{result.ReasonCode}: {result.Message}")
                        { ErrorCode = result.ReasonCode });
            }
            if (request.AtsType != AtsType.OracleRecruitingCloud)
            {
                if (request.OracleRecruitingCloud is not null)
                    context.AddFailure(nameof(request.OracleRecruitingCloud), "Oracle configuration requires the OracleRecruitingCloud provider.");
                return;
            }
            var config = OracleRecruitingCloudConfiguration.Parse(request.AtsIdentifier);
            if (config is null)
                context.AddFailure(nameof(request.AtsIdentifier), "Use oracle:<approved-oracle-host>:<career-site-id>.");
            else if (!config.MatchesCareerPage(request.CareerPageUrl))
                context.AddFailure(nameof(request.CareerPageUrl), "Use the matching HTTPS Oracle career-site jobs URL without credentials, port, query or fragment.");
            if (request.IsActive)
                context.AddFailure(nameof(request.IsActive), OracleRecruitingCloudConfiguration.BlockedMessage);
            if (request.AutoPublishEnabled == true)
                context.AddFailure(nameof(request.AutoPublishEnabled), OracleRecruitingCloudConfiguration.BlockedMessage);
            if (request.OracleRecruitingCloud is { } settings)
            {
                if (config is null || !string.Equals(settings.OracleHost, config.OracleHost, StringComparison.OrdinalIgnoreCase) ||
                    settings.CareerSiteId != config.CareerSiteId)
                    context.AddFailure(nameof(request.OracleRecruitingCloud), "OracleHost and CareerSiteId must match AtsIdentifier.");
                if (settings.AutoPublishEnabled || settings.AuthorizationStatus != OracleRecruitingCloudConfiguration.AuthorizationRequired)
                    context.AddFailure(nameof(request.OracleRecruitingCloud), "Oracle AutoPublishEnabled must be false and AuthorizationStatus must be AuthorizationRequired. Admin input cannot grant integration authorization.");
            }
        });
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
