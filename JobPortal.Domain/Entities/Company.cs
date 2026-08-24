using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? Industry { get; set; }
    public string? Location { get; set; }
    public int? EmployeeCount { get; set; }
    public CompanyType? CompanyType { get; set; }
    public bool IsVerified { get; set; }
    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;
    public CompanySubmissionSource SubmissionSource { get; set; }
    public Guid? SubmittedByCandidateId { get; set; }
    public User? SubmittedByCandidate { get; set; }
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
