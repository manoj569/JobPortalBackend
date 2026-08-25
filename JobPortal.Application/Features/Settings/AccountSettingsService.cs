using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Abstractions.Settings;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Settings;

public sealed record AccountSecurityStatusResponse(
    bool EmailVerified, bool MobileVerified, bool HasPassword, bool GoogleLinked);

public sealed class AccountSettingsService(
    IUserRepository users, IUserExternalLoginRepository externalLogins) : IAccountSettingsService
{
    public async Task<AccountSecurityStatusResponse> GetSecurityStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdWithRoleAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("Authentication is required.");
        var google = await externalLogins.GetByUserProviderAsync(userId, ExternalLoginProvider.Google, cancellationToken);
        return new(user.EmailConfirmed, user.PhoneConfirmed,
            !string.IsNullOrWhiteSpace(user.PasswordHash), google is not null);
    }
}
