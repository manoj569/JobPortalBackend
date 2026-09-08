using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyWorkerIdentityTests
{
    private static readonly Guid Nonce = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Fact]
    public void ShortIdentifierProducesNonEmptyDiagnosticIdentity()
    {
        var identity = Create("host");

        Assert.StartsWith("host-42-", identity.Id, StringComparison.Ordinal);
        Assert.NotEmpty(identity.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrEmptyIdentifierUsesSafeFallback(string? identifier)
    {
        var identity = Create(identifier);

        Assert.StartsWith("worker-42-", identity.Id, StringComparison.Ordinal);
        Assert.NotEmpty(identity.Id);
    }

    [Fact]
    public void IdentifierShorterThanTruncationLengthIsNotOverread()
    {
        const string identifier = "short-container-id";

        var identity = Create(identifier);

        Assert.StartsWith($"{identifier}-42-", identity.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentifierExactlyAtTruncationLengthIsPreserved()
    {
        var identifier = new string('a', 32);

        var identity = Create(identifier);

        Assert.StartsWith($"{identifier}-42-", identity.Id, StringComparison.Ordinal);
        Assert.True(identity.Id.Length <= 80);
    }

    [Fact]
    public void LongIdentifierReproducingRenderHostnameShapeIsSafelyTruncated()
    {
        var identifier = new string('b', 64);

        var exception = Record.Exception(() => Create(identifier));

        Assert.Null(exception);
        var identity = Create(identifier);
        Assert.StartsWith($"{new string('b', 32)}-42-", identity.Id, StringComparison.Ordinal);
        Assert.InRange(identity.Id.Length, 1, 80);
    }

    [Fact]
    public void DifferentInstanceIdentifiersRemainDistinguishable()
    {
        var first = Create("render-instance-a");
        var second = Create("render-instance-b");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void DependencyInjectionResolvesOneStableIdentityForTheProcessLifetime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAIApplyWorkerIdentity, AIApplyWorkerIdentity>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAIApplyWorkerIdentity>();
        var second = provider.GetRequiredService<IAIApplyWorkerIdentity>();

        Assert.Same(first, second);
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
    }

    private static AIApplyWorkerIdentity Create(string? identifier) =>
        new(identifier, 42, Nonce);
}
