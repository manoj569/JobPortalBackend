using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class WorkdayBatchIngestionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DuplicatePathsAndPlaceholdersAreSkippedButNeverCertifyCompleteness()
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json("""{"total":4,"jobPostings":[{"title":"Engineer","externalPath":"/job/Pune/Engineer_1"},{"title":"Engineer","externalPath":"/job/Pune/Engineer_1"},{"bulletFields":["placeholder"]},{"title":"Engineer 2","externalPath":"/job/Pune/Engineer_2"}]}""")
            : Json(Detail(request)))));
        var snapshot = await Provider(http).FetchSnapshotAsync(Source());
        Assert.False(snapshot.IsComplete);
        Assert.Equal(2, snapshot.Jobs.Count);
        Assert.All(snapshot.Jobs, x => Assert.Contains("IN", x.CountryCodes));
    }

    [Fact]
    public async Task RepeatedPageStopsPaginationAndRetainsValidJobsWithoutCompleteness()
    {
        var posts = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Task.FromResult(Json(Listing(40, 0, 20))); }
            return Task.FromResult(Json(Detail(request)));
        }));
        var snapshot = await Provider(http).FetchSnapshotAsync(Source());
        Assert.Equal(2, posts);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(20, snapshot.Jobs.Count);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task FailedDetailIsIsolatedAndValidJobsArePersisted(int status)
    {
        using var f = Fixture();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(3, 0, 3))
            : request.RequestUri!.AbsolutePath.EndsWith("_1", StringComparison.Ordinal)
                ? new HttpResponseMessage((HttpStatusCode)status) : Json(Detail(request)))));
        var result = await Runner(f, Provider(http)).RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Closed);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        Assert.Null((await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc);
    }

    [Theory]
    [InlineData(21, true)]
    [InlineData(2000, false)]
    [InlineData(2021, true)]
    [InlineData(3879, true)]
    public async Task CompletePaginationKeepsOnlySuspectedSearchCapIncomplete(int total, bool expectedComplete)
    {
        var offsets = new List<int>();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                offsets.Add(offset);
                return Json(Listing(total, offset, Math.Min(20, total - offset)));
            }
            return Json(Detail(request));
        }));
        var source = Source();
        if (total == 3879)
        {
            source.AtsIdentifier = "pwc/Global_Experienced_Careers";
            source.CareerPageUrl = "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers";
            source.Company!.Name = "PwC";
        }
        var result = await Provider(http, budget: 120).FetchSnapshotAsync(source);
        Assert.Equal(Enumerable.Range(0, (total + 19) / 20).Select(x => x * 20), offsets);
        Assert.Equal(total, result.Jobs.Count);
        Assert.Equal(expectedComplete, result.IsComplete);
        Assert.Equal(total, result.Jobs.Select(x => x.ExternalId).Distinct().Count());
    }

    [Fact]
    public async Task ChangedAdvertisedTotalCannotCertifyACompleteSnapshot()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                return Json(Listing(offset == 0 ? 40 : 0, offset, 20));
            }
            return Json(Detail(request));
        }));
        var snapshot = await Provider(http).FetchSnapshotAsync(Source());
        Assert.Equal(40, snapshot.Jobs.Count);
        Assert.False(snapshot.IsComplete);
    }

    [Fact]
    public async Task ShortListingBeforeAdvertisedTotalFailsClosed()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Listing(40, 0, 10)))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).FetchSnapshotAsync(Source()));
    }

    [Fact]
    public async Task DetailTimeoutDoesNotCancelOtherDetails()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) return Json(Listing(2, 0, 2));
            if (request.RequestUri!.AbsolutePath.EndsWith("_0", StringComparison.Ordinal))
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(Detail(request));
        }));
        var result = await Provider(http).FetchSnapshotAsync(Source());
        Assert.Single(result.Jobs);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task ListingPostRetries429AndRespectsRetryAfter()
    {
        var requests = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan first = default;
        TimeSpan second = default;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (++requests == 1)
            {
                first = timer.Elapsed;
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
                return Task.FromResult(response);
            }
            second = timer.Elapsed;
            return Task.FromResult(Json(Listing(0, 0, 0)));
        }));
        var result = await Provider(http, attempts: 2).FetchSnapshotAsync(Source());
        Assert.Equal(2, requests);
        Assert.True(second - first >= TimeSpan.FromSeconds(2));
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task PermanentListingFailureIsNotRetried()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }));
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http, attempts: 3).FetchSnapshotAsync(Source()));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task LongRetryAfterBlocksSubsequentDetailsAndNewProviderScopeForSameHost()
    {
        var requests = 0;
        var state = new AggregationHttpRetryState();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Json(Listing(3, 0, 3)));
            requests++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }));
        var settings = Options.Create(new JobAggregationOptions { Workday = new() { DetailConcurrency = 1, BatchSize = 1 } });
        var provider = new WorkdayJobSourceProvider(new Factory(http), options: settings, retryState: state);
        var result = await provider.FetchSnapshotAsync(Source());
        Assert.Equal(1, requests);
        Assert.Equal(3, result.Skipped);
        Assert.False(result.IsComplete);
        var nextScope = new WorkdayJobSourceProvider(new Factory(http), options: settings, retryState: state);
        await Assert.ThrowsAsync<HttpRequestException>(() => nextScope.FetchSnapshotAsync(Source()));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"jobPostingInfo\":{\"title\":\"Engineer\"}}")]
    public async Task InvalidDetailCannotFabricateADescription(string invalid)
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(1, 0, 1)) : Json(invalid))));
        var result = await Provider(http).FetchSnapshotAsync(Source());
        Assert.Empty(result.Jobs);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task CancelledAfterCommittedBatchResumesFromDurableIdentitiesWithoutDuplicates()
    {
        using var f = Fixture();
        using var cancellation = new CancellationTokenSource();
        var firstCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = true;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) return Json(Listing(4, 0, 4));
            if (cancel && request.RequestUri!.AbsolutePath.EndsWith("_2", StringComparison.Ordinal))
            {
                await firstCommit.Task.WaitAsync(ct);
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return Json(Detail(request));
        }));
        var runner = Runner(f, Provider(http), unitOverride: new CommitSignalUnit(f, firstCommit));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(f.Source.Id, cancellation.Token));
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        Assert.Null((await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc);
        cancel = false;
        // A fresh runner/ingestion scope has no old in-memory progress; only committed identities matter.
        var resumed = await Runner(f, Provider(http)).RunAsync(f.Source.Id);
        Assert.True(resumed.Succeeded);
        Assert.Equal(2, resumed.Created);
        Assert.Equal(2, resumed.Unchanged);
        Assert.Equal(4, await f.Context.Jobs.CountAsync());
        Assert.Equal(4, await f.Context.Jobs.Select(x => x.ExternalJobId).Distinct().CountAsync());
    }

    [Fact]
    public async Task RunBudgetStopsPromptlyAndRetainsCommittedBatchCounters()
    {
        using var f = Fixture();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) return Json(Listing(3, 0, 3));
            if (request.RequestUri!.AbsolutePath.EndsWith("_2", StringComparison.Ordinal))
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(Detail(request));
        }));
        var result = await Runner(f, Provider(http, budget: 1)).RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        Assert.Equal(0, result.Closed);
    }

    [Fact]
    public async Task TestCapIsGlobalAcrossBatchesAndNeverClosesStaleJobs()
    {
        using var f = Fixture();
        var settings = new JobAggregationOptions();
        settings.SourceApprovals[f.Source.Id.ToString("D")] = new() { TestImportLimit = 1 };
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(4, 0, 4)) : Json(Detail(request)))));
        var result = await Runner(f, Provider(http), new JobSourcePublicationPolicy(Options.Create(settings), TimeProvider.System)).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Closed);
        Assert.Single(await f.Context.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task SuccessfulBatchesReachExistingQualityGateWithoutInventingMissingPublicationFields()
    {
        using var f = Fixture();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(3, 0, 3)) : Json(Detail(request)))));
        var runner = Runner(f, Provider(http), publish: true);
        var result = await runner.RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Published);
        Assert.Equal(3, result.NeedsReview);
        Assert.Equal(3, await f.Context.Jobs.CountAsync(x => x.Status == JobStatus.Draft));
        var again = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, again.Created);
        Assert.Equal(0, again.Published);
        Assert.Equal(3, again.Unchanged);
    }

    [Fact]
    public async Task OtherWorkdayTenantsReceiveNoAccentureFacetOrGenericRemoteIndiaClaim()
    {
        var source = Source();
        source.AtsIdentifier = "example/Careers";
        source.CareerPageUrl = "https://example.wd5.myworkdayjobs.com/Careers";
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Empty(body.RootElement.GetProperty("appliedFacets").EnumerateObject());
                return Json(Listing(1, 0, 1));
            }
            return Json("""{"jobPostingInfo":{"title":"Engineer","jobReqId":"1","jobDescription":"Build software","location":"Remote","remoteType":"remote"}}""");
        }));
        var result = await Provider(http).FetchSnapshotAsync(source);
        Assert.Empty(Assert.Single(result.Jobs).CountryCodes);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task DetailTransientRetryRecoversInsteadOfDroppingTheJob(int status)
    {
        var attempts = 0;
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(1, 0, 1)) : ++attempts == 1
                ? new HttpResponseMessage((HttpStatusCode)status) : Json(Detail(request)))));
        var result = await Provider(http, attempts: 2).FetchSnapshotAsync(Source());
        Assert.Equal(2, attempts);
        Assert.Single(result.Jobs);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task ListingRequestsKeepExactAccentureIndiaFacetAndStableOfficialUrls()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("c4f78be1a8f14da0ab49ce1162348a5e",
                    body.RootElement.GetProperty("appliedFacets").GetProperty("locationCountry")[0].GetString());
                return Json(Listing(1, 0, 1));
            }
            return Json(Detail(request));
        }));
        var result = await Provider(http).FetchSnapshotAsync(Source());
        var job = Assert.Single(result.Jobs);
        Assert.Equal("REQ-0", job.ExternalId);
        Assert.Equal("https://accenture.wd103.myworkdayjobs.com/en-US/AccentureCareers/job/Pune/Engineer_0", job.ApplicationUrl);
    }

    [Fact]
    public async Task IncompleteRerunCannotClosePreviouslyImportedMissingJobs()
    {
        using var f = Fixture();
        var incomplete = false;
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(incomplete ? 2 : 3, 0, incomplete ? 2 : 3))
            : incomplete && request.RequestUri!.AbsolutePath.EndsWith("_1", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Gone) : Json(Detail(request)))));
        var runner = Runner(f, Provider(http));
        Assert.True((await runner.RunAsync(f.Source.Id)).Succeeded);
        var success = (await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc;
        incomplete = true;
        var result = await runner.RunAsync(f.Source.Id);
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Closed);
        Assert.Equal(3, await f.Context.Jobs.CountAsync());
        Assert.Equal(0, await f.Context.Jobs.CountAsync(x => x.Status == JobStatus.Closed));
        Assert.Equal(success, (await f.Repository.GetByIdAsync(f.Source.Id))!.LastSuccessfulRunAtUtc);
    }

    [Fact]
    public async Task ContradictoryForeignDetailDoesNotGainIndiaEligibilityFromFacet()
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(1, 0, 1))
            : Json("""{"jobPostingInfo":{"title":"Engineer","jobReqId":"1","jobDescription":"Build software","country":{"alpha2Code":"US"},"location":"Remote"}}"""))));
        var result = await Provider(http).FetchSnapshotAsync(Source());
        Assert.Empty(result.Jobs);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task ListingTimeoutIsBoundedAndHasTimeoutRatherThanCallerCancellationCategory()
    {
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }));
        await Assert.ThrowsAsync<TimeoutException>(() => Provider(http).FetchSnapshotAsync(Source()));
    }

    [Fact]
    public async Task BatchesWithExplicitCompletePublicationFactsArePersistedAndPublished()
    {
        using var f = Fixture();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(Listing(3, 0, 3)) : Json(Detail(request)))));
        var runner = Runner(f, new ExplicitPublicationFixture(Provider(http)), publish: true);
        var result = await runner.RunAsync(f.Source.Id);
        Assert.Equal(3, result.Published);
        Assert.Equal(3, await f.Context.Jobs.CountAsync(x => x.Status == JobStatus.Published));
        Assert.Equal(0, (await runner.RunAsync(f.Source.Id)).Published);
    }

    // Supplies explicitly known fixture facts to test the unchanged publication
    // pipeline. Production Workday must not invent expiry/experience to pass it.
    private sealed class ExplicitPublicationFixture(WorkdayJobSourceProvider inner) : IBatchedExternalJobProvider
    {
        private readonly DateTime expiry = DateTime.UtcNow.AddDays(10);
        public AtsType AtsType => AtsType.Workday;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var batch in inner.FetchBatchesAsync(source, cancellationToken))
                yield return batch with { Jobs = batch.Jobs.Select(x => x with { ExpiresAtUtc = expiry, ExperienceLevel = ExperienceLevel.Entry }).ToArray() };
        }
    }

    private static JobSource Source() => new()
    {
        AtsType = AtsType.Workday, AtsIdentifier = "accenture/AccentureCareers",
        CareerPageUrl = "https://accenture.wd103.myworkdayjobs.com/AccentureCareers",
        Company = new Company { Name = "Accenture" }
    };

    private static JobSourceFixture Fixture()
    {
        var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Workday;
        f.Source.AtsIdentifier = Source().AtsIdentifier;
        f.Source.CareerPageUrl = Source().CareerPageUrl;
        f.Map(f.Category.Id.ToString());
        f.Context.SaveChanges();
        return f;
    }

    private static JobSourceRunner Runner(JobSourceFixture f, IExternalJobProvider provider,
        IJobSourcePublicationPolicy? policy = null, bool publish = false, JobPortal.Application.Abstractions.Persistence.IUnitOfWork? unitOverride = null,
        IExternalJobMetadataEnricher? enrichment = null)
    {
        var jobs = new JobRepository(f.Context);
        var unit = unitOverride ?? new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var jobService = new JobService(jobs, unit, f.Audit, new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System, sources: f.Repository);
        var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), jobService,
            Options.Create(new JobAggregationOptions { AutoPublishEnabled = publish }), TimeProvider.System);
        return new(f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            autoPublishService: publisher, jobRepository: jobs, publicationPolicy: policy, enricher: enrichment);
    }

    private static WorkdayJobSourceProvider Provider(HttpClient client, int attempts = 1, int budget = 60) =>
        new(new Factory(client), options: Options.Create(new JobAggregationOptions
        { Workday = new() { BatchSize = 2, MaximumAttempts = attempts, RequestTimeoutSeconds = 1, RunBudgetSeconds = budget } }), timeProvider: new FastClock());

    private static string Listing(int total, int start, int count) => JsonSerializer.Serialize(new
    {
        total, jobPostings = Enumerable.Range(start, count).Select(i => new
        { title = $"Engineer {i}", externalPath = $"/job/Pune/Engineer_{i}", locationsText = "Pune" })
    });

    private static string Detail(HttpRequestMessage request)
    {
        var id = request.RequestUri!.AbsolutePath.Split('_').Last();
        return JsonSerializer.Serialize(new { jobPostingInfo = new { title = $"Engineer {id}", jobReqId = $"REQ-{id}",
            jobDescription = $"<p>Build reliable software for product {id}, collaborating with the engineering team and delivering high quality code.</p>",
            location = "Pune, India", timeType = "Full time", remoteType = "On-site", startDate = DateTime.UtcNow.ToString("yyyy-MM-dd") } });
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class CommitSignalUnit(JobSourceFixture fixture, TaskCompletionSource signal)
        : JobPortal.Application.Abstractions.Persistence.IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var result = await fixture.Context.SaveChangesAsync(cancellationToken);
            if (fixture.Context.ChangeTracker.Entries<Job>().Any()) signal.TrySetResult();
            return result;
        }
        public void ResetAfterFailure() => fixture.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task DetailFetchOverlapsPausedConsumerAndCancellationJoinsProducer()
    {
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Json(Listing(6, 0, 6)));
            if (request.RequestUri!.AbsolutePath.EndsWith("_2", StringComparison.Ordinal)) second.TrySetResult();
            return Task.FromResult(Json(Detail(request)));
        }));
        await using var enumerator = Provider(http).FetchBatchesAsync(Source()).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Jobs.Count);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Disposing a stopped consumer must cancel and await the bounded producer,
        // including when it is waiting for channel capacity.
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public async Task ConfiguredDetailConcurrencyIsBoundedAndMeasurable(int concurrency)
    {
        var active = 0;
        var peak = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                return Json(Listing(40, offset, 20));
            }
            var current = Interlocked.Increment(ref active);
            int observed;
            do { observed = Volatile.Read(ref peak); }
            while (current > observed && Interlocked.CompareExchange(ref peak, current, observed) != observed);
            try { await Task.Delay(25, token); return Json(Detail(request)); }
            finally { Interlocked.Decrement(ref active); }
        }));
        var provider = new WorkdayJobSourceProvider(new Factory(http), options: Options.Create(new JobAggregationOptions
        { Workday = new() { DetailConcurrency = concurrency, BatchSize = 50, MaximumAttempts = 1 } }), timeProvider: new FastClock());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await provider.FetchSnapshotAsync(Source());
        Assert.True(snapshot.IsComplete);
        Assert.Equal(40, snapshot.Jobs.Count);
        Assert.InRange(peak, 2, concurrency);
        Assert.Equal(0, active);
        output.WriteLine($"MOCK DETAIL concurrency={concurrency} peak={peak} elapsedMs={watch.ElapsedMilliseconds}");
    }

    [Theory]
    [InlineData(500, 8)]
    [InlineData(500, 12)]
    [InlineData(500, 16)]
    [InlineData(1000, 8)]
    [InlineData(1000, 12)]
    [InlineData(1000, 16)]
    public async Task MeasureSyntheticWorkdayPipelineWithRealRepositories(int count, int concurrency)
    {
        using var f = Fixture();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var offset = body.RootElement.GetProperty("offset").GetInt32();
                return Json(Listing(count, offset, Math.Min(20, count - offset)));
            }
            await Task.Delay(25, token);
            return Json(Detail(request));
        }));
        var provider = new WorkdayJobSourceProvider(new Factory(http), options: Options.Create(new JobAggregationOptions
        { Workday = new() { DetailConcurrency = concurrency, BatchSize = 100, MaximumAttempts = 1 } }), timeProvider: new FastClock());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await Runner(f, provider, publish: true, enrichment: new ExternalJobMetadataEnricher()).RunAsync(f.Source.Id);
        output.WriteLine($"SYNTHETIC PIPELINE jobs={count} concurrency={concurrency} batch=100 elapsedMs={watch.ElapsedMilliseconds} " +
            $"created={result.Created} updated={result.Updated} failed={result.Failed} needsReview={result.NeedsReview} published={result.Published}");
        Assert.True(result.Succeeded);
        Assert.Equal(count, result.Created);
        Assert.Equal(count, result.NeedsReview);
        Assert.Equal(0, result.Published);
        Assert.Equal(count, await f.Context.Jobs.CountAsync());
        Assert.Equal(f.Locks.CreationAcquisitions, f.Locks.CreationReleases);
    }

    [Fact]
    public async Task RetryAttemptsShareTheSameMinimumRequestSpacing()
    {
        var starts = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var attempts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            starts.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());
            if (request.Method == HttpMethod.Post) return Task.FromResult(Json(Listing(3, 0, 3)));
            var attempt = attempts.AddOrUpdate(request.RequestUri!.AbsolutePath, 1, (_, previous) => previous + 1);
            return Task.FromResult(attempt == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(Detail(request)));
        }));
        using var provider = new WorkdayJobSourceProvider(new Factory(http), options: Options.Create(new JobAggregationOptions
        { Workday = new() { BatchSize = 3, DetailConcurrency = 8, MaximumAttempts = 2 } }));
        Assert.True((await provider.FetchSnapshotAsync(Source())).IsComplete);
        var recorded = starts.ToArray();
        Assert.Equal(7, recorded.Length);
        for (var i = 1; i < recorded.Length; i++)
            Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(recorded[i - 1], recorded[i]) >= TimeSpan.FromMilliseconds(220));
    }
    // Advance mocked network timestamps to avoid spending real time on request spacing.
    // Actual timeout timers still run, exercising request/run cancellation deterministically.
    private sealed class FastClock : TimeProvider
    {
        private long seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddSeconds(Interlocked.Increment(ref seconds));
    }
}
