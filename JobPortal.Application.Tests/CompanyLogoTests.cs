using System.Net;
using System.Reflection;
using System.Text;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CompanyLogoTests
{
    private static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS7sAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.9.8.7/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://168.63.129.16/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://service.internal/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/logo")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("not-a-url")]
    public async Task UnsafeInputDoesNotMakeRequests(string url)
    {
        using var factory = new Factory(_ => throw new InvalidOperationException("Unexpected network call"));
        Assert.False((await new CompanyLogoResolver(factory).ResolveAsync("Example", url, null)).Resolved);
        Assert.Equal(0, factory.Count);
    }

    [Theory]
    [InlineData("https://kyndryl.wd5.myworkdayjobs.com/careers")]
    [InlineData("https://boards.greenhouse.io/acme")]
    [InlineData("https://jobs.lever.co/acme")]
    [InlineData("https://jobs.ashbyhq.com/acme")]
    public async Task AtsCannotBeOfficialWebsiteOrCareerFallback(string url)
    {
        using var factory = new Factory(_ => throw new InvalidOperationException());
        var resolver = new CompanyLogoResolver(factory);
        Assert.False((await resolver.ResolveAsync("Example", url, null)).Resolved);
        Assert.False((await resolver.ResolveAsync("Example", null, url)).Resolved);
        Assert.Equal(0, factory.Count);
    }

    [Theory]
    [InlineData("<link rel='icon' href='/logo.png'>")]
    [InlineData("<link href='/logo.png' rel='shortcut icon'>")]
    [InlineData("<link rel='apple-touch-icon' href='/logo.png'>")]
    [InlineData("<script type='application/ld+json'>{\"@type\":\"Organization\",\"logo\":\"/logo.png\"}</script>")]
    [InlineData("<script type='application/ld+json'>{\"@graph\":[{\"@type\":\"Corporation\",\"logo\":{\"url\":\"/logo.png\"}}]}</script>")]
    public async Task OfficialMetadataResolvesRelativeImage(string html)
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html(html) : Image());
        var result = await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", "https://example.myworkdayjobs.com/careers");
        Assert.True(result.Resolved);
        Assert.Equal("https://example.com/logo.png", result.LogoUrl);
        Assert.Equal("OfficialWebsiteMetadata", result.Source);
        Assert.Equal(2, factory.Count);
    }

    [Fact]
    public async Task OrganizationLogoPrecedesIconAndDuplicateCandidatesAreNotFetchedTwice()
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html("""
            <link rel="icon" href="/icon.png">
            <script type="application/ld+json">{"@type":"Organization","logo":"/logo.png"}</script>
            <link rel="icon" href="/icon.png">
            """) : Image());
        var result = await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null);
        Assert.Equal("https://example.com/logo.png", result.LogoUrl);
        Assert.Equal(2, factory.Count);
    }

    [Fact]
    public async Task RedirectToPrivateAddressIsNeverFollowed()
    {
        using var factory = new Factory(_ => new HttpResponseMessage(HttpStatusCode.Found)
        { Headers = { Location = new Uri("http://127.0.0.1/logo.png") } });
        Assert.False((await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).Resolved);
        Assert.Equal(1, factory.Count);
    }

    [Theory]
    [InlineData("image/png", "<html>error</html>")]
    [InlineData("text/html", "<html>error</html>")]
    [InlineData("image/svg+xml", "<html><svg></svg></html>")]
    public async Task InvalidImageResponsesAreRejected(string type, string body)
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html("<link rel='icon' href='/logo'>") : Response(type, Encoding.UTF8.GetBytes(body)));
        Assert.False((await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).Resolved);
    }

    [Fact]
    public async Task SvgRootAcceptedWithoutXmlParsing()
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html("<link rel='icon' href='/logo.svg'>")
            : Response("image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray()));
        Assert.True((await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).Resolved);
    }

    [Fact]
    public async Task OversizedHtmlAndImageAreRejected()
    {
        using var html = new Factory(_ => Html(new string('x', 512 * 1024 + 1)));
        Assert.False((await new CompanyLogoResolver(html).ResolveAsync("Example", "https://example.com/", null)).Resolved);
        using var image = new Factory(uri => uri.AbsolutePath == "/" ? Html("<link rel='icon' href='/logo'>") : Response("image/png", new byte[2 * 1024 * 1024 + 1]));
        Assert.False((await new CompanyLogoResolver(image).ResolveAsync("Example", "https://example.com/", null)).Resolved);
    }

    [Fact]
    public async Task TimeoutReturnsNoneButCallerCancellationPropagates()
    {
        using var factory = new Factory(_ => throw new TaskCanceledException("Transport timeout"));
        Assert.False((await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).Resolved);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null, cancellation.Token));
    }

    [Fact]
    public async Task UnknownLengthOversizedResponseIsBoundedDuringStreaming()
    {
        using var factory = new Factory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamingContent(new byte[512 * 1024 + 8192]) { Headers = { ContentType = new("text/html") } } });
        Assert.False((await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).Resolved);
        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public async Task SafeRedirectResolvesAgainstFinalPageAndStopsRedirectLoops()
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? new HttpResponseMessage(HttpStatusCode.Found)
            { Headers = { Location = new Uri("/brand/", UriKind.Relative) } }
            : uri.AbsolutePath == "/brand/" ? Html("<link rel='icon' href='logo.png'>") : Image());
        Assert.Equal("https://example.com/brand/logo.png", (await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).LogoUrl);
        Assert.Equal(3, factory.Count);
        using var loop = new Factory(_ => new HttpResponseMessage(HttpStatusCode.Found)
            { Headers = { Location = new Uri("https://example.com/") } });
        Assert.False((await new CompanyLogoResolver(loop).ResolveAsync("Example", "https://example.com/", null)).Resolved);
        Assert.Equal(1, loop.Count);
    }

    [Fact]
    public async Task UntrustedImagesAndDuplicateFailedUrlsAreSkipped()
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html("""
            <link rel="icon" href="https://untrusted.com/logo.png">
            <link rel="icon" href="/logo.png#one">
            <link rel="icon" href="/logo.png#two">
            """) : new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.False((await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).Resolved);
        Assert.Equal(2, factory.Count);
    }

    [Fact]
    public async Task LogoFragmentIsPreservedWhileRequestsAreDeduplicated()
    {
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html("<link rel='icon' href='/logo.svg#brand'>")
            : Response("image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray()));
        Assert.Equal("https://example.com/logo.svg#brand",
            (await new CompanyLogoResolver(factory).ResolveAsync("Example", "https://example.com/", null)).LogoUrl);
        Assert.Equal(2, factory.Count);
    }

    [Fact]
    public async Task NoMetadataIsSuccessfulUnresolvedPreview()
    {
        using var f = new JobSourceFixture();
        using var factory = new Factory(_ => Html("<html><body>No logo</body></html>"));
        var controller = new AdminCompaniesController(Service(f));
        var action = await controller.ResolveLogo(f.Company.Id, new("https://example.com/"), new CompanyLogoResolver(factory), default);
        var preview = Assert.IsType<ApiResponse<CompanyLogoPreviewResponse>>(Assert.IsType<OkObjectResult>(action.Result).Value).Data;
        Assert.False(preview.Resolved);
        Assert.Equal("None", preview.Source);
        Assert.Null(preview.LogoUrl);
        Assert.Null(f.Company.LogoUrl);
    }

    [Fact]
    public async Task ExistingManualLogoRequiresExplicitReplacementAndPreviewNeverSaves()
    {
        using var f = new JobSourceFixture();
        f.Company.LogoUrl = "https://example.com/manual.png";
        await f.Context.SaveChangesAsync();
        using var factory = new Factory(uri => uri.AbsolutePath == "/" ? Html("<link rel='icon' href='/logo.png'>") : Image());
        var controller = new AdminCompaniesController(Service(f));
        var resolver = new CompanyLogoResolver(factory);
        var action = await controller.ResolveLogo(f.Company.Id, new("https://example.com/"), resolver, default);
        var result = Assert.IsType<ApiResponse<CompanyLogoPreviewResponse>>(Assert.IsType<OkObjectResult>(action.Result).Value).Data;
        Assert.Equal(f.Company.LogoUrl, result.LogoUrl);
        Assert.Equal("ExistingCompanyLogo", result.Source);
        Assert.Equal(0, factory.Count);
        await controller.ResolveLogo(f.Company.Id, new("https://example.com/", ReplaceExisting: true), resolver, default);
        Assert.Equal(2, factory.Count);
        Assert.Equal("https://example.com/manual.png", f.Company.LogoUrl);
        var company = f.Company;
        await Service(f).UpdateAsync(company.Id, new(company.Name, company.Slug, company.Description, company.WebsiteUrl,
            null, company.Industry, company.Location, company.EmployeeCount, company.IsVerified));
        Assert.Null(company.LogoUrl);
    }

    [Fact]
    public void PreviewRequiresExistingAdministratorAuthorization()
    {
        var authorization = typeof(AdminCompaniesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorization);
        Assert.Equal("Administrator", authorization.Roles);
        Assert.Empty(typeof(AdminCompaniesController).GetMethod(nameof(AdminCompaniesController.ResolveLogo))!.GetCustomAttributes<AllowAnonymousAttribute>());
        using var handler = CompanyLogoHttpTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses([IPAddress.Parse("10.0.0.1")]));
        Assert.Throws<HttpRequestException>(() => SuccessFactorsHttpTransport.ValidateAddresses([IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/logo.png")]
    public async Task HundredJobsReadOneCompanyLogoWithoutHttp(string? logo)
    {
        using var f = new JobSourceFixture();
        f.Company.LogoUrl = logo;
        f.Context.Jobs.AddRange(Enumerable.Range(1, 100).Select(i => new Job
        {
            Slug = "company-logo-" + i, Title = "Engineer", Company = f.Company, CompanyId = f.Company.Id,
            Category = f.Category, CategoryId = f.Category.Id, Status = JobStatus.Published,
            PublishedAtUtc = JobSourceFixture.Now, ExpiresAtUtc = JobSourceFixture.Now.AddDays(1)
        }));
        await f.Context.SaveChangesAsync();
        using var factory = new Factory(_ => throw new InvalidOperationException("Public reads cannot fetch logos"));
        var repository = new PublicJobRepository(f.Context, new Clock());
        var (items, count) = await repository.SearchAsync(new PublicJobQuery(PageSize: 100));
        Assert.Equal(100, count);
        Assert.Equal(100, items.Count);
        Assert.All(items, item => { Assert.Equal(f.Company.Id, item.CompanyId); Assert.Equal(logo, item.CompanyLogoUrl); });
        var detail = await repository.GetDetailsAsync("company-logo-1");
        Assert.NotNull(detail);
        Assert.Equal(logo, detail.CompanyLogoUrl);
        var controller = new PublicJobsController(new PublicJobService(repository, new PublicJobQueryValidator()));
        var listAction = await controller.Search(new PublicJobQuery(PageSize: 100), default);
        Assert.IsType<OkObjectResult>(listAction.Result);
        var detailAction = await controller.Details("company-logo-1", default);
        var detailResponse = Assert.IsType<ApiResponse<PublicJobDetails>>(Assert.IsType<OkObjectResult>(detailAction.Result).Value);
        Assert.Equal(logo, detailResponse.Data.CompanyLogoUrl);
        Assert.Equal(0, factory.Count);
    }

    private static CompanyManagementService Service(JobSourceFixture f) => new(
        new CompanyManagementRepository(f.Context), new UnitOfWork(f.Context), f.Audit,
        new CreateCompanyRequestValidator(), new UpdateCompanyRequestValidator(), new CompanySearchQueryValidator());
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(JobSourceFixture.Now); }
    private static HttpResponseMessage Html(string html) => Response("text/html", Encoding.UTF8.GetBytes(html));
    private static HttpResponseMessage Image() => Response("image/png", Png);
    private static HttpResponseMessage Response(string type, byte[] bytes) => new(HttpStatusCode.OK)
    { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new(type) } } };
    private sealed class Factory(Func<Uri, HttpResponseMessage> respond) : IHttpClientFactory, IDisposable
    {
        private readonly Handler handler = new(respond);
        public int Count => handler.Count;
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        public void Dispose() => handler.Dispose();
    }
    private sealed class Handler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return Task.FromResult(respond(request.RequestUri!));
        }
    }
    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => CreateContentReadStreamAsync();
    }
}
