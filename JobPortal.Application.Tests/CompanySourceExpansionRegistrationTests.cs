using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CompanySourceExpansionRegistrationTests
{
    // Only boards with both official ownership evidence and live India detail evidence.
    [Theory]
    [InlineData("NVIDIA", "nvidia.wd5.myworkdayjobs.com", "nvidia", "NVIDIAExternalCareerSite")]
    [InlineData("Kyndryl", "kyndryl.wd5.myworkdayjobs.com", "kyndryl", "KyndrylProfessionalCareers")]
    [InlineData("Johnson Controls", "jci.wd5.myworkdayjobs.com", "jci", "JCI")]
    [InlineData("Mastercard", "mastercard.wd1.myworkdayjobs.com", "mastercard", "CorporateCareers")]
    [InlineData("Intel", "intel.wd1.myworkdayjobs.com", "intel", "External")]
    [InlineData("Philips", "philips.wd3.myworkdayjobs.com", "philips", "jobs-and-careers")]
    public async Task InactiveRegistrationReusesCompanyAndRejectsDuplicateWithoutIngestion(
        string name, string host, string tenant, string site)
    {
        using var fixture = new JobSourceFixture();
        var company = new Company { Name = name, NormalizedName = name.ToUpperInvariant(), Slug = tenant };
        fixture.Context.Companies.Add(company);
        await fixture.Context.SaveChangesAsync();
        var request = new SaveJobSourceRequest(company.Id, $"https://{host}/{site}",
            AtsType.Workday, $"{tenant}/{site}", IsActive: false, ScanIntervalMinutes: 720);

        var created = await fixture.Service.CreateAsync(request);
        var source = await fixture.Context.JobSources.SingleAsync(x => x.Id == created.Id);
        var target = WorkdayJobSourceProvider.ValidateSource(source);
        Assert.Equal(company.Id, created.CompanyId);
        Assert.False(created.IsActive);
        Assert.Null(created.CategoryId);
        Assert.Null(created.LastRunAtUtc);
        Assert.Equal(tenant, target.Tenant);
        Assert.Equal(site, target.Site);
        Assert.Equal(new Uri($"https://{host}/wday/cxs/{tenant}/{site}"), target.ApiRoot);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.CreateAsync(request with { AtsIdentifier = $" {tenant}/{site} " }));
        Assert.Equal("duplicate_job_source", duplicate.Code);
        Assert.Equal(1, await fixture.Context.JobSources.CountAsync(x => x.CompanyId == company.Id));
        Assert.Equal(1, await fixture.Context.Companies.CountAsync(x => x.Id == company.Id));
        Assert.Empty(await fixture.Context.Jobs.ToArrayAsync());
        Assert.Equal(fixture.Request.CareerPageUrl, fixture.Source.CareerPageUrl);
    }
}
