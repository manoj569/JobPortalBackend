using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PtcWorkdaySourceRegistrationTests
{
    [Fact]
    public async Task VerifiedPtcConfigurationRegistersWithoutIngestionAndRejectsRepeatedRegistration()
    {
        using var f = new JobSourceFixture();
        var company = new Company { Name = "PTC", NormalizedName = "PTC", Slug = "ptc" };
        f.Context.Companies.Add(company);
        await f.Context.SaveChangesAsync();
        var request = new SaveJobSourceRequest(company.Id,
            "https://ptc.wd1.myworkdayjobs.com/PTC",
            AtsType.Workday, "ptc/PTC", IsActive: false, ScanIntervalMinutes: 720);

        var created = await f.Service.CreateAsync(request);
        var source = await f.Context.JobSources.SingleAsync(x => x.Id == created.Id);
        var target = WorkdayJobSourceProvider.ValidateSource(source);
        Assert.Equal(company.Id, created.CompanyId);
        Assert.Equal("PTC", created.CompanyName);
        Assert.Equal(request.CareerPageUrl, created.CareerPageUrl);
        Assert.Equal(AtsType.Workday, created.AtsType);
        Assert.Equal("ptc/PTC", created.AtsIdentifier);
        Assert.False(created.IsActive);
        Assert.Equal(720, created.ScanIntervalMinutes);
        Assert.Equal("ptc", target.Tenant);
        Assert.Equal("PTC", target.Site);
        Assert.Equal(new Uri("https://ptc.wd1.myworkdayjobs.com/wday/cxs/ptc/PTC"), target.ApiRoot);
        Assert.Null(created.LastRunAtUtc);
        Assert.Null(created.LastSuccessfulRunAtUtc);
        Assert.Null(created.CategoryId);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(request with { AtsIdentifier = " ptc/PTC " }));
        Assert.Equal("duplicate_job_source", duplicate.Code);
        Assert.Equal(1, await f.Context.JobSources.CountAsync(x => x.CompanyId == company.Id));
        Assert.Equal(f.Request.CareerPageUrl, f.Source.CareerPageUrl);
        Assert.Equal(AtsType.Greenhouse, f.Source.AtsType);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
    }
}
