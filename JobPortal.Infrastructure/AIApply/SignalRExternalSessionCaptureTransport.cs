using System.Text;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobPortal.Infrastructure.AIApply;

public sealed class SignalRExternalSessionCaptureTransport(
    PlaywrightBrowserManager browsers,
    ExternalNavigationPolicy navigation,
    IOptions<AIApplyOptions> options,
    TimeProvider clock) : IExternalSessionCaptureTransport
{
    public bool IsAvailable => options.Value.ExternalSessions.Capture.Transport.Enabled;
    public string InteractionMode => "SignalRRemoteBrowser";

    public async Task<IExternalSessionCaptureBrowser> StartAsync(ExternalSessionSiteDescriptor site, CancellationToken ct)
    {
        if (!IsAvailable) throw new InvalidOperationException("Interactive browser transport is disabled.");
        var context = await browsers.CreateIsolatedContextAsync(ct);
        try
        {
            var browser = new Browser(context, site, navigation, options, clock);
            await browser.InitializeAsync(ct);
            return browser;
        }
        catch { await context.DisposeAsync(); throw; }
    }

    private sealed class Browser(
        IBrowserContext context,
        ExternalSessionSiteDescriptor site,
        ExternalNavigationPolicy navigation,
        IOptions<AIApplyOptions> options,
        TimeProvider clock) : IExternalSessionCaptureBrowser
    {
        private IPage? activePage;
        private int disposed;

        public async Task InitializeAsync(CancellationToken ct)
        {
            await context.RouteAsync("**/*", async route =>
            {
                if (route.Request.ResourceType != "document") { await route.ContinueAsync(); return; }
                if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var target) || !AllowedHost(target.DnsSafeHost) ||
                    !await navigation.IsAllowedAsync(target, ct)) { await route.AbortAsync("blockedbyclient"); return; }
                await route.ContinueAsync();
            });
            context.Page += (_, page) => activePage = page;
            activePage = await context.NewPageAsync();
            await activePage.GotoAsync(site.LoginEntryUri.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        }

        public async Task<ExternalSessionCaptureSnapshot> InspectAsync(CancellationToken ct)
        {
            var page = Page();
            var url = new Uri(page.Url);
            var login = await VisibleAsync(page, "input[type=password], form[action*=login i]");
            var challenge = await VisibleAsync(page, "iframe[src*=captcha i], [id*=captcha i], [class*=captcha i], input[autocomplete=one-time-code]");
            var authenticated = !login && !challenge && await VisibleAsync(page, "nav, [aria-label*=account i], [data-test*=profile i], [data-testid*=profile i]");
            return new(url, authenticated, login, challenge);
        }

        public async Task<byte[]> CaptureStorageStateAsync(CancellationToken ct) => Encoding.UTF8.GetBytes(await context.StorageStateAsync());
        public async Task<ExternalSessionBrowserFrame> CaptureFrameAsync(CancellationToken ct)
        {
            var page = Page(); var width = page.ViewportSize?.Width ?? 1280; var height = page.ViewportSize?.Height ?? 800;
            var bytes = await page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 65, FullPage = false, Animations = ScreenshotAnimations.Disabled, Caret = ScreenshotCaret.Hide });
            if (bytes.Length > options.Value.ExternalSessions.Capture.Transport.MaximumFrameBytes)
                throw new InvalidOperationException("Rendered browser frame exceeds the configured limit.");
            return new(bytes, "image/jpeg", width, height, clock.GetUtcNow().UtcDateTime);
        }
        public Task ClickAsync(double x, double y, CancellationToken ct) => Page().Mouse.ClickAsync((float)x, (float)y);
        public Task ScrollAsync(double deltaX, double deltaY, CancellationToken ct) => Page().Mouse.WheelAsync((float)deltaX, (float)deltaY);
        public Task InsertTextAsync(string text, CancellationToken ct) => Page().Keyboard.InsertTextAsync(text);
        public Task PressKeyAsync(string key, CancellationToken ct) => Page().Keyboard.PressAsync(key);
        public async Task GoBackAsync(CancellationToken ct) { await Page().GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded }); }
        public Task ResizeAsync(int width, int height, CancellationToken ct) => Page().SetViewportSizeAsync(width, height);
        public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref disposed, 1) == 0) await context.DisposeAsync(); }
        private IPage Page() => activePage is { IsClosed: false } ? activePage : throw new InvalidOperationException("Interactive browser page is unavailable.");
        private bool AllowedHost(string host) => (site.AllowedAuthenticationHosts ?? new HashSet<string> { site.LoginEntryUri.DnsSafeHost })
            .Any(allowed => host.Equals(allowed, StringComparison.OrdinalIgnoreCase) || host.EndsWith('.' + allowed, StringComparison.OrdinalIgnoreCase));
        private static async Task<bool> VisibleAsync(IPage page, string selector) => await page.Locator(selector).First.IsVisibleAsync();
    }
}
