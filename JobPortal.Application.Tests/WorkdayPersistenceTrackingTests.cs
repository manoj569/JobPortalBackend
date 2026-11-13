using JobPortal.Domain.Entities;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class WorkdayPersistenceTrackingTests
{
    [Fact]
    public async Task ImportedJobUpdateDoesNotReattachOrOverwriteOtherCommittedJobs()
    {
        using var f = new JobSourceFixture();
        var first = new Job { Company = f.Company, CompanyId = f.Company.Id, JobSourceId = f.Source.Id, Title = "First vacancy", ReferenceNumber = "FIRST" };
        var other = new Job { Company = f.Company, CompanyId = f.Company.Id, JobSourceId = f.Source.Id, Title = "Other vacancy", ReferenceNumber = "OTHER" };
        f.Context.AddRange(first, other);
        await f.Context.SaveChangesAsync();
        f.Context.Entry(other).State = EntityState.Detached;
        Assert.Contains(other, f.Company.Jobs);
        new JobRepository(f.Context).Update(first);
        Assert.Equal(EntityState.Detached, f.Context.Entry(other).State);
        Assert.Single(f.Context.ChangeTracker.Entries<Job>(), x => x.State == EntityState.Modified);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconciliationTracksOnlyClosuresAndPreservesPendingSuppression(bool suppress)
    {
        using var f = new JobSourceFixture();
        var present = new Job { Company = f.Company, Category = f.Category, JobSourceId = f.Source.Id,
            ExternalJobId = "present", Title = "Present", ReferenceNumber = "PRESENT", SourceOwnsPublication = true };
        var missing = new Job { Company = f.Company, Category = f.Category, JobSourceId = f.Source.Id,
            ExternalJobId = "missing", Title = "Missing", ReferenceNumber = "MISSING", SourceOwnsPublication = true };
        f.Context.AddRange(present, missing);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var repository = new JobRepository(f.Context);
        var observedIds = new[] { "present" };
        Assert.Equal(0, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, observedIds, Guid.NewGuid(), JobSourceFixture.Now, 2));
        Assert.Empty(f.Context.ChangeTracker.Entries<Job>());
        await f.Context.SaveChangesAsync();
        if (suppress)
        {
            var pending = await f.Context.Jobs.AsTracking().SingleAsync(x => x.Id == missing.Id);
            pending.IsHidden = true;
            Assert.True(f.Context.Jobs.Local.Single(x => x.Id == missing.Id).IsHidden);
        }
        Assert.Equal(suppress ? 0 : 1, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, observedIds, Guid.NewGuid(), JobSourceFixture.Now.AddHours(1), 2));
        Assert.Equal(missing.Id, Assert.Single(f.Context.ChangeTracker.Entries<Job>()).Entity.Id);
        await f.Context.SaveChangesAsync();
        f.Context.ChangeTracker.Clear();
        var stored = await f.Context.Jobs.AsNoTracking().SingleAsync(x => x.Id == missing.Id);
        Assert.Equal(suppress ? JobPortal.Domain.Enums.JobStatus.Draft : JobPortal.Domain.Enums.JobStatus.Closed, stored.Status);
        Assert.Equal(suppress, stored.IsHidden);
        Assert.Equal(JobPortal.Domain.Enums.JobStatus.Draft, (await f.Context.Jobs.AsNoTracking().SingleAsync(x => x.Id == present.Id)).Status);
    }
}
