using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class EmployerTalentPool : BaseEntity
{
    public Guid EmployerUserId { get; set; }

    public User EmployerUser { get; set; } = null!;

    public Guid CandidateUserId { get; set; }

    public User CandidateUser { get; set; } = null!;

    public string? Notes { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}
