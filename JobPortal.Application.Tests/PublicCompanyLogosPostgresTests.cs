using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PublicCompanyLogosPostgresTests(WorkdayCheckpointDatabase database)
    : IClassFixture<WorkdayCheckpointDatabase>
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    [DisposableLocalPostgresFact]
    public async Task CompanyLogoQueryExecutesOnPostgresAndReturnsCompleteVisibleSet()
    {
        await using var db = database.Open();
        var owner = new User
        {
            Email = "company-logos@example.test",
            NormalizedEmail = "COMPANY-LOGOS@EXAMPLE.TEST",
            FirstName = "Logo",
            LastName = "Fixture",
            RoleId = await db.Roles.Select(role => role.Id).FirstAsync()
        };
        var companies = Enumerable.Range(0, 55).Select(index => new Company
        {
            Name = $"Logo Company {index:D2}",
            Slug = $"logo-company-{index:D2}",
            LogoUrl = $"https://logos.example.test/{index:D2}.png",
            OwnerUser = owner
        }).ToArray();
        var noJobs = MakeCompany(owner, "No jobs", "no-jobs", "https://logos.example.test/no-jobs.png");
        var noLogo = MakeCompany(owner, "No logo", "no-logo", null);
        var blankLogo = MakeCompany(owner, "Blank logo", "blank-logo", "   ");
        var invisible = MakeCompany(owner, "Invisible", "invisible", "https://logos.example.test/invisible.png");
        var deletedCompany = MakeCompany(owner, "Deleted", "deleted", "https://logos.example.test/deleted.png");
        deletedCompany.IsDeleted = true;

        db.Companies.AddRange(companies);
        db.Companies.AddRange(noJobs, noLogo, blankLogo, invisible, deletedCompany);
        for (var index = 0; index < companies.Length; index++)
        {
            db.Jobs.Add(MakeJob(companies[index], $"logo-job-{index:D2}-one", JobStatus.Published));
            if (index > 0)
                db.Jobs.Add(MakeJob(companies[index], $"logo-job-{index:D2}-two", JobStatus.Published));
        }
        db.Jobs.Add(MakeJob(noLogo, "no-logo-job", JobStatus.Published));
        db.Jobs.Add(MakeJob(blankLogo, "blank-logo-job", JobStatus.Published));
        db.Jobs.Add(MakeJob(invisible, "draft-job", JobStatus.Draft));
        db.Jobs.Add(MakeJob(invisible, "hidden-job", JobStatus.Published, hidden: true));
        db.Jobs.Add(MakeJob(invisible, "deleted-job", JobStatus.Published, deleted: true));
        db.Jobs.Add(MakeJob(invisible, "expired-job", JobStatus.Published, expiresAt: Now));
        db.Jobs.Add(MakeJob(deletedCompany, "deleted-company-job", JobStatus.Published));
        await db.SaveChangesAsync();

        var result = await new PublicJobRepository(db, new FixedTimeProvider(Now))
            .GetCompaniesWithCandidateVisibleJobsAsync();

        Assert.Equal(55, result.Count);
        Assert.Equal(55, result.Select(company => company.CompanyId).Distinct().Count());
        Assert.Equal(companies.Select(company => company.Id), result.Select(company => company.CompanyId));
        Assert.All(result, company => Assert.False(string.IsNullOrWhiteSpace(company.LogoUrl)));
    }

    private static Company MakeCompany(User owner, string name, string slug, string? logoUrl) => new()
    {
        Name = name,
        Slug = slug,
        LogoUrl = logoUrl,
        OwnerUser = owner
    };

    private static Job MakeJob(
        Company company,
        string slug,
        JobStatus status,
        bool hidden = false,
        bool deleted = false,
        DateTime? expiresAt = null) => new()
    {
        ReferenceNumber = slug,
        Title = slug,
        Slug = slug,
        Description = "Fixture job",
        ApplicationUrl = "https://example.test/apply",
        Status = status,
        IsHidden = hidden,
        IsDeleted = deleted,
        PublishedAtUtc = Now.AddDays(-1),
        ExpiresAtUtc = expiresAt ?? Now.AddDays(1),
        Company = company
    };

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class DisposableLocalPostgresFactAttribute : FactAttribute
    {
        public DisposableLocalPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires the disposable loopback PostgreSQL fixture; production configuration is never used.";
        }
    }
}
