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

public sealed class PublicPopularCompaniesTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GetApiJobsCompaniesPopularQueryIsSqlServerTranslatable()
    {
        using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseSqlServer(
                    "Server=(localdb)\\MSSQLLocalDB;Database=TranslationOnly;Trusted_Connection=True")
                .Options);
        var repository = new PublicJobRepository(
            context, new FixedTimeProvider(Now));

        var sql = repository.PopularCompaniesQuery(10).ToQueryString();

        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExpiresAtUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CompaniesWithCandidateVisibleJobsQueryTranslatesToPostgresExists()
    {
        using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseNpgsql("Host=localhost;Database=TranslationOnly")
                .Options);
        var repository = new PublicJobRepository(context, new FixedTimeProvider(Now));

        var sql = repository.CompaniesWithCandidateVisibleJobsQuery().ToQueryString();

        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("btrim", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetApiJobsCompaniesPopularPreservesVisibilityAndOrdering()
    {
        await using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var alpha = Company("Alpha", true);
        var bravo = Company("Bravo", false);
        var charlie = Company("Charlie", true);
        var unavailable = Company("Unavailable", true);
        var deleted = Company("Deleted", true);
        deleted.IsDeleted = true;
        context.Companies.AddRange(alpha, bravo, charlie, unavailable, deleted);
        context.Jobs.AddRange(
            AvailableJob(alpha, "alpha-1"),
            AvailableJob(alpha, "alpha-2"),
            AvailableJob(bravo, "bravo-1"),
            AvailableJob(charlie, "charlie-1"),
            Job(unavailable, "hidden", JobStatus.Published, true, Now.AddDays(2)),
            Job(unavailable, "expired", JobStatus.Published, false, Now),
            Job(unavailable, "closed", JobStatus.Closed, false, Now.AddDays(2)),
            Job(unavailable, "deleted-job", JobStatus.Published, false, Now.AddDays(2), true),
            AvailableJob(deleted, "deleted-company"));
        await context.SaveChangesAsync();
        var repository = new PublicJobRepository(
            context, new FixedTimeProvider(Now));
        var service = new PublicJobService(
            repository, new PublicJobQueryValidator());
        var controller = new PublicJobsController(service);

        var action = await controller.PopularCompanies(10);
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<
            ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>>(
            response.Value);
        var result = envelope.Data;

        Assert.Collection(
            result,
            company =>
            {
                Assert.Equal(alpha.Id, company.Id);
                Assert.Equal(2, company.ActiveJobCount);
            },
            company =>
            {
                Assert.Equal(charlie.Id, company.Id);
                Assert.Equal(1, company.ActiveJobCount);
            },
            company =>
            {
                Assert.Equal(bravo.Id, company.Id);
                Assert.Equal(1, company.ActiveJobCount);
            });
    }

    [Fact]
    public async Task CompaniesEndpointReturnsAllDistinctLogoCompaniesWithCandidateVisibleJobs()
    {
        await using var context = new JobPortalDbContext(
            new DbContextOptionsBuilder<JobPortalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var companies = Enumerable.Range(0, 55)
            .Select(index => Company($"Company {index:D2}", false, $"https://logos.test/{index}.png"))
            .ToArray();
        var noJobs = Company("No jobs", false, "https://logos.test/no-jobs.png");
        var noLogo = Company("No logo", false, null);
        var blankLogo = Company("Blank logo", false, "  ");
        var unpublished = Company("Unpublished", false, "https://logos.test/unpublished.png");
        var unavailable = Company("Unavailable", false, "https://logos.test/unavailable.png");
        var deletedCompany = Company("Deleted company", false, "https://logos.test/deleted.png");
        deletedCompany.IsDeleted = true;
        context.Companies.AddRange(companies);
        context.Companies.AddRange(noJobs, noLogo, blankLogo, unpublished, unavailable, deletedCompany);

        for (var index = 0; index < companies.Length; index++)
        {
            var company = companies[index];
            context.Jobs.Add(AvailableJob(company, $"{company.Slug}-one"));
            if (index > 0)
                context.Jobs.Add(AvailableJob(company, $"{company.Slug}-two"));
        }
        context.Jobs.Add(Job(noLogo, "no-logo-job", JobStatus.Published, false, Now.AddDays(2)));
        context.Jobs.Add(Job(blankLogo, "blank-logo-job", JobStatus.Published, false, Now.AddDays(2)));
        context.Jobs.Add(Job(unpublished, "draft-job", JobStatus.Draft, false, Now.AddDays(2)));
        context.Jobs.Add(Job(unavailable, "hidden-job", JobStatus.Published, true, Now.AddDays(2)));
        context.Jobs.Add(Job(unavailable, "expired-job", JobStatus.Published, false, Now));
        context.Jobs.Add(Job(unavailable, "deleted-job", JobStatus.Published, false, Now.AddDays(2), true));
        context.Jobs.Add(AvailableJob(deletedCompany, "deleted-company-job"));
        await context.SaveChangesAsync();

        var service = new PublicJobService(
            new PublicJobRepository(context, new FixedTimeProvider(Now)),
            new PublicJobQueryValidator());
        var controller = new PublicJobsController(service);

        var action = await controller.Companies(CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResponse<IReadOnlyCollection<PublicCompanyLogoResponse>>>(response.Value);
        var result = envelope.Data.ToArray();

        Assert.Equal(55, result.Length);
        Assert.Equal(55, result.Select(company => company.CompanyId).Distinct().Count());
        Assert.Equal(companies.Select(company => company.Id).Order().ToArray(),
            result.Select(company => company.CompanyId).Order().ToArray());
        Assert.Equal(result.OrderBy(company => company.Name).ThenBy(company => company.CompanyId), result);
        Assert.All(result, company => Assert.False(string.IsNullOrWhiteSpace(company.LogoUrl)));
        Assert.Equal(new[] { "CompanyId", "LogoUrl", "Name" },
            typeof(PublicCompanyLogoResponse).GetProperties().Select(property => property.Name).Order().ToArray());
    }

    private static Company Company(string name, bool isVerified, string? logoUrl = null) =>
        new()
        {
            Name = name,
            Slug = name.ToLowerInvariant(),
            LogoUrl = logoUrl,
            IsVerified = isVerified,
            OwnerUserId = Guid.NewGuid()
        };

    private static Job AvailableJob(Company company, string slug) =>
        Job(company, slug, JobStatus.Published, false, Now.AddDays(2));

    private static Job Job(
        Company company,
        string slug,
        JobStatus status,
        bool isHidden,
        DateTime? expiresAtUtc,
        bool isDeleted = false) =>
        new()
        {
            ReferenceNumber = $"REF-{slug}",
            Title = slug,
            Slug = slug,
            Description = "Description",
            ApplicationUrl = "https://example.test/apply",
            Status = status,
            IsHidden = isHidden,
            IsDeleted = isDeleted,
            PublishedAtUtc = Now.AddDays(-1),
            ExpiresAtUtc = expiresAtUtc,
            CompanyId = company.Id,
            Company = company,
            CategoryId = null
        };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
