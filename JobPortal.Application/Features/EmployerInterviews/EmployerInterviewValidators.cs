using FluentValidation;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerInterviews;

public sealed class EmployerInterviewQueryValidator
    : AbstractValidator<EmployerInterviewQuery>
{
    public EmployerInterviewQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Status)
            .IsInEnum()
            .When(x => x.Status.HasValue);

        RuleFor(x => x.Result)
            .IsInEnum()
            .When(x => x.Result.HasValue);

        RuleFor(x => x.RoundType)
            .IsInEnum()
            .When(x => x.RoundType.HasValue);
    }
}

public sealed class CreateEmployerInterviewRequestValidator
    : AbstractValidator<CreateEmployerInterviewRequest>
{
    public CreateEmployerInterviewRequestValidator()
    {
        RuleFor(x => x.RoundType)
            .IsInEnum();

        RuleFor(x => x.Title)
            .MaximumLength(160);

        RuleFor(x => x.ScheduledAtUtc)
            .NotEmpty();

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(10, 480);

        RuleFor(x => x.InterviewFormat)
            .IsInEnum();

        RuleFor(x => x.MeetingUrl)
            .MaximumLength(2048);

        RuleFor(x => x.Location)
            .MaximumLength(500);

        RuleFor(x => x.InterviewerName)
            .MaximumLength(160);

        RuleFor(x => x.InterviewerEmail)
            .MaximumLength(256)
            .EmailAddress()
            .When(x =>
                !string.IsNullOrWhiteSpace(
                    x.InterviewerEmail));

        RuleFor(x => x.InternalNotes)
            .MaximumLength(2000);
    }
}

public sealed class UpdateEmployerInterviewRequestValidator
    : AbstractValidator<UpdateEmployerInterviewRequest>
{
    public UpdateEmployerInterviewRequestValidator()
    {
        RuleFor(x => x.RoundType)
            .IsInEnum();

        RuleFor(x => x.Title)
            .MaximumLength(160);

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(10, 480);

        RuleFor(x => x.InterviewFormat)
            .IsInEnum();

        RuleFor(x => x.MeetingUrl)
            .MaximumLength(2048);

        RuleFor(x => x.Location)
            .MaximumLength(500);

        RuleFor(x => x.InterviewerName)
            .MaximumLength(160);

        RuleFor(x => x.InterviewerEmail)
            .MaximumLength(256)
            .EmailAddress()
            .When(x =>
                !string.IsNullOrWhiteSpace(
                    x.InterviewerEmail));

        RuleFor(x => x.InternalNotes)
            .MaximumLength(2000);
    }
}

public sealed class RescheduleEmployerInterviewRequestValidator
    : AbstractValidator<RescheduleEmployerInterviewRequest>
{
    public RescheduleEmployerInterviewRequestValidator()
    {
        RuleFor(x => x.ScheduledAtUtc)
            .NotEmpty();

        RuleFor(x => x.MeetingUrl)
            .MaximumLength(2048);

        RuleFor(x => x.Location)
            .MaximumLength(500);
    }
}

public sealed class CancelEmployerInterviewRequestValidator
    : AbstractValidator<CancelEmployerInterviewRequest>
{
    public CancelEmployerInterviewRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(2000);
    }
}

public sealed class CompleteEmployerInterviewRequestValidator
    : AbstractValidator<CompleteEmployerInterviewRequest>
{
    public CompleteEmployerInterviewRequestValidator()
    {
        RuleFor(x => x.Result)
            .Must(x =>
                x is EmployerInterviewResult.Passed
                    or EmployerInterviewResult.Failed
                    or EmployerInterviewResult.NoShow)
            .WithMessage(
                "Result must be Passed, Failed, or NoShow.");

        RuleFor(x => x.Score)
            .InclusiveBetween(0m, 100m)
            .When(x => x.Score.HasValue);

        RuleFor(x => x.Feedback)
            .MaximumLength(4000);

        RuleFor(x => x.InternalNotes)
            .MaximumLength(2000);
    }
}
