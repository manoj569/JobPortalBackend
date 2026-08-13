using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> GetByPasswordResetTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<User?>(null);
    Task<User?> GetByNormalizedPhoneAsync(
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default);
    Task<bool> RegistrationIdentityExistsAsync(
        string normalizedEmail,
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default);
    Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    void Update(User user);
}

public interface IAuthenticationChallengeRepository
{
    Task<PendingRegistration?> GetPendingByIdentityAsync(
        string normalizedEmail,
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default);
    Task<OtpChallenge?> GetChallengeByIdAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default);
    Task<OtpChallenge?> GetLatestForPhoneAsync(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default);
    Task<int> CountSentSinceAsync(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default);
    Task AddPendingAsync(
        PendingRegistration pendingRegistration,
        CancellationToken cancellationToken = default);
    Task AddChallengeAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken = default);
    void Update(PendingRegistration pendingRegistration);
    void Update(OtpChallenge challenge);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
    void Update(RefreshToken refreshToken);
    Task RevokeActiveForUserAsync(Guid userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default);
}

public interface IUserExternalLoginRepository
{
    Task<UserExternalLogin?> GetByProviderSubjectAsync(
        ExternalLoginProvider provider,
        string providerSubject,
        CancellationToken cancellationToken = default);
    Task AddAsync(UserExternalLogin externalLogin, CancellationToken cancellationToken = default);
    void Update(UserExternalLogin externalLogin);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    void ResetAfterFailure() { }
}
