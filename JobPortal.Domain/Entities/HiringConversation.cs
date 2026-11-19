using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class HiringConversation : BaseEntity
{
    public Guid JobApplicationId { get; set; }
    public JobApplication JobApplication { get; set; } = null!;

    public Guid CandidateUserId { get; set; }
    public User CandidateUser { get; set; } = null!;

    public Guid EmployerUserId { get; set; }
    public User EmployerUser { get; set; } = null!;

    public DateTime? LastMessageAtUtc { get; set; }

    public DateTime? CandidateLastReadAtUtc { get; set; }
    public DateTime? EmployerLastReadAtUtc { get; set; }

    public ICollection<HiringMessage> Messages { get; set; }
        = new List<HiringMessage>();
}
