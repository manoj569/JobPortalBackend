using System.Net;
using System.Net.Http.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.EmployerDirectIntegrationTests;

namespace JobPortal.Application.Tests;

public sealed class GenericEmployerContractTests
{
    [Theory]
    [InlineData(null, GenericEmployerContract.Identifier)]
    [InlineData("employer-direct", GenericEmployerContract.Identifier)]
    [InlineData(" employer-direct ", GenericEmployerContract.Identifier)]
    [InlineData(GenericEmployerContract.Identifier, GenericEmployerContract.Identifier)]
    [InlineData("legacy-custom", "legacy-custom")]
    [InlineData(EmployerDirectInventoryContract.Identifier, EmployerDirectInventoryContract.Identifier)]
    public async Task CreateAndUpdateNormalizeOnlyGenericIdentifiers(string? identifier, string expected)
    {
        using var f = new JobSourceFixture();
        var request = f.Request with { AtsType = AtsType.Custom, AtsIdentifier = identifier };
        var created = await f.Service.CreateAsync(request);
        Assert.Equal(expected, created.AtsIdentifier);
        var updated = await f.Service.UpdateAsync(created.Id, request);
        Assert.Equal(expected, updated.AtsIdentifier);
    }

    [Theory]
    [InlineData(GenericEmployerContract.LegacyIdentifier)]
    [InlineData(GenericEmployerContract.Identifier)]
    public async Task ExistingSourceExecutesThroughProviderAndWorker(string identifier)
    {
        var source = new JobSource { AtsType = AtsType.Custom, AtsIdentifier = identifier,
            CompanyId = Guid.NewGuid(), CareerPageUrl = "https://example.com/careers" };
        var run = new JobSourceRun { JobSourceId = source.Id };
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)run.Id);
        var store = Stub.Create<IJobSourceRunStore>((_, _) => Task.FromResult<JobSourceRun?>(run));
        var handler = new ObservationHandler(source, run.Id);
        var factory = Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false));
        var scanner = new GenericEmployerScanner(factory, Options.Create(new EmployerAcquisitionOptions
            { Endpoint = "https://worker.example.com", Token = new string('t', 32) }), checkpoint);
        var provider = new EmployerDirectSnapshotProvider(store, checkpoint, scanner);
        var snapshot = await provider.FetchSnapshotAsync(source);
        Assert.True(handler.Called);
        Assert.False(snapshot.IsComplete);
        Assert.Empty(snapshot.Jobs);
    }

    [Theory]
    [InlineData("legacy-custom", AtsType.Custom)]
    [InlineData(GenericEmployerContract.Identifier, AtsType.Greenhouse)]
    public async Task ScannerRejectsUnrelatedSourcesBeforeHttp(string identifier, AtsType type)
    {
        var factory = Stub.Create<IHttpClientFactory>((_, _) => throw new Xunit.Sdk.XunitException("Unexpected HTTP"));
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)Guid.NewGuid());
        var scanner = new GenericEmployerScanner(factory, Options.Create(new EmployerAcquisitionOptions()), checkpoint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(
            new JobSource { AtsType = type, AtsIdentifier = identifier }, default));
    }

    [Theory]
    [InlineData(GenericEmployerContract.LegacyIdentifier, true)]
    [InlineData(GenericEmployerContract.Identifier, true)]
    [InlineData(EmployerDirectInventoryContract.Identifier, true)]
    [InlineData("legacy-custom", false)]
    public async Task SchedulerIncludesOnlyExecutableCustomSources(string identifier, bool expected)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Custom;
        f.Source.AtsIdentifier = identifier;
        await f.Context.SaveChangesAsync();
        Assert.Equal(expected, (await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25)).Count > 0);
    }

    [Fact]
    public async Task UnrelatedCustomIdentifierCannotFallThroughToScanner()
    {
        var run = new JobSourceRun();
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)run.Id);
        var store = Stub.Create<IJobSourceRunStore>((_, _) => Task.FromResult<JobSourceRun?>(run));
        var scanner = Stub.Create<IEmployerDirectInventoryScanner>((_, _) => throw new Xunit.Sdk.XunitException("Unexpected scan"));
        var provider = new EmployerDirectSnapshotProvider(store, checkpoint, scanner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchSnapshotAsync(
            new JobSource { AtsType = AtsType.Custom, AtsIdentifier = "legacy-custom" }));
    }

    [Fact]
    public async Task CanonicalCreateCannotDuplicateExistingAlias()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Custom;
        f.Source.AtsIdentifier = GenericEmployerContract.LegacyIdentifier;
        await f.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<JobPortal.Application.Common.Exceptions.ConflictException>(() =>
            f.Service.CreateAsync(f.Request with { AtsType = AtsType.Custom, AtsIdentifier = GenericEmployerContract.LegacyIdentifier }));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WorkerCannotChangeSourceBindingOrClaimUnprovenCompleteness(bool wrongSource, bool complete)
    {
        var source = new JobSource { AtsType = AtsType.Custom, AtsIdentifier = GenericEmployerContract.LegacyIdentifier,
            CareerPageUrl = "https://example.com/careers" };
        var runId = Guid.NewGuid();
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)runId);
        var handler = new ObservationHandler(source, runId, wrongSource, complete);
        var factory = Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false));
        var scanner = new GenericEmployerScanner(factory, Options.Create(new EmployerAcquisitionOptions
            { Endpoint = "https://worker.example.com", Token = new string('t', 32) }), checkpoint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(source, default));
        Assert.True(handler.Called);
    }

    private sealed class ObservationHandler(JobSource source, Guid runId, bool wrongSource = false, bool complete = false) : HttpMessageHandler
    {
        public bool Called { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Called = true;
            Assert.Equal("https://worker.example.com/employer/scan", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                runId, sourceId = wrongSource ? Guid.NewGuid() : source.Id, source.CompanyId, source.CareerPageUrl,
                observedAtUtc = DateTimeOffset.UtcNow, isComplete = complete, completionReason = "INCOMPLETE",
                status = "partial", jobs = Array.Empty<object>(), providerRoutes = Array.Empty<object>(),
                strategy = new { }, metrics = new { }, errors = Array.Empty<object>()
            }) });
        }
    }
}

