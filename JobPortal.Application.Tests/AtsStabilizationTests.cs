using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.API.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class AtsStabilizationTests
{
    [Theory]
    [InlineData("stripe",200,true)]
    [InlineData("airbnb",200,true)]
    [InlineData("stripe",300,true)]
    [InlineData("airbnb",300,true)]
    [InlineData("stripe",500,true)]
    [InlineData("airbnb",500,true)]
    [InlineData("stripe",500,false)]
    [InlineData("airbnb",500,false)]
    public async Task LargeGreenhouseBoardPersistsBeforeLaterDetailsAndCompletes(string board,int total,bool autoPublish)
    {
        using var f = new JobSourceFixture(); Configure(f, AtsType.Greenhouse, board);
        f.Source.AutoPublishEnabled = autoPublish;
        await f.Context.SaveChangesAsync();
        var proof = 0; var details = 0;
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs", StringComparison.Ordinal)) return Json(Greenhouse(board, total));
            var id = int.Parse(request.RequestUri.Segments[^1], System.Globalization.CultureInfo.InvariantCulture);
            Interlocked.Increment(ref details);
            if (id == 26 && Interlocked.CompareExchange(ref proof, 1, 0) == 0) Assert.Equal(25, await f.Context.Jobs.CountAsync(ct));
            return Json(JsonSerializer.Serialize(new { id }));
        });
        using var factory = new Factory(handler);
        var result = await Runner(f, new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.Equal(1, proof); Assert.Equal(total, details);
        Assert.Equal(total, result.Fetched); Assert.Equal(total, result.Parsed);
        Assert.Equal(total, result.Created); Assert.Equal(autoPublish ? total : 0, result.Published);
        Assert.True(result.Created <= result.IndiaEligible);
        Assert.Equal(autoPublish ? 0 : total,result.AutoPublishDisabled); Assert.Equal(autoPublish ? 0 : total,result.NotPublished);
        Assert.True(result.Succeeded); Assert.True(result.ReconciliationSafe);
        Assert.Equal(total, await f.Context.Jobs.CountAsync());
        var again = await Runner(f, new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.Equal(0, again.Created); Assert.Equal(total, again.Existing); Assert.Equal(0, again.Published);
    }

    [Theory]
    [InlineData("airbnb", "https://boards.greenhouse.io/airbnb")]
    [InlineData("stripe", "https://job-boards.greenhouse.io/stripe")]
    [InlineData("enterpret", "https://boards-api.greenhouse.io/v1/boards/enterpret/jobs")]
    public async Task GreenhouseCanonicalUrlsAndIdentifiersFetchOnlyTheirResolvedBoard(string board, string careerUrl)
    {
        var paths = new List<string>();
        using var handler = new Handler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            return Task.FromResult(Json(path.EndsWith("/jobs", StringComparison.Ordinal)
                ? Greenhouse(board, 1) : "{\"id\":1}"));
        });
        using var factory = new Factory(handler);
        var source = new JobSource { Id = Guid.NewGuid(), AtsType = AtsType.Greenhouse,
            CareerPageUrl = careerUrl, AtsIdentifier = board };
        var jobs = await new GreenhouseExternalJobProvider(factory).FetchJobsAsync(source);
        Assert.Single(jobs);
        Assert.Equal(board, JobSourceConfiguration.Resolve(AtsType.Greenhouse, careerUrl, board).Identifier);
        Assert.Equal(2, paths.Count);
        Assert.All(paths, path => Assert.Contains($"/boards/{board}/", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GreenhouseConflictStopsBeforeAnyRequestOrJobInsert()
    {
        using var f = new JobSourceFixture();
        f.Source.CareerPageUrl = "https://boards.greenhouse.io/airbnb";
        f.Source.AtsIdentifier = "enterpret";
        await f.Context.SaveChangesAsync();
        var requests = 0;
        using var handler = new Handler((_, _) => { requests++; return Task.FromResult(Json("{\"jobs\":[]}")); });
        using var factory = new Factory(handler);
        var result = await Runner(f, new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.Equal(0, requests);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
        Assert.Equal("GREENHOUSE_BOARD_CONFIGURATION_CONFLICT", result.FailureReasonCode);
        Assert.False(result.ReconciliationSafe);
    }

    [Fact]
    public async Task ThreeOneHundredJobBatchesCreateEachEligibleJobExactlyOnce()
    {
        using var f = new JobSourceFixture();
        Configure(f, AtsType.Greenhouse, "batch-board");
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        var progress = new RecordingProgress();
        var runner = Runner(f, new ThreeBatchesProvider(), progress);
        var result = await runner.RunAsync(f.Source.Id);

        Assert.Equal(300, result.Fetched);
        Assert.Equal(300, result.Parsed);
        Assert.Equal(300, result.IndiaEligible);
        Assert.Equal(300, result.Created);
        Assert.Equal(0, result.Existing);
        Assert.Equal(300, await f.Context.Jobs.CountAsync());
        Assert.All(progress.Snapshots, item =>
        {
            Assert.Equal(item.Counters.TotalReceived, item.Processed);
            Assert.True(item.Counters.Created <= item.Counters.IndiaEligible);
        });

        var replay = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, replay.Created);
        Assert.Equal(300, replay.Existing);
        Assert.Equal(300, await f.Context.Jobs.CountAsync());
    }

    [Fact]
    public void RunStatusDtoExposesOneCumulativeCounterScope()
    {
        var diagnostics = new JobSourceRunResult
        {
            TotalReceived = 300, Parsed = 300, Created = 100, Updated = 20,
            EligibilityReasonCounts = new Dictionary<string, int> { ["IndiaLocationConfirmed"] = 100 },
            SelectionReasonCounts = new Dictionary<string, int> { ["NonIndiaLocation"] = 180, ["AmbiguousLocation"] = 20 }
        };
        var progress = new JobSourceRunProgress();
        progress.Report(new("Running", 120, diagnostics));
        Assert.Equal(300, progress.Snapshot.Processed);

        var response = JobSourceRunResponse.From(new JobSourceRun
        {
            Id = Guid.NewGuid(), JobSourceId = Guid.NewGuid(), Status = JobSourceRunStatus.Running,
            QueuedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow,
            Processed = progress.Snapshot.Processed, TotalReceived = diagnostics.TotalReceived,
            Created = diagnostics.Created, Updated = diagnostics.Updated,
            DiagnosticsJson = JsonSerializer.Serialize(diagnostics)
        });
        Assert.Equal(response.TotalReceived, response.Processed);
        Assert.Equal(300, response.Fetched);
        Assert.Equal(300, response.Parsed);
        Assert.Equal(100, response.IndiaEligible);
        Assert.Equal(180, response.NonIndiaRejected);
        Assert.Equal(20, response.IndiaEligibilityUnknown);
        Assert.Equal(100, response.Created);
        Assert.True(response.Created <= response.IndiaEligible);
    }

    [Fact]
    public void GreenhouseBoardTokenComparisonPreservesProviderPathCasing()
    {
        var conflict = JobSourceConfiguration.Resolve(AtsType.Greenhouse,
            "https://boards.greenhouse.io/airbnb", "Airbnb");
        Assert.False(conflict.IsValid);
        Assert.Equal("GREENHOUSE_BOARD_CONFIGURATION_CONFLICT", conflict.ReasonCode);
        Assert.True(JobSourceConfiguration.Resolve(AtsType.Greenhouse,
            "https://boards.greenhouse.io/Airbnb", "Airbnb").IsValid);
    }

    [Fact]
    public async Task CancelledGreenhouseRetainsFirst50CommitsAndCannotCloseOldJobs()
    {
        using var f = new JobSourceFixture(); Configure(f, AtsType.Greenhouse, "stripe"); await f.Context.SaveChangesAsync();
        await Runner(f, new SnapshotProvider(AtsType.Greenhouse, [Vacancy(999)])).RunAsync(f.Source.Id);
        var old = await f.Context.Jobs.AsNoTracking().SingleAsync();
        using var stop = new CancellationTokenSource();
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs", StringComparison.Ordinal)) return Json(Greenhouse("stripe", 200));
            var id = int.Parse(request.RequestUri.Segments[^1], System.Globalization.CultureInfo.InvariantCulture);
            if (id > 50) { stop.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            return Json(JsonSerializer.Serialize(new { id }));
        });
        using var factory = new Factory(handler); var progress = new JobSourceRunProgress();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(f, new GreenhouseExternalJobProvider(factory), progress).RunAsync(f.Source.Id, stop.Token));
        Assert.Equal(51, await f.Context.Jobs.CountAsync());
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.AsNoTracking().SingleAsync(job => job.Id == old.Id)).Status);
        Assert.False(progress.Snapshot.Counters.Succeeded); Assert.False(progress.Snapshot.Counters.ReconciliationSafe);
        Assert.Equal("EXECUTION_CANCELLED", progress.Snapshot.Counters.FailureReasonCode);
    }

    [Theory]
    [InlineData("malformed", "PROVIDER_RESPONSE_INVALID")]
    [InlineData("timeout", "PROVIDER_TIMEOUT")]
    [InlineData("missing", "PROVIDER_NOT_FOUND")]
    public async Task GreenhouseIndividualFailureContinuesButNeverReconciles(string failure, string reason)
    {
        using var f = new JobSourceFixture(); Configure(f, AtsType.Greenhouse, "airbnb"); await f.Context.SaveChangesAsync();
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs", StringComparison.Ordinal)) return Task.FromResult(Json(Greenhouse("airbnb", 60)));
            var id = int.Parse(request.RequestUri.Segments[^1], System.Globalization.CultureInfo.InvariantCulture);
            if (id == 30)
            {
                if (failure == "timeout") throw new TaskCanceledException("test timeout with secret data must not be persisted");
                return Task.FromResult(failure == "missing" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json("{invalid"));
            }
            return Task.FromResult(Json(JsonSerializer.Serialize(new { id })));
        });
        using var factory = new Factory(handler);
        var result = await Runner(f, new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.Equal(59, result.Created); Assert.Equal(59, result.Published); Assert.Equal(1, result.ProviderSkipped);
        Assert.False(result.Succeeded); Assert.False(result.ReconciliationSafe); Assert.Equal(0, result.Closed);
        Assert.Equal(1, result.ProviderReasonCounts![reason]);
    }

    [Fact]
    public async Task GreenhouseForeignBoardIsFilteredBeforePersistence()
    {
        using var f = new JobSourceFixture(); Configure(f, AtsType.Greenhouse, "stripe"); await f.Context.SaveChangesAsync();
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs", StringComparison.Ordinal))
                return Task.FromResult(Json(Greenhouse("stripe", 200, "London, UK")));
            var id = int.Parse(request.RequestUri.Segments[^1],System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(Json(JsonSerializer.Serialize(new { id })));
        });
        using var factory = new Factory(handler);
        var result = await Runner(f, new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded); Assert.Equal(200, result.Fetched); Assert.Equal(200, result.NonIndiaRejected);
        Assert.Equal(0, result.Created); Assert.Equal(0, result.Published); Assert.Equal(0,result.NotPublished);
        Assert.Equal(0,await f.Context.Jobs.CountAsync()); Assert.Equal(0,result.SkippedInvalid);
    }

    [Theory]
    [InlineData("stripe","https://stripe.com/jobs","https://stripe.com/jobs/search?gh_jid=1")]
    [InlineData("airbnb","https://careers.airbnb.com","https://careers.airbnb.com/positions/1?gh_jid=1")]
    public async Task GreenhouseEmployerHostedLinksFromPublicContractsReachPersistence(string board,string careerUrl,string applyUrl)
    {
        using var f = new JobSourceFixture(); Configure(f,AtsType.Greenhouse,board);
        f.Source.CareerPageUrl = careerUrl; await f.Context.SaveChangesAsync();
        var body = JsonSerializer.Serialize(new { meta = new { total = 1 }, jobs = new[] { new { id = 1,
            internal_job_id = 10,title = "Principal Engineer", content = "Senior engineer with 12 years of experience.",
            location = new { name = "Pune, India" }, absolute_url = applyUrl } } });
        using var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/jobs",StringComparison.Ordinal) ? body : "{\"id\":1}")));
        using var factory = new Factory(handler);
        var result = await Runner(f,new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded); Assert.Equal(1,result.Created); Assert.Equal(0,result.SkippedInvalid);
        var saved = Assert.Single(await f.Context.Jobs.ToArrayAsync()); Assert.Equal(applyUrl,saved.ApplicationUrl);
        Assert.Equal("Principal Engineer",saved.Title);
    }

    [Fact]
    public async Task UnverifiedGreenhouseApplicationUrlExplainsZeroCreationWithoutAllowingClosure()
    {
        using var f = new JobSourceFixture(); Configure(f,AtsType.Greenhouse,"stripe"); await f.Context.SaveChangesAsync();
        var body = Greenhouse("stripe",1).Replace("https://job-boards.greenhouse.io/stripe/jobs/1","https://other.example/jobs/1",StringComparison.Ordinal);
        using var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/jobs",StringComparison.Ordinal) ? body : "{\"id\":1}")));
        using var factory = new Factory(handler);
        var result = await Runner(f,new GreenhouseExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.Equal(1,result.Fetched); Assert.Equal(0,result.Created); Assert.Equal(1,result.SkippedInvalid);
        Assert.Equal(1,result.ProviderReasonCounts!["PROVIDER_APPLICATION_URL_UNVERIFIED"]);
        Assert.False(result.ReconciliationSafe); Assert.Equal(0,result.Closed);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse, "airbnb")]
    [InlineData(AtsType.Greenhouse, "stripe")]
    [InlineData(AtsType.Greenhouse, "third-board")]
    [InlineData(AtsType.Lever, "acme")]
    [InlineData(AtsType.Lever, "second-site")]
    [InlineData(AtsType.Lever, "ThirdSite")]
    [InlineData(AtsType.Ashby, "Sierra.ai")]
    [InlineData(AtsType.Ashby, "CaseSensitive")]
    [InlineData(AtsType.Ashby, "third-board")]
    public async Task PublicProviderContractsCompleteThroughExistingPublicationPipeline(AtsType provider, string board)
    {
        using var f = new JobSourceFixture(); Configure(f, provider, board); await f.Context.SaveChangesAsync();
        using var handler = new Handler((request, _) => Task.FromResult(Response(provider, board, request)));
        using var factory = new Factory(handler);
        var result = await Runner(f, Provider(provider, factory)).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded); Assert.Equal(1, result.Created); Assert.Equal(1, result.Published);
        Assert.Equal(1, result.IndiaEligible);
        Assert.True(result.Created <= result.IndiaEligible);
        Assert.Equal(board, (await f.Repository.GetByIdAsync(f.Source.Id))!.AtsIdentifier);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse, 404, "PROVIDER_NOT_FOUND")]
    [InlineData(AtsType.Greenhouse, 429, "PROVIDER_RATE_LIMITED")]
    [InlineData(AtsType.Greenhouse, 500, "PROVIDER_UNAVAILABLE")]
    [InlineData(AtsType.Lever, 404, "PROVIDER_NOT_FOUND")]
    [InlineData(AtsType.Lever, 429, "PROVIDER_RATE_LIMITED")]
    [InlineData(AtsType.Lever, 500, "PROVIDER_UNAVAILABLE")]
    [InlineData(AtsType.Ashby, 404, "PROVIDER_NOT_FOUND")]
    [InlineData(AtsType.Ashby, 429, "PROVIDER_RATE_LIMITED")]
    [InlineData(AtsType.Ashby, 500, "PROVIDER_UNAVAILABLE")]
    public async Task ProviderHttpFailuresAreClassifiedAndCannotReconcile(AtsType provider, int status, string reason)
    {
        using var f = new JobSourceFixture(); Configure(f, provider, "acme"); await f.Context.SaveChangesAsync();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var factory = new Factory(handler);
        var result = await Runner(f, Provider(provider, factory)).RunAsync(f.Source.Id);
        Assert.False(result.Succeeded); Assert.Equal(reason, result.FailureReasonCode);
        Assert.False(result.ReconciliationSafe); Assert.Equal(0, result.Closed);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    [InlineData(AtsType.Workday)]
    [InlineData(AtsType.SuccessFactors)]
    public async Task ConnectionTestUsesListingOnlyAndDoesNotWriteJobs(AtsType provider)
    {
        using var f = new JobSourceFixture(); Configure(f, provider, "acme");
        if (provider == AtsType.Workday) { f.Source.CareerPageUrl = "https://acme.wd504.myworkdayjobs.com/ExactCase"; f.Source.AtsIdentifier = "acme/ExactCase"; }
        if (provider == AtsType.SuccessFactors) { f.Source.CareerPageUrl = "https://careers.example.test/go/Board/123"; f.Source.AtsIdentifier = "123"; }
        var calls = 0;
        using var handler = new Handler((request, _) =>
        {
            calls++;
            if (provider == AtsType.SuccessFactors) return Task.FromResult(Json("<div>Results 0 to 0 of 0</div>"));
            if (provider == AtsType.Workday) return Task.FromResult(Json("{\"total\":0,\"jobPostings\":[]}"));
            Assert.False(int.TryParse(request.RequestUri!.Segments[^1], out var postingNumber));
            return Task.FromResult(provider == AtsType.Lever ? Json("[]") : Json("{\"jobs\":[]}"));
        });
        using var factory = new Factory(handler);
        var result = await new JobSourceConnectionTester(factory).TestAsync(f.Source);
        Assert.True(result.ConfigurationValid); Assert.True(result.ListingReachable); Assert.Equal(1, calls);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync()); Assert.Empty(await f.Context.JobSourceRuns.ToArrayAsync());
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    public void ConfigurationRejectsUrlsAsIdentifiersAndDerivesSafeBoardRoots(AtsType provider)
    {
        Assert.False(JobSourceConfiguration.Resolve(provider, "https://example.test/careers", "https://evil.com/?token=secret").IsValid);
        var host = provider == AtsType.Greenhouse ? "job-boards.greenhouse.io" : provider == AtsType.Lever ? "jobs.lever.co" : "jobs.ashbyhq.com";
        Assert.Equal("ExactCase", JobSourceConfiguration.Resolve(provider, $"https://{host}/ExactCase", null).Identifier);
        Assert.False(JobSourceConfiguration.Resolve(provider, $"https://{host}/ExactCase", "other").IsValid);
    }

    [Theory]
    [InlineData(AtsType.Lever, false)]
    [InlineData(AtsType.Lever, true)]
    [InlineData(AtsType.Ashby, false)]
    [InlineData(AtsType.Ashby, true)]
    public async Task MalformedAndTimedOutListingsFailWithSafeReasons(AtsType provider, bool timeout)
    {
        using var f = new JobSourceFixture(); Configure(f, provider, "acme"); await f.Context.SaveChangesAsync();
        using var handler = new Handler((_, _) => timeout ? throw new TaskCanceledException("secret response") : Task.FromResult(Json("{bad")));
        using var factory = new Factory(handler);
        var result = await Runner(f, Provider(provider, factory)).RunAsync(f.Source.Id);
        Assert.False(result.Succeeded); Assert.False(result.ReconciliationSafe);
        Assert.Equal(timeout ? "PROVIDER_TIMEOUT" : "PROVIDER_RESPONSE_INVALID", result.FailureReasonCode);
        Assert.DoesNotContain("secret", result.Error ?? ""); Assert.Equal(0, result.Closed);
    }

    [Fact]
    public async Task LeverPageCommitsBeforeLaterPageFailure()
    {
        using var f = new JobSourceFixture(); Configure(f, AtsType.Lever, "acme"); await f.Context.SaveChangesAsync();
        using var handler = new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.Query.Contains("skip=100", StringComparison.Ordinal))
            {
                Assert.Equal(100, await f.Context.Jobs.CountAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return Json(JsonSerializer.Serialize(Enumerable.Range(1,100).Select(id => new
            { id = $"REQ{id}", text = $"Graduate Engineer {id}", descriptionPlain = "Build safe systems", applyUrl = $"https://jobs.lever.co/acme/REQ{id}/apply", categories = new { location = "Pune" } })));
        });
        using var factory = new Factory(handler);
        var result = await Runner(f, new LeverExternalJobProvider(factory)).RunAsync(f.Source.Id);
        Assert.Equal(100, result.Created); Assert.Equal(100, result.Published); Assert.False(result.Succeeded);
        Assert.False(result.ReconciliationSafe); Assert.Equal(0, result.Closed); Assert.Equal("PROVIDER_UNAVAILABLE", result.FailureReasonCode);
    }

    [Fact]
    public async Task SchedulerIsolatesActualMixedProvidersAndBoundsConcurrency()
    {
        var fixtures = new List<JobSourceFixture>(); var factories = new List<Factory>(); var handlers = new List<Handler>();
        var runners = new Dictionary<Guid,IJobSourceRunner>();
        try
        {
            foreach (var entry in new[] { (AtsType.Greenhouse,"valid",200), (AtsType.Greenhouse,"missing",404),
                (AtsType.Greenhouse,"throttled",429), (AtsType.Lever,"lever",200), (AtsType.Ashby,"Ashby",200),
                (AtsType.Workday,"Airbus",200), (AtsType.Workday,"JJ",200), (AtsType.Workday,"walmartexternal",200) })
            {
                var (ats,board,status) = entry;
                var f = new JobSourceFixture(); fixtures.Add(f); Configure(f,ats,board);
                if (ats == AtsType.Workday) { f.Source.CareerPageUrl = $"https://tenant.wd504.myworkdayjobs.com/{board}"; f.Source.AtsIdentifier = $"tenant/{board}"; }
                await f.Context.SaveChangesAsync();
                var handler = new Handler((request, _) =>
                {
                    if (status != 200) return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
                    if (ats == AtsType.Workday) return Task.FromResult(Json(request.Method == HttpMethod.Post
                        ? """{"total":1,"jobPostings":[{"title":"Graduate Engineer","externalPath":"/job/Pune/Graduate_REQ-1","locationsText":"Pune","bulletFields":["REQ-1"]}]}"""
                        : """{"jobPostingInfo":{"title":"Graduate Engineer","jobReqId":"REQ-1","jobDescription":"Build safe systems","location":"Pune","jobRequisitionLocation":{"country":{"alpha2Code":"IN","descriptor":"India"}}}}"""));
                    return Task.FromResult(Response(ats,board,request));
                });
                handlers.Add(handler); var factory = new Factory(handler); factories.Add(factory);
                IExternalJobProvider adapter = ats == AtsType.Workday ? new WorkdayJobSourceProvider(factory) : Provider(ats,factory);
                runners.Add(f.Source.Id, Runner(f,adapter));
            }
            var dispatcher = new Dispatch(runners);
            var services = new ServiceCollection();
            services.AddSingleton<IJobSourceRepository>(new Sources(fixtures.Select(f => f.Source).ToArray()));
            services.AddSingleton<IJobSourceExecutionLock>(new TestAggregationLocks());
            services.AddSingleton<IJobSourceRunner>(dispatcher);
            await using var scopeProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scheduler = new JobAggregationScheduler(scopeProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new JobAggregationOptions { Scheduler = new() { Enabled = true, BatchSize = 20, MaxConcurrentSources = 3 } }),
                new JobSourceRunGuard(), TimeProvider.System, NullLogger<JobAggregationScheduler>.Instance);
            await scheduler.RunOnceAsync();
            Assert.Equal(8, dispatcher.Results.Count); Assert.InRange(dispatcher.Peak,2,3);
            Assert.Equal(6, dispatcher.Results.Values.Count(result => result.Succeeded));
            Assert.Equal(2, dispatcher.Results.Values.Count(result => !result.Succeeded));
            Assert.All(dispatcher.Results.Values.Where(result => result.Succeeded), result =>
            { Assert.Equal(1, result.IndiaEligible); Assert.True(result.Created <= result.IndiaEligible); });
            Assert.All(dispatcher.Results.Values.Where(result => !result.Succeeded), result =>
            { Assert.False(result.ReconciliationSafe); Assert.Equal(0,result.Closed); Assert.Equal(0,result.Created); });
            Assert.Contains(dispatcher.Results.Values, result => result.FailureReasonCode == "PROVIDER_NOT_FOUND");
            Assert.Contains(dispatcher.Results.Values, result => result.FailureReasonCode == "PROVIDER_RATE_LIMITED");
            Assert.Equal(6, dispatcher.Results.Values.Sum(result => result.Created));
        }
        finally
        {
            foreach (var runner in runners.Values.OfType<IDisposable>()) runner.Dispose();
            foreach (var factory in factories) factory.Dispose(); foreach (var handler in handlers) handler.Dispose();
            foreach (var f in fixtures) f.Dispose();
        }
    }

    [Theory]
    [InlineData("Example", "123")]
    [InlineData("India-Board", "456")]
    [InlineData("Graduate_Careers", "789")]
    public async Task SuccessFactorsBoardVariantsUseTheSameIngestionAndPublicationPipeline(string name, string id)
    {
        using var f = new JobSourceFixture(); Configure(f,AtsType.SuccessFactors,id);
        f.Source.CareerPageUrl = $"https://careers.example.test/go/{name}/{id}"; await f.Context.SaveChangesAsync();
        using var handler = new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.StartsWith("/job/",StringComparison.Ordinal)
            ? "<span itemprop=\"title\">Graduate Engineer</span><span itemprop=\"description\">Build reliable systems.</span>"
            : "<div>Results 1 to 1 of 1</div><table><tr class=\"data-row\"><td><a class=\"jobTitle-link\" href=\"/job/engineer/55/\">Graduate Engineer</a></td><td><span class=\"jobLocation\">Pune, India</span></td><td><span class=\"jobDate\">Oct 4, 2026</span></td></tr></table>")));
        using var factory = new Factory(handler);
        var result = await Runner(f,new SuccessFactorsJobSourceProvider(factory)).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded); Assert.Equal(1,result.Created); Assert.Equal(1,result.Published);
        Assert.True(result.ReconciliationSafe);
    }

    [Fact]
    public async Task EmptySuccessFactorsBoardCompletesWithZeroCounts()
    {
        using var f = new JobSourceFixture(); Configure(f,AtsType.SuccessFactors,"123");
        f.Source.CareerPageUrl = "https://careers.example.test/go/Example/123"; await f.Context.SaveChangesAsync();
        using var handler = new Handler((_, _) => Task.FromResult(Json("<div>Results 0 to 0 of 0</div>")));
        using var factory = new Factory(handler);
        var result = await Runner(f,new SuccessFactorsJobSourceProvider(factory)).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded); Assert.Equal(0,result.Fetched); Assert.Equal(0,result.Created); Assert.Equal(0,result.Published);
    }

    [Fact]
    public async Task UnexpectedWorkdayFailureBeforeHttpRetainsSafeStageAndServerStack()
    {
        using var f = new JobSourceFixture(); Configure(f,AtsType.Workday,"ag/Airbus");
        f.Source.CareerPageUrl = "https://ag.wd3.myworkdayjobs.com/Airbus"; await f.Context.SaveChangesAsync();
        var logs = new Logs<JobSourceRunner>();
        var result = await Runner(f,new SnapshotProvider(AtsType.Workday,[]), logger: logs,
            sources: new FaultingSources(f.Repository)).RunAsync(f.Source.Id);
        Assert.False(result.Succeeded); Assert.False(result.ReconciliationSafe);
        Assert.Equal("UNEXPECTED_INTERNAL_ERROR",result.FailureReasonCode); Assert.Equal("RecordAttempt",result.FailureStage);
        Assert.DoesNotContain("PRIVATE_SENTINEL",JsonSerializer.Serialize(result));
        Assert.Contains(logs.Messages, message => message.Contains("WorkdayConfigurationInput",StringComparison.Ordinal));
        Assert.Contains(logs.Messages, message => message.Contains("WorkdayPreExecutionFailure",StringComparison.Ordinal));
        var exception = Assert.Single(logs.Exceptions);
        Assert.Contains(nameof(FaultingSources),exception.StackTrace);
        Assert.DoesNotContain("PRIVATE_SENTINEL",exception.ToString());
    }

    [Theory]
    [InlineData("ag.wd3.myworkdayjobs.com","ag/Airbus","Airbus")]
    [InlineData("walmart.wd504.myworkdayjobs.com","walmart/walmartexternal","walmartexternal")]
    [InlineData("jj.wd5.myworkdayjobs.com","jj/JJ","JJ")]
    public async Task IntendedWorkdayInputsProduceResolutionAndSpecificRejectionDiagnostics(string host,string identifier,string site)
    {
        using var f = new JobSourceFixture(); Configure(f,AtsType.Workday,identifier);
        f.Source.CareerPageUrl = $"https://{host}/{site}";
        var logs = new Logs<WorkdayJobSourceProvider>();
        using var handler = new Handler((_, _) => Task.FromResult(Json("{\"total\":0,\"jobPostings\":[]}")));
        using var factory = new Factory(handler); using var adapter = new WorkdayJobSourceProvider(factory,logs);
        await adapter.FetchSnapshotAsync(f.Source);
        Assert.Contains(logs.Messages, message => message.Contains($"AtsIdentifier={identifier}",StringComparison.Ordinal));
        Assert.Contains(logs.Messages, message => message.Contains($"ResolvedCareerSite={site}",StringComparison.Ordinal));
        f.Source.AtsIdentifier = "other/PRIVATE_SENTINEL";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.FetchSnapshotAsync(f.Source));
        Assert.Equal("WORKDAY_CONFIGURATION_CONFLICT",exception.Data["JobSourceReasonCode"]);
        Assert.Contains(logs.Messages, message => message.Contains("WorkdayConfigurationRejected",StringComparison.Ordinal));
        Assert.All(logs.Messages,message => Assert.DoesNotContain("PRIVATE_SENTINEL",message));
    }

    private sealed class FaultingSources(IJobSourceRepository sources) : IJobSourceRepository
    {
        public Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(DateTime nowUtc,int maxResults,CancellationToken cancellationToken = default)
            => sources.GetDueSourcesAsync(nowUtc,maxResults,cancellationToken);
        public Task<JobSource?> GetByIdAsync(Guid id,CancellationToken cancellationToken = default) => sources.GetByIdAsync(id,cancellationToken);
        public void Update(JobSource source) => throw new InvalidOperationException("PRIVATE_SENTINEL connection string");
    }
    private sealed class Logs<T> : ILogger<T>
    {
        public List<string> Messages { get; } = []; public List<Exception> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)
        { Messages.Add(formatter(state,exception)); if (exception is not null) Exceptions.Add(exception); }
    }

    private sealed class Sources(JobSource[] sources) : IJobSourceRepository
    {
        public Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<JobSource>>(sources.Take(maxResults).ToArray());
        public Task<JobSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(sources.FirstOrDefault(source => source.Id == id));
        public void Update(JobSource source) { }
    }
    private sealed class Dispatch(Dictionary<Guid,IJobSourceRunner> runners) : IJobSourceRunner
    {
        private int active; public int Peak { get; private set; }
        private readonly object sync = new();
        public ConcurrentDictionary<Guid,JobSourceRunResult> Results { get; } = new();
        public async Task<JobSourceRunResult> RunAsync(Guid jobSourceId, CancellationToken cancellationToken = default)
        {
            lock (sync) { active++; Peak = Math.Max(Peak,active); }
            try { await Task.Delay(30,cancellationToken); var result = await runners[jobSourceId].RunAsync(jobSourceId,cancellationToken); Results[jobSourceId] = result; return result; }
            finally { lock (sync) active--; }
        }
    }

    private static void Configure(JobSourceFixture f, AtsType provider, string board)
    { f.Source.AtsType = provider; f.Source.AtsIdentifier = board; f.Source.CareerPageUrl = $"https://example.test/careers"; }
    private static IExternalJobProvider Provider(AtsType provider, IHttpClientFactory factory) => provider switch
    { AtsType.Greenhouse => new GreenhouseExternalJobProvider(factory), AtsType.Lever => new LeverExternalJobProvider(factory), _ => new AshbyExternalJobProvider(factory) };
    private static HttpResponseMessage Response(AtsType provider, string board, HttpRequestMessage request)
    {
        if (provider == AtsType.Greenhouse) return request.RequestUri!.AbsolutePath.EndsWith("/jobs", StringComparison.Ordinal) ? Json(Greenhouse(board, 1)) : Json("{\"id\":1}");
        if (provider == AtsType.Lever) return Json(JsonSerializer.Serialize(new[] { new { id = "REQ1", text = $"Graduate {board} Engineer", descriptionPlain = "Build safe systems", applyUrl = $"https://jobs.lever.co/{board}/REQ1/apply", categories = new { location = "Pune, India" } } }));
        const string uuid = "00000000-0000-0000-0000-000000000001";
        return Json(JsonSerializer.Serialize(new { apiVersion = "1", jobs = new[] { new { title = $"Graduate {board} Engineer", descriptionPlain = "Build safe systems", location = "Pune, India", isListed = true, jobUrl = $"https://jobs.ashbyhq.com/{board}/{uuid}", applyUrl = $"https://jobs.ashbyhq.com/{board}/{uuid}/application" } } }));
    }
    private static string Greenhouse(string board, int total, string location = "Pune, India") => JsonSerializer.Serialize(new
    { meta = new { total }, jobs = Enumerable.Range(1, total).Select(id => new { id, internal_job_id = id, title = $"Graduate {board} Engineer {id}", content = "Build safe systems", location = new { name = location }, absolute_url = $"https://job-boards.greenhouse.io/{board}/jobs/{id}" }) });
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken); }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> clients = [];
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, false) { BaseAddress = new Uri(name == LeverExternalJobProvider.HttpClientName ? "https://api.lever.co/" : name == AshbyExternalJobProvider.HttpClientName ? "https://api.ashbyhq.com/" : "https://boards-api.greenhouse.io/") };
            clients.Add(client); return client;
        }
        public void Dispose() { foreach (var client in clients) client.Dispose(); }
    }

    private sealed class ThreeBatchesProvider : IBatchedExternalJobProvider
    {
        public AtsType AtsType => AtsType.Greenhouse;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RawExternalJob>>(Enumerable.Range(1, 300).Select(Vacancy).ToArray());
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalJobSourceSnapshot(Enumerable.Range(1, 300).Select(Vacancy).ToArray(), 0, true));
        public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var batch = 0; batch < 3; batch++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var jobs = Enumerable.Range(batch * 100 + 1, 100).Select(Vacancy).ToArray();
                yield return new(jobs, 0, batch == 2);
                await Task.Yield();
            }
        }
    }

    private sealed class RecordingProgress : IJobSourceRunProgress
    {
        private readonly List<JobSourceRunProgressSnapshot> snapshots = [];
        public IReadOnlyList<JobSourceRunProgressSnapshot> Snapshots => snapshots;
        public JobSourceRunProgressSnapshot Snapshot => snapshots.LastOrDefault() ?? new("ProviderFetch", 0, new());
        public void Report(JobSourceRunProgressSnapshot snapshot) => snapshots.Add(snapshot);
    }
}
