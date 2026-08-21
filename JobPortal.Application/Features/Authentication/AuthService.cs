using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Legal;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobPortal.Application.Features.Authentication;

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IEmailService emailService,
    IAuditWriter auditWriter,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<RequestPasswordResetRequest> requestPasswordResetValidator,
    IValidator<CompletePasswordResetRequest> completeResetValidator,
    IValidator<RefreshTokenRequest> refreshValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    IValidator<VerifyEmailRequest> verifyEmailValidator,
    IRegistrationEmailOutbox registrationEmails,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly Action<ILogger, string, string, Exception?> AuthenticationInformation =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1200, nameof(AuthenticationInformation)),
            "Authentication event {Category}: status {Status}.");

    private static readonly Action<ILogger, string, string, Exception?> AuthenticationWarning =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1201, nameof(AuthenticationWarning)),
            "Authentication event {Category}: status {Status}.");

    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan EmailVerificationTokenLifetime = TimeSpan.FromHours(24);
    private static readonly Action<ILogger, string, double, Exception?> RegistrationTiming =
        LoggerMessage.Define<string, double>(LogLevel.Information,
            new EventId(1202, nameof(RegistrationTiming)),
            "Registration stage {Operation} completed in {DurationMilliseconds:F1} ms.");
    private const string RegistrationSuccessMessage = "Registration successful. Please log in.";
    private const string PasswordChangedMessage = "Password changed successfully. Please log in.";
    private const string PasswordResetRequestedMessage =
        "If an account exists for this email address, a password reset link has been sent.";

    public async Task<RegistrationResponse> RegisterAsync(
     RegisterRequest request,
     CancellationToken cancellationToken = default)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        request = request with
        {
            FullName = request.FullName?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty
        };

        var stageStarted = Stopwatch.GetTimestamp();
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);
        LogRegistrationTiming("validation", stageStarted);
        _ = PersonalName.TrySplit(request.FullName, out var firstName, out var lastName);
        var normalizedEmail = NormalizeEmail(request.Email);
        _ = IndianMobileNumber.TryNormalizeTenDigit(request.PhoneNumber, out var normalizedPhoneNumber);

        stageStarted = Stopwatch.GetTimestamp();
        var identityExists = await users.RegistrationIdentityExistsAsync(
            normalizedEmail,
            normalizedPhoneNumber,
            cancellationToken);
        LogRegistrationTiming("duplicate_identity_lookup", stageStarted);
        if (identityExists)
        {
            LogAuthenticationEvent("send_skipped_existing_user", "skipped");
            LogRegistrationTiming("total", totalStarted);
            return new(RegistrationSuccessMessage);
        }

        stageStarted = Stopwatch.GetTimestamp();
        var passwordHash = passwordHasher.Hash(request.Password);
        LogRegistrationTiming("password_hash", stageStarted);
        var verificationToken = GenerateEmailVerificationToken();
        var user = new User
        {
            Id = Guid.NewGuid(), // <--- VERY IMPORTANT: We need to generate an ID!
            Email = request.Email,
            NormalizedEmail = normalizedEmail,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = passwordHash,
            PhoneNumber = normalizedPhoneNumber,
            NormalizedPhoneNumber = normalizedPhoneNumber,
            PhoneConfirmed = true,
            TermsAndPrivacyAcceptedAtUtc = UtcNow,
            TermsAndPrivacyVersion = LegalDocumentCatalog.CurrentVersion,
            Status = UserStatus.Active,
            EmailConfirmed = false,
            EmailVerificationTokenHash = HashEmailVerificationToken(verificationToken),
            EmailVerificationTokenExpiresAtUtc = UtcNow.Add(EmailVerificationTokenLifetime),
            EmailVerificationSentAtUtc = null,
            RoleId = SystemRoleIds.Candidate,
            Role = new Role
            {
                Id = SystemRoleIds.Candidate,
                Name = "Candidate",
                NormalizedName = "CANDIDATE"
            }
        };

        await users.AddAsync(user, cancellationToken);
        await registrationEmails.EnqueueAsync(new RegistrationEmailRequest
        {
            UserId = user.Id,
            User = user,
            VerificationToken = verificationToken,
            ExpiresAtUtc = user.EmailVerificationTokenExpiresAtUtc.Value,
            NextAttemptAtUtc = UtcNow
        }, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Create,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?> { ["source"] = "passwordRegistration" },
            new(user.Id, "Candidate")), cancellationToken);

        try
        {
            stageStarted = Stopwatch.GetTimestamp();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogRegistrationTiming("database_transaction", stageStarted);
        }
        catch (UniqueConstraintException exception)
        {
            LogAuthenticationEvent("registration_identity_checked", "uniqueness_conflict", exception);
            LogRegistrationTiming("total", totalStarted);
            return new(RegistrationSuccessMessage);
        }

        LogAuthenticationEvent("registration_completed", "created");
        LogRegistrationTiming("total", totalStarted);
        return new(RegistrationSuccessMessage);
    }

    public async Task<MessageResponse> VerifyEmailAsync(
        VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        await verifyEmailValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tokenHash = HashEmailVerificationToken(request.Token);
        var user = await users.GetByEmailVerificationTokenHashAsync(tokenHash, cancellationToken);
        if (user is null || user.EmailConfirmed ||
            user.EmailVerificationTokenExpiresAtUtc is null ||
            user.EmailVerificationTokenExpiresAtUtc <= UtcNow ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(tokenHash),
                Convert.FromHexString(user.EmailVerificationTokenHash!)))
            throw new BadRequestException("The email verification link is invalid or expired.",
                "invalid_email_verification_token");
        user.EmailConfirmed = true;
        user.EmailVerificationTokenHash = null;
        user.EmailVerificationTokenExpiresAtUtc = null;
        users.Update(user);
        await auditWriter.AppendAsync(new(AuditAction.Update, "User", user.Id.ToString(),
            new Dictionary<string, string?> { ["operation"] = "emailVerified" },
            new(user.Id, user.Role.Name)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new("Email verified successfully.");
    }

    public async Task<AuthenticationResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        request = request with { Identifier = request.Identifier?.Trim() ?? string.Empty };
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);
        User? user;
        if (request.Identifier.Contains('@', StringComparison.Ordinal))
        {
            user = await users.GetByNormalizedEmailAsync(NormalizeEmail(request.Identifier), cancellationToken);
        }
        else
        {
            _ = IndianMobileNumber.TryNormalize(request.Identifier, out var normalizedPhoneNumber);
            user = await users.GetByNormalizedPhoneAsync(normalizedPhoneNumber, cancellationToken);
        }

        if (user is null || user.Status != UserStatus.Active ||
            string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !passwordHasher.Verify(request.Password, user.PasswordHash))
            throw InvalidCredentials();

        user.LastLoginAtUtc = UtcNow;
        users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<MessageResponse> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        request = request with { Email = request.Email?.Trim() ?? string.Empty };
        await requestPasswordResetValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByNormalizedEmailAsync(
            NormalizeEmail(request.Email),
            cancellationToken);
        if (user is not { Status: UserStatus.Active } ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
            return new(PasswordResetRequestedMessage);

        var rawToken = GeneratePasswordResetToken();
        user.PasswordResetTokenHash = HashPasswordResetToken(rawToken);
        user.PasswordResetTokenExpiresAtUtc = UtcNow.Add(PasswordResetTokenLifetime);
        users.Update(user);
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["operation"] = "passwordResetRequested"
            },
            new(user.Id, user.Role.Name)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _ = await emailService.SendPasswordResetAsync(
            user,
            rawToken,
            cancellationToken);
        return new(PasswordResetRequestedMessage);
    }

    public async Task<MessageResponse> CompletePasswordResetAsync(
        CompletePasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        await completeResetValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tokenHash = HashPasswordResetToken(request.Token);
        var user = await users.GetByPasswordResetTokenHashAsync(
            tokenHash,
            cancellationToken);
        if (user is not { Status: UserStatus.Active } ||
            string.IsNullOrWhiteSpace(user.PasswordResetTokenHash) ||
            user.PasswordResetTokenExpiresAtUtc is null ||
            user.PasswordResetTokenExpiresAtUtc <= UtcNow ||
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(tokenHash),
                Convert.FromHexString(user.PasswordResetTokenHash)))
            throw InvalidPasswordReset();

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAtUtc = null;
        users.Update(user);
        await refreshTokens.RevokeActiveForUserAsync(user.Id, UtcNow, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "User",
            user.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["operation"] = "passwordResetCompleted"
            },
            new(user.Id, user.Role.Name)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(PasswordChangedMessage);
    }

    public async Task<AuthenticationResponse> RefreshAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tokenHash = jwtTokenService.HashToken(request.RefreshToken);
        var existingToken = await refreshTokens.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (existingToken is null ||
            existingToken.RevokedAtUtc is not null ||
            existingToken.ExpiresAtUtc <= UtcNow ||
            existingToken.User.Status != UserStatus.Active)
            throw new UnauthorizedException("The refresh token is invalid or expired.");

        var response = await IssueTokensAsync(existingToken.User, ipAddress, cancellationToken);
        existingToken.RevokedAtUtc = UtcNow;
        existingToken.RevokedByIp = ipAddress;
        existingToken.ReplacedByToken = jwtTokenService.HashToken(response.RefreshToken);
        refreshTokens.Update(existingToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await users.GetByIdWithRoleAsync(userId, cancellationToken) ?? throw new UnauthorizedException();
        if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BadRequestException("The current password is incorrect.", "invalid_current_password");

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        users.Update(user);
        await refreshTokens.RevokeActiveForUserAsync(userId, UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAsync(
        Guid userId,
        LogoutRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await refreshValidator.ValidateAndThrowAsync(new RefreshTokenRequest(request.RefreshToken), cancellationToken);
        var token = await refreshTokens.GetByTokenHashAsync(jwtTokenService.HashToken(request.RefreshToken), cancellationToken);
        if (token is null || token.UserId != userId || token.RevokedAtUtc is not null) return;

        token.RevokedAtUtc = UtcNow;
        token.RevokedByIp = ipAddress;
        refreshTokens.Update(token);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void LogAuthenticationEvent(
        string category,
        string status,
        Exception? exception = null,
        LogLevel level = LogLevel.Information)
    {
        var write = level == LogLevel.Warning ? AuthenticationWarning : AuthenticationInformation;
        write(logger, category, status, exception);
    }

    private async Task<AuthenticationResponse> IssueTokensAsync(
        User user,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var accessToken = jwtTokenService.CreateAccessToken(user);
        var rawRefreshToken = jwtTokenService.GenerateRefreshToken();
        var refreshToken = new RefreshToken
        {
            Token = jwtTokenService.HashToken(rawRefreshToken),
            ExpiresAtUtc = UtcNow.Add(RefreshTokenLifetime),
            CreatedByIp = ipAddress,
            UserId = user.Id
        };
        await refreshTokens.AddAsync(refreshToken, cancellationToken);
        return new(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshToken.ExpiresAtUtc,
            new(user.Id, user.Email, user.FirstName, user.LastName, user.Role.Name));
    }

    private static UnauthorizedException InvalidCredentials() => new("Invalid identifier or password.");

    private static BadRequestException InvalidPasswordReset() => new(
        "The password reset link is invalid or expired.",
        "invalid_password_reset");

    private static string GeneratePasswordResetToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashPasswordResetToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string GenerateEmailVerificationToken() => GeneratePasswordResetToken();
    private static string HashEmailVerificationToken(string rawToken) => HashPasswordResetToken(rawToken);

    private void LogRegistrationTiming(string operation, long startedTimestamp) =>
        RegistrationTiming(logger, operation,
            Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds, null);

    private static bool VerifyPasswordResetToken(
        string rawToken,
        string expectedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(HashPasswordResetToken(rawToken)),
                Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
