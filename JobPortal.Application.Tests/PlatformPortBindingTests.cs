using JobPortal.API.Startup;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PlatformPortBindingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPortPreservesExistingServerFallback(string? configuredPort)
    {
        Assert.Null(PlatformPortBinding.ResolveUrl(configuredPort));
    }

    [Theory]
    [InlineData("1", "http://0.0.0.0:1")]
    [InlineData("10000", "http://0.0.0.0:10000")]
    [InlineData(" 10000 ", "http://0.0.0.0:10000")]
    [InlineData("65535", "http://0.0.0.0:65535")]
    public void ValidPortBindsHttpToEveryInterface(string configuredPort, string expectedUrl)
    {
        Assert.Equal(expectedUrl, PlatformPortBinding.ResolveUrl(configuredPort));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    [InlineData("10000.0")]
    public void InvalidPortFailsWithActionableConfigurationError(string configuredPort)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PlatformPortBinding.ResolveUrl(configuredPort));

        Assert.Equal("PORT must be an integer between 1 and 65535.", exception.Message);
    }
}
