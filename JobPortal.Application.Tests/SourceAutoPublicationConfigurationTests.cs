using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Auditing;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static JobPortal.Application.Tests.GenericIndiaJobIngestionTests;

namespace JobPortal.Application.Tests;

public sealed class SourceAutoPublicationConfigurationTests
{
    [Theory]
    [InlineData(AtsType.Greenhouse, true)]
    [InlineData(AtsType.Greenhouse, false)]
    [InlineData(AtsType.Lever, true)]
    [InlineData(AtsType.Lever, false)]
    [InlineData(AtsType.Ashby, true)]
    [InlineData(AtsType.Ashby, false)]
    [InlineData(AtsType.Workday, true)]
    [InlineData(AtsType.Workday, false)]
    [InlineData(AtsType.SuccessFactors, true)]
    [InlineData(AtsType.SuccessFactors, false)]
    public async Task ExplicitCreateSettingPersistsAndAppearsInDetailAndList(AtsType provider, bool enabled)
    {
        using var f = new JobSourceFixture();
        var created = await f.Service.CreateAsync(f.Request with
            { AtsType = provider, AtsIdentifier = "new-source", AutoPublishEnabled = enabled });
        f.Context.ChangeTracker.Clear();
        Assert.Equal(enabled, (await f.Repository.GetByIdAsync(created.Id))!.AutoPublishEnabled);
        Assert.Equal(enabled, (await f.Service.GetByIdAsync(created.Id)).AutoPublishEnabled);
        Assert.Equal(enabled, (await f.Service.SearchAsync(new())).Items.Single(x => x.Id == created.Id).AutoPublishEnabled);
        var json = JsonSerializer.Serialize(created, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(enabled, document.RootElement.GetProperty("autoPublishEnabled").GetBoolean());
    }

    [Fact]
    public async Task LegacyOmittedCreateRetainsExplicitOptInDefault()
    {
        using var f = new JobSourceFixture();
        Assert.Null(f.Request.AutoPublishEnabled);
        var response = await f.Service.CreateAsync(f.Request with { AtsIdentifier = "legacy-caller" });
        Assert.False(response.AutoPublishEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullUpdateWithOmittedSettingPreservesExistingDecision(bool enabled)
    {
        using var f = new JobSourceFixture();
        f.Source.AutoPublishEnabled = enabled;
        await f.Context.SaveChangesAsync();
        await f.Service.UpdateAsync(f.Source.Id, f.Request with { ScanIntervalMinutes = 1440 });
        f.Context.ChangeTracker.Clear();
        var saved = (await f.Repository.GetByIdAsync(f.Source.Id))!;
        Assert.Equal(enabled, saved.AutoPublishEnabled);
        Assert.Equal(1440, saved.ScanIntervalMinutes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullUpdateCanExplicitlyChangeBothDirections(bool enabled)
    {
        using var f = new JobSourceFixture();
        f.Source.AutoPublishEnabled = !enabled;
        await f.Context.SaveChangesAsync();
        await f.Service.UpdateAsync(f.Source.Id, f.Request with { AutoPublishEnabled = enabled });
        f.Context.ChangeTracker.Clear();
        Assert.Equal(enabled, (await f.Repository.GetByIdAsync(f.Source.Id))!.AutoPublishEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ToggleOnlyUpdatePreservesActivityAndAllOtherSourceSettings(bool active, bool enabled)
    {
        using var f = new JobSourceFixture();
        f.Source.IsActive = active;
        f.Source.AutoPublishEnabled = !enabled;
        f.Source.ScanIntervalMinutes = 1440;
        f.Source.LastRunAtUtc = JobSourceFixture.Now;
        f.Source.ConsecutiveFailures = 2;
        await f.Context.SaveChangesAsync();
        var before = await f.Context.JobSources.AsNoTracking().SingleAsync();
        var response = await f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(enabled));
        f.Context.ChangeTracker.Clear();
        var saved = (await f.Repository.GetByIdAsync(f.Source.Id))!;
        Assert.Equal(enabled, response.AutoPublishEnabled);
        Assert.Equal(enabled, saved.AutoPublishEnabled);
        Assert.Equal(active, saved.IsActive);
        Assert.Equal(before.CompanyId, saved.CompanyId);
        Assert.Equal(before.AtsType, saved.AtsType);
        Assert.Equal(before.AtsIdentifier, saved.AtsIdentifier);
        Assert.Equal(before.CareerPageUrl, saved.CareerPageUrl);
        Assert.Equal(before.ScanIntervalMinutes, saved.ScanIntervalMinutes);
        Assert.Equal(before.LastRunAtUtc, saved.LastRunAtUtc);
        Assert.Equal(before.ConsecutiveFailures, saved.ConsecutiveFailures);
        Assert.Contains(f.Audit.Events, audit => audit.Metadata is not null &&
            audit.Metadata.GetValueOrDefault("autoPublishEnabledBefore") == (!enabled).ToString() &&
            audit.Metadata.GetValueOrDefault("autoPublishEnabledAfter") == enabled.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"autoPublishEnabled\":null}")]
    public async Task ToggleOnlyUpdateRequiresExplicitBoolean(string json)
    {
        using var f = new JobSourceFixture();
        var request = JsonSerializer.Deserialize<UpdateJobSourceAutoPublicationRequest>(json, JsonSerializerOptions.Web)!;
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.UpdateAutoPublicationAsync(f.Source.Id, request));
        Assert.True((await f.Repository.GetByIdAsync(f.Source.Id))!.AutoPublishEnabled);
        Assert.Empty(f.Audit.Events);
    }

    [Theory]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    [InlineData(AtsType.Workday)]
    [InlineData(AtsType.SuccessFactors)]
    [InlineData((AtsType)99)]
    public async Task SavedToggleDrivesExistingPublisherAndPreservesAdminSuppression(AtsType provider)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = provider;
        await f.Context.SaveChangesAsync();
        await f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(false));
        var runner = Runner(f, new SnapshotProvider(provider, [Vacancy(1)]));
        var disabled = await runner.RunAsync(f.Source.Id);
        Assert.Equal(1, disabled.AutoPublishDisabled);
        Assert.Equal(0, disabled.Published);
        var id = await f.Context.Jobs.Select(x => x.Id).SingleAsync();
        var log = new DecisionLogger();
        var publisher = new JobAutoPublishService(new JobRepository(f.Context), new JobQualityGate(), ManualService(f),
            Options.Create(new JobAggregationOptions()), TimeProvider.System, f.Repository, f.Locks, logger: log);
        Assert.Equal(JobAutoPublishOutcome.Disabled, (await publisher.TryPublishAsync(id)).Outcome);
        Assert.Contains(log.Messages, message => message.Contains("AUTO_PUBLISH_DISABLED", StringComparison.Ordinal));
        await f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(true));
        Assert.Equal(1, (await runner.RunAsync(f.Source.Id)).Published);
        Assert.Equal(1, (await new PublicJobRepository(f.Context, TimeProvider.System).SearchAsync(new())).TotalCount);
        await ManualService(f).UnpublishAsync(id);
        await f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(false));
        await f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(true));
        Assert.Equal(0, (await runner.RunAsync(f.Source.Id)).Published);
        Assert.Equal(JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ToggleCannotAuthorizeConfigurationOnlyProvider()
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.OracleRecruitingCloud;
        f.Source.AutoPublishEnabled = false;
        await f.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(true)));
        Assert.False((await f.Repository.GetByIdAsync(f.Source.Id))!.AutoPublishEnabled);
    }

    [Fact]
    public async Task ToggleRespectsExistingSourceRunGuard()
    {
        using var f = new JobSourceFixture();
        using var lease = f.Guard.Acquire(f.Source.Id);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.UpdateAutoPublicationAsync(f.Source.Id, new(false)));
        Assert.True((await f.Repository.GetByIdAsync(f.Source.Id))!.AutoPublishEnabled);
    }

    [Fact]
    public async Task OmittedFullUpdateCannotCarryEnabledSettingIntoUnauthorizedProvider()
    {
        using var f = new JobSourceFixture();
        var request = f.Request with
        {
            AtsType = AtsType.OracleRecruitingCloud,
            AtsIdentifier = "oracle:tenant.fa.ocs.oraclecloud.com:CX_1",
            CareerPageUrl = "https://tenant.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs",
            IsActive = false
        };
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => f.Service.UpdateAsync(f.Source.Id, request));
        f.Context.ChangeTracker.Clear();
        var source = (await f.Repository.GetByIdAsync(f.Source.Id))!;
        Assert.Equal(AtsType.Greenhouse, source.AtsType);
        Assert.True(source.AutoPublishEnabled);
    }

    [Fact]
    public void PublicationConfigurationSurvivesAuditSerializationWithoutExposingSecrets()
    {
        var metadata = new Dictionary<string, string?>
        {
            ["autoPublishEnabled"] = "True",
            ["autoPublishEnabledBefore"] = "False",
            ["autoPublishEnabledAfter"] = "True",
            ["apiKey"] = "test-secret"
        };
        var persisted = AuditMetadataPolicy.Serialize(metadata);
        var response = AuditMetadataPolicy.Deserialize(persisted);
        Assert.Equal("True", response["autoPublishEnabled"]);
        Assert.Equal("False", response["autoPublishEnabledBefore"]);
        Assert.Equal("True", response["autoPublishEnabledAfter"]);
        Assert.False(response.ContainsKey("apiKey"));
        Assert.DoesNotContain("test-secret", persisted);
    }

    private sealed class DecisionLogger : ILogger<JobAutoPublishService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
