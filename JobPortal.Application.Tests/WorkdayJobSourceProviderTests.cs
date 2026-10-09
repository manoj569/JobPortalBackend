using System.Net;
using System.Text;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class WorkdayJobSourceProviderTests
{
    [Fact]
    public void ValidateSourceAcceptsAccentureCareerSite()
    {
        var source = new JobSource
        {
            AtsType = AtsType.Workday,
            AtsIdentifier = "accenture/AccentureCareers",
            CareerPageUrl = "https://accenture.wd103.myworkdayjobs.com/AccentureCareers"
        };

        var target = WorkdayJobSourceProvider.ValidateSource(source);

        Assert.Equal("accenture", target.Tenant);
        Assert.Equal("AccentureCareers", target.Site);
        Assert.Equal(
            new Uri("https://accenture.wd103.myworkdayjobs.com/"),
            target.Origin);
        Assert.Equal(
            new Uri("https://accenture.wd103.myworkdayjobs.com/wday/cxs/accenture/AccentureCareers"),
            target.ApiRoot);
    }

    [Theory]
    [InlineData("https://accenture.wd103.myworkdayjobs.com/en-US/AccentureCareers")]
    [InlineData("https://accenture.wd103.myworkdayjobs.com/en-us/AccentureCareers")]
    public void ValidateSourceAcceptsLocalizedCareerSite(string url)
    {
        var source = new JobSource
        {
            AtsType = AtsType.Workday,
            AtsIdentifier = "accenture/AccentureCareers",
            CareerPageUrl = url
        };

        var target = WorkdayJobSourceProvider.ValidateSource(source);

        Assert.Equal("accenture", target.Tenant);
        Assert.Equal("AccentureCareers", target.Site);
    }

    [Theory]
    [InlineData("https://example.com/AccentureCareers", "accenture/AccentureCareers")]
    [InlineData("http://accenture.wd103.myworkdayjobs.com/AccentureCareers", "accenture/AccentureCareers")]
    [InlineData("https://accenture.wd103.myworkdayjobs.com/AccentureCareers?x=1", "accenture/AccentureCareers")]
    [InlineData("https://accenture.wd103.myworkdayjobs.com/AccentureCareers", "other/AccentureCareers")]
    [InlineData("https://accenture.wd103.myworkdayjobs.com/en-US/AccentureCareers/job/foo", "accenture/AccentureCareers")]
    public void ValidateSourceRejectsUnsafeOrMismatchedSource(string url, string identifier)
    {
        var source = new JobSource
        {
            AtsType = AtsType.Workday,
            AtsIdentifier = identifier,
            CareerPageUrl = url
        };

        Assert.Throws<InvalidOperationException>(
            () => WorkdayJobSourceProvider.ValidateSource(source));
    }

    [Fact]
    public void ParseListingPageMapsExpectedFields()
    {
        const string json = """
        {
          "total": 2,
          "jobPostings": [
            {
              "title": "Application Developer",
              "externalPath": "/job/Pune/Application-Developer_ATCI-123",
              "locationsText": "Pune",
              "postedOn": "Posted Today",
              "bulletFields": ["ATCI-123"]
            },
            {
              "title": "Business Analyst",
              "externalPath": "/job/Bengaluru/Business-Analyst_ATCI-456",
              "locationsText": "3 Locations",
              "postedOn": "Posted Yesterday",
              "bulletFields": ["ATCI-456"]
            }
          ]
        }
        """;

        var page = WorkdayJobSourceProvider.ParseListingPage(json);

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Postings.Count);

        var first = page.Postings.First();
        Assert.Equal("Application Developer", first.Title);
        Assert.Equal("/job/Pune/Application-Developer_ATCI-123", first.ExternalPath);
        Assert.Equal("Pune", first.LocationsText);
        Assert.Equal("ATCI-123", first.BulletId);
    }

    [Fact]
    public void ParseDetailMapsAccentureStyleJob()
    {
        const string json = """
        {
          "jobPostingInfo": {
            "title": "Custom Software Engineer",
            "jobReqId": "ATCI-5529346-S2023977",
            "jobPostingId": "Custom-Software-Engineer_ATCI-5529346-S2023977-1",
            "jobDescription": "<p>Minimum 3 year(s) of experience is required.</p>",
            "startDate": "2026-10-01",
            "location": "Pune",
            "additionalLocations": ["Bengaluru", "Hyderabad"],
            "timeType": "Full time",
            "remoteType": "Hybrid",
            "jobRequisitionLocation": {
              "country": {
                "alpha2Code": "IN",
                "descriptor": "India"
              }
            }
          }
        }
        """;

        var target = new WorkdayJobSourceProvider.WorkdayTarget(
            new Uri("https://accenture.wd103.myworkdayjobs.com/"),
            new Uri("https://accenture.wd103.myworkdayjobs.com/wday/cxs/accenture/AccentureCareers"),
            "accenture",
            "AccentureCareers");

        var source = new JobSource
        {
            AtsType = AtsType.Workday,
            AtsIdentifier = "accenture/AccentureCareers",
            CareerPageUrl = "https://accenture.wd103.myworkdayjobs.com/AccentureCareers",
            Company = new JobPortal.Domain.Entities.Company { Name = "Accenture" }
        };

        var listing = new WorkdayJobSourceProvider.ListingEntry(
            "Custom Software Engineer",
            "/job/Pune/Custom-Software-Engineer_ATCI-5529346-S2023977-1",
            "Pune",
            "Posted 6 Days Ago",
            "ATCI-5529346-S2023977");

        var job = WorkdayJobSourceProvider.ParseDetail(
            json,
            target,
            source,
            listing);

        Assert.NotNull(job);
        Assert.Equal("ATCI-5529346-S2023977", job.ExternalId);
        Assert.Equal("Custom Software Engineer", job.Title);
        Assert.Equal("Accenture", job.CompanyName);
        Assert.Equal("Pune", job.Location);
        Assert.Contains("Bengaluru", job.AdditionalLocations);
        Assert.Contains("IN", job.CountryCodes);
        Assert.Equal(EmploymentType.FullTime, job.EmploymentType);
        Assert.Equal(WorkplaceType.Hybrid, job.WorkplaceType);
        Assert.Equal(
            "https://accenture.wd103.myworkdayjobs.com/en-US/AccentureCareers/job/Pune/Custom-Software-Engineer_ATCI-5529346-S2023977-1",
            job.ApplicationUrl);
        Assert.True(job.DescriptionIsHtml);
    }

    [Fact]
    public async Task CompleteSnapshotPaginatesAndFetchesEveryDetail()
    {
        var listPage = """
        {
          "total": 2,
          "jobPostings": [
            {
              "title": "Role A",
              "externalPath": "/job/Pune/Role-A_REQ-1",
              "locationsText": "Pune",
              "postedOn": "Posted Today",
              "bulletFields": ["REQ-1"]
            },
            {
              "title": "Role B",
              "externalPath": "/job/Bengaluru/Role-B_REQ-2",
              "locationsText": "Bengaluru",
              "postedOn": "Posted Today",
              "bulletFields": ["REQ-2"]
            }
          ]
        }
        """;

        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/wday/cxs/accenture/AccentureCareers/job/Pune/Role-A_REQ-1"] = Detail("Role A", "REQ-1", "Pune"),
            ["/wday/cxs/accenture/AccentureCareers/job/Bengaluru/Role-B_REQ-2"] = Detail("Role B", "REQ-2", "Bengaluru")
        };

        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath ==
                "/wday/cxs/accenture/AccentureCareers/jobs")
            {
                return JsonResponse(listPage);
            }

            if (request.Method == HttpMethod.Get &&
                details.TryGetValue(
                    request.RequestUri!.AbsolutePath,
                    out var detail))
            {
                return JsonResponse(detail);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = new TestHttpClientFactory(
            new HttpClient(handler));

        var provider = new WorkdayJobSourceProvider(
            factory,
            NullLogger<WorkdayJobSourceProvider>.Instance);

        var source = new JobSource
        {
            Id = Guid.NewGuid(),
            AtsType = AtsType.Workday,
            AtsIdentifier = "accenture/AccentureCareers",
            CareerPageUrl = "https://accenture.wd103.myworkdayjobs.com/AccentureCareers",
            Company = new JobPortal.Domain.Entities.Company { Name = "Accenture" }
        };

        var snapshot = await provider.FetchSnapshotAsync(source);

        Assert.True(snapshot.IsComplete);
        Assert.Equal(0, snapshot.Skipped);
        Assert.Equal(2, snapshot.Jobs.Count);
        Assert.Equal(
    ["REQ-1", "REQ-2"],
    snapshot.Jobs
        .Select(x => x.ExternalId)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!)
        .Order()
        .ToArray());
    }

    private static string Detail(
        string title,
        string reqId,
        string location) =>
        $$"""
        {
          "jobPostingInfo": {
            "title": "{{title}}",
            "jobReqId": "{{reqId}}",
            "jobDescription": "<p>Job description</p>",
            "startDate": "2026-10-01",
            "location": "{{location}}",
            "timeType": "Full time",
            "jobRequisitionLocation": {
              "country": {
                "alpha2Code": "IN",
                "descriptor": "India"
              }
            }
          }
        }
        """;

    private static HttpResponseMessage JsonResponse(
        string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class TestHttpClientFactory(
        HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
