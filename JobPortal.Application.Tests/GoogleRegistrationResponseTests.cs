using System.Text.Json;
using JobPortal.API.Middleware;
using JobPortal.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GoogleRegistrationResponseTests
{
    [Fact]
    public async Task GoogleConflictUsesExistingApiErrorEnvelopeWithLoginMethod()
    {
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new GoogleRegistrationConflictException(), NullLogger<GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(409, context.Response.StatusCode);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal("ACCOUNT_EXISTS_GOOGLE", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("google", json.RootElement.GetProperty("loginMethod").GetString());
        Assert.Equal("This email is already registered with Google. Please continue with Google to sign in.",
            json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void OrdinaryErrorsDoNotGainAnAuthenticationMethodProperty()
    {
        var json = JsonSerializer.Serialize(new JobPortal.Shared.Models.ApiError("unauthorized", "Invalid identifier or password."),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("loginMethod", json, StringComparison.Ordinal);
    }
}
