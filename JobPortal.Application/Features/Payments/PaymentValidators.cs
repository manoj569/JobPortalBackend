using FluentValidation;

namespace JobPortal.Application.Features.Payments;

public sealed class CreatePaymentOrderRequestValidator : AbstractValidator<CreatePaymentOrderRequest>
{
    public CreatePaymentOrderRequestValidator() { RuleFor(x => x.PlanCode).NotEmpty().MaximumLength(50).Matches("^[A-Za-z][A-Za-z0-9]*$"); }
}

public sealed class ConfirmRazorpayPaymentRequestValidator : AbstractValidator<ConfirmRazorpayPaymentRequest>
{
    public ConfirmRazorpayPaymentRequestValidator()
    {
        RuleFor(x => x.RazorpayOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RazorpayPaymentId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RazorpaySignature).NotEmpty().Length(64).Matches("^[0-9a-fA-F]+$");
    }
}
