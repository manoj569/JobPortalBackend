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
}
