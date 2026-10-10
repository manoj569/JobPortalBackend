using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PwcWorkdaySourceRegistrationTests
{
    [Fact]
    public async Task VerifiedPwcConfigurationRegistersWithoutIngestionAndRejectsRepeatedRegistration()
    {
        using var f = new JobSourceFixture();
        var company = new Company { Name = "PwC", NormalizedName = "PWC", Slug = "pwc" };
        f.Context.Companies.Add(company);
        await f.Context.SaveChangesAsync();
        var request = new SaveJobSourceRequest(company.Id,
            "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers",
            AtsType.Workday, "pwc/Global_Experienced_Careers", IsActive: false);

        var created = await f.Service.CreateAsync(request);
        var source = await f.Context.JobSources.SingleAsync(x => x.Id == created.Id);
        var target = WorkdayJobSourceProvider.ValidateSource(source);
        Assert.Equal(company.Id, created.CompanyId);
        Assert.Equal("PwC", created.CompanyName);
        Assert.False(created.IsActive);
        Assert.Equal(720, created.ScanIntervalMinutes);
        Assert.Equal("pwc", target.Tenant);
        Assert.Equal("Global_Experienced_Careers", target.Site);
        Assert.Equal(new Uri("https://pwc.wd3.myworkdayjobs.com/wday/cxs/pwc/Global_Experienced_Careers"), target.ApiRoot);
        Assert.Null(created.LastRunAtUtc);
        Assert.Null(created.CategoryId);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateAsync(request with { AtsIdentifier = " pwc/Global_Experienced_Careers " }));
        Assert.Equal("duplicate_job_source", duplicate.Code);
        Assert.Equal(1, await f.Context.JobSources.CountAsync(x => x.CompanyId == company.Id));
        Assert.Equal(f.Request.CareerPageUrl, f.Source.CareerPageUrl);
        Assert.Equal(AtsType.Greenhouse, f.Source.AtsType);
        Assert.Empty(await f.Context.Jobs.ToArrayAsync());
    }
}
