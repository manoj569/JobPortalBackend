using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PlaywrightBrowserAdapterTests : IAsyncLifetime
{
    private IPlaywright playwright = null!;
    private IBrowser browser = null!;
    private readonly AIApplyOptions options = new() { Browser = new() { MaximumSteps = 4, SubmissionDetectionTimeoutSeconds = 1, SubmissionConfidenceThreshold = .8m } };

    public async Task InitializeAsync() { playwright = await Playwright.CreateAsync(); browser = await playwright.Chromium.LaunchAsync(new() { Headless = true }); }
    public async Task DisposeAsync() { await browser.DisposeAsync(); playwright.Dispose(); }

    [Fact]
    public async Task SimpleFormFillsKnownValuesAndRequiresIndependentConfirmationEvidence()
    {
        const string html = """<form><label>First Name<input name="first_name" required></label><label>Email<input type="email" required></label><button type="button" onclick="done()">Submit Application</button></form><script>function done(){document.body.innerHTML='<h1>Application submitted</h1>';}</script>""";
        var result = await Execute(html, Values(("FirstName", "Ada"), ("Email", "ada@example.test")));
        Assert.True(result.SubmissionConfirmed, $"{result.FailureKind}:{result.ResultCode}:{string.Join('|', result.Questions?.Select(x => x.Text) ?? [])}"); Assert.True(result.Submission!.Confirmed); Assert.True(result.Submission.Confidence >= .8m); Assert.True(result.Usage!.BrowserSeconds > 0);
    }

    [Fact]
    public async Task ClickingSubmitWithoutEvidenceNeverReportsSubmitted()
    {
        const string html = """<form><label>First Name<input name='first_name' required></label><button type='button'>Submit Application</button></form>""";
        var result = await Execute(html, Values(("FirstName", "Ada")));
        Assert.False(result.SubmissionConfirmed); Assert.Equal(AIApplyFailureKind.SubmissionUnconfirmed, result.FailureKind);
    }

    [Fact]
    public async Task UnknownRequiredQuestionIsReturnedWithoutGuessing()
    {
        const string html = """<form><label>Are you bound by a non-compete?<input required name='non_compete'></label><button type='button'>Submit Application</button></form>""";
        var result = await Execute(html, Values());
        Assert.False(result.SubmissionConfirmed); Assert.Equal(AIApplyFailureKind.MissingUserInformation, result.FailureKind); Assert.Contains(result.Questions!, x => x.Text.Contains("non-compete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CaptchaIsDetectedAndNeverInteractedWith()
    {
        var result = await Execute("<div class='g-recaptcha' data-sitekey='x'></div>", Values());
        Assert.Equal(AIApplyFailureKind.HumanVerificationRequired, result.FailureKind); Assert.Equal("human_verification_required", result.ResultCode);
    }

    [Theory]
    [InlineData("<h1>This job is no longer accepting applications</h1>", AIApplyFailureKind.JobExpired)]
    [InlineData("<h1>You already applied for this position</h1>", AIApplyFailureKind.Duplicate)]
    public async Task ClosedAndAlreadyAppliedStatesStopBeforeFormInteraction(string html, AIApplyFailureKind expected)
    {
        var result = await Execute(html, Values());
        Assert.Equal(expected, result.FailureKind); Assert.False(result.SubmissionConfirmed);
    }

    [Fact]
    public async Task LoginAndMfaAreDetected()
    {
        var login = await Execute("<label>Password<input type='password'></label>", Values());
        var mfa = await Execute("<label>Verification code<input autocomplete='one-time-code'></label>", Values());
        Assert.Equal(AIApplyFailureKind.LoginRequired, login.FailureKind); Assert.Equal(AIApplyFailureKind.HumanVerificationRequired, mfa.FailureKind);
    }

    [Fact]
    public async Task ConfirmationSettingPausesBeforeFinalClick()
    {
        const string html = """<form><label>First Name<input name='first_name' required></label><button id='submit' type='button' onclick=\"document.body.innerHTML='Application submitted'\">Submit Application</button></form>""";
        var result = await Execute(html, Values(("FirstName", "Ada")), requireConfirmation: true);
        Assert.Equal(AIApplyFailureKind.RequiredUserDeclaration, result.FailureKind); Assert.Contains(result.Questions!, x => x.Type == "Confirmation");
    }

    [Fact]
    public async Task MultiStepFormIsReinspectedAndSubmitted()
    {
        const string html = """<div id="step"><label>First Name<input name="first_name" required></label><button type="button" onclick="showNext()">Next</button></div><script>function showNext(){document.getElementById('step').innerHTML='<label>Country<select name="country" required><option>India</option></select></label><button type="button" onclick="done()">Submit Application</button>';} function done(){document.body.innerHTML='<h1>Thank you for applying</h1>';}</script>""";
        var result = await Execute(html, Values(("FirstName", "Ada"), ("Country", "India")));
        Assert.True(result.SubmissionConfirmed, $"{result.FailureKind}:{result.ResultCode}:{string.Join('|', result.Questions?.Select(x => x.Text) ?? [])}"); Assert.Contains(result.Events!, x => x.Code == "StepCompleted");
    }

    [Fact]
    public async Task RequiredDeclarationIsNotAutomaticallyChecked()
    {
        const string html = """<form><label><input type='checkbox' required>I certify this declaration</label><button type='button'>Submit Application</button></form>""";
        var result = await Execute(html, Values());
        Assert.False(result.SubmissionConfirmed); Assert.Contains(result.Questions!, x => x.Type.Equals("checkbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResumeNativeSelectAndRadioAreHandledWithKnownValues()
    {
        const string html = """<form><label>Country<select required><option>India</option></select></label><fieldset><legend>Are you willing to relocate?</legend><label><input type="radio" name="relocate" value="Yes" required>Yes</label><label><input type="radio" name="relocate" value="No" required>No</label></fieldset><label>Resume<input type="file" required></label><button type="button" onclick="document.body.innerHTML='Application received'">Submit Application</button></form>""";
        var resume = new ResumePayload("resume.pdf", "application/pdf", [1, 2, 3]);
        var result = await Execute(html, new BrowserValueSet(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Country"] = "India" },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["are you willing to relocate?"] = "Yes" }), resume: resume);
        Assert.True(result.SubmissionConfirmed, $"{result.FailureKind}:{result.ResultCode}"); Assert.Contains(result.Events!, x => x.Code == "ResumeUploaded");
    }

    [Fact]
    public async Task AccessibleCustomDropdownSelectsOnlyMatchingOption()
    {
        const string html = """<div role="combobox" aria-label="Country" aria-required="true" tabindex="0" onclick="list.hidden=false"></div><div id="list" role="listbox" hidden><div role="option" onclick="list.hidden=true">India</div><div role="option">Canada</div></div><button type="button" onclick="document.body.innerHTML='Successfully applied'">Submit Application</button>""";
        var result = await Execute(html, Values(("Country", "India")));
        Assert.True(result.SubmissionConfirmed, $"{result.FailureKind}:{result.ResultCode}");
    }

    [Fact]
    public async Task UnsafeSchemesAndLoopbackAreRejectedByNavigationPolicy()
    {
        var policy = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()));
        Assert.False(await policy.IsAllowedAsync(new Uri("http://example.com/apply"), CancellationToken.None));
        Assert.False(await policy.IsAllowedAsync(new Uri("https://127.0.0.1/apply"), CancellationToken.None));
    }

    [Theory]
    [InlineData("Work authorization")]
    [InlineData("Will you require visa sponsorship?")]
    [InlineData("Salary expectation")]
    [InlineData("Notice period")]
    [InlineData("Are you willing to relocate?")]
    [InlineData("Security clearance")]
    [InlineData("Criminal declaration")]
    [InlineData("Non-compete conflict")]
    [InlineData("Disability status")]
    [InlineData("Gender")]
    [InlineData("Race or ethnicity")]
    [InlineData("Veteran status")]
    [InlineData("Demographic disclosure")]
    [InlineData("I consent and certify this declaration")]
    public async Task SensitiveAnswersRequireExplicitVerifiedCandidateAnswer(string label)
    {
        var html = $"<form><label>{label}<input name='sensitive' required></label><button type='button'>Submit Application</button></form>";
        var result = await Execute(html, Values(("FullName", "A value that must not be inferred")));

        Assert.Equal(AIApplyFailureKind.MissingUserInformation, result.FailureKind);
        Assert.Contains(result.Questions!, question => question.Text.Contains(label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExistingCandidateEnteredValueIsNeverOverwritten()
    {
        const string html = "<form><label>First Name<input name='first_name' required value='Candidate entered'></label><label>Last Name<input name='last_name' required></label><button type='button' onclick=\"document.body.innerHTML='Application received'\">Submit Application</button></form>";
        await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false });
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/*", route => route.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = html }));
        var adapter = new GenericJobSiteAdapter(new DeterministicApplicationFieldMapper(), Options.Create(options));

        var result = await adapter.ExecuteAsync(page, new(Guid.NewGuid(), Guid.NewGuid(), new("https://forms.test/apply"), true),
            Values(("FirstName", "Profile value"), ("LastName", "Lovelace")), null, CancellationToken.None);

        Assert.Equal(AIApplyFailureKind.RequiredUserDeclaration, result.FailureKind);
        Assert.Equal("Candidate entered", await page.Locator("input[name=first_name]").InputValueAsync());
        Assert.Equal("Lovelace", await page.Locator("input[name=last_name]").InputValueAsync());
    }

    [Theory]
    [InlineData("https://8.8.8.8/apply")]
    [InlineData("https://[2001:4860:4860::8888]/apply")]
    public async Task NavigationPolicyRejectsDirectIpLiterals(string url)
    {
        var policy = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()));
        Assert.False(await policy.IsAllowedAsync(new Uri(url), CancellationToken.None));
    }

    [Fact]
    public async Task CancellationStopsBeforeNavigation()
    {
        await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false });
        var page = await context.NewPageAsync();
        var adapter = new GenericJobSiteAdapter(new DeterministicApplicationFieldMapper(), Options.Create(options));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ExecuteAsync(page,
            new(Guid.NewGuid(), Guid.NewGuid(), new("https://forms.test/apply"), false),
            Values(), null, cancellation.Token));
        Assert.Equal("about:blank", page.Url);
    }

    [Theory]
    [InlineData("https://boards.greenhouse.io/acme/jobs/1", JobSiteIdentifier.Greenhouse)]
    [InlineData("https://jobs.lever.co/acme/1", JobSiteIdentifier.Lever)]
    [InlineData("https://jobs.ashbyhq.com/acme/1", JobSiteIdentifier.Ashby)]
    [InlineData("https://jobs.smartrecruiters.com/Acme/1", JobSiteIdentifier.SmartRecruiters)]
    public void SpecificEnabledAdapterWinsOverGeneric(string url, JobSiteIdentifier expected)
    {
        Assert.Equal(expected, SiteResolver().Resolve(new(url)).Support.Site);
    }

    [Fact]
    public void LookalikeDomainUsesGenericFallback()
    {
        Assert.Equal(JobSiteIdentifier.Generic, SiteResolver().Resolve(new("https://evilgreenhouse.io/jobs/1")).Support.Site);
    }

    [Fact]
    public void AccountOrientedSiteReportsLoginRequiredWithoutCredentials()
    {
        var adapter = SiteResolver().Resolve(new("https://www.linkedin.com/jobs/view/1"));
        Assert.Equal(JobSiteSupportLevel.LoginRequired, adapter.Support.SupportLevel);
        Assert.True(adapter.Capabilities.SupportsAuthenticatedSession);
    }

    [Fact]
    public async Task DisabledSessionStoreNeverReturnsStorageState()
    {
        var store = new DisabledExternalJobSiteSessionStore();
        Assert.Null(await store.GetActiveAsync(Guid.NewGuid(), JobSiteIdentifier.LinkedIn, default));
        await store.RevokeAsync(Guid.NewGuid(), Guid.NewGuid(), default);
    }

    private static JobSiteAdapterResolver SiteResolver()
    {
        var configured = new AIApplyOptions { Sites = new() };
        configured.Sites.Greenhouse.Enabled = configured.Sites.Lever.Enabled = configured.Sites.Ashby.Enabled = configured.Sites.SmartRecruiters.Enabled = configured.Sites.LinkedIn.Enabled = true;
        var siteOptions = Options.Create(configured); var generic = new GenericJobSiteAdapter(new DeterministicApplicationFieldMapper(), siteOptions);
        IJobSiteAdapter[] adapters = [generic, new GreenhouseJobSiteAdapter(generic, siteOptions), new LeverJobSiteAdapter(generic, siteOptions), new AshbyJobSiteAdapter(generic, siteOptions), new SmartRecruitersJobSiteAdapter(generic, siteOptions), new AccountOrientedJobSiteAdapter(generic, siteOptions)];
        return new(adapters);
    }

    private async Task<BrowserApplicationResult> Execute(string html, BrowserValueSet values, bool requireConfirmation = false, ResumePayload? resume = null)
    {
        await using var context = await browser.NewContextAsync(new() { AcceptDownloads = false });
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/*", route => route.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = html }));
        var adapter = new GenericJobSiteAdapter(new DeterministicApplicationFieldMapper(), Options.Create(options));
        return await adapter.ExecuteAsync(page, new(Guid.NewGuid(), Guid.NewGuid(), new("https://forms.test/apply"), requireConfirmation), values, resume, CancellationToken.None);
    }

    private static BrowserValueSet Values(params (string Key, string Value)[] values) => new(values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase), new Dictionary<string, string>());
}
