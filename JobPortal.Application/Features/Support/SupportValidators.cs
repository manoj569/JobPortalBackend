using FluentValidation;

namespace JobPortal.Application.Features.Support;

public sealed class CreateSupportTicketRequestValidator : AbstractValidator<CreateSupportTicketRequest>
{
    public CreateSupportTicketRequestValidator()
    {
        // The service supplies the stored account identity for authenticated requests.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(201).Must(x => !HasControlCharacters(x));
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress().Must(x => !HasControlCharacters(x));
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200).Must(x => !HasControlCharacters(x));
        RuleFor(x => x.Description).NotEmpty().MaximumLength(5000).Must(x =>
            x is null || !x.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')));
    }

    private static bool HasControlCharacters(string? value) => value?.Any(char.IsControl) == true;
}

public sealed class SupportTicketPageQueryValidator : AbstractValidator<SupportTicketPageQuery>
{
    public SupportTicketPageQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class AdminSupportTicketQueryValidator : AbstractValidator<AdminSupportTicketQuery>
{
    public AdminSupportTicketQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Category).IsInEnum().When(x => x.Category.HasValue);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.TicketNumber).MaximumLength(40);
        RuleFor(x => x.FromUtc).Must(x => x is null || x.Value.Kind == DateTimeKind.Utc).WithMessage("FromUtc must be UTC.");
        RuleFor(x => x.ToUtc).Must(x => x is null || x.Value.Kind == DateTimeKind.Utc).WithMessage("ToUtc must be UTC.");
        RuleFor(x => x).Must(x => x.FromUtc is null || x.ToUtc is null || x.FromUtc < x.ToUtc)
            .WithMessage("FromUtc must precede ToUtc.");
    }
}

public sealed class UpdateSupportTicketStatusRequestValidator : AbstractValidator<UpdateSupportTicketStatusRequest>
{
    public UpdateSupportTicketStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class UpdateSupportTicketNotesRequestValidator : AbstractValidator<UpdateSupportTicketNotesRequest>
{
    public UpdateSupportTicketNotesRequestValidator() => RuleFor(x => x.AdminNotes).MaximumLength(5000);
}
