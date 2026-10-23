using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

/// <summary>An additional display configuration for an existing job.</summary>
public sealed class JobPromotion : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string? BannerImageUrl { get; set; }
    public byte[]? BannerImageContent { get; set; }
    public string? BannerContentType { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public int DisplayPriority { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
