using FluentValidation;

namespace JobPortal.Application.Features.HiringMessaging;

public sealed class HiringConversationQueryValidator
    : AbstractValidator<HiringConversationQuery>
{
    public HiringConversationQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100);
    }
}

public sealed class CreateHiringConversationRequestValidator
    : AbstractValidator<CreateHiringConversationRequest>
{
    public CreateHiringConversationRequestValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty();

        RuleFor(x => x.InitialMessage)
            .MaximumLength(5000)
            .Must(BeSafeOptionalText);
    }

    private static bool BeSafeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        !value.Any(char.IsControl);
}

public sealed class SendHiringMessageRequestValidator
    : AbstractValidator<SendHiringMessageRequest>
{
    public SendHiringMessageRequestValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty()
            .MaximumLength(5000)
            .Must(value =>
                !string.IsNullOrWhiteSpace(value) &&
                !value.Any(char.IsControl))
            .WithMessage("Message contains invalid characters.");
    }
}
