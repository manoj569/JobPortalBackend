using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateJobSearchPostgresTests(WorkdayCheckpointDatabase database)
    : IClassFixture<WorkdayCheckpointDatabase>
{
    [DisposablePostgresFact]
    public async Task ContentTokensLiteralWildcardsVisibilityAndFilteredPagesExecuteOnPostgres()
    {
        await using var db = database.Open();
        var owner = new User { Email = "search@example.test", NormalizedEmail = "SEARCH@EXAMPLE.TEST",
            FirstName = "Search", LastName = "Fixture", RoleId = await db.Roles.Select(x => x.Id).FirstAsync() };
        var company = new Company { Name = "Search Fixture", Slug = "search-fixture", OwnerUser = owner };
        Job Make(string slug) => new() { Title = "Support Engineer", Slug = slug, ReferenceNumber = slug,
            Company = company, Description = "Experience with .NET ASP.NET C# C++ Node.js SQL Server 50% foo_bar \\ tools",
            Requirements = "Azure", Responsibilities = "React", Department = "Engineering", RoleCategory = "Software",
            Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow.AddDays(-1), Location = "Pune",
            WorkplaceType = WorkplaceType.Remote, MinimumExperienceYears = 3 };
        var match = Make("match");
        var hidden = Make("hidden"); hidden.IsHidden = true;
        var deleted = Make("deleted"); deleted.IsDeleted = true;
        var draft = Make("draft"); draft.Status = JobStatus.Draft;
        var expired = Make("expired"); expired.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
        var unrelated = Make("unrelated"); unrelated.Description = "Unrelated"; unrelated.Requirements = null;
        unrelated.Responsibilities = null; unrelated.IsFeatured = true;
        db.AddRange(match, hidden, deleted, draft, expired, unrelated);
        await db.SaveChangesAsync();
        var service = new PublicJobService(new PublicJobRepository(db, TimeProvider.System), new PublicJobQueryValidator());
        foreach (var keyword in new[] { ".NET", ".net", "  .NET  ", "NET", "C#", "C++", "Node.js", "SQL Server",
            "%", "_", "\\", "Azure", "React", "engineer azure" })
        {
            var page = await service.SearchAsync(new(Keyword: keyword, PageSize: 1));
            Assert.Equal(match.Id, Assert.Single(page.Items).Id);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(1, page.TotalPages);
        }
        var query = new PublicJobQuery(Keyword: ".net azure", Locations: ["PUNE"], WorkModes: [WorkplaceType.Remote],
            MinExperienceYears: 2, MaxExperienceYears: 4, PageSize: 1);
        Assert.Equal(match.Id, Assert.Single((await service.SearchAsync(query)).Items).Id);
        Assert.Equal(1, Assert.Single((await service.GetFilterOptionsAsync(query)).Locations).Count);
    }

    private sealed class DisposablePostgresFactAttribute : FactAttribute
    {
        public DisposablePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires explicit disposable localhost PostgreSQL fixture; production configuration is never used.";
        }
    }
}
