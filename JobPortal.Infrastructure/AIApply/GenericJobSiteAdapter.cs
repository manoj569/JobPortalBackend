using System.Diagnostics;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobPortal.Infrastructure.AIApply;

public sealed partial class GenericJobSiteAdapter(IApplicationFieldMapper mapper,
    IOptions<AIApplyOptions> options, IApplicationExecutionCheckpoint? checkpoint = null) : IJobSiteAdapter
{
    public int Priority => int.MinValue;
    public SiteAdapterSupportResult Support => new(JobSiteIdentifier.Generic, JobSiteSupportLevel.GenericFallback, "Standards-based generic form handling.", false, false, nameof(GenericJobSiteAdapter), "1");
    public JobSiteCapabilities Capabilities => new(true, false, true, true, true, true, true, false, false);
    public bool CanHandle(Uri uri) => uri.Scheme == Uri.UriSchemeHttps;

    public async Task<BrowserApplicationResult> ExecuteAsync(IPage page,
        BrowserApplicationContext context, BrowserValueSet values, ResumePayload? resume,
        CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var events = new List<BrowserExecutionEvent>();
        ct.ThrowIfCancellationRequested();
        Event(events, "BrowserSessionStarted");
        var questions = new List<DetectedApplicationQuestion>();
        Event(events, "NavigationStarted");
        var navigation = await page.GotoAsync(context.ApplicationUrl.AbsoluteUri,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Event(events, "NavigationCompleted");
        if (navigation?.Status == 429 || navigation?.Status >= 500)
            return Result(false, AIApplyFailureKind.WebsiteError, "site_temporarily_unavailable", null, started, events);
        var initialText = await page.Locator("body").InnerTextAsync();
        if (ClosedTerms().IsMatch(initialText)) return Result(false, AIApplyFailureKind.JobExpired, "external_job_closed", null, started, events);
        if (AlreadyAppliedTerms().IsMatch(initialText)) return Result(false, AIApplyFailureKind.Duplicate, "external_application_already_submitted", null, started, events);

        for (var step = 0; step < Math.Clamp(options.Value.Browser.MaximumSteps, 1, 20); step++)
        {
            ct.ThrowIfCancellationRequested();
            var gate = await DetectGateAsync(page);
            if (gate is not null)
                return Result(false, gate.Value.Kind, gate.Value.Code,
                    [new(gate.Value.Question, "HumanVerification", Required: true)], started, events);

            await DismissSafeOverlaysAsync(page);
            var fields = page.Locator("input:not([type=hidden]), textarea, select, [role=combobox]");
            var count = await fields.CountAsync();
            if (count == 0)
            {
                var existing = await DetectSubmissionAsync(page);
                if (existing.Confirmed) return Result(true, AIApplyFailureKind.None,
                    "submission_confirmed", null, started, events, existing);
                return Result(false, AIApplyFailureKind.UnsupportedApplicationFlow,
                    "unsupported_application_flow", null, started, events);
            }
            Event(events, "FormDetected", count);

            for (var index = 0; index < count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var field = fields.Nth(index);
                if (!await field.IsVisibleAsync() || !await field.IsEnabledAsync()) continue;
                var descriptor = await DescribeAsync(field, index);
                if (descriptor.FieldType is "submit" or "button" or "reset") continue;
                var semantic = mapper.Map(descriptor.Label, descriptor.Name,
                    descriptor.Placeholder, descriptor.FieldType, descriptor.NearbyText);
                string? verified = null;
                if (IsSensitive(descriptor) && !TryVerified(values, descriptor, out verified))
                {
                    if (descriptor.Required) questions.Add(ToQuestion(descriptor));
                    continue;
                }
                var candidateValue = verified ?? (semantic is not null && values.Values.TryGetValue(semantic, out var known) ? known : null);
                if (descriptor.FieldType == "file")
                {
                    if (resume is not null) { await field.SetInputFilesAsync(new FilePayload { Name = resume.FileName, MimeType = resume.ContentType, Buffer = resume.Content }); Event(events, "ResumeUploaded"); }
                    else if (descriptor.Required) questions.Add(new("Please upload a resume for this application.", "File", Required: true));
                    continue;
                }
                if (descriptor.FieldType == "checkbox")
                {
                    if (await field.IsCheckedAsync()) continue;
                    if (descriptor.Required) questions.Add(new(descriptor.Label ?? "Please confirm the required declaration.", "Checkbox", ["Confirmed"], true));
                    continue;
                }
                if (descriptor.FieldType == "radio")
                {
                    if (await field.EvaluateAsync<bool>("e => !!(e.name && e.form?.querySelector(`input[type=radio][name='${CSS.escape(e.name)}']:checked`))")) continue;
                    if (await field.IsCheckedAsync()) continue;
                    if (candidateValue is not null && OptionMatches(descriptor.CurrentValue, candidateValue)) await field.CheckAsync();
                    else if (descriptor.Required) questions.Add(ToQuestion(descriptor));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(candidateValue))
                {
                    if (descriptor.Required) questions.Add(ToQuestion(descriptor));
                    continue;
                }
                if (descriptor.FieldType == "select")
                {
                    if (!await SelectAsync(field, descriptor.Options, candidateValue) && descriptor.Required) questions.Add(ToQuestion(descriptor));
                    continue;
                }
                if (descriptor.FieldType == "combobox")
                {
                    if (!await SelectCustomAsync(page, field, candidateValue) && descriptor.Required)
                        questions.Add(ToQuestion(descriptor));
                    continue;
                }
                if (await IsEditableAsync(field) && string.IsNullOrWhiteSpace(descriptor.CurrentValue))
                    await field.FillAsync(candidateValue);
            }

            if (questions.Count > 0)
            {
                Event(events, "UnknownQuestionDetected", questions.Count);
                return Result(false, AIApplyFailureKind.MissingUserInformation,
                    "unknown_required_question", questions, started, events);
            }

            var validation = await DetectValidationErrorsAsync(page);
            if (validation.Count > 0)
                return Result(false, AIApplyFailureKind.FieldMappingError,
                    "external_form_validation_error", validation.Select(x => new DetectedApplicationQuestion(x, "Validation", Required: true)).ToList(), started, events);

            var submit = await FindButtonAsync(page, SubmitLabels());
            var next = await FindButtonAsync(page, NextLabels());
            if (submit is not null)
            {
                if (context.RequireConfirmationBeforeSubmit && !HasSubmissionConfirmation(values))
                    return Result(false, AIApplyFailureKind.RequiredUserDeclaration,
                        "submission_confirmation_required", [new("Confirm final submission of this application.", "Confirmation", ["Confirm"], true)], started, events);
                var before = page.Url;
                if (checkpoint is not null) await checkpoint.MarkSubmissionAttemptedAsync(context.ApplicationId, ct);
                await submit.ClickAsync(); Event(events, "SubmissionClicked");
                await page.WaitForTimeoutAsync(Math.Clamp(options.Value.Browser.SubmissionDetectionTimeoutSeconds, 1, 60) * 1000);
                var detection = await DetectSubmissionAsync(page, before);
                if (detection.Confirmed && detection.Confidence >= options.Value.Browser.SubmissionConfidenceThreshold)
                { if (checkpoint is not null) await checkpoint.MarkSubmissionConfirmedAsync(context.ApplicationId, ct); Event(events, "SubmissionConfirmed"); return Result(true, AIApplyFailureKind.None, "submission_confirmed", null, started, events, detection); }
                Event(events, "SubmissionUnconfirmed");
                return Result(false, AIApplyFailureKind.SubmissionUnconfirmed,
                    "submission_unconfirmed", null, started, events, detection);
            }
            if (next is null) return Result(false, AIApplyFailureKind.UnsupportedApplicationFlow,
                "unsupported_application_flow", null, started, events);
            await next.ClickAsync(); Event(events, "StepCompleted");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }
        return Result(false, AIApplyFailureKind.UnsupportedApplicationFlow,
            "maximum_form_steps_exceeded", null, started, events);
    }

    private static async Task<ExternalApplicationField> DescribeAsync(ILocator field, int index)
    {
        var type = (await field.GetAttributeAsync("type") ?? await field.EvaluateAsync<string>("e => e.tagName.toLowerCase()") ?? "text").ToLowerInvariant();
        if (string.Equals(await field.GetAttributeAsync("role"), "combobox", StringComparison.OrdinalIgnoreCase)) type = "combobox";
        if (type == "select-one") type = "select";
        var label = await field.EvaluateAsync<string?>("e => { const l=e.labels?.[0]; const f=e.closest('fieldset'); return l?.innerText || e.getAttribute('aria-label') || f?.querySelector('legend')?.innerText || null; }");
        var nearby = await field.EvaluateAsync<string?>("e => e.closest('fieldset, .field, .form-group')?.querySelector('legend,label')?.innerText || null");
        var options = type == "select" ? await field.Locator("option").AllTextContentsAsync() : [];
        var current = type is "checkbox" or "radio" ? await field.GetAttributeAsync("value") : await field.InputValueAsync();
        return new($"field-{index}", type, Clean(label), await field.GetAttributeAsync("name"), await field.GetAttributeAsync("placeholder"), await field.GetAttributeAsync("aria-label"), await field.GetAttributeAsync("autocomplete"), Clean(nearby), options.Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), await field.GetAttributeAsync("required") is not null || string.Equals(await field.GetAttributeAsync("aria-required"), "true", StringComparison.OrdinalIgnoreCase), current);
    }

    private static async Task<(AIApplyFailureKind Kind, string Code, string Question)?> DetectGateAsync(IPage page)
    {
        if (await page.Locator("iframe[src*='recaptcha'], iframe[src*='hcaptcha'], iframe[src*='challenges.cloudflare'], .g-recaptcha, .h-captcha, [data-sitekey]").CountAsync() > 0)
            return (AIApplyFailureKind.HumanVerificationRequired, "human_verification_required", "Complete the external site's human verification.");
        if (await page.Locator("input[autocomplete='one-time-code'], input[name*='otp' i], input[aria-label*='verification code' i]").CountAsync() > 0)
            return (AIApplyFailureKind.HumanVerificationRequired, "mfa_required", "Complete the external site's verification step.");
        if (await page.Locator("input[type=password]").CountAsync() > 0)
            return (AIApplyFailureKind.LoginRequired, "external_login_required", "Sign in to the external application site.");
        return null;
    }

    private static async Task DismissSafeOverlaysAsync(IPage page)
    {
        foreach (var name in new[] { "Close", "Dismiss", "Accept essential cookies", "Reject optional cookies" })
        {
            var button = page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).First;
            if (await button.CountAsync() > 0 && await button.IsVisibleAsync()) { await button.ClickAsync(); break; }
        }
    }
    private static async Task<ILocator?> FindButtonAsync(IPage page, IReadOnlyList<string> labels)
    {
        foreach (var label in labels) { var button = page.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).First; if (await button.CountAsync() > 0 && await button.IsVisibleAsync() && await button.IsEnabledAsync()) return button; }
        var buttons = page.Locator("button, input[type=submit], input[type=button]");
        for (var index = 0; index < await buttons.CountAsync(); index++)
        {
            var button = buttons.Nth(index);
            var text = await button.InnerTextAsync();
            if (string.IsNullOrWhiteSpace(text)) text = await button.GetAttributeAsync("value") ?? string.Empty;
            if (labels.Any(label => Normalize(label) == Normalize(text)) && await button.IsVisibleAsync() && await button.IsEnabledAsync())
                return button;
        }
        return null;
    }
    private static async Task<bool> SelectAsync(ILocator field, IReadOnlyList<string> options, string value)
    {
        var match = options.FirstOrDefault(x => OptionMatches(x, value)); if (match is null) return false;
        await field.SelectOptionAsync(new SelectOptionValue { Label = match }); return true;
    }
    private static async Task<bool> SelectCustomAsync(IPage page, ILocator field, string value)
    {
        await field.ClickAsync();
        var options = page.GetByRole(AriaRole.Option);
        for (var index = 0; index < await options.CountAsync(); index++)
        {
            var option = options.Nth(index);
            if (OptionMatches(await option.InnerTextAsync(), value) && await option.IsVisibleAsync())
            { await option.ClickAsync(); return true; }
        }
        await field.PressAsync("Escape");
        return false;
    }
    private static bool OptionMatches(string? option, string value) => Normalize(option) == Normalize(value) || Normalize(option) is "yes" && Normalize(value) is "true" || Normalize(option) is "no" && Normalize(value) is "false";
    private static bool TryVerified(BrowserValueSet values, ExternalApplicationField field, out string? answer)
    {
        // Radio/checkbox labels are commonly only "Yes" or "No"; the surrounding
        // legend carries the actual sensitive question stored in answer memory.
        foreach (var key in new[] { field.NearbyText, field.Label, field.Name }.Select(Normalize).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal))
            if (values.VerifiedAnswers.TryGetValue(key, out answer)) return true;
        answer = null;
        return false;
    }
    private static bool HasSubmissionConfirmation(BrowserValueSet values) =>
        values.VerifiedAnswers.TryGetValue(Normalize("Confirm final submission of this application."), out var answer) &&
        string.Equals(answer, "Confirm", StringComparison.OrdinalIgnoreCase);
    private static bool IsSensitive(ExternalApplicationField f) { var text = Normalize($"{f.Label} {f.Name} {f.NearbyText}"); return SensitiveTerms().IsMatch(text); }
    private static Task<bool> IsEditableAsync(ILocator locator) => locator.IsEditableAsync();
    private static DetectedApplicationQuestion ToQuestion(ExternalApplicationField field) => new(field.Label ?? field.NearbyText ?? "A required application field needs your answer.", field.FieldType, field.Options, field.Required);
    private static async Task<IReadOnlyList<string>> DetectValidationErrorsAsync(IPage page) => (await page.Locator("[role=alert], .field-validation-error, .error:not(:empty), [aria-invalid=true] + *").AllTextContentsAsync()).Select(Clean).Where(x => !string.IsNullOrWhiteSpace(x)).Take(10).Cast<string>().ToList();
    private static async Task<SubmissionDetectionResult> DetectSubmissionAsync(IPage page, string? previousUrl = null)
    {
        var text = await page.Locator("body").InnerTextAsync(); var match = ConfirmationTerms().Match(text); var confirmed = match.Success; var confidence = confirmed ? (previousUrl is not null && !string.Equals(previousUrl, page.Url, StringComparison.Ordinal) ? .95m : .85m) : 0m;
        return new(confirmed, confidence, confirmed ? "ConfirmationText" : "None", confirmed ? match.Value[..Math.Min(120, match.Value.Length)] : null, null, page.Url);
    }
    private static BrowserApplicationResult Result(bool confirmed, AIApplyFailureKind failure, string code, IReadOnlyList<DetectedApplicationQuestion>? questions, Stopwatch timer, IReadOnlyList<BrowserExecutionEvent> events, SubmissionDetectionResult? submission = null)
    {
        timer.Stop();
        var completedEvents = events.ToList();
        Event(completedEvents, "BrowserSessionCompleted");
        return new(confirmed, failure, code, questions,
            new(0, 0, 0, (decimal)timer.Elapsed.TotalSeconds, 0), submission, completedEvents);
    }
    private static void Event(List<BrowserExecutionEvent> events, string code, int? count = null) => events.Add(new(code, DateTime.UtcNow, count));
    private static string Normalize(string? value) => string.Join(' ', (value ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))[..Math.Min(500, string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Length)];
    private static IReadOnlyList<string> NextLabels() => ["Next", "Continue", "Save & Continue", "Proceed", "Review"];
    private static IReadOnlyList<string> SubmitLabels() => ["Submit Application", "Submit", "Send Application", "Complete Application", "Finish", "Apply"];
    [GeneratedRegex(@"captcha|i'm not a robot|human verification|work authori[sz]ation|authori[sz]ed to work|visa|sponsorship|salary|compensation|notice period|available to (?:start|join)|relocat|(?:government|security) clearance|criminal|disability|veteran|race|ethnicity|gender|demographic|non-compete|conflict of interest|declaration|consent|certif(?:y|ication)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SensitiveTerms();
    [GeneratedRegex(@"thank you for applying|application (?:has been )?submitted|application received|successfully applied|we received your application", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ConfirmationTerms();
    [GeneratedRegex(@"job (?:is )?closed|no longer accepting applications|position (?:has been )?filled|posting (?:was )?removed", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ClosedTerms();
    [GeneratedRegex(@"already applied|already submitted|application previously received", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex AlreadyAppliedTerms();
}
