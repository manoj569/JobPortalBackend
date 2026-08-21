using JobPortal.Application.Features.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
namespace JobPortal.Application.Abstractions.Authentication;
public interface IAuthService
{
    Task<RegistrationResponse> RegisterAsync(
        RegisterRequest request, CancellationToken cancellationToken = default);
    Task<MessageResponse> VerifyEmailAsync(
        VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task<AuthenticationResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<MessageResponse> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default);
    Task<MessageResponse> CompletePasswordResetAsync(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken = default);
    Task<AuthenticationResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(Guid userId, LogoutRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}
public interface IGoogleAuthenticationService
{
    Task<AuthenticationResponse> AuthenticateAsync(
        GoogleAuthenticationRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
    Task<AuthenticationResponse> AuthenticateCodeAsync(
        GoogleAuthorizationCodeRequest request,
        string? origin,
        string? flowHeader,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}

public sealed record ValidatedGoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? Name);

public interface IGoogleCredentialValidator
{
    Task<ValidatedGoogleIdentity> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default);
}

public interface IGoogleAuthorizationCodeExchanger
{
    Task<ValidatedGoogleIdentity> ExchangeAsync(
        string code, string redirectUri, CancellationToken cancellationToken = default);
}

public enum GoogleAuthorizationCodeFailure { Invalid, Unavailable }

public sealed class GoogleAuthorizationCodeException(
    GoogleAuthorizationCodeFailure failure,
    Exception? innerException = null) : Exception("Google authorization code exchange failed.", innerException)
{
    public GoogleAuthorizationCodeFailure Failure { get; } = failure;
}

public sealed class GoogleCredentialValidationException(
    string message = "Google credential validation failed.",
    Exception? innerException = null) : Exception(message, innerException);
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);
public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(User user);
    string GenerateRefreshToken();
    string HashToken(string token);
}
public interface IEmailService
{
    Task<EmailDeliveryResult> SendPasswordResetAsync(
        User user,
        string rawToken,
        CancellationToken cancellationToken = default);
    Task<EmailDeliveryResult> SendApplicationStatusAsync(
        User user, string jobTitle, JobApplicationStatus status,
        CancellationToken cancellationToken = default);
    Task<EmailDeliveryResult> SendRegistrationVerificationAsync(
        User user, string rawToken, CancellationToken cancellationToken = default);
}
public enum EmailDeliveryResult { Sent, Disabled, Failed }
