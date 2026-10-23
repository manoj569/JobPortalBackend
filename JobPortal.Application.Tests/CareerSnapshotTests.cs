using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Postgres;
using JobPortal.Persistence.Postgres.Migrations;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSnapshotTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    internal static UpdateCareerSnapshotRequest Valid => new(CareerStage.Experienced,
        "  .NET   Developer  ", ["C#", "ASP.NET Core", " c# "],
        [WorkPreference.Remote, WorkPreference.Hybrid], 4.2m, " Pune ");

    [Theory]
    [InlineData(CareerStage.Student)]
    [InlineData(CareerStage.Fresher)]
    [InlineData(CareerStage.Experienced)]
    public async Task SnapshotPersistsCanonicalProfileAndCompletionAcrossContexts(CareerStage stage)
    {
        var options = Options();
        var id = await SeedAsync(options);
        await using (var db = new JobPortalDbContext(options))
        {
            var request = Valid with { CareerStage = stage, YearsOfExperience = stage == CareerStage.Experienced ? 4.2m : null };
            var result = await Service(db).UpdateCareerSnapshotAsync(id, request);
            Assert.False(result.CareerSnapshotRequired);
            Assert.Equal(".NET Developer", result.PrimaryRole);
            Assert.Equal(stage, result.CareerStage);
            Assert.Equal(request.YearsOfExperience, result.YearsOfExperience);
            Assert.Equal("Pune", result.BasicDetails!.CurrentCity);
            Assert.Equal(request.WorkPreferences, result.WorkPreferences);
            Assert.Equal(2, result.Skills.Count);
            await Service(db).UpdateCareerSnapshotAsync(id, request);
        }
        await using var fresh = new JobPortalDbContext(options);
        var profile = await Service(fresh).GetProfileAsync(id);
        Assert.False(profile.CareerSnapshotRequired);
        Assert.Equal(".NET Developer", profile.CareerPreferences!.PreferredJobRoles.First());
        Assert.Equal(2, await fresh.CandidateSkills.CountAsync());
        var saved = await fresh.Users.SingleAsync();
        Assert.NotNull(saved.OnboardingCompletedAtUtc);
        Assert.Equal("[]", saved.SkillsJson);
        var skill = await fresh.CandidateSkills.FirstAsync();
        await Service(fresh).UpdateSkillAsync(id, skill.Id, new("Updated Skill"));
        Assert.Contains("Updated Skill", (await Service(fresh).GetProfileAsync(id)).Skills);
    }

    [Fact]
    public async Task NormalProfileCareerPreferencesRemainCanonicalAndDoNotReopenOnboarding()
    {
        var options = Options(); var id = await SeedAsync(options);
        await using var db = new JobPortalDbContext(options);
        var service = Service(db);
        await service.UpdateCareerSnapshotAsync(id, Valid);
        await service.UpdateCareerPreferencesAsync(id, new([" Java   Developer "], ["Pune"], null,
            [], [], []));
        var profile = await service.GetProfileAsync(id);
        Assert.Equal("Java Developer", profile.PrimaryRole);
        Assert.False(profile.CareerSnapshotRequired);
        await service.UpdateProfileAsync(id, new(null, null, null, [], [], [], null, null, []));
        Assert.False((await service.GetProfileAsync(id)).CareerSnapshotRequired);
    }

    [Fact]
    public async Task OmittedCityPreservesExistingCityAndExistingSkillsKeepTheirMetadata()
    {
        var options = Options(); var id = await SeedAsync(options);
        await using var db = new JobPortalDbContext(options);
        var user = await db.Users.SingleAsync(); user.CurrentCity = "Mumbai"; user.Location = "Mumbai";
        db.CandidateSkills.Add(new() { UserId = id, Name = "C#", NormalizedName = "C#", Proficiency = CandidateSkillProficiency.Expert });
        await db.SaveChangesAsync();
        var result = await Service(db).UpdateCareerSnapshotAsync(id, Valid with { City = null });
        Assert.Equal("Mumbai", result.BasicDetails!.CurrentCity);
        Assert.Equal(CandidateSkillProficiency.Expert, (await db.CandidateSkills.SingleAsync(x => x.Name == "C#")).Proficiency);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [Valid with { CareerStage = 0 }];
        yield return [Valid with { PrimaryRole = null! }];
        yield return [Valid with { PrimaryRole = " " }];
        yield return [Valid with { PrimaryRole = "Other" }];
        yield return [Valid with { PrimaryRole = new string('x', 101) }];
        yield return [Valid with { PrimaryRole = "Role\n" }];
        yield return [Valid with { Skills = null! }];
        yield return [Valid with { Skills = [] }];
        yield return [Valid with { Skills = [" "] }];
        yield return [Valid with { Skills = [new string('s', 101)] }];
        yield return [Valid with { YearsOfExperience = null }];
        yield return [Valid with { YearsOfExperience = -1 }];
        yield return [Valid with { YearsOfExperience = 51 }];
        yield return [Valid with { YearsOfExperience = 4.25m }];
        yield return [Valid with { City = new string('c', 151) }];
        yield return [Valid with { WorkPreferences = null! }];
        yield return [Valid with { WorkPreferences = [(WorkPreference)99] }];
        yield return [Valid with { WorkPreferences = [WorkPreference.Remote, WorkPreference.Remote] }];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidSnapshotDoesNotChangeProfileOrComplete(UpdateCareerSnapshotRequest request)
    {
        var options = Options(); var id = await SeedAsync(options);
        await using var db = new JobPortalDbContext(options);
        await Assert.ThrowsAsync<ValidationException>(() => Service(db).UpdateCareerSnapshotAsync(id, request));
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        Assert.True(user.CareerSnapshotRequired); Assert.Null(user.CareerStage);
        Assert.Null(user.OnboardingCompletedAtUtc); Assert.Empty(await db.CandidateSkills.ToArrayAsync());
    }

    [Fact]
    public async Task FailedSaveClearsPendingChangesAndDoesNotComplete()
    {
        var options = Options(); var id = await SeedAsync(options);
        await using var db = new JobPortalDbContext(options);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db, new FailedSave(db))
            .UpdateCareerSnapshotAsync(id, Valid));
        Assert.Empty(db.ChangeTracker.Entries());
        await db.SaveChangesAsync();
        await using var fresh = new JobPortalDbContext(options);
        var user = await fresh.Users.SingleAsync();
        Assert.True(user.CareerSnapshotRequired); Assert.Null(user.CareerStage);
        Assert.Null(user.OnboardingCompletedAtUtc); Assert.Empty(await fresh.CandidateSkills.ToArrayAsync());
    }

    [Theory]
    [InlineData("Candidate", true)]
    [InlineData("Employer", false)]
    [InlineData("Administrator", false)]
    [InlineData(null, false)]
    public async Task EndpointRequiresAuthenticatedCandidate(string? role, bool allowed)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var method = typeof(CandidateController).GetMethod(nameof(CandidateController.UpdateCareerSnapshot))!;
        var data = typeof(CandidateController).GetCustomAttributes<AuthorizeAttribute>()
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>());
        var policy = await AuthorizationPolicy.CombineAsync(provider, data);
        var identity = role is null ? new ClaimsIdentity() : new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test");
        var result = await services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(identity), null, policy!);
        Assert.Equal(allowed, result.Succeeded);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
        services.Dispose();
    }

    [Fact]
    public void SnapshotCannotOverpostUserIdentityOrCompletionState()
    {
        foreach (var property in new[] { "userId", "careerSnapshotRequired", "role", "completedAtUtc" })
        {
            var json = "{\"" + property + "\":\"attacker\"}";
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateCareerSnapshotRequest>(json, WebJson));
        }
    }

    [Fact]
    public async Task ServiceRejectsOtherRolesAndUpdatesOnlyTheAuthenticatedCandidate()
    {
        var options = Options(); var id = await SeedAsync(options);
        await using var db = new JobPortalDbContext(options);
        var second = new User { RoleId = SystemRoleIds.Candidate, Status = UserStatus.Active, CareerSnapshotRequired = true };
        db.Users.Add(second); await db.SaveChangesAsync();
        await Service(db).UpdateCareerSnapshotAsync(id, Valid);
        Assert.True((await db.Users.SingleAsync(x => x.Id == second.Id)).CareerSnapshotRequired);
        second.RoleId = SystemRoleIds.Employer; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service(db).UpdateCareerSnapshotAsync(second.Id, Valid));
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service(db).UpdateCareerSnapshotAsync(Guid.NewGuid(), Valid));
    }

    [Fact]
    public void MigrationAddsOnlyFalseDefaultRequirementAndModelMatchesSnapshot()
    {
        var migration = new AddCareerSnapshotRequirement();
        var column = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("Users", column.Table); Assert.Equal("CareerSnapshotRequired", column.Name);
        Assert.Equal(false, column.DefaultValue); Assert.False(column.IsNullable);
        Assert.False(new User().CareerSnapshotRequired);
        using var db = new PostgresJobPortalDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    internal static CandidateService Service(JobPortalDbContext db, IUnitOfWork? unit = null) => new(
        null!, new CandidateRepository(db, TimeProvider.System), null!, null!, new TestProfilePhotoStorage(),
        unit ?? new UnitOfWork(db), new AuditWriterTestDouble(), new UpdateCandidateProfileRequestValidator(),
        new UpdateCandidateAboutRequestValidator(), new UpsertCandidateSkillRequestValidator(),
        new UpdateCandidateOnboardingRequestValidator(TimeProvider.System), new UpdateCandidateBasicDetailsRequestValidator(),
        new UpdateCandidateCareerPreferencesRequestValidator(), new CandidatePageQueryValidator(),
        new JobApplicationQueryValidator(), new CreateJobApplicationRequestValidator(), TimeProvider.System);

    private static DbContextOptions<JobPortalDbContext> Options() => new DbContextOptionsBuilder<JobPortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static async Task<Guid> SeedAsync(DbContextOptions<JobPortalDbContext> options)
    {
        await using var db = new JobPortalDbContext(options);
        var user = new User { RoleId = SystemRoleIds.Candidate, Status = UserStatus.Active,
            CareerSnapshotRequired = true, Email = "candidate@fixture.test" };
        db.Users.Add(user); await db.SaveChangesAsync(); return user.Id;
    }
    private sealed class FailedSave(JobPortalDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Injected persistence failure");
        public void ResetAfterFailure() => db.ChangeTracker.Clear();
    }
}
