using FluentValidation;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerApplications;

public sealed class EmployerApplicationQueryValidator
    : AbstractValidator<EmployerApplicationQuery>
{
    public EmployerApplicationQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Status)
            .IsInEnum()
            .When(x => x.Status.HasValue);

        RuleFor(x => x.Keyword)
            .MaximumLength(250);

        RuleFor(x => x)
            .Must(x =>
                !x.SubmittedFromUtc.HasValue ||
                !x.SubmittedToUtc.HasValue ||
                x.SubmittedFromUtc <= x.SubmittedToUtc)
            .WithMessage(
                "SubmittedFromUtc must be earlier than or equal to SubmittedToUtc.");
    }
}

public sealed class UpdateEmployerApplicationStatusRequestValidator
    : AbstractValidator<UpdateEmployerApplicationStatusRequest>
{
    private static readonly JobApplicationStatus[] AllowedStatuses =
    [
        JobApplicationStatus.Reviewed,
        JobApplicationStatus.Shortlisted,
        JobApplicationStatus.Rejected
    ];

    public UpdateEmployerApplicationStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(x => AllowedStatuses.Contains(x))
            .WithMessage(
                "Status must be Reviewed, Shortlisted, or Rejected.");

        RuleFor(x => x.InternalNote)
            .MaximumLength(2000)
            .Must(IsSafeNote)
            .WithMessage(
                "Internal note contains unsupported control characters.");
    }

    private static bool IsSafeNote(string? value) =>
        value is null ||
        value.All(character =>
            !char.IsControl(character) ||
            character is '\r' or '\n' or '\t');
}
