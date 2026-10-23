using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using static JobPortal.Application.Tests.EmployerDirectIntegrationTests;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class GenericEmployerAuthorityTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly int[] ExpectedAuthorityRuns = [12, 24, 36];

    private static JsonArray Responses() => JsonNode.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "PublicAts", "generic-employer-authority.json")))!["responses"]!.AsArray();

    [Fact]
    public async Task Worker382TraceIngestsEveryJobAndClosesRemovalOnlyAfterTwoIndependentAuthoritativeMisses()
    {
        var responses = Responses();
        using var f = new JobSourceFixture();
        Assert.True(f.Context.Database.IsInMemory());
        await f.Context.Database.EnsureDeletedAsync();
        f.Context.ChangeTracker.Clear();
        f.Source.Id = Guid.Parse(responses[0]!["sourceId"]!.GetValue<string>());
        f.Company.Id = Guid.Parse(responses[0]!["companyId"]!.GetValue<string>());
        f.Source.CompanyId = f.Company.Id;
        f.Source.CareerPageUrl = responses[0]!["careerPageUrl"]!.GetValue<string>();
        f.Source.AtsType = AtsType.Custom;
        f.Source.AtsIdentifier = GenericEmployerContract.Identifier;
        f.Source.AutoPublishEnabled = false;
        f.Context.AddRange(f.Company, f.Category, f.Source);
        await f.Context.SaveChangesAsync();
        var index = 0;
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)Guid.Parse(responses[index]!["runId"]!.GetValue<string>()));
        var handler = new TraceHandler(responses, () => index);
        var scanner = new GenericEmployerScanner(Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false)),
            Options.Create(new EmployerAcquisitionOptions { Endpoint = "https://worker.example.test", Token = new string('t', 40) }), checkpoint);
        var provider = new ScannerProvider(scanner);
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var runner = new JobSourceRunner(f.Repository, [provider], ingestion, unit, TimeProvider.System, f.Resolver, new ExternalJobNormalizer(),
            autoPublishService: Publisher(f), jobRepository: jobs,
            publicationPolicy: new JobSourcePublicationPolicy(Options.Create(new JobAggregationOptions()), TimeProvider.System));
        string? removedId = null;
        var reconciledRuns = new List<int>();
        for (; index < responses.Count; index++)
        {
            if (index == 11)
            {
                var existing = await f.Context.Jobs.SingleAsync(j => j.ApplicationUrl == "https://careers.example.com/careers/job/1");
                existing.Title = "Reviewed title retained by presence census";
                existing.Description = "Reviewed description retained by presence census";
                await f.Context.SaveChangesAsync();
            }
            var result = await runner.RunAsync(f.Source.Id);
            Assert.Equal(0, result.Failed);
            if (index >= 10) Assert.Equal(382, await f.Context.Jobs.CountAsync());
            var complete = responses[index]!["isComplete"]!.GetValue<bool>();
            Assert.Equal(complete, result.ReconciliationSafe);
            if (complete) reconciledRuns.Add(index + 1);
            if (index == 11)
            {
                Assert.True(result.Succeeded);
                Assert.Equal(382, result.ObservedIdentities);
                Assert.Empty(responses[index]!["jobs"]!.AsArray());
                Assert.Equal(382, f.Source.LastCompleteSnapshotCount);
                var unchanged = await f.Context.Jobs.SingleAsync(j => j.ApplicationUrl == "https://careers.example.com/careers/job/1");
                Assert.Equal("Reviewed title retained by presence census", unchanged.Title);
                Assert.Equal("Reviewed description retained by presence census", unchanged.Description);
                removedId = (await f.Context.Jobs.SingleAsync(j => j.ApplicationUrl == "https://careers.example.com/careers/job/1")).ExternalJobId!;
            }
            if (removedId is not null)
            {
                var member = await f.Context.Set<JobSourceVacancy>().SingleAsync(v => v.ExternalJobId == removedId);
                var job = await f.Context.Jobs.SingleAsync(j => j.ExternalJobId == removedId);
                Assert.Equal(index < 23 ? 0 : index < 35 ? 1 : 2, member.MissingCompleteSnapshots);
                Assert.Equal(index == 35 ? JobStatus.Closed : JobStatus.Draft, job.Status);
                Assert.Equal(index == 35 ? 1 : 0, result.Closed);
                Assert.Equal(381, await f.Context.Jobs.CountAsync(j => j.ExternalJobId != removedId && j.Status != JobStatus.Closed));
            }
            else Assert.Equal(0, result.Closed);
        }
        Assert.Equal(ExpectedAuthorityRuns, reconciledRuns);
        Assert.Equal(382, await f.Context.Jobs.Select(j => j.ExternalJobId).Distinct().CountAsync());
        Assert.Equal(1, await f.Context.Jobs.CountAsync(j => j.Status == JobStatus.Closed));
        Assert.Equal(381, f.Source.LastCompleteSnapshotCount);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("digest")]
    [InlineData("count")]
    [InlineData("missingEvidence")]
    [InlineData("wrongSource")]
    [InlineData("partial")]
    [InlineData("missingJob")]
    [InlineData("errors")]
    [InlineData("censusMismatch")]
    [InlineData("truncated")]
    [InlineData("emptyGeneration")]
    [InlineData("badVersion")]
    public async Task InvalidPresenceEvidenceNeverReachesReconciliation(string mutation)
    {
        var response = Responses()[11]!.DeepClone();
        var source = BoundSource(response);
        var ids = response["observedExternalIds"]!.AsArray();
        switch (mutation)
        {
            case "censusMismatch": response["inventoryEvidence"]!["censusBDigest"] = "wrong"; break;
            case "truncated": ids.RemoveAt(ids.Count - 1); break;
            case "emptyGeneration": response["inventoryEvidence"]!["generation"] = Guid.Empty.ToString(); break;
            case "badVersion": response["inventoryEvidence"]!["version"] = 999; break;
            case "duplicate": ids[1] = ids[0]!.GetValue<string>(); break;
            case "digest": response["inventoryEvidence"]!["digest"] = "wrong"; break;
            case "count": response["inventoryEvidence"]!["count"] = 1; break;
            case "missingEvidence": response["inventoryEvidence"] = null; break;
            case "wrongSource": response["sourceId"] = Guid.NewGuid().ToString(); break;
            case "partial": response["isComplete"] = false; break;
            case "missingJob": response["jobs"] = JsonSerializer.SerializeToNode(new[] { Vacancy(999) }); break;
            case "errors": response["errors"] = JsonNode.Parse("[{\"reason\":\"TIMEOUT\"}]"); break;
        }
        var scanner = Scanner(source, response);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(source, default));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    public async Task CorruptProviderMemoryRecoversButRecoveryRunCannotAuthorize(string memory)
    {
        var response = Responses()[11]!.DeepClone();
        var source = BoundSource(response);
        source.EmployerStrategyJson = memory;
        var result = await Scanner(source, response, expectNullStrategy: true).ScanAsync(source, default);
        Assert.False(result.IsComplete);
        Assert.Contains("AcquisitionStrategyRecovery", result.ProviderReasonCounts!.Keys);
        Assert.NotNull(JsonNode.Parse(source.EmployerStrategyJson!));
    }

    [Fact]
    public async Task SameCensusGenerationCannotSupplyAnotherAuthoritativeMiss()
    {
        var response = Responses()[11]!.DeepClone();
        var source = BoundSource(response);
        source.LastInventoryVersion = response["inventoryEvidence"]!["generation"]!.GetValue<string>();
        source.LastObservedSnapshotId = Guid.NewGuid();
        var result = await Scanner(source, response).ScanAsync(source, default);
        Assert.False(result.IsComplete);
        Assert.Contains("CensusGenerationAlreadyObserved", result.ProviderReasonCounts!.Keys);
    }

    [Fact]
    public async Task LegacyResponseWithoutPresenceIdsRetainsJobsDerivedContract()
    {
        var response = Responses()[1]!.DeepClone();
        response["isComplete"] = true;
        response["completionReason"] = "AUTHORITATIVE_API_CENSUS";
        var source = BoundSource(response);
        var result = await Scanner(source, response).ScanAsync(source, default);
        Assert.True(result.IsComplete);
        Assert.Null(result.ObservedExternalIds);
        Assert.Equal(response["jobs"]!.AsArray().Count, result.Jobs.Count);
    }

    [Fact]
    public async Task PresenceOnlyCensusNeverCreatesOrPublishesJobs()
    {
        using var f = new JobSourceFixture();
        f.Source.AutoPublishEnabled = true;
        f.Source.AtsType = AtsType.Custom;
        f.Source.AtsIdentifier = GenericEmployerContract.Identifier;
        var row = Responses()[11]!;
        var ids = row["observedExternalIds"]!.Deserialize<string[]>()!;
        var evidence = row["inventoryEvidence"]!.Deserialize<ExternalJobInventoryEvidence>(WebJson)!;
        var snapshot = new ExternalJobSourceSnapshot([], 0, true) { ObservedExternalIds = ids,
            InventoryEvidence = evidence, InventoryVersion = evidence.Generation.ToString("D"),
            InventoryDigest = evidence.Digest, InventoryAtUtc = DateTime.UtcNow, ObservedAtUtc = DateTime.UtcNow };
        var runner = Runner(f, new CensusProvider(snapshot));
        var result = await runner.RunAsync(f.Source.Id);
        Assert.True(result.ReconciliationSafe);
        Assert.Equal(382, result.ObservedIdentities);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Published);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
        Assert.Empty(await f.Context.Set<JobSourceVacancy>().ToArrayAsync());
    }

    [Theory]
    [InlineData("count")]
    [InlineData("duplicate")]
    [InlineData("digest")]
    [InlineData("censusMismatch")]
    [InlineData("truncated")]
    [InlineData("missingEvidence")]
    [InlineData("emptyGeneration")]
    [InlineData("wrongFence")]
    public async Task InvalidGenericCensusNeverGrantsReconciliationSafety(string mutation)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.Custom;
        f.Source.AtsIdentifier = GenericEmployerContract.Identifier;
        var row = Responses()[11]!;
        var ids = row["observedExternalIds"]!.Deserialize<string[]>()!;
        ExternalJobInventoryEvidence? evidence = row["inventoryEvidence"]!.Deserialize<ExternalJobInventoryEvidence>(WebJson)!;
        var version = evidence.Generation.ToString("D");
        var digest = evidence.Digest;
        switch (mutation)
        {
            case "count": evidence = evidence with { Count = 1 }; break;
            case "duplicate": ids[1] = ids[0]; break;
            case "digest": evidence = evidence with { Digest = "wrong" }; break;
            case "censusMismatch": evidence = evidence with { CensusBDigest = "wrong" }; break;
            case "truncated": ids = ids[..^1]; break;
            case "missingEvidence": evidence = null; break;
            case "emptyGeneration": evidence = evidence with { Generation = Guid.Empty }; break;
            case "wrongFence": version = Guid.NewGuid().ToString("D"); break;
        }
        var snapshot = new ExternalJobSourceSnapshot([], 0, true) { ObservedExternalIds = ids,
            InventoryEvidence = evidence, InventoryVersion = version, InventoryDigest = digest,
            InventoryAtUtc = DateTime.UtcNow, ObservedAtUtc = DateTime.UtcNow };
        var result = await Runner(f, new CensusProvider(snapshot)).RunAsync(f.Source.Id);
        Assert.False(result.ReconciliationSafe);
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Published);
        Assert.Equal(0, result.Closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedEvidenceCannotDeserializeIntoAnAuthoritativeSnapshot(bool malformedGeneration)
    {
        var response = Responses()[11]!.DeepClone();
        var source = BoundSource(response);
        if (malformedGeneration) response["inventoryEvidence"]!["generation"] = "not-a-generation";
        else response["inventoryEvidence"] = "not-an-evidence-object";
        await Assert.ThrowsAsync<JsonException>(() => Scanner(source, response).ScanAsync(source, default));
    }

    private sealed class CensusProvider(ExternalJobSourceSnapshot snapshot) : ICompleteExternalJobProvider
    {
        public AtsType AtsType => AtsType.Custom;
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult(snapshot.Jobs);
    }

    private static JobSource BoundSource(JsonNode response) => new() { Id = Guid.Parse(response["sourceId"]!.GetValue<string>()),
        CompanyId = Guid.Parse(response["companyId"]!.GetValue<string>()), CareerPageUrl = response["careerPageUrl"]!.GetValue<string>(),
        AtsType = AtsType.Custom, AtsIdentifier = GenericEmployerContract.Identifier };

    private static GenericEmployerScanner Scanner(JobSource source, JsonNode response, bool expectNullStrategy = false)
    {
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((_, _) => (Guid?)Guid.Parse(response["runId"]!.GetValue<string>()));
        var handler = new SingleHandler(response, expectNullStrategy);
        return new(Stub.Create<IHttpClientFactory>((_, _) => new HttpClient(handler, false)),
            Options.Create(new EmployerAcquisitionOptions { Endpoint = "https://worker.example.test", Token = new string('t', 40) }), checkpoint);
    }

    private sealed class SingleHandler(JsonNode response, bool expectNullStrategy) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (expectNullStrategy)
                Assert.Null(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!["source"]!["strategy"]);
            return new(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TraceHandler(JsonArray responses, Func<int> index) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Assert.Equal(responses[index()]!["runId"]!.GetValue<string>(), payload["runId"]!.GetValue<string>());
            if (index() > 0) Assert.True(JsonNode.DeepEquals(responses[index()-1]!["strategy"], payload["source"]!["strategy"]));
            return new(HttpStatusCode.OK) { Content = new StringContent(responses[index()]!.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ScannerProvider(GenericEmployerScanner scanner) : ICompleteExternalJobProvider
    {
        public AtsType AtsType => AtsType.Custom;
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => scanner.ScanAsync(source, cancellationToken);
        public async Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => (await scanner.ScanAsync(source, cancellationToken)).Jobs;
    }
}
