using Google.Apis.Auth;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Features.Authentication;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Authentication;

public sealed class GoogleCredentialValidator(
    IOptions<GoogleAuthenticationOptions> options) : IGoogleCredentialValidator
{
    public async Task<ValidatedGoogleIdentity> ValidateAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [options.Value.ClientId]
            };
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                credential, settings).WaitAsync(cancellationToken);
            return new(payload.Subject ?? string.Empty, payload.Email ?? string.Empty,
                payload.EmailVerified, payload.GivenName, payload.FamilyName, payload.Name);
        }
        catch (Exception exception) when (exception is InvalidJwtException or ArgumentException)
        {
            throw new GoogleCredentialValidationException(
                "Google credential validation failed.", exception);
        }
    }
}
