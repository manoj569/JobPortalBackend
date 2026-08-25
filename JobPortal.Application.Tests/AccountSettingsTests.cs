using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Settings;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AccountSettingsTests
{
    [Fact]
    public async Task SecurityStatusReturnsOnlyOwnedBooleanSecuritySignals()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), EmailConfirmed = true, PhoneConfirmed = false,
            PasswordHash = "stored-password-hash", RoleId = SystemRoleIds.Candidate
        };
        var service = new AccountSettingsService(new UserStore(user), new LoginStore(user.Id));

        var result = await service.GetSecurityStatusAsync(user.Id);

        Assert.Equal(new AccountSecurityStatusResponse(true, false, true, true), result);
        Assert.DoesNotContain("stored-password-hash", System.Text.Json.JsonSerializer.Serialize(result));
    }

    private sealed class UserStore(User user) : IUserRepository
    {
        public Task<User?> GetByIdWithRoleAsync(Guid id, CancellationToken ct = default) => Task.FromResult(id == user.Id ? user : null);
        public Task<User?> GetByNormalizedEmailAsync(string value, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<User?> GetByNormalizedPhoneAsync(string value, CancellationToken ct = default) => Task.FromResult<User?>(null);
        public Task<bool> RegistrationIdentityExistsAsync(string email, string phone, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddAsync(User value, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(User value) { }
    }

    private sealed class LoginStore(Guid ownerId) : IUserExternalLoginRepository
    {
        public Task<UserExternalLogin?> GetByProviderSubjectAsync(ExternalLoginProvider provider, string subject, CancellationToken ct = default) => Task.FromResult<UserExternalLogin?>(null);
        public Task<UserExternalLogin?> GetByUserProviderAsync(Guid userId, ExternalLoginProvider provider, CancellationToken ct = default) =>
            Task.FromResult(userId == ownerId && provider == ExternalLoginProvider.Google
                ? new UserExternalLogin { UserId = userId, Provider = provider, ProviderSubject = "private-provider-id" }
                : null);
        public Task AddAsync(UserExternalLogin value, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(UserExternalLogin value) { }
    }
}
