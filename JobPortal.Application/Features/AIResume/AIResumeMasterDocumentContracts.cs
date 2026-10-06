using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.AIResume;

// Stored only in existing JSON columns. Never return storage references or send them to Claude.
public sealed record ResumeDocumentBinding(string TargetId, int Paragraph, int Offset, string OriginalText);
public sealed record ResumeMasterDocument(string StorageKey, string Extension, string Sha256, ResumeDocumentBinding[] Bindings);
public sealed record ResumeSessionEvidence(int Version, ResumeSourceEvidence[] Evidence, ResumeMasterDocument MasterDocument);
public sealed record ResumeArtifact(string StorageKey, string Extension, string Sha256);
public sealed record StoredTailoring(int Version, TailoredResumeContent Content, ResumePatchDecision[] Decisions,
    string[] EmphasizedSkillEvidenceIds, ResumeMasterDocument MasterDocument, ResumeArtifact Artifact);
public sealed record ResumeDocumentCapabilities(string PreservationMode, string OriginalFileType, bool LayoutPreserved,
    string DefaultDownloadFormat, string[] DownloadFormats, string? Limitation);
public sealed record ResumeTailoringReview(ResumePatchDecision[] Replacements, string[] EmphasizedSkillEvidenceIds);
public interface IAIResumeMasterDocuments
{
    Task<ResumeMasterDocument> CaptureAsync(User candidate, CancellationToken ct);
    Task<ResumeMasterDocument> BindAsync(ResumeMasterDocument master, TailoredResumeContent source, CancellationToken ct);
    Task<ResumeArtifact> CreateArtifactAsync(ResumeMasterDocument master, TailoredResumeContent source,
        ResumePatchResult result, CancellationToken ct);
    Task<AIResumeDownload> DownloadAsync(StoredTailoring tailoring, string format, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
    ResumeDocumentCapabilities Capabilities(ResumeMasterDocument master);
}
