using System.Net;
using System.Text;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class ExternalJobProviderTests
{
    [Fact]
    public async Task Greenhouse_MapsPublicBoardResponse()
    {
        const string json = """
        {
          "jobs": [
            null,
            { "id": {} },
            {
              "id": 12345,
              "title": "Senior .NET Developer",
              "absolute_url": "https://boards.greenhouse.io/acme/jobs/12345",
              "location": {
                "name": "Pune, India"
              },
              "content": "Build backend services."
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(json);
        var provider = new GreenhouseExternalJobProvider(
            CreateFactory(handler));

        var source = CreateSource(
            AtsType.Greenhouse,
            "acme");

        var jobs = await provider.FetchJobsAsync(source);

        Assert.Equal(3, jobs.Count);
        Assert.Equal(2, jobs.Count(x => string.IsNullOrEmpty(x.Title)));
        var job = Assert.Single(jobs, x => !string.IsNullOrEmpty(x.Title));

        Assert.Equal("Senior .NET Developer", job.Title);
        Assert.Equal("Acme Corp", job.CompanyName);
        Assert.Equal("Pune, India", job.Location);
        Assert.Equal("Build backend services.", job.Description);
        Assert.Equal(
            "https://boards.greenhouse.io/acme/jobs/12345",
            job.ApplicationUrl);
        Assert.Equal("12345", job.ExternalId);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal(
            "/v1/boards/acme/jobs",
            handler.LastRequestUri.AbsolutePath);
        Assert.Equal("?content=true", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task Lever_MapsPublicPostingsResponse()
    {
        const string json = """
        [
          null,
          { "id": {} },
          {
            "id": "lever-123",
            "text": "Backend Engineer",
            "hostedUrl": "https://jobs.lever.co/acme/lever-123",
            "descriptionPlain": "Build APIs and services.",
            "categories": {
              "location": "Remote - India"
            }
          }
        ]
        """;

        var handler = new StubHttpMessageHandler(json);
        var provider = new LeverExternalJobProvider(
            CreateFactory(handler));

        var source = CreateSource(
            AtsType.Lever,
            "acme");

        var jobs = await provider.FetchJobsAsync(source);

        Assert.Equal(3, jobs.Count);
        Assert.Equal(2, jobs.Count(x => string.IsNullOrEmpty(x.Title)));
        var job = Assert.Single(jobs, x => !string.IsNullOrEmpty(x.Title));

        Assert.Equal("Backend Engineer", job.Title);
        Assert.Equal("Acme Corp", job.CompanyName);
        Assert.Equal("Remote - India", job.Location);
        Assert.Equal(
            "Build APIs and services.",
            job.Description);
        Assert.Equal(
            "https://jobs.lever.co/acme/lever-123",
            job.ApplicationUrl);
        Assert.Equal("lever-123", job.ExternalId);

        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal(
            "/v0/postings/acme",
            handler.LastRequestUri.AbsolutePath);
        Assert.Equal("?mode=json", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task Greenhouse_MissingIdentifier_Throws()
    {
        var handler = new StubHttpMessageHandler("""{"jobs":[]}""");

        var provider = new GreenhouseExternalJobProvider(
            CreateFactory(handler));

        var source = CreateSource(
            AtsType.Greenhouse,
            null);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.FetchJobsAsync(source));

        Assert.Equal(
            "Greenhouse ATS identifier is required.",
            exception.Message);

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Lever_MissingIdentifier_Throws()
    {
        var handler = new StubHttpMessageHandler("[]");

        var provider = new LeverExternalJobProvider(
            CreateFactory(handler));

        var source = CreateSource(
            AtsType.Lever,
            null);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.FetchJobsAsync(source));

        Assert.Equal(
            "Lever ATS identifier is required.",
            exception.Message);

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Greenhouse_HttpFailure_Throws()
    {
        var handler = new StubHttpMessageHandler(
            "{}",
            HttpStatusCode.BadGateway);

        var provider = new GreenhouseExternalJobProvider(
            CreateFactory(handler));

        var source = CreateSource(
            AtsType.Greenhouse,
            "acme");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchJobsAsync(source));
    }

    [Fact]
    public async Task Lever_HttpFailure_Throws()
    {
        var handler = new StubHttpMessageHandler(
            "[]",
            HttpStatusCode.InternalServerError);

        var provider = new LeverExternalJobProvider(
            CreateFactory(handler));

        var source = CreateSource(
            AtsType.Lever,
            "acme");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchJobsAsync(source));
    }

    private static JobSource CreateSource(
        AtsType atsType,
        string? atsIdentifier)
    {
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Acme Corp"
        };

        return new JobSource
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            CareerPageUrl = "https://example.com/careers",
            AtsType = atsType,
            AtsIdentifier = atsIdentifier,
            IsActive = true
        };
    }

    private static IHttpClientFactory CreateFactory(
        HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/")
        };

        return new TestHttpClientFactory(client);
    }

    private sealed class TestHttpClientFactory(
        HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(
        string responseContent,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;

            return Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(
                        responseContent,
                        Encoding.UTF8,
                        "application/json")
                });
        }
    }
}
