using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using JobPortal.API.Controllers;
using JobPortal.Application.Features.Authentication;
using JobPortal.Infrastructure.Authentication;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerSnapshotPostgresTests(WorkdayCheckpointDatabase database)
    : IClassFixture<WorkdayCheckpointDatabase>
{
    [SnapshotPostgresFact]
    public async Task PasswordRegistrationTokenAuthenticatesSnapshotAndCompletionSurvivesNewSession()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "snapshot-tests", ["Jwt:Audience"] = "snapshot-tests", ["Jwt:Key"] = key
        }).Build();
        var jwt = new JwtTokenService(config, TimeProvider.System);
        var email = Guid.NewGuid().ToString("N") + "@snapshot.test";
        var phone = Random.Shared.NextInt64(7000000000, 9999999999).ToString(CultureInfo.InvariantCulture);
        string refresh;
        Guid id;
        await using (var db = database.Open())
        {
            var registered = await Authentication(db, jwt).RegisterAsync(new("Snapshot Candidate", email, "fixture-password", phone, true));
            var session = Assert.IsType<AuthenticationResponse>(registered.Authentication);
            Assert.True(session.User.CareerSnapshotRequired); refresh = session.RefreshToken; id = session.User.Id;
            Assert.NotEqual(refresh, (await db.RefreshTokens.SingleAsync(x => x.UserId == id)).Token);
            var principal = new JwtSecurityTokenHandler().ValidateToken(session.AccessToken, new TokenValidationParameters
            {
                ValidIssuer = "snapshot-tests", ValidAudience = "snapshot-tests",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuerSigningKey = true, ValidateLifetime = true
            }, out _);
            Assert.True(principal.Identity!.IsAuthenticated);
            Assert.True(principal.IsInRole("Candidate"));
            var controller = new CandidateController(CareerSnapshotTests.Service(db))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
            };
            Assert.IsType<OkObjectResult>((await controller.UpdateCareerSnapshot(CareerSnapshotTests.Valid, default)).Result);
        }
        await using var fresh = database.Open();
        var auth = Authentication(fresh, jwt);
        var refreshed = await auth.RefreshAsync(new(refresh), null);
        Assert.False(refreshed.User.CareerSnapshotRequired);
        await auth.LogoutAsync(id, new(refreshed.RefreshToken), null);
        var login = await auth.LoginAsync(new(email, "fixture-password"), null);
        Assert.False(login.User.CareerSnapshotRequired);
        Assert.Equal(".NET Developer", (await CareerSnapshotTests.Service(fresh).GetProfileAsync(id)).PrimaryRole);
    }

    private static AuthService Authentication(JobPortalDbContext db, JwtTokenService jwt) => new(
        new UserRepository(db), new UserExternalLoginRepository(db), new RefreshTokenRepository(db),
        new UnitOfWork(db), new Pbkdf2PasswordHasher(), jwt, null!, new AuditWriterTestDouble(),
        new RegisterRequestValidator(), new LoginRequestValidator(), new RequestPasswordResetRequestValidator(),
        new CompletePasswordResetRequestValidator(), new RefreshTokenRequestValidator(),
        new ChangePasswordRequestValidator(), new VerifyEmailRequestValidator(), TimeProvider.System,
        NullLogger<AuthService>.Instance);

    [SnapshotPostgresFact]
    public async Task SnapshotCommitsProfileSkillsAndCompletionAndReadsInFreshSession()
    {
        Guid id;
        await using (var db = database.Open())
        {
            var user = Candidate(); id = user.Id; db.Users.Add(user); await db.SaveChangesAsync();
            var response = await CareerSnapshotTests.Service(db).UpdateCareerSnapshotAsync(id, CareerSnapshotTests.Valid);
            Assert.False(response.CareerSnapshotRequired);
        }
        await using var fresh = database.Open();
        var profile = await CareerSnapshotTests.Service(fresh).GetProfileAsync(id);
        Assert.False(profile.CareerSnapshotRequired); Assert.Equal(".NET Developer", profile.PrimaryRole);
        Assert.Equal(4.2m, profile.YearsOfExperience); Assert.Equal("Pune", profile.BasicDetails!.CurrentCity);
        Assert.Equal(2, await fresh.CandidateSkills.CountAsync(x => x.UserId == id));
        Assert.Equal(new[] { WorkPreference.Remote, WorkPreference.Hybrid }, profile.WorkPreferences);
    }

    [SnapshotPostgresFact]
    public async Task CommitFailureRollsBackAlreadyWrittenProfileAndSkillsTogether()
    {
        await using var db = database.Open();
        var user = Candidate(); db.Users.Add(user); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_snapshot_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected snapshot commit failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_snapshot_commit AFTER INSERT ON "CandidateSkills"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_snapshot_commit();
            """);
        try
        {
            var failure = await Record.ExceptionAsync(() => CareerSnapshotTests.Service(db)
                .UpdateCareerSnapshotAsync(user.Id, CareerSnapshotTests.Valid));
            Assert.NotNull(failure);
            Assert.Contains("injected snapshot commit failure", failure.ToString());
            Assert.Empty(db.ChangeTracker.Entries());
            await using var fresh = database.Open();
            var saved = await fresh.Users.SingleAsync(x => x.Id == user.Id);
            Assert.True(saved.CareerSnapshotRequired); Assert.Null(saved.CareerStage);
            Assert.Null(saved.OnboardingCompletedAtUtc); Assert.Equal("[]", saved.PreferredJobRolesJson);
            Assert.False(await fresh.CandidateSkills.AnyAsync(x => x.UserId == user.Id));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER reject_snapshot_commit ON "CandidateSkills"; DROP FUNCTION reject_snapshot_commit();""");
        }
    }

    [SnapshotPostgresFact]
    public async Task MigrationBackfillsExistingIncompleteCandidateToNotRequired()
    {
        await using var db = database.Open();
        var user = Candidate(); db.Users.Add(user); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith("_AddCareerSnapshotRequirement", migrations[^1]);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2]);
        await migrator.MigrateAsync(migrations[^1]);
        var saved = await db.Users.SingleAsync(x => x.Id == user.Id);
        Assert.False(saved.CareerSnapshotRequired); Assert.Null(saved.OnboardingCompletedAtUtc);
        Assert.Null(saved.CareerStage);
        Assert.False((await CareerSnapshotTests.Service(db).GetProfileAsync(user.Id)).CareerSnapshotRequired);
    }

    private static User Candidate()
    {
        var email = Guid.NewGuid().ToString("N") + "@snapshot.test";
        return new() { Email = email, NormalizedEmail = email, FirstName = "Snapshot", LastName = "Candidate",
            RoleId = SystemRoleIds.Candidate, Status = UserStatus.Active, CareerSnapshotRequired = true };
    }

    private sealed class SnapshotPostgresFactAttribute : FactAttribute
    {
        public SnapshotPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires disposable localhost careerharbor_workday_test PostgreSQL.";
        }
    }
}
