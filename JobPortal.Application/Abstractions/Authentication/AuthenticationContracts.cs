using JobPortal.Application.Features.Authentication;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
namespace JobPortal.Application.Abstractions.Authentication;
public interface IAuthService
{
    Task<RegistrationResponse> RegisterAsync(
        RegisterRequest request, CancellationToken cancellationToken = default);
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
}
public enum EmailDeliveryResult { Sent, Disabled, Failed }
