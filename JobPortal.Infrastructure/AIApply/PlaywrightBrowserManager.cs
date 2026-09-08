using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using JobPortal.Application.Features.AIApply;
using System.Text;
using System.Text.Json;

namespace JobPortal.Infrastructure.AIApply;

public interface IPlaywrightBrowserRuntime
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

public sealed class PlaywrightBrowserManager(IOptions<AIApplyOptions> options) : IAsyncDisposable, IPlaywrightBrowserRuntime
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;
    private int disposed;

    public async Task<IBrowserContext> CreateIsolatedContextAsync(CancellationToken ct, ReadOnlyMemory<byte> storageState = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            playwright ??= await Playwright.CreateAsync();
            if (browser is null || !browser.IsConnected)
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = options.Value.Browser.Headless,
                    ChromiumSandbox = true,
                    DownloadsPath = null
                });
            string? state = null;
            if (!storageState.IsEmpty)
            {
                if (storageState.Length > options.Value.ExternalSessions.MaximumStorageStateBytes)
                    throw new InvalidOperationException("External session state exceeds the configured limit.");
                using var document = JsonDocument.Parse(storageState);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("cookies", out var cookies) || cookies.ValueKind != JsonValueKind.Array ||
                    !document.RootElement.TryGetProperty("origins", out var origins) || origins.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("External session state has an invalid schema.");
                state = Encoding.UTF8.GetString(storageState.Span);
            }
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = false,
                JavaScriptEnabled = true,
                StorageState = state,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
            context.SetDefaultTimeout(Math.Clamp(options.Value.Browser.ActionTimeoutSeconds, 1, 120) * 1000);
            context.SetDefaultNavigationTimeout(Math.Clamp(options.Value.Browser.NavigationTimeoutSeconds, 2, 180) * 1000);
            return context;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await CreateIsolatedContextAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        await gate.WaitAsync();
        try
        {
            if (browser is not null) await browser.DisposeAsync();
            playwright?.Dispose();
        }
        finally { gate.Release(); gate.Dispose(); }
    }
}
