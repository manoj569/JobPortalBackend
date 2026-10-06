using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

/// <summary>Private, durable bytes for candidate-uploaded and AI Resume documents.</summary>
public sealed class ResumeDocumentBlob : BaseEntity
{
    public Guid OwnerUserId { get; set; }
    public Guid? ResumeId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long FileLength { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
}
