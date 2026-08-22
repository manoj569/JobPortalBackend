using System.Text.Json;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Legal;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class GoogleAuthenticationTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
    private const string RawCredential = "opaque-google-id-token-never-store-or-log";
    private static readonly ValidatedGoogleIdentity ValidIdentity =
        new("google-subject-123", "Candidate@Example.com", true, "Manoj", "Shekapure", "Manoj Shekapure");

    [Fact]
    public async Task NewRegistrationCreatesCandidateWithoutPasswordOrPhoneAndIssuesSession()
    {
        var fixture = CreateFixture();
        var response = await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Register, true), "127.0.0.1");

        var user = Assert.Single(fixture.Users.Items);
        var login = Assert.Single(fixture.ExternalLogins.Items);
        Assert.Equal(SystemRoleIds.Candidate, user.RoleId);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.NormalizedPhoneNumber);
        Assert.Equal(LegalDocumentCatalog.CurrentVersion, user.TermsAndPrivacyVersion);
        Assert.Equal(ExternalLoginProvider.Google, login.Provider);
        Assert.Equal(ValidIdentity.Subject, login.ProviderSubject);
        Assert.Equal(user.Id, response.User.Id);
        Assert.Single(fixture.RefreshTokens.Items);
        Assert.DoesNotContain(RawCredential, JsonSerializer.Serialize(user));
        Assert.DoesNotContain(RawCredential, JsonSerializer.Serialize(login));
        Assert.DoesNotContain(fixture.Logger.Messages, x => x.Contains(RawCredential, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthorizationCodeLoginUsesSharedLinkedAccountFlow()
    {
        var fixture = CreateFixture();
        var user = Candidate();
        fixture.Users.Items.Add(user);
        fixture.ExternalLogins.Items.Add(Link(user));

        var response = await fixture.Service.AuthenticateCodeAsync(
            new("one-time-code", GoogleAuthenticationIntent.Login),
            "https://careerharbor.in", "1", "127.0.0.1");

        Assert.Equal(user.Id, response.User.Id);
        Assert.Single(fixture.RefreshTokens.Items);
    }

    [Fact]
    public async Task AuthorizationCodeRegisterCreatesCandidateAndIsIdempotent()
    {
        var fixture = CreateFixture();
        var request = new GoogleAuthorizationCodeRequest(
            "one-time-code", GoogleAuthenticationIntent.Register, true);

        var first = await fixture.Service.AuthenticateCodeAsync(
            request, "https://www.careerharbor.in", "1", null);
        var second = await fixture.Service.AuthenticateCodeAsync(
            request, "https://www.careerharbor.in", "1", null);

        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Single(fixture.Users.Items);
        Assert.Single(fixture.ExternalLogins.Items);
    }

    [Fact]
    public async Task AuthorizationCodeFlowEnforcesTermsOriginHeaderAndSafeProviderErrors()
    {
        var fixture = CreateFixture();
        var register = new GoogleAuthorizationCodeRequest(
            "one-time-code", GoogleAuthenticationIntent.Register);
        Assert.Equal("terms_acceptance_required", (await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateCodeAsync(register, "https://careerharbor.in", "1", null))).Code);

        var login = new GoogleAuthorizationCodeRequest(
            "one-time-code", GoogleAuthenticationIntent.Login);
        Assert.Equal("google_origin_not_allowed", (await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateCodeAsync(login, null, "1", null))).Code);
        Assert.Equal("google_origin_not_allowed", (await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateCodeAsync(login, "https://evil.example", "1", null))).Code);
        Assert.Equal("invalid_google_code_request", (await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateCodeAsync(login, "http://localhost:5173", null, null))).Code);

        fixture.CodeExchanger.Exception = new GoogleAuthorizationCodeException(
            GoogleAuthorizationCodeFailure.Invalid);
        Assert.Equal("invalid_google_authorization_code", (await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateCodeAsync(login, "http://localhost:5173", "1", null))).Code);
        fixture.CodeExchanger.Exception = new GoogleAuthorizationCodeException(
            GoogleAuthorizationCodeFailure.Unavailable);
        Assert.Equal("google_authentication_unavailable", (await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateCodeAsync(login, "https://careerharbor.in", "1", null))).Code);
        Assert.DoesNotContain(fixture.Logger.Messages,
            message => message.Contains("one-time-code", StringComparison.Ordinal));
    }

    [Fact]
    public void AuthorizationCodeRequestRejectsUnknownProperties()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.NotNull(JsonSerializer.Deserialize<GoogleAuthorizationCodeRequest>(
            """{"code":"opaque","intent":"Login","acceptTerms":false}""", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GoogleAuthorizationCodeRequest>(
            """{"code":"opaque","intent":"Login","acceptTerms":false,"redirectUri":"https://evil.example"}""", options));
    }

    [Fact]
    public async Task RegisterRequiresTermsAndCredentialIsBounded()
    {
        var validator = new GoogleAuthenticationRequestValidator();
        var noTerms = await validator.ValidateAsync(new GoogleAuthenticationRequest(
            RawCredential, GoogleAuthenticationIntent.Register));
        var tooLong = await validator.ValidateAsync(new GoogleAuthenticationRequest(
            new string('x', 8193), GoogleAuthenticationIntent.Login));
        Assert.Contains(noTerms.Errors, x => x.PropertyName == "AcceptTerms");
        Assert.Contains(tooLong.Errors, x => x.PropertyName == "Credential");
    }

    [Fact]
    public async Task ExistingProviderRegistrationIsIdempotentAndLoginUpdatesLastLogin()
    {
        var fixture = CreateFixture();
        var user = Candidate();
        var linked = Link(user);
        fixture.Users.Items.Add(user);
        fixture.ExternalLogins.Items.Add(linked);

        var registered = await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Register, true), null);
        var loggedIn = await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null);

        Assert.Equal(user.Id, registered.User.Id);
        Assert.Equal(user.Id, loggedIn.User.Id);
        Assert.Single(fixture.Users.Items);
        Assert.Single(fixture.ExternalLogins.Items);
        Assert.Equal(Now, user.LastLoginAtUtc);
        Assert.Equal(Now, linked.LastLoginAtUtc);
    }

    [Fact]
    public async Task UnknownLoginIsRejectedButExistingLocalEmailIsLinked()
    {
        var login = CreateFixture();
        var loginError = await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            login.Service.AuthenticateAsync(new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("google_account_not_registered", loginError.Code);

        var register = CreateFixture();
        var local = Candidate();
        register.Users.Items.Add(local);
        var response = await register.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null);
        Assert.Equal(local.Id, response.User.Id);
        Assert.Single(register.Users.Items);
        Assert.Equal(local.Id, Assert.Single(register.ExternalLogins.Items).UserId);
        Assert.Equal("existing-password-hash", local.PasswordHash);
    }

    [Fact]
    public async Task AdministratorEmailAndLinkedAdministratorCannotUseGoogle()
    {
        var emailMatch = CreateFixture();
        var administrator = Candidate(SystemRoleIds.Administrator, "Administrator");
        emailMatch.Users.Items.Add(administrator);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => emailMatch.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Register, true), null));
        Assert.Equal("google_identity_link_conflict", conflict.Code);

        var linkedAdmin = CreateFixture();
        linkedAdmin.Users.Items.Add(administrator);
        linkedAdmin.ExternalLogins.Items.Add(Link(administrator));
        var unavailable = await Assert.ThrowsAsync<AuthenticationFlowException>(() => linkedAdmin.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("candidate_account_unavailable", unavailable.Code);
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Inactive)]
    public async Task NonActiveLinkedCandidateIsRejected(UserStatus status)
    {
        var fixture = CreateFixture();
        var user = Candidate(); user.Status = status;
        fixture.Users.Items.Add(user); fixture.ExternalLogins.Items.Add(Link(user));
        var error = await Assert.ThrowsAsync<AuthenticationFlowException>(() => fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("candidate_account_unavailable", error.Code);
    }

    [Fact]
    public async Task DisabledAndInvalidCredentialsReturnDedicatedSafeCodes()
    {
        var disabled = CreateFixture(enabled: false);
        var disabledError = await Assert.ThrowsAsync<AuthenticationFlowException>(() => disabled.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("google_authentication_disabled", disabledError.Code);

        var invalid = CreateFixture();
        invalid.Google.Exception = new GoogleCredentialValidationException();
        var invalidError = await Assert.ThrowsAsync<AuthenticationFlowException>(() => invalid.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("invalid_google_credential", invalidError.Code);
    }

    [Theory]
    [InlineData("invalid_signature")]
    [InlineData("wrong_audience")]
    [InlineData("expired")]
    public async Task CryptographicValidationFailuresAreMappedWithoutLibraryDetails(string reason)
    {
        var fixture = CreateFixture();
        fixture.Google.Exception = new GoogleCredentialValidationException(reason);
        var error = await Assert.ThrowsAsync<AuthenticationFlowException>(() =>
            fixture.Service.AuthenticateAsync(
                new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("invalid_google_credential", error.Code);
        Assert.Equal("The Google credential is invalid or expired.", error.Message);
        Assert.DoesNotContain(fixture.Logger.Messages,
            message => message.Contains(RawCredential, StringComparison.Ordinal));
    }

    [Fact]
    public void GoogleConfigurationIsOptionalButEnabledProviderRequiresCompleteSafeCodeConfiguration()
    {
        var disabled = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Authentication:Google:Enabled"] = "false" }).Build();
        var services = new ServiceCollection();
        _ = JobPortal.Infrastructure.ServiceCollectionExtensions.AddInfrastructure(services, disabled);

        var enabledWithoutId = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Authentication:Google:Enabled"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            JobPortal.Infrastructure.ServiceCollectionExtensions.AddInfrastructure(
                new ServiceCollection(), enabledWithoutId));

        var valid = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "test.apps.googleusercontent.com",
                ["Authentication:Google:ClientSecret"] = "secret",
                ["Authentication:Google:AllowedCodeOrigins:0"] = "https://careerharbor.in",
                ["Authentication:Google:AllowedCodeOrigins:1"] = "http://localhost:5173"
            }).Build();
        _ = JobPortal.Infrastructure.ServiceCollectionExtensions.AddInfrastructure(
            new ServiceCollection(), valid);

        var insecureOrigin = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "test.apps.googleusercontent.com",
                ["Authentication:Google:ClientSecret"] = "secret",
                ["Authentication:Google:AllowedCodeOrigins:0"] = "http://careerharbor.in/path"
            }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            JobPortal.Infrastructure.ServiceCollectionExtensions.AddInfrastructure(
                new ServiceCollection(), insecureOrigin));
    }

    [Theory]
    [MemberData(nameof(InvalidClaims))]
    public async Task MissingOrUnverifiedRequiredClaimsAreRejected(ValidatedGoogleIdentity identity)
    {
        var fixture = CreateFixture(); fixture.Google.Identity = identity;
        var error = await Assert.ThrowsAsync<AuthenticationFlowException>(() => fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("invalid_google_credential", error.Code);
    }

    public static TheoryData<ValidatedGoogleIdentity> InvalidClaims => new()
    {
        new("", "candidate@example.com", true, null, null, null),
        new("subject", "", true, null, null, null),
        new("subject", "candidate@example.com", false, null, null, null),
        new(new string('s', 256), "candidate@example.com", true, null, null, null)
    };

    [Fact]
    public async Task UniqueRaceResolvesProviderIdentityWithoutDuplicate()
    {
        var fixture = CreateFixture();
        fixture.UnitOfWork.ThrowUniqueOnce = true;
        fixture.UnitOfWork.OnUnique = () =>
        {
            var persisted = Candidate();
            fixture.Users.Items.Clear(); fixture.Users.Items.Add(persisted);
            fixture.ExternalLogins.Items.Clear(); fixture.ExternalLogins.Items.Add(Link(persisted));
        };

        var response = await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Register, true), null);

        Assert.Single(fixture.Users.Items);
        Assert.Single(fixture.ExternalLogins.Items);
        Assert.Equal(fixture.Users.Items[0].Id, response.User.Id);
    }

    [Fact]
    public async Task LinkingPreservesPasswordMembershipProfileApplicationsAndResume()
    {
        var fixture = CreateFixture();
        var user = Candidate();
        user.ResumeStorageKey = "private/resumes/existing.pdf";
        var membership = new Membership { UserId = user.Id, User = user };
        var profile = new CandidateResumeProfile { UserId = user.Id, User = user };
        var application = new JobApplication { UserId = user.Id, User = user };
        user.Memberships.Add(membership);
        user.ResumeProfile = profile;
        user.JobApplications.Add(application);
        fixture.Users.Items.Add(user);

        var response = await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null);

        Assert.Equal(user.Id, response.User.Id);
        Assert.Equal("existing-password-hash", user.PasswordHash);
        Assert.Same(membership, Assert.Single(user.Memberships));
        Assert.Same(profile, user.ResumeProfile);
        Assert.Same(application, Assert.Single(user.JobApplications));
        Assert.Equal("private/resumes/existing.pdf", user.ResumeStorageKey);
        Assert.Single(fixture.Users.Items);
    }

    [Fact]
    public async Task SubjectOwnedByDifferentEmailAccountAndDifferentSubjectOnUserAreRejectedSafely()
    {
        var subjectConflict = CreateFixture();
        var subjectOwner = Candidate();
        subjectOwner.Email = "first@example.com";
        subjectOwner.NormalizedEmail = "first@example.com";
        var emailOwner = Candidate();
        subjectConflict.Users.Items.AddRange([subjectOwner, emailOwner]);
        subjectConflict.ExternalLogins.Items.Add(Link(subjectOwner));
        var error = await Assert.ThrowsAsync<ConflictException>(() => subjectConflict.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("google_identity_link_conflict", error.Code);

        var differentSubject = CreateFixture();
        var local = Candidate();
        differentSubject.Users.Items.Add(local);
        var prior = Link(local);
        prior.ProviderSubject = "different-google-subject";
        differentSubject.ExternalLogins.Items.Add(prior);
        error = await Assert.ThrowsAsync<ConflictException>(() => differentSubject.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null));
        Assert.Equal("google_identity_link_conflict", error.Code);
        Assert.Single(differentSubject.ExternalLogins.Items);
    }

    [Fact]
    public async Task ConcurrentLinkRaceUsesPersistedLinkAndDoesNotDuplicateUser()
    {
        var fixture = CreateFixture();
        var local = Candidate();
        fixture.Users.Items.Add(local);
        fixture.UnitOfWork.ThrowUniqueOnce = true;
        fixture.UnitOfWork.OnUnique = () =>
        {
            fixture.ExternalLogins.Items.Clear();
            fixture.ExternalLogins.Items.Add(Link(local));
        };

        var response = await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null);

        Assert.Equal(local.Id, response.User.Id);
        Assert.Single(fixture.Users.Items);
        Assert.Single(fixture.ExternalLogins.Items);
    }

    [Fact]
    public async Task AuthenticationLogsContainNoCredentialEmailOrProviderSubject()
    {
        var fixture = CreateFixture();
        fixture.Users.Items.Add(Candidate());
        await fixture.Service.AuthenticateAsync(
            new(RawCredential, GoogleAuthenticationIntent.Login), null);
        var logs = string.Join("\n", fixture.Logger.Messages);
        Assert.DoesNotContain(RawCredential, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ValidIdentity.Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValidIdentity.Subject, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("existing-password-hash", logs, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointIsAnonymousAndRequestCannotOverpostRoleOrUser()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Google))!;
        Assert.NotNull(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var valid = JsonSerializer.Deserialize<GoogleAuthenticationRequest>(
            """{"credential":"token","intent":"Login","acceptTerms":false}""", options);
        Assert.Equal(GoogleAuthenticationIntent.Login, valid!.Intent);
        const string json = """
          {"credential":"token","intent":"Login","acceptTerms":false,"role":"Administrator"}
          """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GoogleAuthenticationRequest>(json, options));

        var codeMethod = typeof(AuthController).GetMethod(nameof(AuthController.GoogleCode))!;
        Assert.NotNull(codeMethod.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
    }

    private static Fixture CreateFixture(bool enabled = true)
    {
        var users = new UserRepositoryFake();
        var external = new ExternalLoginRepositoryFake();
        var refresh = new RefreshTokenRepositoryFake(users);
        var unit = new UnitOfWorkFake();
        var google = new GoogleValidatorFake { Identity = ValidIdentity };
        var codeExchanger = new GoogleCodeExchangerFake { Identity = ValidIdentity };
        var logger = new TestLogger<GoogleAuthenticationService>();
        var service = new GoogleAuthenticationService(users, external, refresh, unit,
            new JwtTokenServiceFake(), google, codeExchanger, new AuditWriterTestDouble(),
            new GoogleAuthenticationRequestValidator(),
            new GoogleAuthorizationCodeRequestValidator(),
            Options.Create(new GoogleAuthenticationOptions
            {
                Enabled = enabled,
                ClientId = "test.apps.googleusercontent.com",
                ClientSecret = "test-secret",
                AllowedCodeOrigins = ["https://careerharbor.in", "https://www.careerharbor.in", "http://localhost:5173"]
            }),
            new FixedTimeProvider(), logger);
        return new(service, users, external, refresh, unit, google, codeExchanger, logger);
    }

    private static User Candidate(Guid? roleId = null, string roleName = "Candidate") => new()
    {
        Id = Guid.NewGuid(), Email = "Candidate@Example.com", NormalizedEmail = "candidate@example.com",
        FirstName = "Manoj", LastName = "Shekapure", PasswordHash = "existing-password-hash",
        RoleId = roleId ?? SystemRoleIds.Candidate, Status = UserStatus.Active,
        Role = new Role { Id = roleId ?? SystemRoleIds.Candidate, Name = roleName, NormalizedName = roleName.ToUpperInvariant() }
    };
    private static UserExternalLogin Link(User user) => new()
    { UserId = user.Id, User = user, Provider = ExternalLoginProvider.Google,
      ProviderSubject = ValidIdentity.Subject, ProviderEmail = ValidIdentity.Email };

    private sealed record Fixture(GoogleAuthenticationService Service, UserRepositoryFake Users,
        ExternalLoginRepositoryFake ExternalLogins, RefreshTokenRepositoryFake RefreshTokens,
        UnitOfWorkFake UnitOfWork, GoogleValidatorFake Google,
        GoogleCodeExchangerFake CodeExchanger, TestLogger<GoogleAuthenticationService> Logger);

    private sealed class UserRepositoryFake : IUserRepository
    {
        public List<User> Items { get; } = [];
        public Task<User?> GetByNormalizedEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(x => x.NormalizedEmail == email));
        public Task<User?> GetByNormalizedPhoneAsync(string phone, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<bool> RegistrationIdentityExistsAsync(string email, string phone, CancellationToken ct = default) => Task.FromResult(Items.Any(x => x.NormalizedEmail == email));
        public Task<User?> GetByIdWithRoleAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task AddAsync(User user, CancellationToken ct = default) { Items.Add(user); return Task.CompletedTask; }
        public void Update(User user) { }
    }
    private sealed class ExternalLoginRepositoryFake : IUserExternalLoginRepository
    {
        public List<UserExternalLogin> Items { get; } = [];
        public Task<UserExternalLogin?> GetByProviderSubjectAsync(ExternalLoginProvider provider, string subject, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(x => x.Provider == provider && x.ProviderSubject == subject));
        public Task<UserExternalLogin?> GetByUserProviderAsync(Guid userId, ExternalLoginProvider provider, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(x => x.UserId == userId && x.Provider == provider));
        public Task AddAsync(UserExternalLogin login, CancellationToken ct = default) { Items.Add(login); return Task.CompletedTask; }
        public void Update(UserExternalLogin login) { }
    }
    private sealed class RefreshTokenRepositoryFake(UserRepositoryFake users) : IRefreshTokenRepository
    {
        public List<RefreshToken> Items { get; } = [];
        public Task AddAsync(RefreshToken token, CancellationToken ct = default) { token.User = users.Items.Single(x => x.Id == token.UserId); Items.Add(token); return Task.CompletedTask; }
        public Task<RefreshToken?> GetByTokenHashAsync(string hash, CancellationToken ct = default) => Task.FromResult(Items.SingleOrDefault(x => x.Token == hash));
        public void Update(RefreshToken token) { }
        public Task RevokeActiveForUserAsync(Guid id, DateTime at, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public bool ThrowUniqueOnce { get; set; } public Action? OnUnique { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        { if (ThrowUniqueOnce) { ThrowUniqueOnce = false; OnUnique?.Invoke(); throw new UniqueConstraintException("race"); } return Task.FromResult(1); }
    }
    private sealed class GoogleValidatorFake : IGoogleCredentialValidator
    {
        public ValidatedGoogleIdentity Identity { get; set; } = ValidIdentity; public Exception? Exception { get; set; }
        public Task<ValidatedGoogleIdentity> ValidateAsync(string credential, CancellationToken ct = default) => Exception is null ? Task.FromResult(Identity) : Task.FromException<ValidatedGoogleIdentity>(Exception);
    }
    private sealed class GoogleCodeExchangerFake : IGoogleAuthorizationCodeExchanger
    {
        public ValidatedGoogleIdentity Identity { get; set; } = ValidIdentity;
        public Exception? Exception { get; set; }
        public Task<ValidatedGoogleIdentity> ExchangeAsync(string code, string redirectUri, CancellationToken ct = default) =>
            Exception is null ? Task.FromResult(Identity) : Task.FromException<ValidatedGoogleIdentity>(Exception);
    }
    private sealed class JwtTokenServiceFake : IJwtTokenService
    {
        public AccessTokenResult CreateAccessToken(User user) => new("career-harbor-access", Now.AddMinutes(15));
        public string GenerateRefreshToken() => $"career-harbor-refresh-{Guid.NewGuid():N}";
        public string HashToken(string token) => $"hash:{token}";
    }
    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
