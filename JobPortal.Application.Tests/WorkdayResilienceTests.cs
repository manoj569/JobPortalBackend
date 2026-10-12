using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class WorkdayResilienceTests
{
    [Theory]
    [InlineData(174, 174, 174, false, "Complete", true)]
    [InlineData(174, 174, 174, true, "Complete", true)]
    [InlineData(21, 21, 21, false, "Complete", true)]
    [InlineData(1086, 1085, 1085, true, "ListingTotalDrift", false)]
    [InlineData(1085, 1085, 1086, true, "ListingTotalDrift", false)]
    [InlineData(1086, 1085, 1086, true, "ListingEndedEarly", false)]
    [InlineData(1086, 1050, 1050, true, "ListingTotalDrift", false)]
    [InlineData(1086, 1100, 1100, true, "ListingTotalDrift", false)]
    public async Task CountsAndFinalAnchorsNeverAuthorizeDriftedMembership(int initial, int observed, int final,
        bool omitted, string reason, bool complete)
    {
        using var handler = new Board(initial, observed, final) { OmitContinuationTotal = omitted };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.Equal(observed, snapshot.Jobs.Count);
        Assert.Equal(complete, snapshot.IsComplete);
        var evidence = Assert.IsType<WorkdayListingDiagnostics>(snapshot.ListingDiagnostics);
        Assert.Equal(initial, evidence.InitialReportedTotal);
        Assert.Equal(observed, evidence.RawSlots);
        Assert.Equal(observed, evidence.UniquePaths);
        Assert.Equal(0, evidence.DuplicatePaths);
        Assert.Equal(0, evidence.PlaceholderCount);
        Assert.True(evidence.IngestionSafe);
        Assert.Equal(complete, evidence.ReconciliationSafe);
        Assert.Equal(reason, evidence.ReasonCode);
        Assert.Equal(!complete, evidence.RetryRecommended);
        var anchorRequired = omitted || observed != initial;
        Assert.Equal(anchorRequired ? (int?)final : null, evidence.FinalReportedTotal);
        Assert.Equal((observed + 19) / 20 + (anchorRequired ? 1 : 0), handler.ListingRequests);
        Assert.Equal(observed, handler.DetailRequests);
        Assert.Equal(observed, snapshot.Jobs.Select(job => job.ExternalId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task LiterallyMissingContinuationTotalStillUsesTrustedInitialAnchor()
    {
        using var handler = new Board(41, 41, 41) { MissingContinuationTotal = true };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete);
        Assert.Equal(41, snapshot.Jobs.Count);
        Assert.Equal(4, handler.ListingRequests);
    }

    [Fact]
    public async Task MissingInitialTotalCannotCertifyEvenAnEmptyBoard()
    {
        using var handler = new Board(0, 0, 0) { MissingInitialTotal = true };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
        Assert.Equal(0, handler.DetailRequests);
    }

    [Theory]
    [InlineData("repeated", "RepeatedPage")]
    [InlineData("duplicate", "DuplicateListingPaths")]
    [InlineData("placeholder", "ListingPlaceholders")]
    [InlineData("anchorHttp", "AnchorRevalidationFailed")]
    [InlineData("membership", "ListingMembershipChanged")]
    [InlineData("multipleChanges", "ListingTotalDrift")]
    public async Task StructuralAndAnchorFailuresRemainBoundedAndUnsafe(string mutation, string reason)
    {
        var total = mutation == "repeated" ? 240 : 41;
        using var handler = new Board(total, total, total) { Mutation = mutation, OmitContinuationTotal = true };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete);
        Assert.False(snapshot.ListingDiagnostics!.ReconciliationSafe);
        Assert.Equal(reason, snapshot.ListingDiagnostics.ReasonCode);
        Assert.True(snapshot.ListingDiagnostics.RetryRecommended);
        Assert.InRange(handler.ListingRequests, 2, (total + 19) / 20 + 1);
        if (mutation == "repeated") Assert.Equal(200, snapshot.ListingDiagnostics.LastOffset);
        if (mutation == "duplicate") Assert.Equal(1, snapshot.ListingDiagnostics.DuplicatePaths);
        if (mutation == "placeholder") Assert.Equal(1, snapshot.ListingDiagnostics.PlaceholderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringPaginationOrDetailsStopsWithoutCompleteness(bool details)
    {
        using var cancelled = new CancellationTokenSource();
        using var handler = new Board(41, 41, 41) { Cancel = cancelled, CancelDuringDetails = details };
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchSnapshotAsync(Source(), cancelled.Token));
        Assert.InRange(handler.ListingRequests, 1, 3);
    }

    [Fact]
    public async Task EmptyUnexpectedPageAndMalformedPathsAreRejectedBeforeDetails()
    {
        foreach (var mutation in new[] { "empty", "invalidPath" })
        {
            using var handler = new Board(41, 41, 41) { Mutation = mutation };
            using var http = new HttpClient(handler);
            using var provider = Provider(http);
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.FetchSnapshotAsync(Source()));
            Assert.Equal(0, handler.DetailRequests);
        }
    }

    [Fact]
    public async Task DriftAndFailedDetailsPreservePublishedInventoryAndRepeatWithoutDuplicates()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday;
        f.Source.AtsIdentifier = "fixture/Careers";
        f.Source.CareerPageUrl = "https://fixture.wd1.myworkdayjobs.com/Careers";
        f.Map(f.Category.Id.ToString());
        await f.Context.SaveChangesAsync();
        using var handler = new Board(43, 43, 43);
        using var http = new HttpClient(handler);
        using var provider = Provider(http);
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver,
            new ExternalJobNormalizer(), jobRepository: jobs);
        Assert.True((await runner.RunAsync(f.Source.Id)).Succeeded);
        foreach (var job in f.Context.Jobs) { job.Status = JobStatus.Published; job.PublishedAtUtc = DateTime.UtcNow; }
        await f.Context.SaveChangesAsync();
        var before = await f.Context.Jobs.ToDictionaryAsync(job => job.ExternalJobId!, job => job.Id);
        var successfulAt = f.Source.LastSuccessfulRunAtUtc;
        handler.Reset(43, 42, 42);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            handler.AnchorReads = 0;
            var result = await runner.RunAsync(f.Source.Id);
            Assert.False(result.Succeeded);
            Assert.False(result.ReconciliationSafe);
            Assert.Equal("ListingTotalDrift", result.StopReason);
            Assert.Equal(42, result.Existing);
            Assert.Equal(0, result.Created);
            Assert.Equal(0, result.Closed);
            Assert.Equal(successfulAt, f.Source.LastSuccessfulRunAtUtc);
        }
        handler.Reset(43, 43, 43);
        handler.FailDetail = true;
        var incomplete = await runner.RunAsync(f.Source.Id);
        Assert.False(incomplete.ReconciliationSafe);
        Assert.Equal("FailedDetails", incomplete.StopReason);
        Assert.Equal(0, incomplete.Closed);
        Assert.All(f.Context.Jobs, job => { Assert.Equal(before[job.ExternalJobId!], job.Id); Assert.Equal(JobStatus.Published, job.Status); });
        Assert.Equal(43, await f.Context.Jobs.CountAsync());
        handler.Reset(43, 43, 43);
        handler.Mutation = "duplicate";
        var structural = await runner.RunAsync(f.Source.Id);
        Assert.False(structural.ReconciliationSafe);
        Assert.Equal("DuplicateListingPaths", structural.StopReason);
        Assert.Equal(0, structural.Created);
        Assert.Equal(0, structural.Existing);
        Assert.Equal(0, structural.Closed);
        Assert.False(structural.ListingDiagnostics!.IngestionSafe);
        Assert.All(f.Context.Jobs, job => Assert.Equal(JobStatus.Published, job.Status));
    }

    private static WorkdayJobSourceProvider Provider(HttpClient http) => new(new Factory(http),
        options: Options.Create(new JobAggregationOptions { Workday = new() { MaximumAttempts = 1 } }),
        timeProvider: new FastClock());

    [Fact]
    public async Task LegacyCheckpointsRemainReadableButVersionTwoRequiresTheSafetyRestriction()
    {
        using var f = new JobSourceFixture();
        var checkpoint = new JobSourceRunCheckpoint(f.Context, TimeProvider.System);
        await checkpoint.BindAsync(new JobSourceRun { CheckpointJson = """{"Version":1,"ListingTotal":0,"Cursor":0,"ObservedJobs":[]}""" }, default);
        Assert.Equal(1, checkpoint.State!.Version);
        Assert.Null(checkpoint.State.ListingDiagnostics);
        var invalid = await Assert.ThrowsAsync<JobSourceSnapshotUnsafeException>(() => checkpoint.BindAsync(
            new JobSourceRun { CheckpointJson = """{"Version":2,"ListingTotal":0,"Cursor":0,"ObservedJobs":[]}""" }, default));
        Assert.Equal("InvalidCheckpoint", invalid.ReasonCode);
    }
    private static JobSource Source() => new() { AtsType = AtsType.Workday, AtsIdentifier = "fixture/Careers",
        CareerPageUrl = "https://fixture.wd1.myworkdayjobs.com/Careers", Company = new Company { Name = "Fixture" } };
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class FastClock : TimeProvider
    { private long seconds; public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddSeconds(Interlocked.Increment(ref seconds)); }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private sealed class Board(int initial, int observed, int final) : HttpMessageHandler
    {
        public bool OmitContinuationTotal { get; init; }
        public bool MissingContinuationTotal { get; init; }
        public bool MissingInitialTotal { get; init; }
        public string? Mutation { get; set; }
        public CancellationTokenSource? Cancel { get; init; }
        public bool CancelDuringDetails { get; init; }
        public bool FailDetail { get; set; }
        public int AnchorReads { get; set; }
        public int ListingRequests { get; private set; }
        public int DetailRequests;
        public void Reset(int newInitial, int newObserved, int newFinal)
        { initial = newInitial; observed = newObserved; final = newFinal; AnchorReads = 0; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post)
            {
                ListingRequests++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                if (Cancel is not null && !CancelDuringDetails && offset > 0) { Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
                if (offset == 0) AnchorReads++;
                if (offset == 0 && AnchorReads > 1 && Mutation == "anchorHttp") return new(HttpStatusCode.ServiceUnavailable);
                var start = Mutation == "repeated" && offset == 200 ? 0 : offset;
                var count = Math.Min(20, Math.Max(0, observed - offset));
                if (Mutation == "empty" && offset == 20) count = 0;
                var entries = Enumerable.Range(start, count).Select(i => new
                {
                    title = $"Software Engineer {i}", externalPath = Mutation == "invalidPath" ? "/../invalid" :
                        $"/job/Pune/Engineer_{(Mutation == "duplicate" && i == 20 ? 19 : i)}",
                    locationsText = "Pune, India", bulletFields = new[] { $"R-{i}" }
                }).Cast<object>().ToList();
                if (offset == 0 && AnchorReads > 1 && Mutation == "membership")
                    entries[0] = new { title = "New Engineer", externalPath = "/job/Pune/Engineer_999", locationsText = "Pune, India" };
                if (Mutation == "placeholder" && offset == 20) entries[0] = new { bulletFields = Array.Empty<string>() };
                var document = new Dictionary<string, object> { ["jobPostings"] = entries };
                if (!(offset > 0 && MissingContinuationTotal) && !(offset == 0 && MissingInitialTotal))
                    document["total"] = offset == 0 ? AnchorReads > 1 ? final : initial :
                        Mutation == "multipleChanges" ? initial + 2 : OmitContinuationTotal ? 0 : initial;
                return Json(JsonSerializer.Serialize(document));
            }
            Interlocked.Increment(ref DetailRequests);
            if (Cancel is not null && CancelDuringDetails) { Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            var id = int.Parse(request.RequestUri!.AbsolutePath.Split('_').Last(), CultureInfo.InvariantCulture);
            if (FailDetail && id == 0) return new(HttpStatusCode.ServiceUnavailable);
            return Json(JsonSerializer.Serialize(new { jobPostingInfo = new { title = $"Software Engineer {id}", jobReqId = $"R-{id}",
                location = "Pune, India", timeType = "Full time", country = new { alpha2Code = "IN" },
                jobDescription = $"Build and maintain reliable software product {id}. Minimum 3 years experience required." } }));
        }
    }
}
