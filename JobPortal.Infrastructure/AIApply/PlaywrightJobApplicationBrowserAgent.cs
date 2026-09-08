using System.Diagnostics;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Security.Cryptography;
using System.Text;

namespace JobPortal.Infrastructure.AIApply;

public sealed class PlaywrightJobApplicationBrowserAgent(
    PlaywrightBrowserManager browsers,
    IJobSiteAdapterResolver adapters,
    ExternalNavigationPolicy navigation,
    IAIApplyRepository repository,
    IResumeStorage resumeStorage,
    IApplicationQuestionMatcher questionMatcher,
    IJobSiteAdapterHealthService siteHealth,
    IExternalJobSiteSessionStore sessions,
    IOptions<AIApplyOptions> options,
    ILogger<PlaywrightJobApplicationBrowserAgent> logger) : IJobApplicationBrowserAgent
{
    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning, new EventId(5301, "AIApplyBrowserFailure"),
        "AI Apply browser session ended with category {Category}.");

    public async Task<BrowserApplicationResult> ApplyAsync(BrowserApplicationContext context, CancellationToken ct)
    {
        using var activity = AIApplyTelemetry.ActivitySource.StartActivity("aiapply.browser.application", ActivityKind.Internal);
        var timer = Stopwatch.StartNew();
        if (!options.Value.Browser.Enabled)
            return Failure(AIApplyFailureKind.ApplicationUnavailable,
                "browser_execution_disabled", timer);
        if (!await navigation.IsAllowedAsync(context.ApplicationUrl, ct))
            return Failure(AIApplyFailureKind.ApplicationUnavailable,
                "unsafe_application_url", timer);

        var adapter = adapters.Resolve(context.ApplicationUrl);
        UsableExternalJobSiteSession? session = null;
        if (options.Value.ExternalSessions.Enabled && adapter.Capabilities.SupportsAuthenticatedSession)
            session = await sessions.GetActiveAsync(context.UserId, adapter.Support.Site, ct);
        IBrowserContext browserContext;
        try { browserContext = await browsers.CreateIsolatedContextAsync(ct, session?.StorageState ?? default); }
        finally { if (session is not null) CryptographicOperations.ZeroMemory(session.StorageState); }
        await using (browserContext)
        {
        await browserContext.RouteAsync("**/*", async route =>
        {
            if (route.Request.ResourceType == "document" &&
                (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var target) ||
                 !await navigation.IsAllowedAsync(target, ct)))
                await route.AbortAsync("blockedbyclient");
            else await route.ContinueAsync();
        });
        var page = await browserContext.NewPageAsync();
        try
        {
            var values = await ResolveValuesAsync(context.UserId, ct);
            var resume = await ResolveResumeAsync(context.UserId, ct);
            if (!await siteHealth.CanExecuteAsync(adapter.Support.Site, DateTime.UtcNow, ct)) return Failure(AIApplyFailureKind.WebsiteError, "site_circuit_open", timer) with { Adapter = adapter.Support };
            var result = await adapter.ExecuteAsync(page, context, values, resume, ct);
            if (session is not null)
            {
                if (result.FailureKind is AIApplyFailureKind.LoginRequired or AIApplyFailureKind.HumanVerificationRequired)
                    await sessions.RequireReauthenticationAsync(context.UserId, session.Metadata.Id, session.Metadata.Version, ct);
                else
                {
                    var refreshed = Encoding.UTF8.GetBytes(await browserContext.StorageStateAsync());
                    try { await sessions.SaveAsync(context.UserId, adapter.Support.Site, refreshed, session.Metadata.ExpiresAtUtc, session.Metadata.Version, ct); }
                    finally { CryptographicOperations.ZeroMemory(refreshed); }
                }
            }
            if (result.SubmissionConfirmed) await siteHealth.RecordOutcomeAsync(adapter.Support.Site, true, DateTime.UtcNow, ct);
            else if (result.FailureKind is AIApplyFailureKind.WebsiteError or AIApplyFailureKind.Timeout or AIApplyFailureKind.UnsupportedApplicationFlow) await siteHealth.RecordOutcomeAsync(adapter.Support.Site, false, DateTime.UtcNow, ct);
            if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var finalUri) ||
                !await navigation.IsAllowedAsync(finalUri, ct))
                return Failure(AIApplyFailureKind.ApplicationUnavailable,
                    "unsafe_navigation_detected", timer);
            var events = (result.Events ?? []).Append(new BrowserExecutionEvent("AdapterSelected", DateTime.UtcNow)).ToList();
            AIApplyTelemetry.BrowserDurationSeconds.Record(timer.Elapsed.TotalSeconds, AIApplyTelemetry.Tags("browser_application", result.SubmissionConfirmed ? "confirmed" : "not_confirmed", adapter.Support.Site.ToString()));
            activity?.SetTag("site", adapter.Support.Site.ToString()); activity?.SetTag("adapter", adapter.Support.AdapterName); activity?.SetTag("adapter_version", adapter.Support.AdapterVersion); activity?.SetTag("result", result.SubmissionConfirmed ? "confirmed" : "not_confirmed");
            return result with { Usage = MergeDuration(result.Usage, timer.Elapsed), Events = events, Adapter = result.Adapter ?? adapter.Support };
        }
        catch (TimeoutException)
        {
            Failed(logger, "timeout", null);
            AIApplyTelemetry.BrowserFailures.Add(1, AIApplyTelemetry.Tags("browser_application", "timeout"));
            return Failure(AIApplyFailureKind.Timeout, "browser_timeout", timer);
        }
        catch (PlaywrightException)
        {
            Failed(logger, "playwright_error", null);
            AIApplyTelemetry.BrowserFailures.Add(1, AIApplyTelemetry.Tags("browser_application", "playwright_error"));
            return Failure(AIApplyFailureKind.WebsiteError, "browser_execution_error", timer);
        }
        finally { await page.CloseAsync(); }
        }
    }

    private async Task<BrowserValueSet> ResolveValuesAsync(Guid userId, CancellationToken ct)
    {
        var user = await repository.GetUserProfileAsync(userId, ct)
            ?? throw new InvalidOperationException("Candidate profile is unavailable.");
        var profile = await repository.GetProfileAsync(userId, ct);
        var currentEmployment = user.CandidateExperiences
            .OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.StartDate).FirstOrDefault();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(values, "FirstName", user.FirstName); Add(values, "LastName", user.LastName);
        Add(values, "FullName", $"{user.FirstName} {user.LastName}"); Add(values, "Email", user.Email);
        Add(values, "Phone", user.PhoneNumber); Add(values, "CurrentLocation", user.Location ?? user.CurrentCity);
        Add(values, "City", user.CurrentCity); Add(values, "Country", user.CurrentCountry);
        Add(values, "LinkedIn", user.LinkedInUrl); Add(values, "GitHub", profile?.GitHubUrl);
        Add(values, "Portfolio", user.PortfolioUrl); Add(values, "CurrentCompany", currentEmployment?.CompanyName);
        Add(values, "CurrentJobTitle", currentEmployment?.JobTitle); Add(values, "TotalExperience", user.YearsOfExperience?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(values, "ExpectedSalary", user.ExpectedAnnualSalary?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(values, "CurrentSalary", user.CurrentAnnualSalary?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(values, "NoticePeriod", user.AvailabilityToJoin?.ToString()); Add(values, "WorkAuthorization", profile?.WorkAuthorization);
        Add(values, "VisaSponsorship", profile?.VisaSponsorshipPreference); Add(values, "Relocation", profile?.WillingToRelocate?.ToString());
        var answers = (await repository.GetAnswersAsync(userId, ct)).Where(x => x.IsActive && x.IsVerified && x.Source == JobPortal.Domain.Enums.AIApplyAnswerSource.UserVerified)
            .ToDictionary(x => questionMatcher.Normalize(x.Question), x => x.Answer, StringComparer.Ordinal);
        return new(values, answers);
    }

    private async Task<ResumePayload?> ResolveResumeAsync(Guid userId, CancellationToken ct)
    {
        var user = await repository.GetUserProfileAsync(userId, ct);
        var resumeSize = user?.ResumeSizeBytes;
        if (user?.ResumeStorageKey is null || user.ResumeFileName is null || user.ResumeContentType is null ||
            !resumeSize.HasValue || resumeSize is <= 0 or > 5 * 1024 * 1024 ||
            user.ResumeContentType is not ("application/pdf" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/msword")) return null;
        await using var source = await resumeStorage.OpenReadAsync(user.ResumeStorageKey, ct);
        if (source is null) return null;
        using var memory = new MemoryStream((int)resumeSize.GetValueOrDefault());
        await source.CopyToAsync(memory, ct);
        if (memory.Length != resumeSize.GetValueOrDefault()) return null;
        return new(Path.GetFileName(user.ResumeFileName), user.ResumeContentType, memory.ToArray());
    }

    private static void Add(Dictionary<string, string> values, string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) values[key] = value.Trim(); }
    private static AIApplyUsage MergeDuration(AIApplyUsage? usage, TimeSpan elapsed) => usage is null ? new(0, 0, 0, (decimal)elapsed.TotalSeconds, 0) : usage with { BrowserSeconds = (decimal)elapsed.TotalSeconds };
    private static BrowserApplicationResult Failure(AIApplyFailureKind kind, string code, Stopwatch timer) { timer.Stop(); return new(false, kind, code, Usage: new(0, 0, 0, (decimal)timer.Elapsed.TotalSeconds, 0)); }
}
