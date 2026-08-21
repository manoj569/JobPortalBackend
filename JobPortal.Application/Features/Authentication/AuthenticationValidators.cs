using System.ComponentModel.DataAnnotations;
using FluentValidation;
using JobPortal.Application.Common.Text;

namespace JobPortal.Application.Features.Authentication;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(201)
            .Must(value => PersonalName.TrySplit(value, out _, out _))
            .WithMessage(
                "FullName must contain Unicode letters separated by single spaces only.");
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(128);
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(value => IndianMobileNumber.TryNormalizeTenDigit(value, out _))
            .WithMessage("PhoneNumber must be a valid ten-digit Indian mobile number.");
        RuleFor(x => x.HasAcceptedTermsAndPrivacy)
            .Equal(true)
            .WithMessage("Terms and Privacy consent is required.");
    }
}

public sealed class VerifyRegistrationOtpRequestValidator :
    AbstractValidator<VerifyRegistrationOtpRequest>
{
    public VerifyRegistrationOtpRequestValidator()
    {
        RuleFor(x => x.ChallengeId).NotEmpty();
        RuleFor(x => x.Otp).Must(OtpValidation.IsValid)
            .WithMessage("Otp must contain exactly six digits.");
    }
}

public sealed class ResendRegistrationOtpRequestValidator :
    AbstractValidator<ResendRegistrationOtpRequest>
{
    public ResendRegistrationOtpRequestValidator() =>
        RuleFor(x => x.ChallengeId).NotEmpty();
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty()
            .MaximumLength(256)
            .Must(BeEmailOrIndianMobile)
            .WithMessage("Identifier must be a valid email address or Indian mobile number.");
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }

    private static bool BeEmailOrIndianMobile(string value) =>
        new EmailAddressAttribute().IsValid(value.Trim()) ||
        IndianMobileNumber.TryNormalize(value, out _);
}

public sealed class RequestLoginOtpRequestValidator :
    AbstractValidator<RequestLoginOtpRequest>
{
    public RequestLoginOtpRequestValidator() =>
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(value => IndianMobileNumber.TryNormalizeTenDigit(value, out _))
            .WithMessage("PhoneNumber must be a valid ten-digit Indian mobile number.");
}

public sealed class LoginWithOtpRequestValidator :
    AbstractValidator<LoginWithOtpRequest>
{
    public LoginWithOtpRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(value => IndianMobileNumber.TryNormalizeTenDigit(value, out _))
            .WithMessage("PhoneNumber must be a valid ten-digit Indian mobile number.");
        RuleFor(x => x.Otp).Must(OtpValidation.IsValid)
            .WithMessage("Otp must contain exactly six digits.");
    }
}

public sealed class RequestPasswordResetRequestValidator :
    AbstractValidator<RequestPasswordResetRequest>
{
    public RequestPasswordResetRequestValidator() =>
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();
}

public sealed class CompletePasswordResetRequestValidator :
    AbstractValidator<CompletePasswordResetRequest>
{
    public CompletePasswordResetRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.NewPassword).SetValidator(new PasswordValidator());
    }
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator() => RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
}

public sealed class GoogleAuthenticationRequestValidator :
    AbstractValidator<GoogleAuthenticationRequest>
{
    public GoogleAuthenticationRequestValidator()
    {
        RuleFor(x => x.Credential).NotEmpty().MaximumLength(8192);
        RuleFor(x => x.Intent).IsInEnum();
        RuleFor(x => x.AcceptTerms).Equal(true)
            .When(x => x.Intent == GoogleAuthenticationIntent.Register)
            .WithMessage("Terms and Privacy consent is required for registration.");
    }
}

public sealed class GoogleAuthorizationCodeRequestValidator :
    AbstractValidator<GoogleAuthorizationCodeRequest>
{
    public GoogleAuthorizationCodeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(8192);
        RuleFor(x => x.Intent).IsInEnum();
        RuleFor(x => x.AcceptTerms).Equal(false)
            .When(x => x.Intent == GoogleAuthenticationIntent.Login)
            .WithMessage("Terms acceptance must be false for login.");
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).SetValidator(new PasswordValidator());
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must differ from the current password.");
    }
}

internal sealed class PasswordValidator : AbstractValidator<string>
{
    public PasswordValidator() =>
        RuleFor(x => x).NotEmpty().MinimumLength(6).MaximumLength(128);
}

file static class OtpValidation
{
    public static bool IsValid(string value) =>
        value is { Length: 6 } && value.All(character => character is >= '0' and <= '9');
}
