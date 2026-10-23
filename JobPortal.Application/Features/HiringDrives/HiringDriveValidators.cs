using FluentValidation;

namespace JobPortal.Application.Features.HiringDrives;

public sealed class SaveHiringDriveValidator : AbstractValidator<SaveHiringDriveRequest>
{
    public SaveHiringDriveValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subtitle).MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(10000);
        RuleFor(x => x.BadgeText).MaximumLength(60);
        RuleFor(x => x.BadgeType).Must(x => x is null or "hiring" or "walkIn" or "openings" or "featured");
        RuleFor(x => x.LocationText).MaximumLength(200);
        RuleFor(x => x.ExperienceText).MaximumLength(100);
        RuleFor(x => x.StartAtUtc).NotEmpty().Must(x => x.Kind == DateTimeKind.Utc);
        RuleFor(x => x.EndAtUtc).Must((r, end) => end is null || end.Value.Kind == DateTimeKind.Utc && end > r.StartAtUtc);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ApplyUrl).MaximumLength(2048).Must(x => x is null || IsHttpsUrl(x));
        RuleFor(x => x.ImageUrl).MaximumLength(2048).Must(x => x is null || IsHttpsUrl(x));
        RuleFor(x => x).Must(x => x.JobId.HasValue || !string.IsNullOrWhiteSpace(x.ApplyUrl))
            .WithMessage("An internal JobId or verified HTTPS ApplyUrl is required.");
    }
    public static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo)
        && !value.Any(char.IsControl) && !value.Contains('\\');
}
public sealed class HiringDriveQueryValidator : AbstractValidator<HiringDriveQuery>
{
    public HiringDriveQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class HiringDriveImpressionsValidator : AbstractValidator<HiringDriveImpressionsRequest>
{
    public HiringDriveImpressionsValidator()
    {
        RuleFor(x => x.DriveIds).NotNull().Must(x => x is { Count: > 0 and <= 50 });
        RuleForEach(x => x.DriveIds).NotEmpty();
    }
}
