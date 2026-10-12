using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class MastercardWorkdayVerificationTests
{
    private const string Board = "https://mastercard.wd1.myworkdayjobs.com/CorporateCareers";
    private const string ApiRoot = "https://mastercard.wd1.myworkdayjobs.com/wday/cxs/mastercard/CorporateCareers";
    private static readonly int[] CapturedOffsets = [0, 20, 40];

    [Fact]
    public void CapturedPagesAreDistinctWithOmittedContinuationTotalsAndStableAnchor()
    {
        var pages = CapturedOffsets.Select(offset => WorkdayJobSourceProvider.ParseListingPage(Read($"listing-{offset}"))).ToArray();
        Assert.Equal(1084, pages[0].Total);
        Assert.All(pages.Skip(1), page => Assert.Equal(0, page.Total));
        Assert.All(pages, page => Assert.Equal(20, page.Postings.Count));
        var paths = pages.SelectMany(page => page.Postings).Select(job => job.ExternalPath).ToArray();
        Assert.Equal(60, paths.Distinct(StringComparer.Ordinal).Count());
        var anchor = WorkdayJobSourceProvider.ParseListingPage(Read("listing-anchor"));
        Assert.Equal(pages[0].Total, anchor.Total);
        Assert.Equal(pages[0].Postings.Select(job => job.ExternalPath), anchor.Postings.Select(job => job.ExternalPath));
    }

    [Theory]
    [InlineData("R-291604", "Gurgaon, India", "IN", IndiaJobEligibilityDecision.Accepted)]
    [InlineData("R-292031", "Hyderabad, India", "IN", IndiaJobEligibilityDecision.Accepted)]
    [InlineData("R-291599", "Navi Mumbai, India (Finicity)", "IN", IndiaJobEligibilityDecision.Accepted)]
    [InlineData("R-278780", "Pune, India", "IN", IndiaJobEligibilityDecision.Accepted)]
    [InlineData("R-291339", "London, England (Angel Lane)", "GB", IndiaJobEligibilityDecision.Rejected)]
    public void LiveDetailFixturesPreserveIdentityApplicationAndEvidence(string id, string location, string country,
        IndiaJobEligibilityDecision decision)
    {
        var source = Source();
        var target = WorkdayJobSourceProvider.ValidateSource(source);
        var entry = WorkdayJobSourceProvider.ParseListingPage(Read("sample-listing")).Postings.Single(job => job.BulletId == id);
        var raw = WorkdayJobSourceProvider.ParseDetail(Read($"detail-{id}"), target, source, entry);
        Assert.NotNull(raw);
        Assert.Equal(id, raw.ExternalId);
        Assert.Equal(entry.Title, raw.Title);
        Assert.Equal("Mastercard", raw.CompanyName);
        Assert.Equal(location, raw.Location);
        Assert.Contains(country, raw.CountryCodes);
        Assert.Equal(decision, IndiaJobEligibilityPolicy.Evaluate(raw).Decision);
        Assert.Equal($"https://mastercard.wd1.myworkdayjobs.com/en-US/CorporateCareers{entry.ExternalPath}", raw.ApplicationUrl);
        using var detail = JsonDocument.Parse(Read($"detail-{id}"));
        var info = detail.RootElement.GetProperty("jobPostingInfo");
        Assert.True(info.GetProperty("canApply").GetBoolean());
        Assert.True(info.GetProperty("posted").GetBoolean());
        Assert.Equal(Board + entry.ExternalPath, info.GetProperty("externalUrl").GetString());
        Assert.Equal(info.GetProperty("jobDescription").GetString(), raw.Description);
        Assert.Equal(EmploymentType.FullTime, raw.EmploymentType);
        Assert.Null(raw.WorkplaceType);
        Assert.Null(raw.SalaryMin);
        Assert.Null(raw.SalaryMax);
        Assert.Null(raw.CurrencyCode);
        Assert.Null(raw.SalaryPeriod);
        var classification = new JobClassificationService().Analyze(new(raw.Title, raw.Description, raw.Location,
            raw.EmploymentType, raw.WorkplaceType));
        Assert.Equal(EmploymentType.FullTime, classification.EmploymentType);
        if (id == "R-278780") Assert.Equal(WorkplaceType.Unknown, classification.WorkplaceType);
        Assert.Null(classification.MinimumSalary);
        Assert.Null(classification.MaximumSalary);
        Assert.Null(classification.CurrencyCode);
        Assert.Null(classification.SalaryPeriod);
    }

    [Theory]
    [InlineData("Remote", "remote", IndiaJobEligibilityDecision.Review)]
    [InlineData("Unknown", null, IndiaJobEligibilityDecision.Review)]
    [InlineData(null, null, IndiaJobEligibilityDecision.Accepted)]
    public void GeographyFixturesDoNotTreatGenericRemoteAsIndia(string? location, string? remote,
        IndiaJobEligibilityDecision expected)
    {
        // Deliberate mutations of a captured detail, not claims about a live vacancy.
        var detail = JsonNode.Parse(Read("detail-R-278780"))!;
        var info = detail["jobPostingInfo"]!;
        info["location"] = location;
        info["remoteType"] = remote;
        info.AsObject().Remove("country");
        info.AsObject().Remove("jobRequisitionLocation");
        var source = Source();
        var entry = new WorkdayJobSourceProvider.ListingEntry("Engineer", "/job/Engineer_R-278780", location, null, "R-278780");
        var raw = WorkdayJobSourceProvider.ParseDetail(detail.ToJsonString(), WorkdayJobSourceProvider.ValidateSource(source), source, entry);
        Assert.NotNull(raw);
        Assert.Empty(raw.CountryCodes);
        Assert.Equal(expected, IndiaJobEligibilityPolicy.Evaluate(raw).Decision);
    }

    [Fact]
    public void ExactExperienceAndEducationDoNotInventMaximumSalaryOrOfficeWork()
    {
        var classification = new JobClassificationService().Analyze(new("Engineer",
            "Minimum 3 years experience required. 15 years full time education.", "Pune, India"));
        Assert.Equal(3, classification.MinimumExperienceYears);
        Assert.Null(classification.MaximumExperienceYears);
        Assert.Equal(EmploymentType.Unknown, classification.EmploymentType);
        Assert.Equal(WorkplaceType.Unknown, classification.WorkplaceType);
        Assert.Null(classification.MinimumSalary);
        Assert.Null(classification.MaximumSalary);
        Assert.Null(classification.CurrencyCode);
        Assert.Null(classification.SalaryPeriod);
    }

    [Fact]
    public async Task BoundedFixtureIngestionIsIdempotentAndIncompleteDetailsNeverReconcile()
    {
        using var fixture = new JobSourceFixture();
        fixture.Company.Name = "Mastercard";
        fixture.Source.AtsType = AtsType.Workday;
        fixture.Source.AtsIdentifier = "mastercard/CorporateCareers";
        fixture.Source.CareerPageUrl = Board;
        fixture.Map(fixture.Category.Id.ToString());
        await fixture.Context.SaveChangesAsync();
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var provider = new WorkdayJobSourceProvider(new Factory(http), options: Options.Create(new JobAggregationOptions
        { Workday = new() { MaximumAttempts = 1, BatchSize = 2 } }));
        var jobs = new JobRepository(fixture.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var unit = new UnitOfWork(fixture.Context);
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(fixture.Context),
            new CategoryManagementRepository(fixture.Context), new JobDeduplicationService(jobs, fingerprints, canonical),
            fingerprints, unit, TimeProvider.System, fixture.Locks, canonical);
        var jobService = new JobService(jobs, unit, fixture.Audit, new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System, sources: fixture.Repository);
        var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), jobService,
            Options.Create(new JobAggregationOptions()), TimeProvider.System);
        var runner = new JobSourceRunner(fixture.Repository, [provider], ingestion, unit, TimeProvider.System,
            fixture.Resolver, new ExternalJobNormalizer(), autoPublishService: publisher, jobRepository: jobs);
        var first = await runner.RunAsync(fixture.Source.Id);
        Assert.True(first.Succeeded);
        Assert.True(first.ReconciliationSafe);
        Assert.Equal(4, first.Created);
        Assert.Equal(4, first.Published);
        Assert.Equal(1, first.NonIndiaRejected);
        var before = await fixture.Context.Jobs.ToDictionaryAsync(job => job.ExternalJobId!, job => job.Id);
        Assert.Equal(4, before.Count);
        Assert.All(fixture.Context.Jobs, job =>
        {
            Assert.Equal(JobStatus.Published, job.Status);
            Assert.Equal(fixture.Source.Id, job.JobSourceId);
            Assert.StartsWith(Board.Replace("/CorporateCareers", "/en-US/CorporateCareers", StringComparison.Ordinal), job.ApplicationUrl, StringComparison.Ordinal);
            Assert.Null(job.MinimumSalary);
            Assert.Null(job.MaximumSalary);
            Assert.Null(job.CurrencyCode);
            Assert.Null(job.SalaryPeriod);
        });
        var repeated = await runner.RunAsync(fixture.Source.Id);
        Assert.True(repeated.Succeeded);
        Assert.Equal(0, repeated.Created);
        Assert.Equal(4, repeated.Existing);
        Assert.Equal(0, repeated.Published);
        Assert.Equal(0, repeated.Closed);
        var priorSuccessful = fixture.Source.LastSuccessfulRunAtUtc;
        handler.FailDetail = true;
        var incomplete = await runner.RunAsync(fixture.Source.Id);
        Assert.False(incomplete.Succeeded);
        Assert.False(incomplete.ReconciliationSafe);
        Assert.Equal(0, incomplete.Closed);
        Assert.Equal(priorSuccessful, fixture.Source.LastSuccessfulRunAtUtc);
        Assert.Equal(4, await fixture.Context.Jobs.CountAsync());
        Assert.All(fixture.Context.Jobs, job => Assert.Equal(before[job.ExternalJobId!], job.Id));
        Assert.All(fixture.Context.Jobs, job => Assert.Equal(JobStatus.Published, job.Status));
    }

    private static JobSource Source() => new()
    {
        AtsType = AtsType.Workday, AtsIdentifier = "mastercard/CorporateCareers", CareerPageUrl = Board,
        Company = new Company { Name = "Mastercard" }
    };

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PublicAts", $"mastercard-{name}.json"));

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public bool FailDetail { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.StartsWith(ApiRoot, request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal(ApiRoot + "/jobs", request.RequestUri.AbsoluteUri);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(20, body.RootElement.GetProperty("limit").GetInt32());
                Assert.Equal(0, body.RootElement.GetProperty("offset").GetInt32());
                Assert.Empty(body.RootElement.GetProperty("appliedFacets").EnumerateObject());
                Assert.Equal(string.Empty, body.RootElement.GetProperty("searchText").GetString());
                // The five-detail local sample is an intentionally reduced census, not the live 1,084-job board.
                return Json(Read("sample-listing"));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            var entries = WorkdayJobSourceProvider.ParseListingPage(Read("sample-listing")).Postings;
            var entry = entries.Single(job => ApiRoot + job.ExternalPath == request.RequestUri.AbsoluteUri);
            return FailDetail && entry.BulletId == "R-278780" ? new(HttpStatusCode.ServiceUnavailable) : Json(Read($"detail-{entry.BulletId}"));
        }
        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}
