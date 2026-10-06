namespace JobPortal.Application.Features.AIResume;

public interface IAIResumeProvider
{
    Task<AIResumeProviderResult<TailoringPatch>> GenerateTailoringPatchAsync(GenerateTailoringPatchRequest request,
        CancellationToken cancellationToken = default) => throw new AIResumeProviderException("patch_provider_not_supported");
    Task<AIResumeProviderResult<ResumeAnalysis>> AnalyzeAsync(AnalyzeResumeRequest request, CancellationToken cancellationToken = default);
    Task<AIResumeProviderResult<TailoredResumeContent>> GenerateTailoredResumeAsync(GenerateTailoredResumeRequest request, CancellationToken cancellationToken = default);
    Task<TailoredResumeValidation> ValidateTailoredResumeAsync(ValidateTailoredResumeRequest request, CancellationToken cancellationToken = default);
}

public sealed record ResumeAnalysis(int OverallMatchScore, string[] MatchedSkills, string[] PartialMatches,
    string[] MissingSkills, string[] JobRequirements, string[] Strengths, string[] AreasToImprove, string[] Suggestions);
public sealed record ResumeContact(string Name, string Email, string Phone, string Location, string[] Links);
public sealed record ResumeExperience(string Employer, string Role, string StartDate, string EndDate, string[] Bullets);
public sealed record ResumeProject(string Name, string[] Technologies, string[] Bullets);
public sealed record ResumeEducation(string Institution, string Qualification, string StartDate, string EndDate);
public sealed record ResumeCertification(string Name, string Issuer, string Date);
public sealed record TailoredResumeContent(ResumeContact Contact, string ProfessionalSummary, string[] Skills,
    ResumeExperience[] Experience, ResumeProject[] Projects, ResumeEducation[] Education,
    ResumeCertification[] Certifications, string[] AdditionalInfo)
{
    public ResumeProvenance[] Evidence { get; init; } = [];
    // Explicit generation field: a generic provenance array cannot require a summary entry in the schema.
    // Older snapshots remain readable; legacy evidence[path=summary] is also validated by the guard.
    public string[] SummarySourceEvidenceIds { get; init; } = [];
}
public sealed record ResumeProvenance(string Path, string Text, string[] SourceEvidenceIds);
public sealed record ResumeSourceEvidence(string Id, string Scope, string Text);
public sealed record AnalyzeResumeRequest(TailoredResumeContent Source, string JobDescription);
public sealed record GenerateTailoredResumeRequest(TailoredResumeContent Source, string JobDescription, ResumeAnalysis Analysis)
{
    public ResumeSourceEvidence[] SourceEvidence { get; init; } = [];
}
public sealed record ValidateTailoredResumeRequest(TailoredResumeContent Source, TailoredResumeContent Generated);
public sealed record TailoredResumeValidation(bool IsValid, string[] Errors);
public sealed record AIResumeProviderResult<T>(T Content, string Model, int InputTokens, int OutputTokens);

public sealed class AIResumeOptions
{
    public const string SectionName = "AIResume";
    public string Provider { get; set; } = "Claude";
    public string Model { get; set; } = "claude-haiku-4-5";
    public string ApiKey { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public int AnalysisMaxTokens { get; set; } = 1200;
    public int GenerationMaxTokens { get; set; } = 6000;
    public int TimeoutSeconds { get; set; } = 60;
    public AIResumePackageOption[] Packages { get; set; } =
    [
        new("AI_RESUME_1", "1 Resume", 19m, 1, false),
        new("AI_RESUME_5", "5 Resumes", 79m, 5, false),
        new("AI_RESUME_10", "10 Resumes", 129m, 10, true),
        new("AI_RESUME_25", "25 Resumes", 249m, 25, false)
    ];
    public bool IsValid() => Provider == "Claude" && Model is not null && Model.StartsWith("claude-", StringComparison.Ordinal) &&
        Model.Length <= 100 && Model.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '.') &&
        AnalysisMaxTokens is >= 256 and <= 2048 && GenerationMaxTokens is >= 1024 and <= 8192 && TimeoutSeconds is >= 5 and <= 120 &&
        Packages is { Length: 4 } && Packages.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() == 4 &&
        Packages.All(x => !string.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= 80 && x.Price is > 0 and <= 100000m) &&
        Packages.SingleOrDefault(x => x.Code == "AI_RESUME_1") is { Credits: 1, IsPopular: false } &&
        Packages.SingleOrDefault(x => x.Code == "AI_RESUME_5") is { Credits: 5, IsPopular: false } &&
        Packages.SingleOrDefault(x => x.Code == "AI_RESUME_10") is { Credits: 10, IsPopular: true } &&
        Packages.SingleOrDefault(x => x.Code == "AI_RESUME_25") is { Credits: 25, IsPopular: false } &&
        Packages.All(x => decimal.Round(x.Price, 2) == x.Price);
}

public sealed record AIResumePackageOption(string Code, string Name, decimal Price, int Credits, bool IsPopular);

public sealed class AIResumeProviderException(string code, bool retryable = false) : Exception("The resume provider could not complete the request.")
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
