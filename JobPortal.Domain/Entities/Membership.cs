using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class Membership : BaseEntity
{
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public bool AutoRenew { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<MembershipHistory> History { get; set; } = new List<MembershipHistory>();
}
