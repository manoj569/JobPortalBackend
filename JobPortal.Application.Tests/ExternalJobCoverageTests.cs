using System.Net;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalJobCoverageTests
{
    [Fact]
    public async Task ExistingCaseInsensitiveBindingAndNumericStringIdsArePreserved()
    {
        using var handler = new Handler("""{"JOBS":[{"ID":"42","TITLE":"Engineer","LOCATION":{"NAME":"Pune"}}]}""");
        using var factory = new Factory(handler);
        var job = Assert.Single(await Provider(AtsType.Greenhouse, factory).FetchJobsAsync(Source(AtsType.Greenhouse)));
        Assert.Equal("42", job.ExternalId);
        Assert.Equal("Engineer", job.Title);
        Assert.Equal("Pune", job.Location);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse, "{\"jobs\":[]}")]
    [InlineData(AtsType.Lever, "[]")]
    [InlineData(AtsType.Ashby, "{\"jobs\":[]}")]
    public async Task IdentifierIsEncodedAsOnePathSegment(AtsType type, string response)
    {
        using var handler = new Handler(response);
        using var factory = new Factory(handler);
        var source = Source(type);
        source.AtsIdentifier = "acme/team?extra=1";
        Assert.Empty(await Provider(type, factory).FetchJobsAsync(source));
        Assert.Contains("acme%2Fteam%3Fextra%3D1", handler.Uri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActualProviderOutputPassesThroughSharedRunnerAndCategoryMapping()
    {
        using var f = new JobSourceFixture();
        using var handler = new Handler("""[{"text":" C#   Engineer ","hostedUrl":"https://example.test/1","categories":{"department":"Engineering","commitment":"contract"},"workplaceType":"remote"}]""");
        using var factory = new Factory(handler);
        f.Provider.Jobs = await new LeverExternalJobProvider(factory).FetchJobsAsync(f.Source);
        f.MapExternal("engineering", f.Category.Id.ToString());
        var result = await f.Service.RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        var job = Assert.Single(f.Context.Jobs);
        Assert.Equal("C# Engineer", job.Title);
        Assert.Equal(EmploymentType.Contract, job.EmploymentType);
        Assert.Equal(WorkplaceType.Remote, job.WorkplaceType);
        Assert.Equal(JobStatus.Draft, job.Status);
    }

    [Fact]
    public async Task GreenhouseDepartmentAndHtmlFlowThroughNormalizer()
    {
        using var handler = new Handler("""
            {"jobs":[{"id":42,"title":" .NET  Developer ","absolute_url":"https://example.test/42",
            "content":"&lt;p&gt;Build &amp; test&lt;/p&gt;","departments":[{"name":" Engineering "}],
            "location":{"name":"Pune,India"}}]}
            """);
        using var factory = new Factory(handler);
        var raw = Assert.Single(await new GreenhouseExternalJobProvider(factory).FetchJobsAsync(Source(AtsType.Greenhouse)));
        Assert.True(raw.DescriptionIsHtml);
        Assert.Equal("42", raw.ExternalId);
        var result = new ExternalJobNormalizer().Normalize(raw);
        Assert.Equal("Build & test", result.Description);
        Assert.Equal("Engineering", result.ExternalCategory);
        Assert.Null(result.EmploymentType);
        Assert.Null(result.WorkplaceType);
        Assert.Equal("/v1/boards/acme/jobs?content=true", handler.Uri!.PathAndQuery);
    }

    [Fact]
    public async Task LeverStructuredCommitmentDepartmentAndWorkplaceAreUsed()
    {
        using var handler = new Handler("""
            [{"id":"a","text":" Engineer ","hostedUrl":"https://example.test/a",
            "descriptionPlain":"Use List<T>","workplaceType":"hybrid",
            "categories":{"location":"New York,NY","commitment":"full-time","department":"Engineering","team":"Platform"}}]
            """);
        using var factory = new Factory(handler);
        var raw = Assert.Single(await new LeverExternalJobProvider(factory).FetchJobsAsync(Source(AtsType.Lever)));
        var result = new ExternalJobNormalizer().Normalize(raw);
        Assert.Equal("Engineering", result.ExternalCategory);
        Assert.Equal(EmploymentType.FullTime, result.EmploymentType);
        Assert.Equal(WorkplaceType.Hybrid, result.WorkplaceType);
        Assert.Equal("Use List<T>", result.Description);
        Assert.Equal("New York, NY", result.Location);
        Assert.Equal("/v0/postings/acme?mode=json", handler.Uri!.PathAndQuery);
    }

    [Fact]
    public async Task AshbyOnlyIncludesExplicitlyListedJobsWithoutInventingIdOrSalary()
    {
        using var handler = new Handler("""
            {"jobs":[null,{"isListed":false,"title":"Unlisted"},{"title":"Missing visibility"},
            {"isListed":true,"title":"Engineer","location":"Remote - US","department":"Engineering",
            "workplaceType":"Remote","employmentType":"FullTime","descriptionPlain":"Build services",
            "jobUrl":"https://jobs.ashbyhq.com/acme/abc","applyUrl":"https://jobs.ashbyhq.com/acme/abc/apply"}]}
            """);
        using var factory = new Factory(handler);
        var raw = Assert.Single(await new AshbyExternalJobProvider(factory).FetchJobsAsync(Source(AtsType.Ashby)));
        var result = new ExternalJobNormalizer().Normalize(raw);
        Assert.Equal("Acme", result.CompanyName);
        Assert.Equal("Engineering", result.ExternalCategory);
        Assert.Equal("Remote - US", result.Location);
        Assert.Equal("Build services", result.Description);
        Assert.Equal(EmploymentType.FullTime, result.EmploymentType);
        Assert.Equal(WorkplaceType.Remote, result.WorkplaceType);
        Assert.Equal("https://jobs.ashbyhq.com/acme/abc", result.ApplicationUrl);
        Assert.Null(result.ExternalId);
        Assert.Null(result.SalaryMin);
        Assert.Null(result.SalaryMax);
        Assert.Equal("/posting-api/job-board/acme", handler.Uri!.PathAndQuery);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse, "{\"jobs\":[null, {\"title\":\"Good\",\"id\":{},\"content\":[],\"departments\":{},\"location\":42}]}")]
    [InlineData(AtsType.Lever, "[null,{\"text\":\"Good\",\"id\":{},\"categories\":42,\"workplaceType\":{},\"descriptionPlain\":[] }]")]
    [InlineData(AtsType.Ashby, "{\"jobs\":[{\"isListed\":true},{\"isListed\":true,\"title\":\"Good\",\"workplaceType\":{},\"employmentType\":[],\"department\":42}]}")]
    public async Task MalformedOptionalDataDoesNotDiscardValidTitleOrOtherRecords(AtsType type, string json)
    {
        using var handler = new Handler(json);
        using var factory = new Factory(handler);
        var jobs = await Provider(type, factory).FetchJobsAsync(Source(type));
        Assert.Equal(2, jobs.Count);
        var good = Assert.Single(jobs, x => x.Title == "Good");
        Assert.Null(good.EmploymentTypeText);
        Assert.Null(good.WorkplaceTypeText);
        Assert.Null(good.ExternalCategory);
        Assert.Single(jobs, x => x.Title.Length == 0);
    }

    [Fact]
    public async Task MultipleGreenhouseDepartmentsAreAmbiguous()
    {
        using var handler = new Handler("""{"jobs":[{"title":"Good","departments":[{"name":"A"},{"name":"B"}]}]}""");
        using var factory = new Factory(handler);
        Assert.Null(Assert.Single(await Provider(AtsType.Greenhouse, factory).FetchJobsAsync(Source(AtsType.Greenhouse))).ExternalCategory);
    }

    [Fact]
    public async Task LeverUsesTeamOnlyWhenDepartmentIsAbsent()
    {
        using var handler = new Handler("""[{"text":"Good","categories":{"department":" ","team":"Platform"}}]""");
        using var factory = new Factory(handler);
        Assert.Equal("Platform", Assert.Single(await Provider(AtsType.Lever, factory).FetchJobsAsync(Source(AtsType.Lever))).ExternalCategory);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    public async Task MissingIdentifierFailsBeforeHttp(AtsType type)
    {
        using var handler = new Handler("{}");
        using var factory = new Factory(handler);
        var source = Source(type);
        source.AtsIdentifier = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(type, factory).FetchJobsAsync(source));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    public async Task HttpFailureRemainsSourceFailure(AtsType type)
    {
        using var handler = new Handler("{}", HttpStatusCode.TooManyRequests);
        using var factory = new Factory(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(type, factory).FetchJobsAsync(Source(type)));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    public async Task CancellationIsNotConvertedToMalformedRecord(AtsType type)
    {
        using var handler = new Handler("{}");
        using var factory = new Factory(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(type, factory).FetchJobsAsync(Source(type), cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    public async Task InvalidEnvelopeIsSourceFailure(AtsType type)
    {
        using var handler = new Handler("{\"error\":\"unavailable\"}");
        using var factory = new Factory(handler);
        await Assert.ThrowsAsync<JsonException>(() => Provider(type, factory).FetchJobsAsync(Source(type)));
    }

    [Fact]
    public void AshbyIdentifierValidationAndPersistedEnumNumbersAreStable()
    {
        var validator = new SaveJobSourceRequestValidator();
        var request = new SaveJobSourceRequest(Guid.NewGuid(), "https://example.test", AtsType.Ashby, "acme");
        Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { AtsIdentifier = null }).IsValid);
        Assert.Equal(0, (int)AtsType.Custom);
        Assert.Equal(1, (int)AtsType.Greenhouse);
        Assert.Equal(2, (int)AtsType.Lever);
        Assert.Equal(3, (int)AtsType.Ashby);
    }

    private static JobSource Source(AtsType type) => new() { AtsType = type, AtsIdentifier = " acme ", Company = new Company { Name = "Acme" } };
    private static IExternalJobProvider Provider(AtsType type, IHttpClientFactory factory) => type switch
    {
        AtsType.Greenhouse => new GreenhouseExternalJobProvider(factory),
        AtsType.Lever => new LeverExternalJobProvider(factory),
        AtsType.Ashby => new AshbyExternalJobProvider(factory),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly HttpClient client = new(handler, false) { BaseAddress = new Uri("https://example.test/") };
        public HttpClient CreateClient(string name) => client;
        public void Dispose() => client.Dispose();
    }

    private sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
