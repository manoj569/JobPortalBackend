using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Features.Authentication;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Authentication;

public sealed class GoogleAuthorizationCodeExchanger(
    IHttpClientFactory httpClientFactory,
    IGoogleCredentialValidator credentialValidator,
    IOptions<GoogleAuthenticationOptions> options) : IGoogleAuthorizationCodeExchanger
{
    public const string HttpClientName = "GoogleOAuth";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ValidatedGoogleIdentity> ExchangeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = options.Value.ClientId,
                ["client_secret"] = options.Value.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            });
            using var response = await httpClientFactory.CreateClient(HttpClientName)
                .PostAsync("token", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new GoogleAuthorizationCodeException(
                    response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                        ? GoogleAuthorizationCodeFailure.Invalid
                        : GoogleAuthorizationCodeFailure.Unavailable);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions);
            if (string.IsNullOrWhiteSpace(token?.IdToken))
                throw new GoogleAuthorizationCodeException(GoogleAuthorizationCodeFailure.Invalid);

            // Google access and refresh tokens are deliberately neither modeled nor retained.
            return await credentialValidator.ValidateAsync(token.IdToken, cancellationToken);
        }
        catch (GoogleAuthorizationCodeException)
        {
            throw;
        }
        catch (GoogleCredentialValidationException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GoogleAuthorizationCodeException(
                GoogleAuthorizationCodeFailure.Unavailable, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new GoogleAuthorizationCodeException(
                GoogleAuthorizationCodeFailure.Unavailable, exception);
        }
        catch (JsonException exception)
        {
            throw new GoogleAuthorizationCodeException(
                GoogleAuthorizationCodeFailure.Unavailable, exception);
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("id_token")] string? IdToken);
}
