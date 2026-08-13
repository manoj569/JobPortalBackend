using System.Net;
using System.Text;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Features.Authentication;
using JobPortal.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GoogleAuthorizationCodeExchangerTests
{
    [Fact]
    public async Task ExchangesAgainstGoogleUsingExactTrustedOriginAndValidatesIdTokenOnly()
    {
        const string code = "single-use-code";
        const string idToken = "signed-id-token";
        const string clientSecret = "configured-client-secret";
        var handler = new RecordingHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"access_token":"discard-me","refresh_token":"discard-me-too","id_token":"{{idToken}}"}""",
                Encoding.UTF8, "application/json")
        });
        var validator = new ValidatorFake();
        var exchanger = Create(handler, validator, clientSecret);

        var identity = await exchanger.ExchangeAsync(
            code, "https://careerharbor.in");

        Assert.Equal("subject", identity.Subject);
        Assert.Equal(idToken, validator.ReceivedCredential);
        Assert.Equal("https://oauth2.googleapis.com/token", handler.RequestUri?.ToString());
        var form = handler.Body!;
        Assert.Contains("redirect_uri=https%3A%2F%2Fcareerharbor.in", form, StringComparison.Ordinal);
        Assert.Contains("grant_type=authorization_code", form, StringComparison.Ordinal);
        Assert.Contains("code=single-use-code", form, StringComparison.Ordinal);
        Assert.Contains("client_secret=configured-client-secret", form, StringComparison.Ordinal);
        Assert.DoesNotContain("discard-me", validator.ReceivedCredential, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, GoogleAuthorizationCodeFailure.Invalid)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GoogleAuthorizationCodeFailure.Unavailable)]
    public async Task ProviderFailuresAreMappedWithoutReturningResponseDetails(
        HttpStatusCode status, GoogleAuthorizationCodeFailure expected)
    {
        const string rawProviderError = "provider-secret-error-body";
        var exchanger = Create(new RecordingHandler(_ => new(status)
        {
            Content = new StringContent(rawProviderError)
        }), new ValidatorFake(), "secret");

        var error = await Assert.ThrowsAsync<GoogleAuthorizationCodeException>(() =>
            exchanger.ExchangeAsync("expired-or-replayed-code", "http://localhost:5173"));

        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain(rawProviderError, error.ToString(), StringComparison.Ordinal);
    }

    private static GoogleAuthorizationCodeExchanger Create(
        HttpMessageHandler handler, IGoogleCredentialValidator validator, string secret)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://oauth2.googleapis.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        return new(new Factory(client), validator,
            Options.Create(new GoogleAuthenticationOptions
            {
                Enabled = true,
                ClientId = "client.apps.googleusercontent.com",
                ClientSecret = secret
            }));
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }

    private sealed class ValidatorFake : IGoogleCredentialValidator
    {
        public string? ReceivedCredential { get; private set; }
        public Task<ValidatedGoogleIdentity> ValidateAsync(
            string credential, CancellationToken cancellationToken = default)
        {
            ReceivedCredential = credential;
            return Task.FromResult(new ValidatedGoogleIdentity(
                "subject", "candidate@example.com", true, "Career", "Harbor", "Career Harbor"));
        }
    }
}
