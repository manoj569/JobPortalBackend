using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed class UserRepository(JobPortalDbContext context) : IUserRepository
{
    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<User?> GetByPasswordResetTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(
            x => x.PasswordResetTokenHash == tokenHash,
            cancellationToken);

    public Task<User?> GetByEmailVerificationTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(
            x => x.EmailVerificationTokenHash == tokenHash, cancellationToken);

    public Task<User?> GetByNormalizedPhoneAsync(
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(
            x => x.NormalizedPhoneNumber == normalizedPhoneNumber,
            cancellationToken);

    public Task<bool> RegistrationIdentityExistsAsync(
        string normalizedEmail,
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(
            user => user.NormalizedEmail == normalizedEmail ||
                user.NormalizedPhoneNumber == normalizedPhoneNumber,
            cancellationToken);

    public Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is not null)
        {
            context.Attach(user.Role);
        }

        await context.Users.AddAsync(user, cancellationToken);
    }

    public void Update(User user) => context.Users.Update(user);
}

public sealed class AuthenticationChallengeRepository(
    JobPortalDbContext context) : IAuthenticationChallengeRepository
{
    public Task<PendingRegistration?> GetPendingByIdentityAsync(
        string normalizedEmail,
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default) =>
        context.PendingRegistrations
            .Include(x => x.OtpChallenges)
            .SingleOrDefaultAsync(x =>
                x.ClosedAtUtc == null &&
                x.NormalizedEmail == normalizedEmail &&
                x.NormalizedPhoneNumber == normalizedPhoneNumber,
                cancellationToken);

    public Task<OtpChallenge?> GetChallengeByIdAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default) =>
        context.OtpChallenges
            .Include(x => x.PendingRegistration)
            .Include(x => x.User)
                .ThenInclude(x => x!.Role)
            .SingleOrDefaultAsync(x => x.Id == challengeId, cancellationToken);

    public Task<OtpChallenge?> GetLatestForPhoneAsync(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default) =>
        context.OtpChallenges
            .Include(x => x.User)
                .ThenInclude(x => x!.Role)
            .Where(x =>
                x.NormalizedPhoneNumber == normalizedPhoneNumber &&
                x.Purpose == purpose)
            .OrderByDescending(x => x.LastSentAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountSentSinceAsync(
        string normalizedPhoneNumber,
        OtpPurpose purpose,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default) =>
        context.OtpChallenges
            .Where(x =>
                x.NormalizedPhoneNumber == normalizedPhoneNumber &&
                x.Purpose == purpose &&
                x.LastSentAtUtc >= sinceUtc)
            .SumAsync(x => x.SendCount, cancellationToken);

    public Task AddPendingAsync(
        PendingRegistration pendingRegistration,
        CancellationToken cancellationToken = default) =>
        context.PendingRegistrations.AddAsync(
            pendingRegistration,
            cancellationToken).AsTask();

    public Task AddChallengeAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken = default) =>
        context.OtpChallenges.AddAsync(challenge, cancellationToken).AsTask();

    public void Update(PendingRegistration pendingRegistration) =>
        context.PendingRegistrations.Update(pendingRegistration);

    public void Update(OtpChallenge challenge) =>
        context.OtpChallenges.Update(challenge);
}

public sealed class UserExternalLoginRepository(
    JobPortalDbContext context) : IUserExternalLoginRepository
{
    public Task<UserExternalLogin?> GetByProviderSubjectAsync(
        ExternalLoginProvider provider,
        string providerSubject,
        CancellationToken cancellationToken = default) =>
        context.UserExternalLogins.Include(x => x.User).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Provider == provider &&
                x.ProviderSubject == providerSubject, cancellationToken);

    public Task AddAsync(
        UserExternalLogin externalLogin,
        CancellationToken cancellationToken = default) =>
        context.UserExternalLogins.AddAsync(externalLogin, cancellationToken).AsTask();

    public void Update(UserExternalLogin externalLogin) =>
        context.UserExternalLogins.Update(externalLogin);
}

public sealed class RegistrationEmailOutbox(
    JobPortalDbContext context,
    IUnitOfWork unitOfWork) : IRegistrationEmailOutbox
{
    public Task EnqueueAsync(
        RegistrationEmailRequest request, CancellationToken cancellationToken = default) =>
        context.RegistrationEmailRequests.AddAsync(request, cancellationToken).AsTask();

    public async Task<RegistrationEmailRequest?> ClaimDueAsync(
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var request = await context.RegistrationEmailRequests
            .Include(x => x.User)
            .Where(x => x.SentAtUtc == null && x.NextAttemptAtUtc <= nowUtc &&
                (x.LockedUntilUtc == null || x.LockedUntilUtc <= nowUtc) &&
                x.ExpiresAtUtc > nowUtc)
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null) return null;
        request.LockedUntilUtc = nowUtc.AddMinutes(2);
        request.AttemptCount++;
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return request;
        }
        catch (DbUpdateConcurrencyException)
        {
            unitOfWork.ResetAfterFailure();
            return null;
        }
    }

    public async Task MarkSentAsync(
        Guid requestId, DateTime sentAtUtc, CancellationToken cancellationToken = default)
    {
        var request = await context.RegistrationEmailRequests
            .SingleAsync(x => x.Id == requestId, cancellationToken);
        request.SentAtUtc = sentAtUtc;
        request.LockedUntilUtc = null;
        request.VerificationToken = string.Empty;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid requestId, DateTime nextAttemptAtUtc, CancellationToken cancellationToken = default)
    {
        var request = await context.RegistrationEmailRequests
            .SingleAsync(x => x.Id == requestId, cancellationToken);
        request.NextAttemptAtUtc = nextAttemptAtUtc;
        request.LockedUntilUtc = null;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RefreshTokenRepository(JobPortalDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.Include(x => x.User).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Token == tokenHash, cancellationToken);

    public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.AddAsync(refreshToken, cancellationToken).AsTask();

    public void Update(RefreshToken refreshToken) => context.RefreshTokens.Update(refreshToken);

    public Task RevokeActiveForUserAsync(
        Guid userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default) =>
        context.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > revokedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, revokedAtUtc)
                .SetProperty(x => x.UpdatedAtUtc, revokedAtUtc), cancellationToken);
}

public sealed class UnitOfWork(JobPortalDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            throw new UniqueConstraintException(
                "A database uniqueness constraint was violated.", exception);
        }
    }

    public void ResetAfterFailure() => context.ChangeTracker.Clear();
}
