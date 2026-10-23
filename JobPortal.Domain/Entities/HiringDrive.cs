using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class HiringDrive : BaseEntity
{
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? BadgeText { get; set; }
    public string? BadgeType { get; set; }
    public string? ImageUrl { get; set; }
    public string? LocationText { get; set; }
    public string? ExperienceText { get; set; }
    public Guid? JobId { get; set; }
    public Job? Job { get; set; }
    public string? ApplyUrl { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public bool IsSponsored { get; set; }
    public bool ShowOnHome { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public long ImpressionCount { get; set; }
    public long ClickCount { get; set; }
}
