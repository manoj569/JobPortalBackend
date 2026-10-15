using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobSourceTrackingTests
{
    [Fact]
    public void UpdateDoesNotAttachDuplicateEvenWhenTrackedSourceIsMarkedDeleted()
    {
        using var f = new JobSourceFixture();
        f.Context.Entry(f.Source).State = EntityState.Deleted;
        var detached = new JobSource { Id = f.Source.Id, LastError = "Run failed", ConsecutiveFailures = 1 };
        f.Repository.Update(detached);
        var entry = Assert.Single(f.Context.ChangeTracker.Entries<JobSource>());
        Assert.Same(f.Source, entry.Entity);
        Assert.Equal(EntityState.Deleted, entry.State);
        Assert.Equal(EntityState.Detached, f.Context.Entry(detached).State);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("detached")]
    [InlineData("reloaded")]
    public async Task UpdatePersistsOnlyRunMetadataAndKeepsOneTrackedIdentity(string state)
    {
        using var f = new JobSourceFixture();
        var source = (await f.Repository.GetByIdAsync(f.Source.Id))!;
        if (state != "same") f.Context.Entry(source).State = EntityState.Detached;
        JobSource? reloaded = state == "reloaded" ? await f.Repository.GetByIdAsync(source.Id) : null;
        var careerUrl = source.CareerPageUrl;
        source.LastRunAtUtc = DateTime.UtcNow;
        source.LastSuccessfulRunAtUtc = source.LastRunAtUtc;
        source.LastError = "item failed";
        source.ConsecutiveFailures = 2;
        // Detached configuration is stale and must never replace tracked configuration.
        if (state != "same") source.CareerPageUrl = "https://stale.test/";
        f.Category.Name = "Pending unrelated change";

        f.Repository.Update(source);

        var entry = Assert.Single(f.Context.ChangeTracker.Entries<JobSource>());
        Assert.Same(reloaded ?? source, entry.Entity);
        Assert.Equal(new[] { "ConsecutiveFailures", "LastError", "LastRunAtUtc", "LastSuccessfulRunAtUtc" },
            entry.Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).OrderBy(x => x).ToArray());
        Assert.Same(f.Company, Assert.Single(f.Context.Companies.Local));
        await f.Context.SaveChangesAsync();
        var saved = await f.Context.JobSources.AsNoTracking().SingleAsync(x => x.Id == source.Id);
        Assert.Equal(careerUrl, saved.CareerPageUrl);
        Assert.Equal(source.LastRunAtUtc, saved.LastRunAtUtc);
        Assert.Equal(source.LastSuccessfulRunAtUtc, saved.LastSuccessfulRunAtUtc);
        Assert.Equal(source.LastError, saved.LastError);
        Assert.Equal(source.ConsecutiveFailures, saved.ConsecutiveFailures);
        Assert.Equal("Pending unrelated change", (await f.Context.Categories.AsNoTracking().SingleAsync()).Name);
    }
}
