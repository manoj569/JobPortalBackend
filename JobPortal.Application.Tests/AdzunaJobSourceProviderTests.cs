using System.Net;
using System.Text;
using System.Net.Http.Headers;
using JobPortal.Application.Features.JobDiscovery;
using JobPortal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AdzunaJobSourceProviderTests
{
    [Fact]
    public async Task SearchMapsProviderResponseAndUsesConfiguredCountry()
    {
        var handler = new Handler("""{"results":[{"id":"42","title":"Engineer","redirect_url":"https://example.test/apply","company":{"display_name":"Acme"},"category":{"label":"Engineering"},"location":{"display_name":"Pune"}}]}""");
        var provider = Provider(handler, true);
        var result = await provider.SearchAsync(new("dotnet", "Pune", "in"), CancellationToken.None);
        var job = Assert.Single(result);
        Assert.Equal("42", job.SourceJobId);
        Assert.Equal("Acme", job.CompanyName);
        Assert.Contains("/in/search/1", handler.RequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchWhenNotConfiguredIsNoOp()
    {
        var handler = new Handler("{}");
        var result = await Provider(handler, false).SearchAsync(new(), CancellationToken.None);
        Assert.Empty(result);
        Assert.Null(handler.RequestUri);
    }

    private static AdzunaJobSourceProvider Provider(Handler handler, bool configured)
    {
        var values = configured ? new Dictionary<string, string?> { ["JobDiscovery:Adzuna:AppId"] = "id", ["JobDiscovery:Adzuna:ApiKey"] = "secret" } : [];
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new AdzunaJobSourceProvider(new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://api.adzuna.com/") }), config);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(string json) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf8" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
