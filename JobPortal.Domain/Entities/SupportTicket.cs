using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class SupportTicket : BaseEntity
{
    public string TicketNumber { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public SupportCategory Category { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ScreenshotPath { get; set; }
    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;
    public string? AdminNotes { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public long Revision { get; set; }
}
