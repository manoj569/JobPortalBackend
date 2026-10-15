using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

/// <summary>A system-level discovery channel which may submit jobs for many employers.</summary>
public sealed class AcquisitionSource : BaseEntity
{
    public string ProviderKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool AutoPublishEnabled { get; set; }
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
