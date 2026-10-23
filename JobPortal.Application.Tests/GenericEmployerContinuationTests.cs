using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.EmployerDirectIntegrationTests;

namespace JobPortal.Application.Tests;

public sealed class GenericEmployerContinuationTests
{
    [Fact]
    public async Task SitemapSlicesAdvanceFromPersistedSourceStrategyAndLateIndiaJobPublishesOnce()
    {
        using var fixture = new JobSourceFixture();
        fixture.Source.AtsType = AtsType.Custom;
        fixture.Source.AtsIdentifier = GenericEmployerContract.Identifier;
        fixture.Source.AutoPublishEnabled = true;
        await fixture.Context.SaveChangesAsync();

        var first = Enumerable.Range(1, 35).Select(number => UsJob(fixture.Source, number)).ToArray();
        var second = Enumerable.Range(36, 35).Select(number => UsJob(fixture.Source, number)).ToArray();
        var india = IndiaJob(fixture.Source, "eightfold:0071");
        var handler = new ContinuationObservationHandler(fixture.Source,
        [
            new(0, 35, first),
            new(35, 70, second),
            new(70, 105, [india]),
            new(105, 106, [india])
        ]);
        var factory = Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false));
        var publisher = new RecordingPublisher();
        var runId = Guid.NewGuid();
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((name, _) => name switch
        {
            "get_RunId" => (Guid?)runId,
            "get_Enabled" => false,
            "get_State" => null,
            _ => null
        });
        var scanner = new GenericEmployerScanner(factory, Options.Create(new EmployerAcquisitionOptions
        {
            Endpoint = "https://worker.example.test", Token = new string('t', 32)
        }), checkpoint);
        var provider = new EmployerDirectSnapshotProvider(scanner: scanner);
        var ingestion = GenericEmployerContentTests.Ingestion(fixture.Context);
        var runner = new JobSourceRunner(fixture.Repository, [provider], ingestion,
            new UnitOfWork(fixture.Context), TimeProvider.System, fixture.Resolver,
            new ExternalJobNormalizer(), autoPublishService: publisher, checkpoint: checkpoint);

        var firstRun = await runner.RunAsync(fixture.Source.Id);
        Assert.True(firstRun.Succeeded);
        Assert.True(firstRun.ContinuationPending);
        Assert.False(firstRun.ScanComplete);
        Assert.False(firstRun.ReconciliationSafe);
        Assert.Equal("ExecutionSliceComplete", firstRun.StopReason);
        Assert.Equal(35, firstRun.TotalReceived);
        Assert.Equal(0, firstRun.Created);
        Assert.Equal(35, firstRun.SelectionReasonCounts!["NonIndiaLocation"]);
        Assert.Null(fixture.Source.LastError);
        Assert.Equal(0, fixture.Source.ConsecutiveFailures);
        Assert.Empty(await fixture.Context.Jobs.ToListAsync());

        // Rehydrate the source from the database to model a backend process restart.
        var sourceId = fixture.Source.Id;
        fixture.Context.Entry(fixture.Source).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _ = await fixture.Context.JobSources.SingleAsync(source => source.Id == sourceId);

        runId = Guid.NewGuid();
        var secondRun = await runner.RunAsync(sourceId);
        Assert.True(secondRun.Succeeded);
        Assert.True(secondRun.ContinuationPending);
        Assert.False(secondRun.ReconciliationSafe);
        Assert.Equal(35, secondRun.SelectionReasonCounts!["NonIndiaLocation"]);
        Assert.Empty(await fixture.Context.Jobs.ToListAsync());

        fixture.Context.Entry(fixture.Source).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _ = await fixture.Context.JobSources.SingleAsync(source => source.Id == sourceId);
        runId = Guid.NewGuid();
        var indiaRun = await runner.RunAsync(sourceId);
        Assert.True(indiaRun.Succeeded);
        Assert.True(indiaRun.ContinuationPending);
        Assert.False(indiaRun.ReconciliationSafe);
        Assert.Equal(1, indiaRun.Created);
        Assert.Equal(1, indiaRun.Published);
        Assert.Equal(1, publisher.Calls);
        var saved = await fixture.Context.Jobs.SingleAsync(job => job.JobSourceId == sourceId);
        Assert.Equal("eightfold:0071", saved.ExternalJobId);
        Assert.Contains("India", saved.SourceMetadataJson, StringComparison.Ordinal);

        fixture.Context.Entry(fixture.Source).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        _ = await fixture.Context.JobSources.SingleAsync(source => source.Id == sourceId);
        runId = Guid.NewGuid();
        var replay = await runner.RunAsync(sourceId);
        Assert.True(replay.Succeeded);
        Assert.Equal(0, replay.Created);
        Assert.Equal(1, replay.Unchanged);
        Assert.Equal(0, replay.Closed);
        Assert.Single(await fixture.Context.Jobs.Where(job => job.JobSourceId == sourceId).ToListAsync());
        Assert.Equal(new[] { 0, 35, 70, 105 }, handler.RequestedCursors);
        var savedSource = await fixture.Context.JobSources.AsNoTracking().SingleAsync(source => source.Id == sourceId);
        Assert.Equal(106, ContinuationCursor(savedSource.EmployerStrategyJson));

        var dueAt = savedSource.LastSuccessfulRunAtUtc!.Value.AddMinutes(savedSource.ScanIntervalMinutes);
        Assert.True(JobSourceSchedule.DuePredicate(dueAt, 25).Compile()(savedSource));
        Assert.False(JobSourceSchedule.DuePredicate(dueAt.AddMinutes(-1), 25).Compile()(savedSource));
    }

    private static RawExternalJob UsJob(JobSource source, int number) => new()
    {
        ExternalId = $"eightfold:{number:0000}", Title = $"Engineer {number}", CompanyId = source.CompanyId,
        CompanyName = source.Company!.Name, Location = "Austin, Texas", CountryCodes = ["US"],
        ApplicationUrl = $"https://careers.example.test/jobs/{number}",
        DiscoveredUrl = $"https://careers.example.test/careers/job/{number}",
        Description = "Build reliable services with a collaborative engineering team.",
        SourceMetadataJson = "{\"geography\":{\"country\":\"United States\"}}"
    };

    private static RawExternalJob IndiaJob(JobSource source, string externalId) => new()
    {
        ExternalId = externalId, Title = "Software Engineer", CompanyId = source.CompanyId,
        CompanyName = source.Company!.Name, Location = "Bengaluru, India", CountryCodes = ["India"],
        ApplicationUrl = "https://careers.example.test/jobs/0071",
        DiscoveredUrl = "https://careers.example.test/careers/job/0071",
        Description = "Build reliable services with a collaborative engineering team.",
        SourceMetadataJson = "{\"geography\":{\"addresses\":[{\"addressCountry\":\"India\"}]}}"
    };

    private static int ContinuationCursor(string? json)
    {
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.GetProperty("eightfoldSitemapContinuation").GetProperty("cursor").GetInt32();
    }

    private sealed record Slice(int Start, int End, IReadOnlyCollection<RawExternalJob> Jobs);

    private sealed class ContinuationObservationHandler(JobSource source, IEnumerable<Slice> slices) : HttpMessageHandler
    {
        private readonly Queue<Slice> pending = new(slices);
        private int call;
        public List<int> RequestedCursors { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var requestDocument = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = requestDocument.RootElement;
            var payloadSource = root.GetProperty("source");
            var requested = payloadSource.TryGetProperty("strategy", out var strategy) &&
                strategy.ValueKind == JsonValueKind.Object && strategy.TryGetProperty("eightfoldSitemapContinuation", out var state)
                    ? state.GetProperty("cursor").GetInt32() : 0;
            RequestedCursors.Add(requested);
            var slice = pending.Dequeue();
            Assert.Equal(slice.Start, requested);
            var runId = root.GetProperty("runId").GetGuid();
            var count = 1389;
            var strategyResult = new
            {
                detectedStrategy = "EIGHTFOLD_PUBLIC_SEARCH",
                paginationStrategy = "ROBOTS_ADVERTISED_SITEMAP_OBSERVATIONS",
                sitemapAuthority = false,
                eightfoldSitemapContinuation = new
                {
                    version = 1, binding = "fixture-binding-0123456789abcdef",
                    inventoryDigest = "fixture-inventory-0123456789abcdef", count,
                    cursor = slice.End, retryPositions = Array.Empty<int>(), checksum = "fixture-checksum"
                }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    runId, sourceId = source.Id, source.CompanyId, source.CareerPageUrl,
                    observedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(++call),
                    isComplete = false, completionReason = "ENUMERATION_UNPROVEN", status = "OBSERVED",
                    jobs = slice.Jobs, providerRoutes = Array.Empty<object>(), strategy = strategyResult,
                    metrics = new
                    {
                        inventory_expected_count = count, inventory_batch_start = slice.Start,
                        inventory_batch_end = slice.End, inventory_pending_details = count - slice.End
                    },
                    errors = Array.Empty<object>()
                })
            };
        }
    }

    private sealed class RecordingPublisher : IJobAutoPublishService
    {
        public int Calls { get; private set; }
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new JobAutoPublishResult
            {
                Outcome = Calls == 1 ? JobAutoPublishOutcome.Published : JobAutoPublishOutcome.AlreadyPublished
            });
        }
    }
}
