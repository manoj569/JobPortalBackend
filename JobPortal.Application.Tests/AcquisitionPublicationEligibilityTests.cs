using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class AcquisitionPublicationEligibilityTests
{
    [Theory]
    [InlineData("website-crawler", true, false, false)]
    [InlineData("serpapi-google-jobs", true, true, false)]
    [InlineData("website-crawler", false, true, false)]
    [InlineData("website-crawler", true, true, true)]
    public async Task AutomaticPublicationUsesCurrentSourcePolicyOnEveryAttempt(
        string provider, bool enabled, bool autoPublish, bool deleted)
    {
        using var f = new Fixture();
        var id = await f.SaveJobAsync("Hyderabad, Telangana, India", "IN");
        // A prior attempt/receipt cannot grant permission after policy changes.
        f.Source.AutoPublishEnabled = false;
        await f.Db.SaveChangesAsync();
        Assert.Equal(JobAutoPublishOutcome.Disabled, (await f.Automatic.TryPublishAsync(id)).Outcome);
        f.Source.ProviderKey = provider;
        f.Source.IsEnabled = enabled;
        f.Source.AutoPublishEnabled = autoPublish;
        f.Source.IsDeleted = deleted;
        await f.Db.SaveChangesAsync();
        var allowed = enabled && autoPublish && !deleted;
        Assert.Equal(allowed ? JobAutoPublishOutcome.Published : JobAutoPublishOutcome.Disabled,
            (await f.Automatic.TryPublishAsync(id)).Outcome);
        var saved = await f.Db.Jobs.AsNoTracking().SingleAsync();
        Assert.Equal(allowed ? JobStatus.Published : JobStatus.Draft, saved.Status);
        Assert.Equal(allowed, saved.PublishedAtUtc.HasValue);
    }

    [Fact]
    public async Task ManualPublicationRetainsExistingOverrideWhenAutomaticPublicationIsDisabled()
    {
        using var f = new Fixture();
        f.Source.ProviderKey = "website-crawler";
        f.Source.AutoPublishEnabled = false;
        await f.Db.SaveChangesAsync();
        var id = await f.SaveJobAsync("Hyderabad, Telangana, India", "IN");
        Assert.Equal(JobAutoPublishOutcome.Disabled, (await f.Automatic.TryPublishAsync(id)).Outcome);
        await f.Manual.PublishAsync(id);
        Assert.Equal(JobStatus.Published, (await f.Db.Jobs.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData("Hyderabad, Telangana, India", "IN", true)]
    [InlineData("Chennai, Tamil Nadu, India", "IN", true)]
    [InlineData("Bengaluru, Karnataka, India", "IN", true)]
    [InlineData("Remote", null, false)]
    [InlineData("New York, United States", "US", false)]
    [InlineData("London, United Kingdom", "GB", false)]
    [InlineData("Hyderabad, Telangana, India", "US", false)]
    [InlineData("London, United Kingdom", "IN", false)]
    public async Task PersistedAcquisitionGeographyAgreesForManualAndAutomaticPublication(string location, string? country, bool accepted)
    {
        using var f = new Fixture();
        var manualId = await f.SaveJobAsync(location, country);
        var automaticId = await f.SaveJobAsync(location, country);
        var saved = await f.Db.Jobs.AsNoTracking().SingleAsync(x => x.Id == manualId);
        Assert.Equal(accepted, ImportedJobIntegrity.Eligibility(saved).Decision == IndiaJobEligibilityDecision.Accepted);
        if (accepted)
            await f.Manual.PublishAsync(manualId);
        else
            await Assert.ThrowsAsync<BadRequestException>(() => f.Manual.PublishAsync(manualId));

        var automatic = await f.Automatic.TryPublishAsync(automaticId);
        Assert.Equal(accepted ? JobAutoPublishOutcome.Published : JobAutoPublishOutcome.NeedsReview, automatic.Outcome);
        var expected = accepted ? JobStatus.Published : JobStatus.Draft;
        Assert.Equal(expected, (await f.Db.Jobs.AsNoTracking().SingleAsync(x => x.Id == manualId)).Status);
        Assert.Equal(expected, (await f.Db.Jobs.AsNoTracking().SingleAsync(x => x.Id == automaticId)).Status);
    }

    [Fact]
    public async Task ManualPublicationStillEnforcesJobValidation()
    {
        using var f = new Fixture();
        var id = await f.SaveJobAsync("Hyderabad, Telangana, India", "IN", title: "");
        await Assert.ThrowsAsync<ValidationException>(() => f.Manual.PublishAsync(id));
        Assert.Equal(JobAutoPublishOutcome.Rejected, (await f.Automatic.TryPublishAsync(id)).Outcome);
        Assert.Equal(JobStatus.Draft, (await f.Db.Jobs.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"countryCodes\":true}")]
    public void InvalidAcquisitionMetadataStaysUnderReview(string metadata)
    {
        var job = new Job { AcquisitionSourceId = Guid.NewGuid(), Location = "Hyderabad, Telangana, India", SourceMetadataJson = metadata };
        Assert.Equal(new IndiaJobEligibilityResult(IndiaJobEligibilityDecision.Review, "InvalidSourceMetadata"), ImportedJobIntegrity.Eligibility(job));
    }

    [Theory]
    [InlineData("Remote India", "IN", IndiaJobEligibilityDecision.Accepted)]
    [InlineData("Remote worldwide, excluding India", "IN", IndiaJobEligibilityDecision.Rejected)]
    [InlineData("Remote", null, IndiaJobEligibilityDecision.Review)]
    public void AcquisitionRemoteEvidenceUsesExistingPolicy(string remoteEligibility, string? country, IndiaJobEligibilityDecision expected)
    {
        var job = new Job { AcquisitionSourceId = Guid.NewGuid(), Location = "Remote", WorkplaceType = WorkplaceType.Remote,
            SourceMetadataJson = Metadata(country, remoteEligibility) };
        Assert.Equal(expected, ImportedJobIntegrity.Eligibility(job).Decision);
    }

    [Fact]
    public void AtsMetadataKeepsItsExistingReconstruction()
    {
        var raw = new RawExternalJob { Location = "London, UK", CountryCodes = ["GB"] };
        var job = new Job { JobSourceId = Guid.NewGuid(), Location = "Hyderabad, Telangana, India", SourceMetadataJson = JsonSerializer.Serialize(raw) };
        Assert.Equal(IndiaJobEligibilityPolicy.Evaluate(raw), ImportedJobIntegrity.Eligibility(job));
    }

    private static string Metadata(string? country, string? remoteEligibility = null) => JsonSerializer.Serialize(new
    {
        importId = "acquisition-fixture", sourceType = "fresher-discovery", sourceName = "Fixture",
        countryCodes = country is null ? Array.Empty<string>() : [country],
        remoteEligibility, workplaceType = "On-site"
    });

    private sealed class Fixture : IDisposable
    {
        public JobPortalDbContext Db { get; } = new(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Company company = new() { Name = "Acquisition fixture", Slug = "acquisition-fixture" };
        public AcquisitionSource Source { get; } = new() { ProviderKey = "fresher-fixture", Name = "Fixture", IsEnabled = true, AutoPublishEnabled = true };
        public JobService Manual { get; }
        public JobAutoPublishService Automatic { get; }

        public Fixture()
        {
            Db.AddRange(company, Source);
            Db.SaveChanges();
            var jobs = new JobRepository(Db);
            var unit = new UnitOfWork(Db);
            Manual = new(jobs, unit, new Audit(), new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
                new UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System);
            Automatic = new(jobs, new JobQualityGate(), Manual, Options.Create(new JobAggregationOptions()), TimeProvider.System,
                new JobSourceRepository(Db), acquisitionSources: new FresherAcquisitionStore(Db));
        }

        public async Task<Guid> SaveJobAsync(string location, string? country, string title = "Software Engineer")
        {
            var id = Guid.NewGuid();
            var job = new Job { Id = id, CompanyId = company.Id, AcquisitionSourceId = Source.Id,
                AcquisitionExternalId = id.ToString("N"), SourceOwnsPublication = true, Title = title,
                ReferenceNumber = id.ToString("N"), Slug = id.ToString("N"), Description = "Build and maintain reliable software.",
                Location = location, WorkplaceType = location == "Remote" ? WorkplaceType.Remote : WorkplaceType.OnSite,
                ExperienceLevel = ExperienceLevel.Entry, EmploymentType = EmploymentType.FullTime,
                ApplicationUrl = "https://employer.example/jobs/" + id.ToString("N"), SourceMetadataJson = Metadata(country), Status = JobStatus.Draft };
            Db.Add(job);
            await Db.SaveChangesAsync();
            Db.Entry(job).State = EntityState.Detached;
            return id;
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class Audit : IAuditWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
