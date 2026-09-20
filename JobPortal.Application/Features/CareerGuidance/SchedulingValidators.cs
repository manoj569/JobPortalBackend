using FluentValidation;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class SaveAvailabilityRequestValidator : AbstractValidator<SaveAvailabilityRequest>
{
    public SaveAvailabilityRequestValidator()
    {
        RuleFor(x => x.Revision).NotEmpty();
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Windows).NotNull().Must(w => w is { Length: <= 28 });
        RuleForEach(x => x.Windows).NotNull().ChildRules(w =>
        {
            w.RuleFor(x => x.DayOfWeek).IsInEnum();
            w.RuleFor(x => x).Must(x => x.StartTime < x.EndTime && x.EndTime - x.StartTime >= TimeSpan.FromMinutes(15) &&
                x.EndTime - x.StartTime <= TimeSpan.FromHours(12) && x.StartTime.Ticks % TimeSpan.TicksPerMinute == 0 && x.EndTime.Ticks % TimeSpan.TicksPerMinute == 0)
                .WithMessage("Windows must be minute-aligned, 15 minutes to 12 hours, within one day.");
        });
        RuleFor(x => x.Windows).Must(w => w is null || w.Any(x => x is null) || !w.SelectMany((a, i) => w.Skip(i + 1)
            .Where(b => a.DayOfWeek == b.DayOfWeek && a.StartTime < b.EndTime && a.EndTime > b.StartTime)).Any())
            .WithMessage("Weekly windows cannot overlap or duplicate, including inactive windows.");
    }
}

public sealed class AvailabilityExceptionRequestValidator : AbstractValidator<AvailabilityExceptionRequest>
{
    public AvailabilityExceptionRequestValidator()
    {
        RuleFor(x => x.Revision).NotEmpty();
        RuleFor(x => x.LocalDate).NotEmpty();
        RuleFor(x => x).Must(x => (x.StartTime is null && x.EndTime is null) ||
            (x.StartTime.HasValue && x.EndTime.HasValue && x.StartTime < x.EndTime &&
             x.StartTime.Value.Ticks % TimeSpan.TicksPerMinute == 0 && x.EndTime.Value.Ticks % TimeSpan.TicksPerMinute == 0))
            .WithMessage("Provide a full-day block or a valid minute-aligned local range.");
    }
}

public sealed class CreateCareerBookingRequestValidator : AbstractValidator<CreateCareerBookingRequest>
{
    public CreateCareerBookingRequestValidator()
    {
        RuleFor(x => x.ConsultantId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.StartUtc).NotEmpty().Must(x => x.Offset == TimeSpan.Zero).WithMessage("StartUtc must use UTC (Z or +00:00).");
        RuleFor(x => x.Questionnaire).NotNull().ChildRules(q =>
        {
            q.RuleFor(x => x.TargetCompany).MaximumLength(200);
            q.RuleFor(x => x.TargetRole).MaximumLength(200);
            q.RuleFor(x => x.YearsOfExperience).InclusiveBetween(0, 70).PrecisionScale(4, 1, true);
            q.RuleFor(x => x.CurrentRoleOrStatus).MaximumLength(200);
            q.RuleFor(x => x.SessionGoal).NotEmpty().MaximumLength(2000);
            q.RuleFor(x => x.Questions).MaximumLength(4000);
            q.RuleFor(x => x.Notes).MaximumLength(2000);
        });
    }
}
