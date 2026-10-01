using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

// Real normalizer, repositories, dedup, quality gate, validators and JobService.
// Only the external feed, clock, advisory locks and audit transport are test doubles.
public sealed class JobAggregationPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlagControlsRealPublicationAndRerunsDoNotRepublish(bool enabled)
    {
        using var f = new JobSourceFixture();
        var audit = new PublicationAudit();
        f.Provider.Jobs = [Valid(f), Valid(f)];
        var runner = Runner(f, enabled, audit);
        var result = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Matched);
        Assert.Equal(enabled ? 1 : 0, result.Published);
        Assert.Equal(enabled ? 0 : 1, result.AutoPublishDisabled);
        f.Context.ChangeTracker.Clear();
        var saved = Assert.Single(f.Context.Jobs);
        Assert.Equal(enabled ? JobStatus.Published : JobStatus.Draft, saved.Status);
        Assert.Equal(Valid(f).ExpiresAtUtc, saved.ExpiresAtUtc);
        Assert.Equal(enabled ? 1 : 0, audit.Events.Count);
        if (enabled) Assert.Equal(AuditAction.Publish, Assert.Single(audit.Events).Action);
        var publicJobs = new PublicJobRepository(f.Context, new Clock());
        Assert.Equal(enabled ? 1 : 0, (await publicJobs.SearchAsync(new PublicJobQuery())).TotalCount);

        // Curated data must survive repeated source ingestion, including an extended source expiry.
        saved.Description = "Admin curated description";
        await f.Context.SaveChangesAsync();
        f.Provider.Jobs = [Valid(f) with { Description = "Source replacement", ExpiresAtUtc = Valid(f).ExpiresAtUtc!.Value.AddDays(10) }];
        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Matched);
        Assert.Equal(0, second.Published);
        f.Context.ChangeTracker.Clear();
        saved = Assert.Single(f.Context.Jobs);
        Assert.Equal("Admin curated description", saved.Description);
        Assert.Equal(Valid(f).ExpiresAtUtc, saved.ExpiresAtUtc);
        Assert.Equal(enabled ? 1 : 0, audit.Events.Count);
    }

    [Theory]
    [InlineData("expiry", JobQualityReasonCode.MissingExpiry)]
    [InlineData("location", JobQualityReasonCode.MissingLocation)]
    [InlineData("workplace", JobQualityReasonCode.MissingWorkplaceType)]
    [InlineData("employment", JobQualityReasonCode.MissingEmploymentType)]
    public async Task MissingSourceFieldsRemainDraftWithObservableReasons(string field, JobQualityReasonCode reason)
    {
        using var f = new JobSourceFixture();
        var raw = Valid(f);
        f.Provider.Jobs = [field switch
        {
            "expiry" => raw with { ExpiresAtUtc = null },
            "location" => raw with { Location = null },
            "workplace" => raw with { WorkplaceType = null },
            _ => raw with { EmploymentType = null }
        }];
        var audit = new PublicationAudit();
        var result = await Runner(f, true, audit).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.NeedsReview);
        Assert.Equal(1, result.QualityReasonCounts![reason]);
        Assert.Equal(JobStatus.Draft, Assert.Single(f.Context.Jobs).Status);
        Assert.Empty(audit.Events);
    }

    [Theory]
    [InlineData("expired", true)]
    [InlineData("description", true)]
    [InlineData("url", false)]
    [InlineData("salary", false)]
    [InlineData("experience", false)]
    [InlineData("negative-experience", false)]
    public async Task RejectionsNeverPublish(string field, bool createsDraft)
    {
        using var f = new JobSourceFixture();
        var raw = Valid(f);
        f.Provider.Jobs = [field switch
        {
            "expired" => raw with { ExpiresAtUtc = JobSourceFixture.Now.AddSeconds(-1) },
            "description" => raw with { Description = null },
            "url" => raw with { ApplicationUrl = "javascript:alert(1)" },
            "salary" => raw with { SalaryMin = 100, SalaryMax = 10 },
            "negative-experience" => raw with { MinimumExperienceYears = -1 },
            _ => raw with { MinimumExperienceYears = 5, MaximumExperienceYears = 2 }
        }];
        var audit = new PublicationAudit();
        var result = await Runner(f, true, audit).RunAsync(f.Source.Id);
        Assert.Equal(0, result.Published);
        Assert.Equal(createsDraft ? 1 : 0, result.QualityRejected);
        Assert.Equal(createsDraft ? 0 : 1, result.Rejected);
        Assert.All(f.Context.Jobs, job => Assert.Equal(JobStatus.Draft, job.Status));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task PublicationFailureClearsPendingStateButKeepsCommittedDraftAndContinues()
    {
        using var f = new JobSourceFixture();
        var audit = new PublicationAudit { FailFirst = true };
        f.Provider.Jobs = [Valid(f), Valid(f) with { Title = "Accountant", ApplicationUrl = "https://example.test/accountant" }];
        var result = await Runner(f, true, audit).RunAsync(f.Source.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.PublishFailed);
        Assert.Equal(1, result.Published);
        Assert.Equal(1, result.ReasonCounts![JobIngestionReasonCode.AutoPublishFailed]);
        f.Context.ChangeTracker.Clear();
        var jobs = await f.Context.Jobs.ToArrayAsync();
        Assert.Equal(JobStatus.Draft, Assert.Single(jobs, x => x.Title == "Engineer").Status);
        Assert.Equal(JobStatus.Published, Assert.Single(jobs, x => x.Title == "Accountant").Status);
    }

    [Fact]
    public async Task MissingExperienceLevelRequiresReviewWithoutPublicationOrFabrication()
    {
        using var f = new JobSourceFixture();

        f.Provider.Jobs =
        [
            Valid(f) with
        {
            ExperienceLevel = null
        }
        ];

        var audit = new PublicationAudit();

        var result = await Runner(f, true, audit)
            .RunAsync(f.Source.Id);

        // The job is valid enough to ingest and persist as Draft.
        Assert.Equal(1, result.Created);

        // Missing ExperienceLevel is now caught by the quality gate
        // before JobService.PublishAsync is attempted.
        Assert.Equal(1, result.NeedsReview);
        Assert.Equal(0, result.PublishFailed);
        Assert.Equal(0, result.Published);

        f.Context.ChangeTracker.Clear();

        var job = Assert.Single(f.Context.Jobs);

        // Never fabricate an experience level for an external job.
        Assert.Equal(default, job.ExperienceLevel);

        // Jobs requiring human review must remain Draft.
        Assert.Equal(JobStatus.Draft, job.Status);

        // No real publication was attempted.
        Assert.Empty(audit.Events);

        // Verify the exact quality-gate reason.
        var quality = new JobQualityGate()
            .Evaluate(job, JobSourceFixture.Now);

        Assert.Equal(
            JobQualityDecision.NeedsReview,
            quality.Decision);

        Assert.Contains(
            JobQualityReasonCode.MissingExperienceLevel,
            quality.Reasons);
    }

    [Fact]
    public async Task CancellationDuringPublicationPropagatesWithoutPublishingOrProcessingNextJob()
    {
        using var f = new JobSourceFixture();
        using var cancel = new CancellationTokenSource();
        var audit = new PublicationAudit { Cancel = cancel };
        f.Provider.Jobs = [Valid(f), Valid(f) with { Title = "Accountant" }];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(f, true, audit).RunAsync(f.Source.Id, cancel.Token));
        f.Context.ChangeTracker.Clear();
        Assert.Equal(JobStatus.Draft, Assert.Single(f.Context.Jobs).Status);
    }

    [Fact]
    public async Task AdminQualityReviewIsReadOnlyAndWorksWhenAutoPublishIsDisabled()
    {
        using var f = new JobSourceFixture();
        f.Provider.Jobs = [Valid(f) with { ExpiresAtUtc = null }];
        await Runner(f, false, new PublicationAudit()).RunAsync(f.Source.Id);
        f.Context.ChangeTracker.Clear();
        var job = Assert.Single(f.Context.Jobs);
        var review = new JobQualityReviewService(new JobRepository(f.Context), new JobQualityGate(), new Clock());
        var result = await review.ReviewAsync(job.Id);
        Assert.Equal(JobQualityDecision.NeedsReview, result.Decision);
        Assert.Contains(JobQualityReasonCode.MissingExpiry, result.Reasons);
        Assert.False(f.Context.ChangeTracker.HasChanges());
        Assert.Equal(JobStatus.Draft, job.Status);
        await Assert.ThrowsAsync<NotFoundException>(() => review.ReviewAsync(Guid.NewGuid()));
        var authorization = Assert.Single(typeof(JobsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Administrator", authorization.Roles);
        var route = Assert.Single(typeof(JobsController).GetMethod(nameof(JobsController.Quality))!
            .GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        Assert.Equal("{id:guid}/quality", route.Template);
    }

    private static RawExternalJob Valid(JobSourceFixture f) => new()
    {
        Title = "Engineer", CompanyName = f.Company.Name, Description = "Build reliable software.",
        CategoryId = f.Category.Id, Location = "Bangalore Karnataka", ApplicationUrl = "https://example.test/engineer",
        EmploymentType = EmploymentType.FullTime, WorkplaceType = WorkplaceType.Remote,
        ExperienceLevel = ExperienceLevel.Entry, ExpiresAtUtc = JobSourceFixture.Now.AddDays(10)
    };

    private static JobSourceRunner Runner(JobSourceFixture f, bool enabled, IAuditWriter audit)
    {
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var clock = new Clock();
        var fingerprints = new JobFingerprintService();
        var canonicalizer = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context),
            new CategoryManagementRepository(f.Context), new JobDeduplicationService(jobs, fingerprints, canonicalizer),
            fingerprints, unit, clock, f.Locks, canonicalizer);
        var jobService = new JobService(jobs, unit, audit, new CreateJobRequestValidator(),
            new UpdateJobRequestValidator(), new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), clock);
        var publisher = new JobAutoPublishService(jobs, new JobQualityGate(), jobService,
            Options.Create(new JobAggregationOptions { AutoPublishEnabled = enabled }), clock);
        return new(f.Repository, [f.Provider], ingestion, unit, clock, f.Resolver, new ExternalJobNormalizer(), autoPublishService: publisher);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(JobSourceFixture.Now);
    }

    private sealed class PublicationAudit : IAuditWriter
    {
        private int calls;
        public bool FailFirst { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public List<AuditEvent> Events { get; } = [];
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (++calls == 1 && FailFirst) throw new InvalidOperationException("simulated audit failure before save");
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
