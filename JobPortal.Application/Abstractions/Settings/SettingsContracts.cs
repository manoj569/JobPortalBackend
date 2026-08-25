using JobPortal.Application.Features.Settings;

namespace JobPortal.Application.Abstractions.Settings;

public interface IAccountSettingsService
{
    Task<AccountSecurityStatusResponse> GetSecurityStatusAsync(Guid userId, CancellationToken cancellationToken = default);
}
