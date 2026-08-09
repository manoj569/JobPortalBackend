using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Legal;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AuthenticationTests
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Manoj Shekapure", true)]
    [InlineData("सुमित कुमार", true)]
    [InlineData("Cher", true)]
    [InlineData("Manoj  Shekapure", false)]
    [InlineData("Manoj7 Shekapure", false)]
    [InlineData("Manoj-Shekapure", false)]
    [InlineData("Manoj 🙂", false)]
    [InlineData("", false)]
    public async Task FullNameAllowsOnlyUnicodeLettersAndSingleSpaces(
        string fullName,
        bool expectedValid)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRegistration() with { FullName = fullName });

        Assert.Equal(
            expectedValid,
            !result.Errors.Any(error => error.PropertyName == "FullName"));
    }

    [Fact]
    public async Task RegistrationCreatesActiveCandidateDirectly()
    {
        var fixture = CreateFixture();

        var response = await fixture.Service.RegisterAsync(
            ValidRegistration() with
            {
                FullName = "  Manoj Shekapure  ",
                Email = "  User@Example.COM  "
            });

        Assert.Equal("Registration successful. Please log in.", response.Message);
        var user = Assert.Single(fixture.Users.Items);
        Assert.Equal("Manoj", user.FirstName);
        Assert.Equal("Shekapure", user.LastName);
        Assert.Equal("user@example.com", user.NormalizedEmail);
        Assert.Equal("+919876543210", user.NormalizedPhoneNumber);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.PhoneConfirmed);
        Assert.True(user.EmailConfirmed);
        Assert.Equal(SystemRoleIds.Candidate, user.RoleId);
        Assert.NotEqual("abc123", user.PasswordHash);
        Assert.DoesNotContain("abc123", user.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(LegalDocumentCatalog.CurrentVersion, user.TermsAndPrivacyVersion);
        Assert.Equal(Now, user.TermsAndPrivacyAcceptedAtUtc);
        var audit = Assert.Single(fixture.Audit.Events);
        Assert.Equal(AuditAction.Create, audit.Action);
        Assert.DoesNotContain(
            "AccessToken",
            JsonSerializer.Serialize(response),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc12", false)]
    [InlineData("abcdef", true)]
    [InlineData("abc123", true)]
    [InlineData("ABCDEF", true)]
    [InlineData("!!!!!!", true)]
    public async Task RegistrationUsesSixCharacterMinimumWithoutComplexity(
        string password,
        bool expectedValid)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRegistration() with { Password = password });

        Assert.Equal(
            expectedValid,
            !result.Errors.Any(error => error.PropertyName == "Password"));
    }

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("+919876543210", false)]
    [InlineData("09876543210", false)]
    [InlineData("98765 43210", false)]
    [InlineData("98765-43210", false)]
    [InlineData("5876543210", false)]
    [InlineData("9999999999", false)]
    [InlineData("98765abcde", false)]
    public async Task RegistrationAcceptsOnlyTenDigitIndianMobile(
        string phoneNumber,
        bool expectedValid)
    {
        var result = await new RegisterRequestValidator().ValidateAsync(
            ValidRegistration() with { PhoneNumber = phoneNumber });

        Assert.Equal(
            expectedValid,
            !result.Errors.Any(error => error.PropertyName == "PhoneNumber"));
    }

    [Fact]
    public async Task DuplicateIdentityResponseIsPrivateAndConcurrentConflictCreatesNoDuplicateUser()
    {
        var duplicate = CreateFixture();
        duplicate.Users.Items.Add(NewUser());

        var response = await duplicate.Service.RegisterAsync(ValidRegistration());

        Assert.Equal("Registration successful. Please log in.", response.Message);
        Assert.Single(duplicate.Users.Items);
        Assert.Contains(duplicate.Logger.Messages, message =>
            message.Contains("send_skipped_existing_user", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "user@example.com",
            JsonSerializer.Serialize(response),
            StringComparison.OrdinalIgnoreCase);

        var concurrent = CreateFixture();
        concurrent.UnitOfWork.ExceptionToThrow =
            new UniqueConstraintException("duplicate");
        var concurrentResponse = await concurrent.Service.RegisterAsync(
            ValidRegistration());
        Assert.Equal(response.Message, concurrentResponse.Message);
        Assert.Empty(concurrent.Users.Items);
    }

    [Fact]
    public async Task RegistrationRequiresConsentValidEmailAndRejectsOverPosting()
    {
        var validator = new RegisterRequestValidator();
        var noConsent = await validator.ValidateAsync(
            ValidRegistration() with { HasAcceptedTermsAndPrivacy = false });
        var badEmail = await validator.ValidateAsync(
            ValidRegistration() with { Email = "not-an-email" });
        Assert.Contains(
            noConsent.Errors,
            error => error.PropertyName == "HasAcceptedTermsAndPrivacy");
        Assert.Contains(badEmail.Errors, error => error.PropertyName == "Email");

        const string json =
            """
            {
              "fullName":"Manoj Shekapure",
              "email":"user@example.com",
              "password":"abc123",
              "phoneNumber":"9876543210",
              "hasAcceptedTermsAndPrivacy":true,
              "role":"Administrator"
            }
            """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RegisterRequest>(json, WebJson));
    }

    [Fact]
    public async Task PasswordLoginSupportsNormalizedEmailMobileAndAdministrator()
    {
        var fixture = CreateFixture();
        var candidate = NewUser();
        var administrator = NewUser(
            "admin@example.com",
            "+919123456780",
            SystemRoleIds.Administrator,
            "Administrator");
        fixture.Users.Items.AddRange([candidate, administrator]);

        var emailLogin = await fixture.Service.LoginAsync(
            new(" USER@EXAMPLE.COM ", "abc123"),
            null);
        var mobileLogin = await fixture.Service.LoginAsync(
            new("9876543210", "abc123"),
            null);
        var adminLogin = await fixture.Service.LoginAsync(
            new("admin@example.com", "abc123"),
            null);

        Assert.Equal(candidate.Id, emailLogin.User.Id);
        Assert.Equal(candidate.Id, mobileLogin.User.Id);
        Assert.Equal("Administrator", adminLogin.User.Role);
        var missing = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.LoginAsync(
                new("missing@example.com", "abc123"),
                null));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.LoginAsync(
                new("user@example.com", "wrong-password"),
                null));
        Assert.Equal(missing.Message, wrong.Message);
    }

    [Fact]
    public async Task InactiveUserCannotLogin()
    {
        var fixture = CreateFixture();
        var user = NewUser();
        user.Status = UserStatus.Inactive;
        fixture.Users.Items.Add(user);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.LoginAsync(new("user@example.com", "abc123"), null));
    }

    [Fact]
    public async Task PasswordResetRequestIsPrivacySafeAndPersistsHashBeforeEmail()
    {
        var invalidEmail = await new RequestPasswordResetRequestValidator()
            .ValidateAsync(new RequestPasswordResetRequest("not-an-email"));
        Assert.Contains(
            invalidEmail.Errors,
            error => error.PropertyName == "Email");

        var missing = CreateFixture();
        var fixture = CreateFixture();
        var user = NewUser();
        fixture.Users.Items.Add(user);
        fixture.Email.BeforeSend = () =>
            Assert.True(fixture.UnitOfWork.SaveCount > 0);

        var missingResponse = await missing.Service.RequestPasswordResetAsync(
            new("user@example.com"));
        var response = await fixture.Service.RequestPasswordResetAsync(
            new("  USER@Example.COM  "));

        Assert.Equal(missingResponse, response);
        Assert.Equal(
            "If an account exists for this email address, a password reset link has been sent.",
            response.Message);
        Assert.Equal(0, missing.Email.SendCount);
        Assert.Equal(1, fixture.Email.SendCount);
        Assert.Same(user, fixture.Email.User);
        Assert.NotNull(fixture.Email.LastRawToken);
        Assert.NotEqual(fixture.Email.LastRawToken, user.PasswordResetTokenHash);
        Assert.Equal(64, user.PasswordResetTokenHash!.Length);
        Assert.Equal(Now.AddMinutes(30), user.PasswordResetTokenExpiresAtUtc);
        var auditJson = JsonSerializer.Serialize(Assert.Single(fixture.Audit.Events));
        Assert.DoesNotContain(fixture.Email.LastRawToken!, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, auditJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Logger.Messages, message =>
            message.Contains(
                fixture.Email.LastRawToken!,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task PasswordResetTokenIsSingleUseAndRevokesSessions()
    {
        var fixture = CreateFixture();
        var user = NewUser();
        fixture.Users.Items.Add(user);
        await fixture.Service.RequestPasswordResetAsync(new(user.Email));
        var rawToken = fixture.Email.LastRawToken!;
        var completed = await fixture.Service.CompletePasswordResetAsync(
            new(rawToken, "newpass"));

        Assert.Equal(
            "Password changed successfully. Please log in.",
            completed.Message);
        Assert.True(fixture.Passwords.Verify("newpass", user.PasswordHash));
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Null(user.PasswordResetTokenExpiresAtUtc);
        Assert.True(fixture.RefreshTokens.RevokedForUser);
        Assert.All(fixture.Audit.Events, audit => Assert.Equal(AuditAction.Update, audit.Action));
        var completionAudit = JsonSerializer.Serialize(fixture.Audit.Events.Last());
        Assert.DoesNotContain(rawToken, completionAudit, StringComparison.Ordinal);
        Assert.DoesNotContain("newpass", completionAudit, StringComparison.Ordinal);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.CompletePasswordResetAsync(
                new(rawToken, "again12")));
    }

    [Fact]
    public async Task InvalidExpiredAndInactivePasswordResetTokensAreRejected()
    {
        var invalid = CreateFixture();
        var invalidUser = NewUser();
        invalid.Users.Items.Add(invalidUser);
        await invalid.Service.RequestPasswordResetAsync(new(invalidUser.Email));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            invalid.Service.CompletePasswordResetAsync(
                new("wrong-token", "newpass")));

        var expired = CreateFixture();
        var expiredUser = NewUser();
        expired.Users.Items.Add(expiredUser);
        await expired.Service.RequestPasswordResetAsync(new(expiredUser.Email));
        expired.Time.Advance(TimeSpan.FromMinutes(31));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            expired.Service.CompletePasswordResetAsync(
                new(
                    expired.Email.LastRawToken!,
                    "newpass")));

        var inactive = CreateFixture();
        var inactiveUser = NewUser();
        inactiveUser.Status = UserStatus.Inactive;
        inactive.Users.Items.Add(inactiveUser);
        var inactiveResponse = await inactive.Service.RequestPasswordResetAsync(
            new(inactiveUser.Email));
        Assert.Equal(
            "If an account exists for this email address, a password reset link has been sent.",
            inactiveResponse.Message);
        Assert.Equal(0, inactive.Email.SendCount);
        Assert.Null(inactiveUser.PasswordResetTokenHash);
    }

    [Fact]
    public async Task RefreshTokenRotatesAndRevokesPredecessor()
    {
        var fixture = CreateFixture();
        var user = NewUser();
        fixture.Users.Items.Add(user);
        var login = await fixture.Service.LoginAsync(new(user.Email, "abc123"), "127.0.0.1");

        var refreshed = await fixture.Service.RefreshAsync(
            new(login.RefreshToken),
            "127.0.0.1");

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        var original = fixture.RefreshTokens.Added.First();
        Assert.NotNull(original.RevokedAtUtc);
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.RefreshAsync(new(login.RefreshToken), "127.0.0.1"));
    }

    [Fact]
    public async Task ChangePasswordRequiresCurrentPasswordAndRevokesSessions()
    {
        var fixture = CreateFixture();
        var user = NewUser();
        fixture.Users.Items.Add(user);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.ChangePasswordAsync(
                user.Id,
                new("wrong-password", "newpass")));

        await fixture.Service.ChangePasswordAsync(
            user.Id,
            new("abc123", "newpass"));

        Assert.True(fixture.Passwords.Verify("newpass", user.PasswordHash));
        Assert.True(fixture.RefreshTokens.RevokedForUser);
    }

    [Fact]
    public void LegalDocumentsAndApiContractsArePublicAndApplicationOwned()
    {
        var terms = LegalDocumentCatalog.TermsOfUse();
        var privacy = LegalDocumentCatalog.PrivacyPolicy();
        Assert.Equal(LegalDocumentCatalog.CurrentVersion, terms.Version);
        Assert.Equal("text/plain", terms.ContentType);
        Assert.NotEmpty(terms.Content);
        Assert.Equal(terms.EffectiveDate, privacy.EffectiveDate);

        var root = FindRepositoryRoot();
        var legalController = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.API",
            "Controllers",
            "LegalController.cs"));
        var authController = File.ReadAllText(Path.Combine(
            root,
            "JobPortal.API",
            "Controllers",
            "AuthController.cs"));
        Assert.Contains("terms-of-use", legalController, StringComparison.Ordinal);
        Assert.Contains("privacy-policy", legalController, StringComparison.Ordinal);
        Assert.Contains("request-password-reset", authController, StringComparison.Ordinal);
        Assert.Contains("complete-password-reset", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-registration-otp", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("login-with-otp", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("request-login-otp", authController, StringComparison.Ordinal);
        Assert.DoesNotContain("resend-registration-otp", authController, StringComparison.Ordinal);
    }

    private static RegisterRequest ValidRegistration() => new(
        "Manoj Shekapure",
        "user@example.com",
        "abc123",
        "9876543210",
        true);

    private static User NewUser(
        string email = "user@example.com",
        string phone = "+919876543210",
        Guid? roleId = null,
        string roleName = "Candidate") => new()
        {
            Email = email,
            NormalizedEmail = email.ToLowerInvariant(),
            NormalizedPhoneNumber = phone,
            PhoneNumber = phone,
            PhoneConfirmed = true,
            PasswordHash = HashPassword("abc123"),
            FirstName = "Manoj",
            LastName = "Shekapure",
            Status = UserStatus.Active,
            RoleId = roleId ?? SystemRoleIds.Candidate,
            Role = new Role
            {
                Id = roleId ?? SystemRoleIds.Candidate,
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            }
        };

    private static Fixture CreateFixture()
    {
        var time = new MutableTimeProvider(Now);
        var users = new UserRepositoryFake();
        var refreshTokens = new RefreshTokenRepositoryFake(users);
        var unitOfWork = new UnitOfWorkFake();
        var passwords = new PasswordHasherFake();
        var email = new EmailServiceFake();
        var audit = new AuditWriterTestDouble();
        var logger = new TestLogger<AuthService>();
        var service = new AuthService(
            users,
            refreshTokens,
            unitOfWork,
            passwords,
            new JwtTokenServiceFake(time),
            email,
            audit,
            new RegisterRequestValidator(),
            new LoginRequestValidator(),
            new RequestPasswordResetRequestValidator(),
            new CompletePasswordResetRequestValidator(),
            new RefreshTokenRequestValidator(),
            new ChangePasswordRequestValidator(),
            time,
            logger);
        return new(
            service,
            users,
            refreshTokens,
            unitOfWork,
            passwords,
            email,
            audit,
            time,
            logger);
    }

    private sealed record Fixture(
        AuthService Service,
        UserRepositoryFake Users,
        RefreshTokenRepositoryFake RefreshTokens,
        UnitOfWorkFake UnitOfWork,
        PasswordHasherFake Passwords,
        EmailServiceFake Email,
        AuditWriterTestDouble Audit,
        MutableTimeProvider Time,
        TestLogger<AuthService> Logger);

    private sealed class UserRepositoryFake : IUserRepository
    {
        public List<User> Items { get; } = [];

        public Task<User?> GetByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(
                user => user.NormalizedEmail == normalizedEmail));

        public Task<User?> GetByPasswordResetTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(
                user => user.PasswordResetTokenHash == tokenHash));

        public Task<User?> GetByNormalizedPhoneAsync(
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(
                user => user.NormalizedPhoneNumber == normalizedPhoneNumber));

        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail,
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(user =>
                user.NormalizedEmail == normalizedEmail ||
                user.NormalizedPhoneNumber == normalizedPhoneNumber));

        public Task<User?> GetByIdWithRoleAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(user => user.Id == userId));

        public Task AddAsync(
            User user,
            CancellationToken cancellationToken = default)
        {
            Items.Add(user);
            return Task.CompletedTask;
        }

        public void Update(User user)
        {
        }
    }

    private sealed class RefreshTokenRepositoryFake(
        UserRepositoryFake users) : IRefreshTokenRepository
    {
        public List<RefreshToken> Added { get; } = [];
        public bool RevokedForUser { get; private set; }

        public Task<RefreshToken?> GetByTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Added.FirstOrDefault(token =>
                token.Token == tokenHash && token.RevokedAtUtc is null));

        public Task AddAsync(
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default)
        {
            refreshToken.User = users.Items.Single(
                user => user.Id == refreshToken.UserId);
            Added.Add(refreshToken);
            return Task.CompletedTask;
        }

        public void Update(RefreshToken refreshToken)
        {
        }

        public Task RevokeActiveForUserAsync(
            Guid userId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default)
        {
            RevokedForUser = true;
            foreach (var token in Added.Where(item => item.UserId == userId))
                token.RevokedAtUtc = revokedAtUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public Exception? ExceptionToThrow { get; set; }
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return ExceptionToThrow is null
                ? Task.FromResult(1)
                : Task.FromException<int>(ExceptionToThrow);
        }
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) => HashPassword(password);

        public bool Verify(string password, string passwordHash) =>
            Hash(password) == passwordHash;
    }

    private sealed class EmailServiceFake : IEmailService
    {
        public Action? BeforeSend { get; set; }
        public string? LastRawToken { get; private set; }
        public int SendCount { get; private set; }
        public User? User { get; private set; }

        public Task<EmailDeliveryResult> SendPasswordResetAsync(
            User user,
            string rawToken,
            CancellationToken cancellationToken = default)
        {
            BeforeSend?.Invoke();
            User = user;
            LastRawToken = rawToken;
            SendCount++;
            return Task.FromResult(EmailDeliveryResult.Sent);
        }

        public Task<EmailDeliveryResult> SendApplicationStatusAsync(
            User user,
            string jobTitle,
            JobApplicationStatus status,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class JwtTokenServiceFake(
        TimeProvider timeProvider) : IJwtTokenService
    {
        private int tokenNumber;

        public AccessTokenResult CreateAccessToken(User user) =>
            new("access-token", timeProvider.GetUtcNow().UtcDateTime.AddMinutes(15));

        public string GenerateRefreshToken() =>
            $"refresh-token-{++tokenNumber}";

        public string HashToken(string token) => $"hash:{token}";
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTime current = utcNow;

        public void Advance(TimeSpan duration) => current = current.Add(duration);

        public override DateTimeOffset GetUtcNow() => new(current);
    }

    private static string HashPassword(string password) => Sha256(password);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
