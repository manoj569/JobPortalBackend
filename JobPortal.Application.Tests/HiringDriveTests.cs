using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using JobPortal.Application.Abstractions.Auditing;
using System.Text.Json;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Features.HiringDrives;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class HiringDriveTests
{
    private static readonly string[] OrderedTitles = ["C", "B", "A"];
    private static readonly string[] CardFields = ["badgeText", "badgeType", "company", "destination", "experienceText", "id", "imageUrl", "isSponsored", "locationText", "subtitle", "title"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTime Now = new(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
    private static SaveHiringDriveRequest Request() => new("Verified drive", Now.AddDays(-1), ApplyUrl: "https://careers.example.test/apply", IsActive: true);

    [Theory]
    [InlineData("http://example.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("file:///tmp/test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void UnsafeUrlsAreRejected(string url) => Assert.False(new SaveHiringDriveValidator().Validate(Request() with { ApplyUrl = url }).IsValid);

    [Fact]
    public void ValidatesRequiredDestinationDatesAndLengths()
    {
        var validator = new SaveHiringDriveValidator();
        Assert.True(validator.Validate(Request()).IsValid);
        Assert.True(validator.Validate(Request() with { JobId = Guid.NewGuid(), ApplyUrl = null }).IsValid);
        foreach (var request in new[] {
            Request() with { ApplyUrl = null }, Request() with { Title = " " },
            Request() with { StartAtUtc = default }, Request() with { EndAtUtc = Now.AddDays(-1) },
            Request() with { EndAtUtc = Now.AddDays(-2) }, Request() with { DisplayOrder = -1 },
            Request() with { Title = new string('a', 201) }, Request() with { BadgeType = "arbitrary" },
            Request() with { ImageUrl = "javascript:alert(1)" },
            Request() with { StartAtUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) } })
            Assert.False(validator.Validate(request).IsValid);
    }
    [Theory]
    [InlineData(true, -1, 1, true)]
    [InlineData(false, -1, 1, false)]
    [InlineData(true, 1, 2, false)]
    [InlineData(true, -2, -1, false)]
    [InlineData(true, -1, null, true)]
    [InlineData(true, 0, 0, true)]
    public async Task PublicFiltersSchedule(bool active, int startDays, int? endDays, bool expected)
    {
        using var f = new Fixture();
        f.Db.HiringDrives.Add(new HiringDrive { Title = "Drive", IsActive = active, StartAtUtc = Now.AddDays(startDays), EndAtUtc = endDays.HasValue ? Now.AddDays(endDays.Value) : null, ApplyUrl = "https://example.test" });
        await f.Db.SaveChangesAsync();
        Assert.Equal(expected ? 1 : 0, (await f.Repository.PublicAsync(Now, default)).Count);
    }
    [Fact]
    public async Task PublicOrderUsesDisplayThenNewestAndExcludesDeleted()
    {
        using var f = new Fixture();
        var a = new HiringDrive { Title = "A", IsActive = true, StartAtUtc = Now, DisplayOrder = 2 };
        var b = new HiringDrive { Title = "B", IsActive = true, StartAtUtc = Now, DisplayOrder = 1 };
        var c = new HiringDrive { Title = "C", IsActive = true, StartAtUtc = Now, DisplayOrder = 1 };
        f.Db.AddRange(a, b, c, new HiringDrive { IsActive = true, StartAtUtc = Now, IsDeleted = true });
        await f.Db.SaveChangesAsync();
        b.CreatedAtUtc = Now.AddDays(-1); c.CreatedAtUtc = Now; await f.Db.SaveChangesAsync();
        Assert.Equal(OrderedTitles, (await f.Service.PublicAsync(default)).Select(x => x.Title));
    }
    [Fact]
    public async Task MissingCompanyAndJobAreRejected()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.SaveAsync(Guid.NewGuid(), null, Request() with { CompanyId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<ValidationException>(() => f.Service.SaveAsync(Guid.NewGuid(), null, Request() with { JobId = Guid.NewGuid() }, default));
        Assert.Empty(f.Db.HiringDrives);
    }
    [Fact]
    public async Task CrudPreservesAnalyticsAuditsAndSoftDeletes()
    {
        using var f = new Fixture();
        var actor = Guid.NewGuid();
        var saved = await f.Service.SaveAsync(actor, null, Request(), default);
        Assert.Equal(actor, saved.CreatedByUserId);
        Assert.Equal(0, saved.Ctr);
        var entity = await f.Repository.GetAsync(saved.Id, default); entity!.ImpressionCount = 4; entity.ClickCount = 1; await f.Db.SaveChangesAsync();
        var updated = await f.Service.SaveAsync(actor, saved.Id, Request() with { Title = "Updated" }, default);
        Assert.Equal(25, updated.Ctr); Assert.Equal(1, updated.ClickCount);
        await f.Service.DeleteAsync(actor, saved.Id, default);
        Assert.Empty(await f.Service.PublicAsync(default));
        Assert.True((await f.Db.HiringDrives.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(saved.Id, default));
    }
    [Fact]
    public async Task PublicDtoContainsOnlyCardFieldsAndExternalDestination()
    {
        using var f = new Fixture();
        await f.Service.SaveAsync(Guid.NewGuid(), null, Request() with { Description = "Private campaign description" }, default);
        var response = Assert.Single(await f.Service.PublicAsync(default));
        Assert.Equal("external", response.Destination.Type); Assert.Equal(Request().ApplyUrl, response.Destination.Url);
        var json = JsonSerializer.SerializeToElement(response, JsonOptions);
        Assert.Equal(CardFields, json.EnumerateObject().Select(x => x.Name).Order());
        Assert.False(json.GetProperty("destination").TryGetProperty("jobId", out _));
    }
    [Fact]
    public async Task InternalDestinationWinsWhenBothProvided()
    {
        using var f = new Fixture();
        var company = new Company { Name = "Internal fixture", Slug = Guid.NewGuid().ToString("N") };
        var job = new Job
        {
            ReferenceNumber = "internal-fixture",
            Title = "Internal fixture",
            Slug = "internal-fixture-" + Guid.NewGuid().ToString("N"),
            Status = JobPortal.Domain.Enums.JobStatus.Published,
            PublishedAtUtc = Now.AddDays(-1),
            Company = company
        };
        f.Db.Jobs.Add(job); await f.Db.SaveChangesAsync();
        await f.Service.SaveAsync(Guid.NewGuid(), null, Request() with { JobId = job.Id }, default);
        var destination = Assert.Single(await f.Service.PublicAsync(default)).Destination;
        Assert.Equal("internalJob", destination.Type); Assert.Equal(job.Id, destination.JobId); Assert.Null(destination.JobSlug); Assert.Null(destination.Url);
    }
    [Fact]
    public void ManagementRequiresAdministratorAndAnalyticsIsRateLimited()
    {
        var auth = typeof(AdminHiringDrivesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Administrator", auth!.Roles);
        Assert.Null(typeof(AdminHiringDrivesController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.All(typeof(AdminHiringDrivesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        Assert.Equal("HiringDriveAnalytics", typeof(HiringDrivesController).GetMethod("Click")!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal("HiringDriveAnalytics", typeof(HiringDrivesController).GetMethod("Impressions")!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }
    [Theory]
    [InlineData(null, false)]
    [InlineData("Candidate", false)]
    [InlineData("Recruiter", false)]
    [InlineData("Administrator", true)]
    public async Task ManagementPolicyRejectsNonAdministrators(string? role, bool allowed)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var auth = typeof(AdminHiringDrivesController).GetCustomAttribute<AuthorizeAttribute>()!;
        var policy = (await AuthorizationPolicy.CombineAsync(new DefaultAuthorizationPolicyProvider(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>()), [auth]))!;
        var principal = new ClaimsPrincipal(role is null ? new ClaimsIdentity() :
            new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "fixture"));
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy);
        Assert.Equal(allowed, result.Succeeded);
    }
    [Fact]
    public void ImpressionsAndPaginationAreBounded()
    {
        var validator = new HiringDriveImpressionsValidator();
        Assert.False(validator.Validate(new HiringDriveImpressionsRequest([])).IsValid);
        Assert.False(validator.Validate(new HiringDriveImpressionsRequest(Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray())).IsValid);
        Assert.False(validator.Validate(new HiringDriveImpressionsRequest([Guid.Empty])).IsValid);
        Assert.False(new HiringDriveQueryValidator().Validate(new HiringDriveQuery(0, 101)).IsValid);
    }
    private sealed class Fixture : IDisposable
    {
        public JobPortalDbContext Db { get; } = new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public HiringDriveRepository Repository => new(Db);
        public HiringDriveService Service => new(Repository, new UnitOfWork(Db), new Clock(), new Audit(), new SaveHiringDriveValidator(), new HiringDriveQueryValidator(), new HiringDriveImpressionsValidator());
        public void Dispose() => Db.Dispose();
    }
    private sealed class Audit : IAuditWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
}
