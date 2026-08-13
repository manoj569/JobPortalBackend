using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class UserExternalLogin : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ExternalLoginProvider Provider { get; set; }
    public string ProviderSubject { get; set; } = string.Empty;
    public string ProviderEmail { get; set; } = string.Empty;
    public DateTime LastLoginAtUtc { get; set; }
}
