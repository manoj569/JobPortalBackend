using JobPortal.API.Health;
using JobPortal.Application.Features.AIApply;
using JobPortal.Infrastructure.AIApply;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;
using System.Text;

namespace JobPortal.Application.Tests;

[Trait("Category", "BrowserIntegration")]
public sealed class PlaywrightRuntimeReadinessTests
{
    [Fact]
    public async Task BrowserManagerDisposalIsIdempotent()
    {
        var manager = Manager();

        await manager.DisposeAsync();
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task InitialViewportIsConfiguredOnContextAndScreenshotDoesNotHang()
    {
        await using var manager = Manager();
        await using var context = await manager.CreateIsolatedContextAsync(default);
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<main>NAV_PAGE_ONE</main>");

        Assert.Equal(1280, page.ViewportSize?.Width);
        Assert.Equal(800, page.ViewportSize?.Height);
        var screenshot = await page.ScreenshotAsync(new() { Type = Microsoft.Playwright.ScreenshotType.Jpeg, Quality = 65 });
        Assert.True(screenshot.Length > 2);
        Assert.Equal(0xff, screenshot[0]);
        Assert.Equal(0xd8, screenshot[1]);
    }

    [Fact]
    public async Task SharedBrowserCreatesIsolatedSequentialAndConcurrentContextsAndDisposes()
    {
        await using var manager = Manager();
        await using (var first = await manager.CreateIsolatedContextAsync(default))
        {
            await first.AddCookiesAsync([new() { Name = "isolation", Value = "first", Domain = "example.test", Path = "/" }]);
            Assert.Single(await first.CookiesAsync(["https://example.test"]));
        }
        await using (var second = await manager.CreateIsolatedContextAsync(default))
            Assert.Empty(await second.CookiesAsync(["https://example.test"]));

        var contexts = await Task.WhenAll(manager.CreateIsolatedContextAsync(default), manager.CreateIsolatedContextAsync(default));
        try
        {
            await contexts[0].AddCookiesAsync([new() { Name = "bounded", Value = "a", Domain = "example.test", Path = "/" }]);
            Assert.Empty(await contexts[1].CookiesAsync(["https://example.test"]));
            var pages = await Task.WhenAll(contexts.Select(x => x.NewPageAsync()));
            await Task.WhenAll(pages.Select((page, index) => page.SetContentAsync($"<h1>controlled-{index}</h1>")));
            Assert.Equal(["controlled-0", "controlled-1"], await Task.WhenAll(pages.Select(x => x.Locator("h1").InnerTextAsync())));
        }
        finally { foreach (var context in contexts) await context.DisposeAsync(); }
    }

    [Fact]
    public async Task BrowserReadinessIsHealthyWhenDisabledOrAvailableAndUnhealthyWhenMissing()
    {
        var disabled = new AIApplyBrowserHealthCheck(Options.Create(new AIApplyOptions()), new FakeRuntime(false));
        Assert.Equal(HealthStatus.Healthy, (await disabled.CheckHealthAsync(new())).Status);
        var enabled = new AIApplyOptions { Enabled = true, Browser = new() { Enabled = true } };
        Assert.Equal(HealthStatus.Healthy, (await new AIApplyBrowserHealthCheck(Options.Create(enabled), new FakeRuntime(true)).CheckHealthAsync(new())).Status);
        Assert.Equal(HealthStatus.Unhealthy, (await new AIApplyBrowserHealthCheck(Options.Create(enabled), new FakeRuntime(false)).CheckHealthAsync(new())).Status);
    }

    [Fact]
    public async Task InMemoryStorageStateInitializesOnlyItsIsolatedContext()
    {
        await using var manager = Manager();
        var state = Encoding.UTF8.GetBytes("{\"cookies\":[{\"name\":\"session\",\"value\":\"candidate-a\",\"domain\":\"example.test\",\"path\":\"/\",\"expires\":-1,\"httpOnly\":true,\"secure\":true,\"sameSite\":\"Lax\"}],\"origins\":[]}");
        await using var authenticated = await manager.CreateIsolatedContextAsync(default, state);
        await using var otherCandidate = await manager.CreateIsolatedContextAsync(default);
        var cookies = await authenticated.CookiesAsync(["https://example.test"]);
        Assert.Single(cookies); Assert.Equal("candidate-a", cookies[0].Value);
        Assert.Empty(await otherCandidate.CookiesAsync(["https://example.test"]));
    }

    [Fact]
    public async Task SimultaneousContextsIndependentlyIsolateCookiesAndLocalStorage()
    {
        await using var manager = Manager();
        await using var a = await manager.CreateIsolatedContextAsync(default);
        await using var b = await manager.CreateIsolatedContextAsync(default);
        var pageA = await a.NewPageAsync();
        var pageB = await b.NewPageAsync();
        const string origin = "https://isolation.fixture.test/";
        await pageA.RouteAsync("**/*", route => route.FulfillAsync(new() { Body = "<h1>fixture</h1>", ContentType = "text/html" }));
        await pageB.RouteAsync("**/*", route => route.FulfillAsync(new() { Body = "<h1>fixture</h1>", ContentType = "text/html" }));
        await Task.WhenAll(pageA.GotoAsync(origin), pageB.GotoAsync(origin));
        await a.AddCookiesAsync([new() { Name = "AIApplyIsolation", Value = "A_COOKIE", Url = origin }]);
        await b.AddCookiesAsync([new() { Name = "AIApplyIsolation", Value = "B_COOKIE", Url = origin }]);
        await pageA.EvaluateAsync("localStorage.setItem('AIApplyIsolation', 'A_STORAGE')");
        await pageB.EvaluateAsync("localStorage.setItem('AIApplyIsolation', 'B_STORAGE')");

        Assert.Equal("A_COOKIE", Assert.Single(await a.CookiesAsync([origin])).Value);
        Assert.Equal("B_COOKIE", Assert.Single(await b.CookiesAsync([origin])).Value);
        Assert.Equal("A_STORAGE", await pageA.EvaluateAsync<string>("localStorage.getItem('AIApplyIsolation')"));
        Assert.Equal("B_STORAGE", await pageB.EvaluateAsync<string>("localStorage.getItem('AIApplyIsolation')"));
        Assert.DoesNotContain((await a.CookiesAsync([origin])).Select(x => x.Value), x => x == "B_COOKIE");
        Assert.DoesNotContain((await b.CookiesAsync([origin])).Select(x => x.Value), x => x == "A_COOKIE");
    }

    private static PlaywrightBrowserManager Manager() => new(Options.Create(new AIApplyOptions { Browser = new() { Enabled = true, Headless = true, ActionTimeoutSeconds = 10, NavigationTimeoutSeconds = 30 } }));
    private sealed class FakeRuntime(bool available) : IPlaywrightBrowserRuntime { public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(available); }
}
