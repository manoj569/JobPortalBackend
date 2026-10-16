using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class FresherAcquisitionPostgresTests
{
    [FresherPostgresFact]
    public async Task TalentdRegistrationAndPayloadIdempotencyAreDurable()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var store = new FresherAcquisitionStore(db);
        var source = await store.GetEnabledSourceAsync("talentd", default);
        Assert.NotNull(source); Assert.True(source.AutoPublishEnabled); Assert.False(source.IsDeleted);
        var id = "talentd-test-" + Guid.NewGuid().ToString("N");
        var first = await store.StartPayloadRunAsync(source.Id, id, "test", "a".PadLeft(64, 'a'), "c".PadLeft(64, 'c'), 1, default);
        var replay = await store.StartPayloadRunAsync(source.Id, id, "test", "a".PadLeft(64, 'a'), "c".PadLeft(64, 'c'), 1, default);
        Assert.True(replay.IsReplay); Assert.Equal(first.Run.Id, replay.Run.Id);
        await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => store.StartPayloadRunAsync(source.Id, id,
            "test", "b".PadLeft(64, 'b'), "c".PadLeft(64, 'c'), 1, default));
    }

    [FresherPostgresFact]
    public async Task TalentdLogoOnlyFillsMissingAndRejectsUnsafeUrls()
    {
        await using var db = new JobPortalDbContext(CreateOptions());
        await db.Database.MigrateAsync();
        var (_, company) = await CreateSourceAndCompanyAsync(db);
        company.LogoUrl = "https://trusted.example/admin.png";
        await db.SaveChangesAsync();
        var store = new FresherAcquisitionStore(db);
        await store.PopulateMissingLogoAsync(company.Id, "https://source.example/logo.png", default);
        await db.Entry(company).ReloadAsync();
        Assert.Equal("https://trusted.example/admin.png", company.LogoUrl);
        company.LogoUrl = null; await db.SaveChangesAsync();
        await store.PopulateMissingLogoAsync(company.Id, "http://127.0.0.1/logo.png", default);
        await db.Entry(company).ReloadAsync(); Assert.Null(company.LogoUrl);
        await store.PopulateMissingLogoAsync(company.Id, "https://source.example/logo.png", default);
        await db.Entry(company).ReloadAsync(); Assert.Equal("https://source.example/logo.png", company.LogoUrl);
    }
}
