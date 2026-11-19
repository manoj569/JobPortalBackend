using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class HiringMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public HiringConversation Conversation { get; set; } = null!;

    public Guid SenderUserId { get; set; }
    public User SenderUser { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }
}
