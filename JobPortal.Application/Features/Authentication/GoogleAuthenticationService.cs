using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Legal;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.Authentication;

public sealed class GoogleAuthenticationService(
    IUserRepository users,
    IUserExternalLoginRepository externalLogins,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IJwtTokenService jwtTokenService,
    IGoogleCredentialValidator credentialValidator,
    IGoogleAuthorizationCodeExchanger authorizationCodeExchanger,
    IAuditWriter auditWriter,
    IValidator<GoogleAuthenticationRequest> validator,
    IValidator<GoogleAuthorizationCodeRequest> codeValidator,
    IOptions<GoogleAuthenticationOptions> options,
    TimeProvider timeProvider,
    ILogger<GoogleAuthenticationService> logger) : IGoogleAuthenticationService
{
    private const string GoogleAccountNotRegistered =
        "No Google sign-in is available for this account. Create an account with Google or use your existing login method.";
    private const string ExistingAccountRequiresLogin =
        "An account may already use this email. Log in using your existing method.";
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private static readonly Action<ILogger, string, Exception?> GoogleAuthenticationEvent =
        LoggerMessage.Define<string>(LogLevel.Information,
            new EventId(1210, nameof(GoogleAuthenticationEvent)),
            "Google authentication completed with status {Status}.");

    public async Task<AuthenticationResponse> AuthenticateAsync(
        GoogleAuthenticationRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        if (!options.Value.Enabled)
            throw new AuthenticationFlowException(
                "Google authentication is not available.", 503,
                "google_authentication_disabled");

        ValidatedGoogleIdentity identity;
        try
        {
            identity = await credentialValidator.ValidateAsync(
                request.Credential, cancellationToken);
            ValidateIdentity(identity);
        }
        catch (GoogleCredentialValidationException)
        {
            throw InvalidCredential();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _ = exception;
            GoogleAuthenticationEvent(logger, "credential_validation_failed", null);
            throw InvalidCredential();
        }

        return await AuthenticateVerifiedIdentityAsync(
            identity, request.Intent, ipAddress, cancellationToken);
    }

    public async Task<AuthenticationResponse> AuthenticateCodeAsync(
        GoogleAuthorizationCodeRequest request,
        string? origin,
        string? flowHeader,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        await codeValidator.ValidateAndThrowAsync(request, cancellationToken);
        EnsureEnabled();
        if (request.Intent == GoogleAuthenticationIntent.Register && !request.AcceptTerms)
            throw new AuthenticationFlowException(
                "Terms and Privacy consent is required for registration.", 400,
                "terms_acceptance_required");
        if (!string.Equals(flowHeader, "1", StringComparison.Ordinal))
            throw new AuthenticationFlowException(
                "The Google authorization-code request is invalid.", 400,
                "invalid_google_code_request");

        var trustedOrigin = ResolveTrustedOrigin(origin);
        ValidatedGoogleIdentity identity;
        try
        {
            identity = await authorizationCodeExchanger.ExchangeAsync(
                request.Code, trustedOrigin, cancellationToken);
            ValidateIdentity(identity);
        }
        catch (GoogleAuthorizationCodeException exception)
        {
            throw exception.Failure == GoogleAuthorizationCodeFailure.Unavailable
                ? new AuthenticationFlowException(
                    "Google authentication is temporarily unavailable.", 503,
                    "google_authentication_unavailable")
                : InvalidAuthorizationCode();
        }
        catch (GoogleCredentialValidationException)
        {
            throw InvalidAuthorizationCode();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _ = exception;
            GoogleAuthenticationEvent(logger, "code_exchange_failed", null);
            throw new AuthenticationFlowException(
                "Google authentication is temporarily unavailable.", 503,
                "google_authentication_unavailable");
        }

        return await AuthenticateVerifiedIdentityAsync(
            identity, request.Intent, ipAddress, cancellationToken);
    }

    private async Task<AuthenticationResponse> AuthenticateVerifiedIdentityAsync(
        ValidatedGoogleIdentity identity,
        GoogleAuthenticationIntent intent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var subject = identity.Subject.Trim();
        var email = identity.Email.Trim();
        var normalizedEmail = email.ToLowerInvariant();
        var linked = await externalLogins.GetByProviderSubjectAsync(
            ExternalLoginProvider.Google, subject, cancellationToken);

        if (intent == GoogleAuthenticationIntent.Login)
        {
            if (linked is null)
                throw new AuthenticationFlowException(
                    GoogleAccountNotRegistered, 401,
                    "google_account_not_registered");
            return await LoginLinkedAsync(linked, ipAddress, cancellationToken);
        }

        if (linked is not null)
            return await LoginLinkedAsync(linked, ipAddress, cancellationToken);

        if (await users.GetByNormalizedEmailAsync(normalizedEmail, cancellationToken) is not null)
            throw ExistingAccountConflict();

        var (firstName, lastName) = ResolveNames(identity);
        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = null,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = null,
            NormalizedPhoneNumber = null,
            PhoneConfirmed = false,
            EmailConfirmed = true,
            TermsAndPrivacyAcceptedAtUtc = UtcNow,
            TermsAndPrivacyVersion = LegalDocumentCatalog.CurrentVersion,
            Status = UserStatus.Active,
            LastLoginAtUtc = UtcNow,
            RoleId = SystemRoleIds.Candidate,
            Role = CandidateRole()
        };
        var externalLogin = new UserExternalLogin
        {
            UserId = user.Id,
            User = user,
            Provider = ExternalLoginProvider.Google,
            ProviderSubject = subject,
            ProviderEmail = email,
            LastLoginAtUtc = UtcNow
        };
        await users.AddAsync(user, cancellationToken);
        await externalLogins.AddAsync(externalLogin, cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Create, "User", user.Id.ToString(),
            new Dictionary<string, string?> { ["source"] = "googleRegistration" },
            new(user.Id, "Candidate")), cancellationToken);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            unitOfWork.ResetAfterFailure();
            linked = await externalLogins.GetByProviderSubjectAsync(
                ExternalLoginProvider.Google, subject, cancellationToken);
            if (linked is not null)
                return await LoginLinkedAsync(linked, ipAddress, cancellationToken);
            throw ExistingAccountConflict();
        }
        GoogleAuthenticationEvent(logger, "registered", null);
        return response;
    }

    private async Task<AuthenticationResponse> LoginLinkedAsync(
        UserExternalLogin externalLogin,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = externalLogin.User;
        if (user.RoleId != SystemRoleIds.Candidate || user.Status != UserStatus.Active)
            throw new AuthenticationFlowException(
                "This candidate account is not available.", 403,
                "candidate_account_unavailable");
        externalLogin.LastLoginAtUtc = UtcNow;
        user.LastLoginAtUtc = UtcNow;
        externalLogins.Update(externalLogin);
        users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Login, "User", user.Id.ToString(),
            new Dictionary<string, string?> { ["source"] = "google" },
            new(user.Id, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        GoogleAuthenticationEvent(logger, "authenticated", null);
        return response;
    }

    private async Task<AuthenticationResponse> IssueTokensAsync(
        User user, string? ipAddress, CancellationToken cancellationToken)
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
        return new(accessToken.Token, accessToken.ExpiresAtUtc, rawRefreshToken,
            refreshToken.ExpiresAtUtc,
            new(user.Id, user.Email, user.FirstName, user.LastName, user.Role.Name));
    }

    private static void ValidateIdentity(ValidatedGoogleIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Subject) || identity.Subject.Trim().Length > 255 ||
            string.IsNullOrWhiteSpace(identity.Email) || identity.Email.Trim().Length > 256 ||
            !identity.EmailVerified)
            throw new GoogleCredentialValidationException();
    }

    private static (string FirstName, string LastName) ResolveNames(
        ValidatedGoogleIdentity identity)
    {
        var combined = $"{identity.GivenName} {identity.FamilyName}".Trim();
        if (PersonalName.TrySplit(combined, out var first, out var last)) return (first, last);
        if (PersonalName.TrySplit(identity.Name, out first, out last)) return (first, last);
        return ("Career", "Harbor Candidate");
    }

    private static Role CandidateRole() => new()
    {
        Id = SystemRoleIds.Candidate,
        Name = "Candidate",
        NormalizedName = "CANDIDATE"
    };

    private static AuthenticationFlowException InvalidCredential() => new(
        "The Google credential is invalid or expired.", 401,
        "invalid_google_credential");
    private static AuthenticationFlowException InvalidAuthorizationCode() => new(
        "The Google authorization code is invalid or expired.", 401,
        "invalid_google_authorization_code");
    private void EnsureEnabled()
    {
        if (!options.Value.Enabled)
            throw new AuthenticationFlowException(
                "Google authentication is not available.", 503,
                "google_authentication_disabled");
    }

    private string ResolveTrustedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            !options.Value.AllowedCodeOrigins.Any(allowed =>
                string.Equals(allowed, origin, StringComparison.Ordinal)))
            throw new AuthenticationFlowException(
                "This origin is not allowed for Google authentication.", 403,
                "google_origin_not_allowed");
        return origin;
    }
    private static ConflictException ExistingAccountConflict() => new(
        ExistingAccountRequiresLogin, "existing_account_requires_login");
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
