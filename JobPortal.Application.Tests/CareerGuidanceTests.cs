using System.Reflection;
using System.Text.Json;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Auditing;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerGuidanceTests
{
    [Fact]
    public void MigrationOnlyCreatesCareerGuidanceTablesAndIndexes()
    {
        var migration = new JobPortal.Persistence.Postgres.Migrations.AddCareerGuidanceFoundation();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation or
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation));
        var tables = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().ToArray();
        Assert.Equal(3, tables.Length);
        Assert.All(tables, table => Assert.StartsWith("CareerConsultant", table.Name));
    }

    [Fact]
    public async Task ServiceSoftDeletionAndAdminPendingPagination()
    {
        using var f = new Fixture();
        var profile = await f.Apply();
        var queue = await f.Service.AdminSearchAsync(f.Admin.Id, new(Status: ConsultantVerificationStatus.Pending), default);
        Assert.Equal(profile.Profile.Id, Assert.Single(queue.Items).Profile.Id);
        Assert.Empty((await f.Service.AdminSearchAsync(f.Admin.Id, new(Status: ConsultantVerificationStatus.Verified), default)).Items);
        profile = await f.Service.SaveServiceAsync(f.Owner.Id, null, Fixture.Offering(profile), default);
        var id = Assert.Single(profile.Services).Id;
        profile = await f.Service.DeleteServiceAsync(f.Owner.Id, id, profile.Revision, default);
        Assert.Empty(profile.Services);
        Assert.True((await f.Db.CareerConsultantServices.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.Empty(await f.Db.CareerConsultantServices.ToArrayAsync());
    }

    [Fact]
    public async Task ApplicationIsPendingPrivateAndAuditedThenAdminCanVerify()
    {
        using var f = new Fixture();
        var profile = await f.Apply();
        Assert.Equal(ConsultantVerificationStatus.Pending, profile.VerificationStatus);
        Assert.Equal(CareerGuidanceService.PolicyVersion, profile.PolicyVersion);
        Assert.Empty((await f.Service.SearchAsync(new(), default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(profile.Profile.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => f.Apply());
        profile = await f.Review(profile, ConsultantReviewAction.Approve);
        var visible = await f.Service.GetAsync(profile.Profile.Id, default);
        Assert.False(visible.IsAcceptingBookings);
        Assert.Contains("No guaranteed", visible.GuidanceDisclaimer);
        var json = JsonSerializer.Serialize(visible);
        Assert.DoesNotContain("LinkedIn", json);
        Assert.DoesNotContain("UserId", json);
        Assert.DoesNotContain("VerificationReason", json);
        Assert.DoesNotContain("Revision", json);
        Assert.Equal(2, await f.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task OwnerEditRequiresReverificationAndTagRevivalDoesNotDuplicate()
    {
        using var f = new Fixture();
        var profile = await f.Review(await f.Apply(), ConsultantReviewAction.Approve);
        profile = await f.Service.UpdateAsync(f.Owner.Id, f.Request with { Revision = profile.Revision, Languages = ["French"] }, default);
        Assert.Equal(ConsultantVerificationStatus.Pending, profile.VerificationStatus);
        Assert.Null(profile.VerifiedAtUtc);
        Assert.Empty((await f.Service.SearchAsync(new(), default)).Items);
        f.Db.ChangeTracker.Clear();
        profile = await f.Service.UpdateAsync(f.Owner.Id, f.Request with { Revision = profile.Revision }, default);
        Assert.Equal("ENGLISH", Assert.Single(profile.Profile.Languages));
        Assert.Equal(2, await f.Db.CareerConsultantTags.IgnoreQueryFilters().CountAsync(x => x.Kind == ConsultantTagKind.Language));
    }

    [Fact]
    public async Task SuspensionIsAdminOnlyAndBlocksServiceEditsUntilReactivation()
    {
        using var f = new Fixture();
        var profile = await f.Review(await f.Apply(), ConsultantReviewAction.Approve);
        profile = await f.Review(profile, ConsultantReviewAction.Suspend);
        Assert.Empty((await f.Service.SearchAsync(new(), default)).Items);
        await Assert.ThrowsAsync<AppException>(() => f.Service.UpdateAsync(f.Owner.Id, f.Request with { Revision = profile.Revision }, default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.SaveServiceAsync(f.Owner.Id, null, Fixture.Offering(profile), default));
        profile = await f.Review(profile, ConsultantReviewAction.Reactivate);
        Assert.Equal(ConsultantVerificationStatus.Verified, profile.VerificationStatus);
        Assert.Single((await f.Service.SearchAsync(new(), default)).Items);
    }

    [Fact]
    public async Task UnauthorizedAdminActionsAndCrossOwnerServiceIdsFail()
    {
        using var f = new Fixture();
        var profile = await f.Apply();
        await Assert.ThrowsAsync<AppException>(() => f.Service.ReviewAsync(f.Owner.Id, profile.Profile.Id, new(ConsultantReviewAction.Approve, "reason", profile.Revision), default));
        await Assert.ThrowsAsync<AppException>(() => f.Service.AdminSearchAsync(f.Owner.Id, new(), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.MineAsync(f.Other.Id, default));
        profile = await f.Service.SaveServiceAsync(f.Owner.Id, null, Fixture.Offering(profile), default);
        var other = await f.Service.ApplyAsync(f.Other.Id, f.Request, default);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.SaveServiceAsync(f.Other.Id, profile.Services.Single().Id, Fixture.Offering(other), default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.DeleteServiceAsync(f.Other.Id, profile.Services.Single().Id, other.Revision, default));
        Assert.Equal(1, await f.Db.CareerConsultantServices.CountAsync());
    }

    [Fact]
    public async Task InactiveUsersAndSelfVerificationAreRejected()
    {
        using var f = new Fixture();
        f.Other.Status = UserStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.ApplyAsync(f.Other.Id, f.Request, default));
        var ownAdmin = await f.Service.ApplyAsync(f.Admin.Id, f.Request, default);
        await Assert.ThrowsAsync<AppException>(() => f.Service.ReviewAsync(f.Admin.Id, ownAdmin.Profile.Id, new(ConsultantReviewAction.Approve, "reason", ownAdmin.Revision), default));
    }

    [Fact]
    public async Task DiscoveryFiltersPaginationActiveServicesAndUserStatus()
    {
        using var f = new Fixture();
        var profile = await f.Apply();
        profile = await f.Service.SaveServiceAsync(f.Owner.Id, null, Fixture.Offering(profile), default);
        profile = await f.Review(profile, ConsultantReviewAction.Approve);
        var query = new ConsultantSearchQuery(PageSize: 1, CompanyId: f.Company.Id, Company: "example", Role: "engineer",
            Search: "professional", ProfessionalType: CareerProfessionalType.CurrentEmployee,
            Language: "english", Expertise: "interview preparation", ServiceType: "mock interview", Currency: "INR", MinPrice: 10, MaxPrice: 2000, Sort: "price-asc");
        var page = await f.Service.SearchAsync(query, default);
        Assert.Equal(1, page.TotalCount);
        Assert.Single(Assert.Single(page.Items).Services);
        Assert.Empty((await f.Service.SearchAsync(query with { PageNumber = 2 }, default)).Items);
        Assert.Empty((await f.Service.SearchAsync(query with { Currency = "USD" }, default)).Items);
        profile = await f.Service.SaveServiceAsync(f.Owner.Id, profile.Services.Single().Id, Fixture.Offering(profile) with { IsActive = false }, default);
        Assert.Empty((await f.Service.GetAsync(profile.Profile.Id, default)).Services);
        Assert.Empty((await f.Service.SearchAsync(query, default)).Items);
        f.Owner.Status = UserStatus.Suspended;
        await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Service.SearchAsync(new(), default)).Items);
    }

    [Fact]
    public async Task StaleClientRevisionAndDatabaseConcurrentModerationConflict()
    {
        using var f = new Fixture();
        var pending = await f.Apply();
        var verified = await f.Review(pending, ConsultantReviewAction.Approve);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.UpdateAsync(f.Owner.Id, f.Request with { Revision = pending.Revision }, default));
        using var second = new JobPortalDbContext(f.Options);
        var concurrent = await second.CareerConsultants.SingleAsync();
        await f.Review(verified, ConsultantReviewAction.Suspend);
        concurrent.ProfessionalHeadline = "Concurrent stale claim";
        concurrent.Revision = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task RejectRequiresResubmissionAndMissingCompanyFails()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<BadRequestException>(() => f.Service.ApplyAsync(f.Owner.Id, f.Request with { CompanyId = Guid.NewGuid() }, default));
        var profile = await f.Review(await f.Apply(), ConsultantReviewAction.Reject);
        Assert.Equal("Private review reason", profile.VerificationReason);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(profile, ConsultantReviewAction.Approve));
        profile = await f.Service.UpdateAsync(f.Owner.Id, f.Request with { Revision = profile.Revision }, default);
        Assert.Equal(ConsultantVerificationStatus.Pending, profile.VerificationStatus);
    }

    [Theory]
    [InlineData(0, 30, "INR")]
    [InlineData(-1, 30, "INR")]
    [InlineData(10.001, 30, "INR")]
    [InlineData(1000001, 30, "INR")]
    [InlineData(100, 14, "INR")]
    [InlineData(100, 181, "INR")]
    [InlineData(100, 30, "XYZ")]
    public void ServiceBounds(decimal price, int duration, string currency)
    {
        var validator = new ConsultantServiceRequestValidator();
        Assert.False(validator.Validate(new ConsultantServiceRequest("Review", "Review", "Description", duration, price, currency, true, Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void PoliciesAndSearchValidation()
    {
        using var f = new Fixture();
        Assert.False(new ConsultantProfileRequestValidator().Validate(f.Request with { AcceptIndependentGuidancePolicy = false }).IsValid);
        Assert.False(new ConsultantProfileRequestValidator().Validate(f.Request with { LinkedInUrl = "https://linkedin.com.evil.test/in/test" }).IsValid);
        Assert.False(new ConsultantSearchQueryValidator().Validate(new ConsultantSearchQuery(Sort: "price-asc")).IsValid);
        Assert.False(new ConsultantSearchQueryValidator().Validate(new ConsultantSearchQuery(PageSize: 101)).IsValid);
        Assert.False(new ConsultantSearchQueryValidator().Validate(new ConsultantSearchQuery(Currency: "INR", MinPrice: 100, MaxPrice: 10)).IsValid);
    }

    [Fact]
    public void AuthorizationMetadataAndPublicDtoAreRestricted()
    {
        Assert.NotNull(typeof(CareerGuidanceDiscoveryController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(CareerGuidanceOwnerController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("Administrator", typeof(AdminCareerGuidanceController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(typeof(ConsultantPublicResponse).GetProperty("UserId"));
        Assert.Null(typeof(ConsultantPublicResponse).GetProperty("LinkedInUrl"));
        Assert.Null(typeof(ConsultantPublicResponse).GetProperty("VerificationReason"));
    }

    [Theory]
    [InlineData("newest")]
    [InlineData("experience")]
    [InlineData("price-asc")]
    [InlineData("price-desc")]
    public void PostgreSqlQueryTranslationAndModelConstraints(string sort)
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options);
        var query = new CareerGuidanceRepository(db).FilteredQuery(new(Company: "EY", Role: "Consultant", Language: "English", Expertise: "Strategy", ServiceType: "Review", Currency: "INR", MinPrice: 10, Sort: sort)).Take(20);
        var sql = query.ToQueryString();
        Assert.Contains("LIMIT", sql);
        Assert.Contains("VerificationStatus", sql);
        var model = db.Model.FindEntityType(typeof(CareerConsultant))!;
        Assert.True(model.FindProperty(nameof(CareerConsultant.Revision))!.IsConcurrencyToken);
        Assert.Contains(model.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "UserId");
        Assert.DoesNotContain(db.Model.GetEntityTypes().SelectMany(e => e.GetProperties()), p => p.Name == "JobId1");
    }

    private sealed class Fixture : IDisposable
    {
        public DbContextOptions<JobPortalDbContext> Options { get; } = new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public JobPortalDbContext Db { get; }
        public User Owner { get; } = new() { FirstName = "Owner", Status = UserStatus.Active };
        public User Other { get; } = new() { FirstName = "Other", Status = UserStatus.Active };
        public User Admin { get; } = new() { FirstName = "Admin", Status = UserStatus.Active };
        public Company Company { get; } = new() { Name = "Example", Slug = "example" };
        public CareerGuidanceService Service { get; }
        public ConsultantProfileRequest Request => new("Professional", "Experienced professional", "Independent advice",
            Company.Id, Company.Name, "Engineer", 8, CareerProfessionalType.CurrentEmployee, "https://www.linkedin.com/in/example",
            ["English"], ["Interview preparation"], true);

        public Fixture()
        {
            Db = new(Options);
            var candidateRole = new Role { Name = "Candidate", NormalizedName = "CANDIDATE" };
            var adminRole = new Role { Name = "Administrator", NormalizedName = "ADMINISTRATOR" };
            Owner.Role = Other.Role = candidateRole;
            Owner.RoleId = Other.RoleId = candidateRole.Id;
            Admin.Role = adminRole; Admin.RoleId = adminRole.Id;
            Company.OwnerUserId = Admin.Id;
            Db.AddRange(candidateRole, adminRole, Owner, Other, Admin, Company);
            Db.SaveChanges();
            Service = new(new CareerGuidanceRepository(Db), new UserRepository(Db), new CompanyManagementRepository(Db),
                new UnitOfWork(Db), new AuditWriter(new AuditLogRepository(Db), new ActorContext(Admin.Id)), TimeProvider.System,
                new ConsultantProfileRequestValidator(), new ConsultantServiceRequestValidator(), new ConsultantSearchQueryValidator(),
                new ConsultantReviewRequestValidator(), new ConsultantAdminQueryValidator());
        }
        public Task<ConsultantPrivateResponse> Apply() => Service.ApplyAsync(Owner.Id, Request, default);
        public Task<ConsultantPrivateResponse> Review(ConsultantPrivateResponse p, ConsultantReviewAction action) =>
            Service.ReviewAsync(Admin.Id, p.Profile.Id, new(action, "Private review reason", p.Revision), default);
        public static ConsultantServiceRequest Offering(ConsultantPrivateResponse p) => new("Mock interview", "Mock interview", "Practice questions", 30, 1000, "INR", true, p.Revision);
        public void Dispose() => Db.Dispose();
    }

    private sealed class ActorContext(Guid actor) : IAuditContextAccessor
    {
        public Guid? ActorUserId => actor;
        public string? ActorRole => "Administrator";
        public string? CorrelationId => "career-guidance-test";
    }
}
