using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.AIApply;
using JobPortal.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIApplyCandidateContinuationTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(AIApplyFailureKind.LoginRequired)]
    [InlineData(AIApplyFailureKind.HumanVerificationRequired)]
    public async Task OwnerCanReadSafeLinkAndContinueOnlyCandidateActionFailures(AIApplyFailureKind failure)
    {
        var (service, repository, application) = Fixture(AIApplyRunStatus.Failed, failure);
        var beforeRetry = application.RetryCount = 2;
        var detail = await service.GetApplicationAsync(application.UserId, application.Id);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/123", detail.ExternalApplication!.Url);
        Assert.Equal(JobSiteIdentifier.Greenhouse, detail.ExternalApplication.Site);
        Assert.True(detail.Actions!.CanOpenExternalApplication); Assert.True(detail.Actions.CanContinue);

        var continued = await service.ContinueAsync(application.UserId, application.Id);
        Assert.Equal(AIApplyRunStatus.Queued, continued.Status); Assert.Equal(beforeRetry, continued.RetryCount);
        Assert.Equal(1, repository.ContinueCount); Assert.Null(application.SubmissionAttemptedAtUtc);
    }

    [Fact]
    public async Task NonOwnerGetsSafeNotFoundAndCannotReadOrContinue()
    {
        var (service, _, application) = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetApplicationAsync(Guid.NewGuid(), application.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ContinueAsync(Guid.NewGuid(), application.Id));
    }

    [Fact]
    public async Task ListAndDetailReturnRealJobCompanyAndHistoricalResumeMetadata()
    {
        var (service, repository, application) = Fixture(AIApplyRunStatus.Queued, AIApplyFailureKind.None);
        application.ResumeStorageKey = "private/internal/resume-key";
        application.ResumeFileName = "candidate-resume.pdf";
        application.ResumeContentType = "application/pdf";
        application.ResumeSizeBytes = 1234;
        repository.User.ResumeStorageKey = "private/current-resume-key";
        repository.User.ResumeFileName = "new-current-resume.pdf";

        var item = Assert.Single(await service.GetApplicationsAsync(application.UserId, null, null));
        var detail = await service.GetApplicationAsync(application.UserId, application.Id);

        Assert.Equal("Senior Engineer", item.JobTitle);
        Assert.Equal("Acme Ltd", item.CompanyName);
        Assert.Equal("candidate-resume.pdf", item.ResumeUsed!.FileName);
        Assert.Equal(item.JobTitle, detail.JobTitle);
        Assert.Equal(item.CompanyName, detail.CompanyName);
        Assert.Equal(item.ResumeUsed, detail.ResumeUsed);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(detail), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueueSnapshotsTheApplicationSpecificResumeAndKeepsJobIdOnlyRequestCompatible()
    {
        var (service, repository, application) = Fixture(AIApplyRunStatus.Queued, AIApplyFailureKind.None);
        repository.Settings = new AIApplySetting { UserId = application.UserId, Enabled = true, Paused = false };
        repository.User.ResumeStorageKey = "private/snapshot-key";
        repository.User.ResumeFileName = "resume-at-queue.pdf";
        repository.User.ResumeContentType = "application/pdf";
        repository.User.ResumeSizeBytes = 4567;

        var response = await service.QueueAsync(application.UserId, new(repository.Job.Id));

        Assert.Equal("resume-at-queue.pdf", response.ResumeUsed!.FileName);
        Assert.Equal(repository.User.ResumeStorageKey, repository.AddedApplication!.ResumeStorageKey);
        Assert.Equal(repository.User.ResumeFileName, repository.AddedApplication.ResumeFileName);
        Assert.All(typeof(QueueAIApplyRequest).GetConstructors().Single().GetParameters().Skip(1),
            parameter => Assert.True(parameter.HasDefaultValue));
    }

    [Fact]
    public async Task SubmittedRequiresProviderConfirmationAndUncertaintyMapsToNeedsReview()
    {
        var unconfirmed = Fixture(AIApplyRunStatus.Submitted, AIApplyFailureKind.SubmissionUnconfirmed);
        unconfirmed.Application.SubmissionAttemptedAtUtc = Now.AddMinutes(-1);
        var uncertain = await unconfirmed.Service.GetApplicationAsync(
            unconfirmed.Application.UserId, unconfirmed.Application.Id);
        Assert.Equal(AIApplyRunStatus.NeedsReview, uncertain.Status);
        Assert.Equal(AIApplyCandidateProgressState.NeedsReview, uncertain.ProgressState);
        Assert.Null(uncertain.SubmittedAtUtc);

        var confirmed = Fixture(AIApplyRunStatus.Submitted, AIApplyFailureKind.None);
        confirmed.Application.SubmissionConfirmedAtUtc = Now;
        var submitted = await confirmed.Service.GetApplicationAsync(
            confirmed.Application.UserId, confirmed.Application.Id);
        Assert.Equal(AIApplyRunStatus.Submitted, submitted.Status);
        Assert.Equal(AIApplyCandidateProgressState.Submitted, submitted.ProgressState);
        Assert.Equal(Now, submitted.SubmittedAtUtc);
    }

    [Theory]
    [InlineData(AIApplyRunStatus.Queued, AIApplyCandidateProgressState.Queued)]
    [InlineData(AIApplyRunStatus.Processing, AIApplyCandidateProgressState.Processing)]
    [InlineData(AIApplyRunStatus.WaitingForUser, AIApplyCandidateProgressState.WaitingForCandidate)]
    [InlineData(AIApplyRunStatus.NeedsReview, AIApplyCandidateProgressState.NeedsReview)]
    [InlineData(AIApplyRunStatus.Failed, AIApplyCandidateProgressState.Failed)]
    [InlineData(AIApplyRunStatus.Cancelled, AIApplyCandidateProgressState.Cancelled)]
    public async Task CandidateProgressUsesOnlyTruthfulCoarseStates(
        AIApplyRunStatus status, AIApplyCandidateProgressState expected)
    {
        var (service, _, application) = Fixture(status, AIApplyFailureKind.None);
        Assert.Equal(expected,
            (await service.GetApplicationAsync(application.UserId, application.Id)).ProgressState);
    }

    [Fact]
    public async Task UnsafeExternalUrlIsNotExposedAndCannotContinue()
    {
        var (service, _, application) = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired, safeLink: false);
        var detail = await service.GetApplicationAsync(application.UserId, application.Id);
        Assert.Null(detail.ExternalApplication); Assert.False(detail.Actions!.CanOpenExternalApplication); Assert.False(detail.Actions.CanContinue);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ContinueAsync(application.UserId, application.Id));
        Assert.Equal("application_url_unavailable", error.Code);
    }

    [Theory]
    [InlineData(AIApplyRunStatus.Submitted, AIApplyFailureKind.None, false)]
    [InlineData(AIApplyRunStatus.NeedsReview, AIApplyFailureKind.SubmissionUnconfirmed, true)]
    [InlineData(AIApplyRunStatus.Failed, AIApplyFailureKind.Duplicate, false)]
    [InlineData(AIApplyRunStatus.Failed, AIApplyFailureKind.JobExpired, false)]
    [InlineData(AIApplyRunStatus.Cancelled, AIApplyFailureKind.LoginRequired, false)]
    [InlineData(AIApplyRunStatus.Processing, AIApplyFailureKind.LoginRequired, false)]
    [InlineData(AIApplyRunStatus.DeadLettered, AIApplyFailureKind.LoginRequired, false)]
    [InlineData(AIApplyRunStatus.WaitingForUser, AIApplyFailureKind.MissingUserInformation, false)]
    public async Task ProtectedStatesCannotContinue(AIApplyRunStatus status, AIApplyFailureKind failure, bool attempted)
    {
        var (service, repository, application) = Fixture(status, failure);
        if (attempted) application.SubmissionAttemptedAtUtc = Now.AddMinutes(-1);
        await Assert.ThrowsAsync<ConflictException>(() => service.ContinueAsync(application.UserId, application.Id));
        Assert.Equal(0, repository.ContinueCount); Assert.Equal(status, application.Status);
        Assert.Equal(attempted, application.SubmissionAttemptedAtUtc.HasValue);
    }

    [Fact]
    public async Task ExpiredEntitlementCannotContinue()
    {
        var (service, repository, application) = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired, authorized: false);
        var error = await Assert.ThrowsAsync<AppException>(() => service.ContinueAsync(application.UserId, application.Id));
        Assert.Equal(403, error.StatusCode); Assert.Equal(0, repository.ContinueCount);
    }

    [Fact]
    public async Task RepeatedAndConcurrentContinueIsIdempotent()
    {
        var (service, repository, application) = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired);
        var responses = await Task.WhenAll(service.ContinueAsync(application.UserId, application.Id), service.ContinueAsync(application.UserId, application.Id));
        Assert.All(responses, x => Assert.Equal(AIApplyRunStatus.Queued, x.Status)); Assert.Equal(1, repository.ContinueCount);
        Assert.Equal(AIApplyRunStatus.Queued, (await service.ContinueAsync(application.UserId, application.Id)).Status); Assert.Equal(1, repository.ContinueCount);
    }

    [Fact]
    public async Task SubmissionAttemptEvidenceIsNeverClearedOrRequeued()
    {
        var (service, repository, application) = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired);
        application.SubmissionAttemptedAtUtc = Now.AddMinutes(-2);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ContinueAsync(application.UserId, application.Id));
        Assert.Equal("submission_unconfirmed", error.Code); Assert.NotNull(application.SubmissionAttemptedAtUtc); Assert.Equal(0, repository.ContinueCount);
    }

    [Fact]
    public async Task ClosedJobAndChangedDestinationAreRevalidated()
    {
        var closed = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired);
        closed.Repository.Job.Status = JobStatus.Closed;
        Assert.Equal("job_unavailable", (await Assert.ThrowsAsync<ConflictException>(() => closed.Service.ContinueAsync(closed.Application.UserId, closed.Application.Id))).Code);

        var changed = Fixture(AIApplyRunStatus.Failed, AIApplyFailureKind.LoginRequired);
        changed.Repository.Job.ApplicationUrl = "https://boards.greenhouse.io/acme/jobs/changed";
        Assert.False((await changed.Service.GetApplicationAsync(changed.Application.UserId, changed.Application.Id)).Actions!.CanContinue);
        Assert.Equal("application_destination_changed", (await Assert.ThrowsAsync<ConflictException>(() => changed.Service.ContinueAsync(changed.Application.UserId, changed.Application.Id))).Code);
    }

    [Fact]
    public void ContinueRouteIsIndividualAndNarrowlyRateLimited()
    {
        var method = typeof(AIApplyController).GetMethod(nameof(AIApplyController.Continue))!;
        Assert.Equal("applications/{id:guid}/continue", method.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>().Single().Template);
        Assert.Equal("AIApplyContinue", method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Cast<EnableRateLimitingAttribute>().Single().PolicyName);
    }

    [Fact]
    public void CandidateDtoCannotCarrySessionCookieOrCredentialFields()
    {
        var names = typeof(AIApplyApplicationResponse).GetProperties()
            .Concat(typeof(CandidateExternalApplicationResponse).GetProperties())
            .Concat(typeof(AIApplyResumeUsedResponse).GetProperties()).Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, x => x.Contains("Cookie", StringComparison.OrdinalIgnoreCase) || x.Contains("Session", StringComparison.OrdinalIgnoreCase) || x.Contains("Credential", StringComparison.OrdinalIgnoreCase) || x.Contains("Password", StringComparison.OrdinalIgnoreCase) || x.Contains("Otp", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://127.0.0.1/apply")]
    [InlineData("https://127.0.0.1/apply")]
    [InlineData("https://10.0.0.1/apply")]
    [InlineData("https://192.168.1.10/apply")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public async Task NavigationPolicyRejectsUnsafeDestinations(string value)
    {
        var policy = new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()));
        Assert.False(await policy.IsAllowedAsync(new Uri(value), default));
    }

    [Fact]
    public async Task NavigationPolicyAllowsKnownHttpsAtsOnlyWhenDnsIsPublic()
    {
        var uri = new Uri("https://boards.greenhouse.io/acme/jobs/123");
        Assert.True(await new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()), new FakeDns(IPAddress.Parse("203.0.113.10"))).IsAllowedAsync(uri, default));
        Assert.False(await new ExternalNavigationPolicy(Options.Create(new AIApplyOptions()), new FakeDns(IPAddress.Parse("10.1.2.3"))).IsAllowedAsync(uri, default));
    }

    [Theory]
    [InlineData("boards.greenhouse.io", JobSiteIdentifier.Greenhouse)]
    [InlineData("jobs.lever.co", JobSiteIdentifier.Lever)]
    [InlineData("acme.myworkdayjobs.com", JobSiteIdentifier.Workday)]
    [InlineData("evilgreenhouse.io.example.com", JobSiteIdentifier.Generic)]
    public void KnownSiteIdentificationUsesHostnameBoundaries(string host, JobSiteIdentifier expected) => Assert.Equal(expected, JobSiteDomainCatalog.Identify(host));

    private static (AIApplyService Service, FakeRepository Repository, AIApplyApplication Application) Fixture(AIApplyRunStatus status, AIApplyFailureKind failure, bool safeLink = true, bool authorized = true)
    {
        var userId = Guid.NewGuid(); var jobId = Guid.NewGuid();
        var application = new AIApplyApplication { UserId = userId, JobId = jobId, Status = status, FailureKind = failure, ScheduledAtUtc = Now.AddMinutes(-5), CreatedAtUtc = Now.AddMinutes(-10), ExternalApplicationUrl = "https://boards.greenhouse.io/acme/jobs/123", NormalizedApplicationUrl = "https://boards.greenhouse.io/acme/jobs/123" };
        var repository = new FakeRepository(application, new Job { Id = jobId, Title = "Senior Engineer", Company = new Company { Name = "Acme Ltd" }, Status = JobStatus.Published, ExpiresAtUtc = Now.AddDays(2), ApplicationUrl = application.ExternalApplicationUrl });
        var questionMatcher = new ApplicationQuestionMatcher(repository);
        var service = new AIApplyService(repository, new FakeAuthorization(authorized), new DeterministicAIApplyMatcher(), new DeterministicApplicationQuestionClassifier(questionMatcher), new FakeLinks(safeLink), new FixedClock());
        return (service, repository, application);
    }

    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class FakeAuthorization(bool authorized) : IAIApplyAuthorizationService
    {
        public Task<AIApplyAccess> GetAccessAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new AIApplyAccess(authorized, !authorized, authorized ? AIApplyPlanTier.Standard : AIApplyPlanTier.None, authorized, false));
        public async Task<AIApplyAccess> RequireAsync(Guid userId, bool pro = false, CancellationToken ct = default) { var access = await GetAccessAsync(userId, ct); if (!access.CanUseAIApply) throw new AppException("An active AI Apply subscription is required.", 403, "ai_apply_subscription_required"); return access; }
    }
    private sealed class FakeLinks(bool safe) : ICandidateExternalApplicationLinkService
    {
        public Task<CandidateExternalApplicationDestination?> ResolveAsync(string url, CancellationToken ct) => Task.FromResult(safe ? new CandidateExternalApplicationDestination(JobSiteIdentifier.Greenhouse, "Greenhouse", url, true, true) : null);
    }
    private sealed class FakeDns(params IPAddress[] addresses) : IExternalHostAddressResolver { public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(addresses); }

    private sealed class FakeRepository(AIApplyApplication application, Job job) : IAIApplyRepository
    {
        private readonly object sync = new(); public int ContinueCount { get; private set; } public Job Job => job;
        public User User { get; } = new() { Id = application.UserId, Status = UserStatus.Active };
        public AIApplySetting? Settings { get; set; }
        public AIApplyApplication? AddedApplication { get; private set; }
        public Task<AIApplyApplication?> GetApplicationAsync(Guid userId, Guid id, CancellationToken ct = default) => Task.FromResult(application.UserId == userId && application.Id == id ? application : null);
        public Task<Job?> GetJobAsync(Guid id, CancellationToken ct = default) => Task.FromResult(job.Id == id ? job : null);
        public Task<IReadOnlyList<Job>> GetJobsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Job>>(ids.Contains(job.Id) ? [job] : []);
        public Task<bool> TryContinueCandidateActionAsync(Guid userId, Guid id, AIApplyFailureKind expected, DateTime now, CancellationToken ct = default) { lock (sync) { if (application.UserId != userId || application.Id != id || application.Status != AIApplyRunStatus.Failed || application.FailureKind != expected || application.SubmissionAttemptedAtUtc.HasValue) return Task.FromResult(false); application.Status = AIApplyRunStatus.Queued; application.FailureKind = AIApplyFailureKind.None; application.ScheduledAtUtc = now; application.CompletedAtUtc = null; ContinueCount++; return Task.FromResult(true); } }
        public Task<IReadOnlyList<AIApplyApplication>> GetApplicationsAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AIApplyApplication>>(application.UserId == userId ? [application] : []);
        public Task<User?> GetUserProfileAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<User?>(User.Id == userId ? User : null);
        public Task<Membership?> GetMembershipAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<Membership?>(null);
        public Task<AIApplyProfile?> GetProfileAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<AIApplyProfile?>(null);
        public Task<AIApplyPreference?> GetPreferencesAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<AIApplyPreference?>(null);
        public Task<AIApplySetting?> GetSettingsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Settings?.UserId == userId ? Settings : null);
        public Task<IReadOnlyList<AIApplyRule>> GetRulesAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AIApplyRule>>([]);
        public Task<AIApplyRule?> GetRuleAsync(Guid userId, Guid id, CancellationToken ct = default) => Task.FromResult<AIApplyRule?>(null);
        public Task<AIApplyApplication?> ClaimNextAsync(DateTime now, int perUserConcurrency, string workerId, DateTime leaseExpiresAtUtc, CancellationToken ct = default) => Task.FromResult<AIApplyApplication?>(null);
        public Task<int> RecoverExpiredLeasesAsync(DateTime now, int maximum, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> MarkSubmissionAttemptedAsync(Guid applicationId, DateTime now, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> MarkSubmissionConfirmedAsync(Guid applicationId, DateTime now, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsDuplicateAsync(Guid userId, Guid jobId, string normalizedUrl, CancellationToken ct = default) => Task.FromResult(false);
        public Task<AIApplyQuestion?> GetQuestionAsync(Guid userId, Guid id, CancellationToken ct = default) => Task.FromResult<AIApplyQuestion?>(null);
        public Task<IReadOnlyList<AIApplyQuestion>> GetQuestionsAsync(Guid userId, AIApplyQuestionStatus? status, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AIApplyQuestion>>([]);
        public Task<UserApplicationAnswer?> GetAnswerAsync(Guid userId, Guid id, CancellationToken ct = default) => Task.FromResult<UserApplicationAnswer?>(null);
        public Task<UserApplicationAnswer?> FindAnswerAsync(Guid userId, string normalizedQuestion, CancellationToken ct = default) => Task.FromResult<UserApplicationAnswer?>(null);
        public Task<IReadOnlyList<UserApplicationAnswer>> GetAnswersAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UserApplicationAnswer>>([]);
        public Task AddAsync<T>(T entity, CancellationToken ct = default) where T : class
        {
            if (entity is AIApplyApplication added) AddedApplication = added;
            return Task.CompletedTask;
        }
        public void Remove<T>(T entity) where T : class { }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
