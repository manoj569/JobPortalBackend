using JobPortal.Application.Features.HiringDrives;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using FluentValidation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class HiringDrivePostgresTests(WorkdayCheckpointDatabase database) : IClassFixture<WorkdayCheckpointDatabase>
{
    [LocalPostgresFact]
    public async Task HomepageSelectionUsesExplicitFlagStrictEndBoundaryAndExistingOrdering()
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var shownLater = new HiringDrive
        {
            Title = "Home later", StartAtUtc = now.AddDays(-1), IsActive = true,
            ShowOnHome = true, DisplayOrder = 2, ApplyUrl = "https://example.test/later"
        };
        var shownFirst = new HiringDrive
        {
            Title = "Home first", StartAtUtc = now.AddDays(-1), IsActive = true,
            ShowOnHome = true, DisplayOrder = 1, IsSponsored = true,
            ApplyUrl = "https://example.test/first"
        };
        var notSelected = new HiringDrive
        {
            Title = "Not selected", StartAtUtc = now.AddDays(-1), IsActive = true,
            ApplyUrl = "https://example.test/ordinary"
        };
        var endsNow = new HiringDrive
        {
            Title = "Ends now", StartAtUtc = now.AddDays(-1), EndAtUtc = now, IsActive = true,
            ShowOnHome = true, ApplyUrl = "https://example.test/ended"
        };
        db.AddRange(shownLater, shownFirst, notSelected, endsNow);
        await db.SaveChangesAsync();

        var repository = new HiringDriveRepository(db);
        Assert.Equal(new[] { "Home first", "Home later" },
            (await repository.HomepageAsync(now, default)).Select(drive => drive.Title));
        var publicDrives = await repository.PublicAsync(now, default);
        Assert.Contains(publicDrives, drive => drive.Id == notSelected.Id);
        Assert.Contains(publicDrives, drive => drive.Id == endsNow.Id);
        Assert.False((await db.HiringDrives.SingleAsync(drive => drive.Id == notSelected.Id)).ShowOnHome);
    }

    [LocalPostgresFact]
    public async Task CountersAreAtomicBoundedToEligibleDrivesAndClickDestinationIsAuthoritative()
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var drive = new HiringDrive { Title = "Fixture", StartAtUtc = now.AddDays(-1), IsActive = true, ApplyUrl = "https://example.test/apply" };
        var inactive = new HiringDrive { Title = "Inactive", StartAtUtc = now.AddDays(-1), ApplyUrl = "https://example.test/apply" };
        var expired = new HiringDrive { Title = "Expired", StartAtUtc = now.AddDays(-2), EndAtUtc = now.AddDays(-1), IsActive = true, ApplyUrl = "https://example.test/apply" };
        db.AddRange(drive, inactive, expired); await db.SaveChangesAsync();
        var repository = new HiringDriveRepository(db);
        await repository.ImpressionsAsync([drive.Id, drive.Id, inactive.Id, expired.Id, Guid.NewGuid()], now, default);
        var click = await repository.ClickAsync(drive.Id, now, default);
        Assert.Equal(drive.ApplyUrl, click!.ApplyUrl);
        Assert.Null(await repository.ClickAsync(inactive.Id, now, default));
        Assert.Null(await repository.ClickAsync(expired.Id, now, default));
        Assert.Null(await repository.ClickAsync(Guid.NewGuid(), now, default));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var connection = database.Open();
            Assert.NotNull(await new HiringDriveRepository(connection).ClickAsync(drive.Id, now, default));
        }));
        db.ChangeTracker.Clear();
        var saved = await db.HiringDrives.SingleAsync(x => x.Id == drive.Id);
        Assert.Equal(13, saved.ClickCount); Assert.Equal(1, saved.ImpressionCount);
        Assert.Equal(0, (await db.HiringDrives.SingleAsync(x => x.Id == inactive.Id)).ImpressionCount);
        saved.IsActive = false; await db.SaveChangesAsync();
        Assert.Null(await repository.ClickAsync(drive.Id, now, default));
        saved.IsActive = true; saved.IsDeleted = true; await db.SaveChangesAsync();
        Assert.Null(await repository.ClickAsync(drive.Id, now, default));
    }

    [Fact]
    public async Task InternalClickReturnsPersistedJobIdAndSlugAndRejectsNonPublicJobs()
    {
        var now = DateTime.UtcNow;
        await using var db = database.Open();
        var roleId = await db.Roles.Select(role => role.Id).FirstAsync();
        var owner = new User
        {
            Email = Guid.NewGuid().ToString("N") + "@hiring-drive.test",
            FirstName = "Drive", LastName = "Fixture", RoleId = roleId
        };
        var company = new Company { Name = "Drive Fixture " + Guid.NewGuid().ToString("N"), Slug = Guid.NewGuid().ToString("N"), OwnerUser = owner };
        var category = new Category { Name = "Drive Category", Slug = Guid.NewGuid().ToString("N") };
        var publicJob = JobFor(company, category, "authoritative-stored-slug", now);
        var draftCompany = new Company { Name = "Draft Company " + Guid.NewGuid().ToString("N"), Slug = Guid.NewGuid().ToString("N"), OwnerUser = owner };
        var hiddenCompany = new Company { Name = "Hidden Company " + Guid.NewGuid().ToString("N"), Slug = Guid.NewGuid().ToString("N"), OwnerUser = owner, IsDeleted = true };
        var unpublished = JobFor(draftCompany, category, "unpublished-drive-slug", now);
        unpublished.Status = JobStatus.Draft;
        var hidden = JobFor(company, category, "hidden-drive-slug", now);
        hidden.IsHidden = true;
        var expired = JobFor(company, category, "expired-drive-slug", now);
        expired.ExpiresAtUtc = now.AddSeconds(-1);
        var deleted = JobFor(hiddenCompany, category, "deleted-drive-slug", now);
        deleted.IsDeleted = true;
        db.AddRange(publicJob, unpublished, hidden, expired, deleted);
        await db.SaveChangesAsync();

        var drives = new[] { publicJob, unpublished, hidden, expired, deleted }.Select((job, index) =>
            new HiringDrive
            {
                Title = "Drive " + index, JobId = job.Id, ApplyUrl = "https://ignored.example.test/",
                StartAtUtc = now.AddMinutes(-1), IsActive = true
            }).ToArray();
        db.AddRange(drives);
        await db.SaveChangesAsync();

        var repository = new HiringDriveRepository(db);
        var service = new HiringDriveService(repository, new UnitOfWork(db), new FixedClock(now), new NoOpAuditWriter(),
            new SaveHiringDriveValidator(), new HiringDriveQueryValidator(), new HiringDriveImpressionsValidator());
        var internalClick = await service.ClickAsync(drives[0].Id, default);
        Assert.Equal("internalJob", internalClick.DestinationType);
        Assert.Equal(publicJob.Id, internalClick.JobId);
        Assert.Equal("authoritative-stored-slug", internalClick.JobSlug);
        Assert.Null(internalClick.Url);
        db.ChangeTracker.Clear();
        var externalDrive = new HiringDrive
        {
            Title = "External", ApplyUrl = "https://careers.example.test/apply",
            StartAtUtc = now.AddMinutes(-1), IsActive = true
        };
        db.Add(externalDrive);
        await db.SaveChangesAsync();
        var external = await service.ClickAsync(externalDrive.Id, default);
        Assert.Equal("external", external.DestinationType);
        Assert.Null(external.JobId);
        Assert.Null(external.JobSlug);
        Assert.Equal("https://careers.example.test/apply", external.Url);
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.HiringDrives.SingleAsync(drive => drive.Id == externalDrive.Id)).ClickCount);
        foreach (var unavailableDrive in drives.Skip(1))
            Assert.Null(await repository.ClickAsync(unavailableDrive.Id, now, default));

        var publicDrives = await repository.PublicAsync(now, default);
        Assert.Equal(2, publicDrives.Count);
        Assert.Contains(publicDrives, item => item.Id == drives[0].Id);
        Assert.Contains(publicDrives, item => item.Id == externalDrive.Id);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static Job JobFor(Company company, Category category, string slug, DateTime now) => new()
    {
        ReferenceNumber = slug,
        Title = "Fixture job",
        Slug = slug,
        Description = "Public listing fixture",
        ApplicationUrl = "https://jobs.example.test/apply",
        Status = JobStatus.Published,
        PublishedAtUtc = now.AddDays(-1),
        ExpiresAtUtc = now.AddDays(1),
        Company = company,
        Category = category
    };
    private sealed class LocalPostgresFactAttribute : FactAttribute
    {
        public LocalPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires explicitly disposable loopback PostgreSQL fixture; production configuration is never used.";
        }
    }
}
