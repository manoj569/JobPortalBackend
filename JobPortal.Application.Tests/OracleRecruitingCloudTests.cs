using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class OracleRecruitingCloudTests
{
    private const string Host = "fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com";
    private const string Identifier = "oracle:" + Host + ":CX_1";
    private const string CareerPage = "https://" + Host + "/hcmUI/CandidateExperience/en/sites/CX_1/jobs";

    private static SaveJobSourceRequest Request(Guid companyId) => new(companyId, CareerPage,
        AtsType.OracleRecruitingCloud, Identifier, IsActive: false,
        OracleRecruitingCloud: new(Host, "CX_1"));

    [Theory]
    [InlineData("fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com", "CX_1")]
    [InlineData("fa-etvl-saasfaprod1.fa.ocs.oraclecloud.com", "CX_1")]
    [InlineData("fa-ewji-saasfaprod1.fa.ocs.oraclecloud.com", "CX_1")]
    [InlineData("fa-eumz-saasfaprod1.fa.ocs.oraclecloud.com", "CX_2")]
    public void TenantAndSiteAreConfigurationNotCompanySpecific(string host, string site)
    {
        var request = Request(Guid.NewGuid()) with
        {
            AtsIdentifier = $"oracle:{host}:{site}",
            CareerPageUrl = $"https://{host}/hcmUI/CandidateExperience/en/sites/{site}/jobs",
            OracleRecruitingCloud = new(host, site)
        };
        Assert.True(new SaveJobSourceRequestValidator().Validate(request).IsValid);
        Assert.Equal(request.AtsIdentifier, OracleRecruitingCloudConfiguration.Parse(request.AtsIdentifier)!.SourceIdentifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oracle:localhost:CX_1")]
    [InlineData("oracle:127.0.0.1:CX_1")]
    [InlineData("oracle:169.254.169.254:CX_1")]
    [InlineData("oracle:10.0.0.1:CX_1")]
    [InlineData("oracle:[::1]:CX_1")]
    [InlineData("oracle:tenant.oraclecloud.com:CX_1")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com.evil.test:CX_1")]
    [InlineData("oracle:evil.test@tenant.fa.ocs.oraclecloud.com:CX_1")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com:443:CX_1")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com.:CX_1")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com:../CX_1")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com:CX_1?redirect=x")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com:CX_1%2Fjobs")]
    [InlineData("oracle:tenant.fa.ocs.oraclecloud.com:CX_\n1")]
    public void UnsafeIdentifiersAreRejected(string? identifier)
    {
        Assert.Null(OracleRecruitingCloudConfiguration.Parse(identifier));
        Assert.False(new SaveJobSourceRequestValidator().Validate(Request(Guid.NewGuid()) with { AtsIdentifier = identifier }).IsValid);
    }

    [Theory]
    [InlineData("http://" + Host + "/hcmUI/CandidateExperience/en/sites/CX_1/jobs")]
    [InlineData("https://localhost/hcmUI/CandidateExperience/en/sites/CX_1/jobs")]
    [InlineData("https://user:password@" + Host + "/hcmUI/CandidateExperience/en/sites/CX_1/jobs")]
    [InlineData("https://" + Host + ":443/hcmUI/CandidateExperience/en/sites/CX_1/jobs")]
    [InlineData(CareerPage + "?redirect=https://127.0.0.1")]
    [InlineData(CareerPage + "#fragment")]
    [InlineData("https://" + Host + "/hcmUI/CandidateExperience/en/sites/CX_2/jobs")]
    [InlineData("https://" + Host + "/hcmUI/CandidateExperience/en/sites/CX_1/x/../jobs")]
    [InlineData("https://" + Host + "/hcmUI/CandidateExperience/en/sites/CX_1%2fjobs")]
    public void CareerPageMustMatchSafeCanonicalTenantAndSite(string url) =>
        Assert.False(new SaveJobSourceRequestValidator().Validate(Request(Guid.NewGuid()) with { CareerPageUrl = url }).IsValid);

    [Fact]
    public void ActivationPublicationAndSelfDeclaredAuthorizationAreRejected()
    {
        var validator = new SaveJobSourceRequestValidator();
        var request = Request(Guid.NewGuid());
        Assert.False(validator.Validate(request with { IsActive = true }).IsValid);
        Assert.False(validator.Validate(request with { OracleRecruitingCloud = new(Host, "CX_1", AutoPublishEnabled: true) }).IsValid);
        Assert.False(validator.Validate(request with { OracleRecruitingCloud = new(Host, "CX_1", AuthorizationStatus: "Approved") }).IsValid);
        Assert.False(validator.Validate(request with { OracleRecruitingCloud = new(Host, "CX_2") }).IsValid);
        Assert.False(validator.Validate(request with { AtsType = AtsType.Workday }).IsValid);
    }

    [Fact]
    public async Task AdminRoundTripUsesExistingCompaniesAndCanonicalIdentityWithoutDuplicates()
    {
        using var f = new JobSourceFixture();
        var request = Request(f.Company.Id);
        var first = await f.Service.CreateAsync(request with { AtsIdentifier = " oracle:" + Host.ToUpperInvariant() + ":CX_1 " });
        Assert.Equal(Identifier, first.AtsIdentifier);
        Assert.Equal(720, first.ScanIntervalMinutes);
        Assert.False(first.IsActive);
        Assert.Equal(Host, first.OracleRecruitingCloud!.OracleHost);
        Assert.Equal("CX_1", first.OracleRecruitingCloud.CareerSiteId);
        Assert.False(first.OracleRecruitingCloud.AutoPublishEnabled);
        Assert.Equal("AuthorizationRequired", first.OracleRecruitingCloud.AuthorizationStatus);
        Assert.Equal("Oracle Recruiting Cloud", first.OracleRecruitingCloud.ProviderDisplayName);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateAsync(request));
        Assert.Equal("duplicate_job_source", conflict.Code);

        var secondCompany = new Company { Name = "Second Company", NormalizedName = "SECOND COMPANY", Slug = "second" };
        f.Context.Companies.Add(secondCompany);
        await f.Context.SaveChangesAsync();
        var second = await f.Service.CreateAsync(request with { CompanyId = secondCompany.Id,
            AtsIdentifier = "oracle:second.fa.ocs.oraclecloud.com:CX_2",
            CareerPageUrl = "https://second.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_2/jobs",
            OracleRecruitingCloud = new("second.fa.ocs.oraclecloud.com", "CX_2") });
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await f.Context.Companies.CountAsync());
        Assert.Equal(2, (await f.Service.SearchAsync(new(AtsType: AtsType.OracleRecruitingCloud))).TotalCount);
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.UpdateAsync(first.Id, request with { IsActive = true }));
        var updated = await f.Service.UpdateAsync(first.Id, request with { ScanIntervalMinutes = 1440 });
        Assert.False(updated.IsActive);
        Assert.Equal(1440, updated.ScanIntervalMinutes);
        Assert.Equal(first.OracleRecruitingCloud, (await f.Service.GetByIdAsync(first.Id)).OracleRecruitingCloud);
        Assert.DoesNotContain("OracleRecruitingCloud", JsonSerializer.Serialize(await f.Service.GetByIdAsync(f.Source.Id)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderCannotReturnFakeJobsOrCertifyAnEmptySnapshot()
    {
        IExternalJobProvider provider = new OracleRecruitingCloudJobSourceProvider();
        var source = new JobSource { AtsType = AtsType.OracleRecruitingCloud, AtsIdentifier = Identifier, CareerPageUrl = CareerPage };
        Assert.False(provider is ICompleteExternalJobProvider);
        var blocked = await Assert.ThrowsAsync<BadRequestException>(() => provider.FetchJobsAsync(source));
        Assert.Equal(OracleRecruitingCloudConfiguration.ErrorCode, blocked.Code);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FetchJobsAsync(source, cancelled.Token));
        source.CareerPageUrl = "https://localhost/";
        var invalid = await Assert.ThrowsAsync<BadRequestException>(() => provider.FetchJobsAsync(source));
        Assert.Equal("invalid_oracle_source", invalid.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OracleCannotQueueRunPublishOrReconcileEvenWithAnActiveDatabaseRow(bool active)
    {
        using var f = new JobSourceFixture();
        f.Source.AtsType = AtsType.OracleRecruitingCloud;
        f.Source.IsActive = active;
        f.Source.LastSuccessfulRunAtUtc = JobSourceFixture.Now.AddDays(-1);
        f.Source.AtsIdentifier = Identifier;
        f.Source.CareerPageUrl = CareerPage;
        var draft = new Job { CompanyId = f.Company.Id, JobSourceId = f.Source.Id, ExternalJobId = "draft", Status = JobStatus.Draft };
        var published = new Job { CompanyId = f.Company.Id, JobSourceId = f.Source.Id, ExternalJobId = "published", Status = JobStatus.Published };
        f.Context.Jobs.AddRange(draft, published);
        await f.Context.SaveChangesAsync();
        var queue = new JobSourceRunService(f.Repository, new JobSourceRunStore(f.Context), TimeProvider.System);
        var error = await Assert.ThrowsAsync<BadRequestException>(() => queue.EnqueueAsync(f.Source.Id, null, default));
        Assert.Equal(OracleRecruitingCloudConfiguration.ErrorCode, error.Code);
        Assert.Equal(OracleRecruitingCloudConfiguration.ErrorCode,
            (await Assert.ThrowsAsync<BadRequestException>(() => f.Service.RunAsync(f.Source.Id))).Code);
        Assert.Empty(await f.Repository.GetDueSourcesAsync(JobSourceFixture.Now, 25));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await f.Runner.RunAsync(f.Source.Id);
            Assert.False(result.Succeeded);
            Assert.False(result.ScanComplete);
            Assert.False(result.ReconciliationSafe);
            Assert.Equal(OracleRecruitingCloudConfiguration.ErrorCode, result.StopReason);
            Assert.Contains(result.StopReason, result.ReconciliationBlockReasons);
            Assert.Equal(0, result.Created + result.Published + result.Closed);
        }
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(await f.Context.JobSourceRuns.ToArrayAsync());
        Assert.Equal(2, await f.Context.Jobs.CountAsync());
        Assert.Equal(JobStatus.Draft, draft.Status);
        Assert.Equal(JobStatus.Published, published.Status);
        Assert.Equal(JobSourceFixture.Now.AddDays(-1), f.Source.LastSuccessfulRunAtUtc);
        Assert.Null(f.Source.LastRunAtUtc);
    }
}
