using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class SalaryCompatibilityTests
{
    private static readonly PublicJobSort[] SalarySorts = [PublicJobSort.SalaryHighToLow, PublicJobSort.MinimumSalary, PublicJobSort.MaximumSalary];
    [Fact]
    public async Task LegacyControllerRequestsCompareOnlyKnownAnnualRupeesAndNewClientsSelectTheirOwnScope()
    {
        await using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await VerifyContractAsync(db);
    }

    internal static async Task VerifyContractAsync(JobPortalDbContext db)
    {
        var owner = new User { Email = "salary-fixture@example.test", NormalizedEmail = "SALARY-FIXTURE@EXAMPLE.TEST",
            FirstName = "Fixture", LastName = "Owner", RoleId = await db.Roles.Select(role => role.Id).FirstOrDefaultAsync() };
        var company = new Company { Name = "Salary Fixture", Slug = "salary-fixture", OwnerUser = owner, OwnerUserId = owner.Id };
        Job Make(string name, decimal minimum, decimal maximum, string? currency, SalaryPeriod? period) => new()
        {
            Title = name, Slug = name, ReferenceNumber = name, Company = company, Description = "Fixture",
            Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow.AddDays(-1),
            MinimumSalary = minimum, MaximumSalary = maximum, CurrencyCode = currency, SalaryPeriod = period
        };
        db.AddRange(Make("annual-high", 800000, 1200000, "INR", SalaryPeriod.Yearly),
            Make("annual-low", 100000, 200000, "INR", SalaryPeriod.Yearly),
            Make("dollars", 900000, 1200000, "USD", SalaryPeriod.Yearly),
            Make("monthly", 900000, 1200000, "INR", SalaryPeriod.Monthly),
            Make("hourly", 900000, 1200000, "INR", SalaryPeriod.Hourly),
            Make("unknown-period", 900000, 1200000, "INR", null),
            Make("unknown-currency", 900000, 1200000, null, SalaryPeriod.Yearly));
        await db.SaveChangesAsync();
        var repository = new PublicJobRepository(db, TimeProvider.System);
        var service = new PublicJobService(repository, new PublicJobQueryValidator());
        var controller = new PublicJobsController(service);
        var legacy = new PublicJobQuery(MinAmount: 700000, MaxAmount: 1500000, SortBy: PublicJobSort.SalaryHighToLow);
        var action = await controller.Search(legacy, default);
        var payload = Assert.IsType<ApiResponse<PublicJobSearchResponse>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("annual-high", Assert.Single(payload.Data.Items).Title);
        var aliases = await service.SearchAsync(new(MinimumSalary: 700000, MaximumSalary: 1500000));
        Assert.Equal("annual-high", Assert.Single(aliases.Items).Title);
        var explicitDollar = await service.SearchAsync(legacy with { CurrencyCode = "USD", SalaryPeriod = SalaryPeriod.Yearly });
        Assert.Equal("dollars", Assert.Single(explicitDollar.Items).Title);
        var monthly = await service.SearchAsync(legacy with { CurrencyCode = "INR", SalaryPeriod = SalaryPeriod.Monthly });
        Assert.Equal("monthly", Assert.Single(monthly.Items).Title);
        var hourly = await service.SearchAsync(legacy with { CurrencyCode = "INR", SalaryPeriod = SalaryPeriod.Hourly });
        Assert.Equal("hourly", Assert.Single(hourly.Items).Title);
        Assert.Equal(7, (await service.SearchAsync(new())).TotalCount);
        foreach (var sort in SalarySorts)
            Assert.Equal(2, (await service.SearchAsync(new(SortBy: sort))).TotalCount);
        var facets = await service.GetFilterOptionsAsync(new());
        Assert.Equal(new DecimalRangeFacet(100000, 1200000, 2), facets.SalaryRange);
        Assert.Equal(7, facets.EmploymentTypes.Sum(option => option.Count));
        await Assert.ThrowsAsync<ValidationException>(() => service.SearchAsync(legacy with { CurrencyCode = "USD" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.SearchAsync(legacy with { SalaryPeriod = SalaryPeriod.Monthly }));
        Assert.Empty((await repository.SearchAsync(legacy with { CurrencyCode = "USD" })).Items);
    }

    [Theory]
    [InlineData("", SalaryPeriod.Yearly)]
    [InlineData("usd", SalaryPeriod.Yearly)]
    [InlineData("USD", (SalaryPeriod)99)]
    public void InvalidExplicitScopesRemainInvalid(string currency, SalaryPeriod period) =>
        Assert.False(new PublicJobQueryValidator().Validate(new PublicJobQuery(MinAmount: 1, CurrencyCode: currency, SalaryPeriod: period)).IsValid);
}

public sealed class SalaryCompatibilityPostgresTests(WorkdayCheckpointDatabase database) : IClassFixture<WorkdayCheckpointDatabase>
{
    [SalaryLocalPostgresFact]
    public async Task LegacyAndExplicitSalaryApiContractsExecuteSafelyOnPostgres()
    {
        await using var db = database.Open();
        await SalaryCompatibilityTests.VerifyContractAsync(db);
    }

    private sealed class SalaryLocalPostgresFactAttribute : FactAttribute
    {
        public SalaryLocalPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires explicit disposable localhost PostgreSQL fixture.";
        }
    }
}
