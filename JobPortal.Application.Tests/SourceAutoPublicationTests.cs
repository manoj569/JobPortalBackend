using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class SourceAutoPublicationTests
{
    [Theory]
    [InlineData(AtsType.Greenhouse, false)]
    [InlineData(AtsType.Greenhouse, true)]
    [InlineData(AtsType.Workday, false)]
    [InlineData(AtsType.Workday, true)]
    [InlineData(AtsType.SuccessFactors, false)]
    [InlineData(AtsType.SuccessFactors, true)]
    [InlineData(AtsType.Lever, false)]
    [InlineData(AtsType.Lever, true)]
    [InlineData(AtsType.Ashby, false)]
    [InlineData(AtsType.Ashby, true)]
    public async Task SourceControlsPublicationAndPublicQueryAcrossProviders(AtsType ats, bool enabled)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = ats;
        f.Source.AutoPublishEnabled = enabled;
        await f.Context.SaveChangesAsync();
        var provider = new SnapshotProvider(ats, [Vacancy(1), Vacancy(2) with { Location = "London, UK" },
            Vacancy(3) with { Location = "Remote" }]);
        var runner = Runner(f, provider);
        var first = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, first.Created);
        Assert.Equal(enabled ? 1 : 0, first.Published);
        Assert.Equal(enabled ? 0 : 1, first.AutoPublishDisabled);
        Assert.Equal(1, first.NonIndiaRejected);
        Assert.Equal(1, first.AmbiguousLocationHeld);
        var job = Assert.Single(await f.Context.Jobs.AsNoTracking().ToArrayAsync());
        Assert.Equal(enabled ? JobStatus.Published : JobStatus.Draft, job.Status);
        Assert.True(job.SourceOwnsPublication);
        Assert.Equal(f.Source.Id, job.JobSourceId);
        var publicJobs = new PublicJobRepository(f.Context, TimeProvider.System);
        Assert.Equal(enabled ? 1 : 0, (await publicJobs.SearchAsync(new())).TotalCount);
        var second = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Published);
        Assert.Equal(job.Id, Assert.Single(await f.Context.Jobs.AsNoTracking().ToArrayAsync()).Id);
    }

    [Fact]
    public async Task EnablingSourcePublishesManagedDraftButNeverRepublishesAdminUnpublish()
    {
        using var f = new JobSourceFixture();
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        var provider = new SnapshotProvider(f.Source.AtsType, [Vacancy(1)]);
        var runner = Runner(f, provider);
        Assert.Equal(0, (await runner.RunAsync(f.Source.Id)).Published);
        var id = Assert.Single(await f.Context.Jobs.AsNoTracking().ToArrayAsync()).Id;
        f.Source.AutoPublishEnabled = true;
        await f.Context.SaveChangesAsync();
        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Published);
        await ManualService(f).UnpublishAsync(id);
        provider.Jobs = [Vacancy(1) with { Description = "An updated official job description" }];
        var next = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, next.Updated);
        Assert.Equal(0, next.Published);
        Assert.Equal(1, next.NeedsReview);
        var saved = await f.Context.Jobs.AsNoTracking().SingleAsync();
        Assert.Equal(JobStatus.Draft, saved.Status);
        Assert.False(saved.SourceOwnsPublication);
        Assert.Equal(0, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("archive")]
    public async Task EditorialSuppressionSurvivesFutureSourceRuns(string action)
    {
        using var f = new JobSourceFixture();
        var runner = Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1)]));
        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Published);
        var id = await f.Context.Jobs.Select(x => x.Id).SingleAsync();
        var admin = ManualService(f);
        if (action == "hide") await admin.SetHiddenAsync(id, true);
        else if (action == "close") await admin.CloseAsync(id);
        else await admin.ArchiveAsync(id);
        Assert.Equal(0, (await runner.RunAsync(f.Source.Id)).Published);
        Assert.False((await f.Context.Jobs.AsNoTracking().SingleAsync()).SourceOwnsPublication);
        Assert.Equal(0, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Fact]
    public async Task LegacyDraftIsHeldRatherThanGuessingPublicationOwnership()
    {
        using var f = new JobSourceFixture();
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        var runner = Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1)]));
        await runner.RunAsync(f.Source.Id);
        var legacy = await f.Context.Jobs.SingleAsync();
        legacy.SourceOwnsPublication = false; // The migration default for pre-existing rows.
        f.Source.AutoPublishEnabled = true;
        await f.Context.SaveChangesAsync();
        var result = await runner.RunAsync(f.Source.Id);
        Assert.Equal(0, result.Published);
        Assert.Equal(1, result.QualityReasonCounts![JobQualityReasonCode.PublicationOwnershipUnverified]);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
        // Existing administrator publishing remains available.
        await ManualService(f).PublishAsync(legacy.Id);
        Assert.Equal(1, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Fact]
    public async Task MissingLocationCanBeStoredButCannotAutomaticallyBecomeVisible()
    {
        using var f = new JobSourceFixture();
        var result = await Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1) with { Location = null }])).RunAsync(f.Source.Id);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.NeedsReview);
        Assert.Equal(0, result.Published);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.SingleAsync()).Status);
        Assert.Equal(0, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
    }

    [Fact]
    public async Task CrossSourceMatchCannotPublishAnotherSourcesDraft()
    {
        using var f = new JobSourceFixture();
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        await Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1)])).RunAsync(f.Source.Id);
        f.Source.AutoPublishEnabled = true;
        var other = new JobSource { Company = f.Company, CompanyId = f.Company.Id, AtsType = AtsType.Lever,
            AtsIdentifier = "other", AutoPublishEnabled = true };
        f.Context.JobSources.Add(other);
        await f.Context.SaveChangesAsync();
        var result = await Runner(f, new SnapshotProvider(other.AtsType, [Vacancy(1)])).RunAsync(other.Id);
        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Published);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task DisablingSourceDoesNotUnpublishAnExistingPublishedJob()
    {
        using var f = new JobSourceFixture();
        var runner = Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1)]));
        await runner.RunAsync(f.Source.Id);
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        Assert.Equal(0, (await runner.RunAsync(f.Source.Id)).Published);
        Assert.Equal(JobStatus.Published, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task SourceSettingRoundTripsThroughAdminCreateUpdateListAndDetail()
    {
        using var f = new JobSourceFixture();
        var request = f.Request with { AtsIdentifier = "capco", CareerPageUrl = "https://job-boards.greenhouse.io/capco",
            ScanIntervalMinutes = 1440 };
        var created = await f.Service.CreateAsync(request);
        Assert.False(created.AutoPublishEnabled);
        Assert.False(new JobSource().AutoPublishEnabled);
        await f.Service.UpdateAsync(created.Id, request with { AutoPublishEnabled = true });
        f.Context.ChangeTracker.Clear();
        Assert.True((await f.Service.GetByIdAsync(created.Id)).AutoPublishEnabled);
        Assert.True((await f.Service.SearchAsync(new())).Items.Single(x => x.Id == created.Id).AutoPublishEnabled);
        Assert.True((await f.Repository.GetByIdAsync(created.Id))!.AutoPublishEnabled);
    }

    [Fact]
    public async Task OracleCannotOptInOrBypassTheCentralPublisher()
    {
        using var f = new JobSourceFixture();
        var request = f.Request with { AtsType = AtsType.OracleRecruitingCloud, IsActive = false,
            AtsIdentifier = "oracle:tenant.fa.ocs.oraclecloud.com:CX_1",
            CareerPageUrl = "https://tenant.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs",
            AutoPublishEnabled = true };
        Assert.Contains(new SaveJobSourceRequestValidator().Validate(request).Errors, x => x.PropertyName == nameof(request.AutoPublishEnabled));
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        await Runner(f, new SnapshotProvider(f.Source.AtsType, [Vacancy(1)])).RunAsync(f.Source.Id);
        f.Source.AtsType = AtsType.OracleRecruitingCloud;
        f.Source.AutoPublishEnabled = true; // Simulate an invalid pre-existing configuration.
        await f.Context.SaveChangesAsync();
        var id = await f.Context.Jobs.Select(x => x.Id).SingleAsync();
        Assert.Equal(JobAutoPublishOutcome.Disabled, (await Publisher(f).TryPublishAsync(id)).Outcome);
        Assert.Equal(OracleRecruitingCloudConfiguration.ErrorCode,
            (await Runner(f, new SnapshotProvider(f.Source.AtsType, [])).RunAsync(f.Source.Id)).StopReason);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public void ProviderEnumValuesRemainStable()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, new[] { (int)AtsType.Greenhouse, (int)AtsType.Lever,
            (int)AtsType.Ashby, (int)AtsType.SuccessFactors, (int)AtsType.Workday, (int)AtsType.OracleRecruitingCloud });
    }
}
