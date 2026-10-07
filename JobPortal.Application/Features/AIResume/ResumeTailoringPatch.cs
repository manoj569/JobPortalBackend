using System.Text.RegularExpressions;

namespace JobPortal.Application.Features.AIResume;

public sealed record ResumeTextReplacement(string TargetId, string OriginalText, string ReplacementText,
    string[] SourceEvidenceIds, string[] MatchedJdTerms, string Reason);
public sealed record TailoringPatch(ResumeTextReplacement[] Replacements, string[] EmphasizedSkillEvidenceIds);
public sealed record ResumePatchDecision(ResumeTextReplacement Proposal, string Status, string? RejectionCategory,
    string? RejectionReason)
{
    public string? EditedText { get; init; }
}
public sealed record ResumePatchResult(TailoredResumeContent Content, ResumePatchDecision[] Decisions,
    string[] EmphasizedSkillEvidenceIds)
{
    public int AcceptedCount => Decisions.Count(x => x.Status is "accepted" or "edited");
}
public sealed record GenerateTailoringPatchRequest(TailoredResumeContent Source, string JobDescription,
    ResumeAnalysis Analysis, ResumeSourceEvidence[] SourceEvidence, ResumeSourceEvidence[] EditableTargets);
public sealed record AIResumeReplacementReviewRequest(int ExpectedRevision, string Action, string? ReplacementText = null);

// Source IDs remain stable across revisions; generated positions and JD terms never authorize facts.
public static partial class ResumePatchGuard
{
    public static ResumeSourceEvidence[] Targets(TailoredResumeContent source) => ResumeEvidenceCatalog.Create(source)
        .Where(x => x.Scope == "summary" && !string.IsNullOrWhiteSpace(x.Text) || x.Scope == "additional" ||
            x.Scope.StartsWith("experience/", StringComparison.Ordinal) || x.Scope.StartsWith("projects/", StringComparison.Ordinal)).ToArray();

    public static ResumePatchResult Apply(TailoredResumeContent source, TailoringPatch patch, string jd,
        IReadOnlySet<string>? editableIds = null, Action<ResumeGroundingDiagnostic>? diagnostic = null)
    {
        if (!AIResumeContentGuard.WellFormed(source) || patch.Replacements is not { Length: <= 100 } ||
            patch.EmphasizedSkillEvidenceIds is not { Length: <= 100 }) throw new AIResumeProviderException("invalid_tailoring_patch");
        var catalog = ResumeEvidenceCatalog.Create(source);
        var targets = Targets(source);
        var decisions = new List<ResumePatchDecision>();
        var duplicateIds = patch.Replacements.Where(x => x is not null).GroupBy(x => x.TargetId, StringComparer.Ordinal)
            .Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var proposal in patch.Replacements)
        {
            var target = proposal is null ? null : targets.SingleOrDefault(x => x.Id == proposal.TargetId);
            string? reason = proposal is null ? "invalid_replacement" : target is null ? "target_not_editable" :
                duplicateIds.Contains(proposal.TargetId) ? "duplicate_target" :
                editableIds is not null && !editableIds.Contains(proposal.TargetId) ? "document_target_unmapped" :
                proposal.OriginalText != target.Text ? "original_text_mismatch" :
                string.IsNullOrWhiteSpace(proposal.ReplacementText) || proposal.ReplacementText.Length > (target.Scope == "summary" ? 2000 : 1000) ||
                    proposal.ReplacementText.Any(char.IsControl) ? "invalid_replacement_text" :
                proposal.SourceEvidenceIds is not { Length: >= 1 and <= 10 } ||
                    !proposal.SourceEvidenceIds.Contains(target.Id, StringComparer.Ordinal) ? "target_evidence_required" :
                proposal.MatchedJdTerms is not { Length: <= 20 } || proposal.MatchedJdTerms.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || !jd.Contains(x, StringComparison.OrdinalIgnoreCase)) ||
                    proposal.Reason is null || proposal.Reason.Length > 500 || proposal.Reason.Any(char.IsControl) ? "invalid_alignment_metadata" : null;
            var category = "provenance";
            var facts = reason is null ? proposal!.SourceEvidenceIds.Select(id => catalog.SingleOrDefault(x => x.Id == id)).ToArray() : [];
            if (reason is null && facts.Any(x => x is null)) reason = "unknown_evidence_id";
            if (reason is null && facts.Any(x => x!.Scope != target!.Scope)) reason = "evidence_scope_mismatch";
            if (reason is null)
            {
                category = ResumeClaimGuard.RejectionCategory(proposal!.ReplacementText, facts.Select(x => x!.Text).ToArray()) ?? "";
                if (category.Length > 0) reason = "claim_not_supported_by_cited_evidence";
            }
            if (reason is null && !Metrics().Matches(target!.Text).Select(x => Metric(x.Value)).Order(StringComparer.Ordinal)
                .SequenceEqual(Metrics().Matches(proposal!.ReplacementText).Select(x => Metric(x.Value)).Order(StringComparer.Ordinal)))
            { category = "metric"; reason = "source_metrics_must_be_preserved"; }
            // Stricter than the legacy guard: new lower-case factual nouns/unknown technologies also reject.
            // Only a small, explicit grammar/verb vocabulary may differ. There is no fuzzy matching.
            if (reason is null)
            {
                var supportedWords = facts.SelectMany(x => Words().Matches(x!.Text).Select(m => m.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (Words().Matches(proposal!.ReplacementText).Any(x => !supportedWords.Contains(x.Value) && !Grammar.Contains(x.Value) &&
                    !(x.Value.Equals("web", StringComparison.OrdinalIgnoreCase) && WebApi().IsMatch(proposal.ReplacementText) &&
                        facts.Any(f => RestApi().IsMatch(f!.Text)))))
                { category = "factual_wording"; reason = "new_factual_word_not_supported"; }
            }
            var status = reason is not null ? "rejected" : NormalizedWhitespace(proposal!.ReplacementText) == NormalizedWhitespace(target!.Text) ? "original" : "accepted";
            var safeProposal = proposal ?? new("[invalid-target]", "", "", [], [], "");
            decisions.Add(new(safeProposal, status, reason is null ? null : category, reason));
            if (reason is not null)
            {
                var ids = (proposal?.SourceEvidenceIds ?? []).Take(10).ToArray();
                diagnostic?.Invoke(new(target?.Id ?? "content", category, reason,
                    ids.Select(id => catalog.Any(x => x.Id == id) ? id : "[invalid-id-redacted]").ToArray(),
                    ids.Select(id => catalog.Any(x => x.Id == id)).ToArray()));
            }
        }
        var accepted = decisions.Where(x => x.Status == "accepted").Select(x => x.Proposal).ToArray();
        var content = Materialize(source, accepted);
        // A second, complete legacy guard checks the resulting resume, never just the proposed fragments.
        if (!AIResumeContentGuard.Validate(source, content, diagnostic).IsValid) throw new AIResumeProviderException("unsupported_claims");
        var emphasized = patch.EmphasizedSkillEvidenceIds.Where(id => catalog.Any(x => x.Id == id && x.Scope == "skills"))
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(content, decisions.ToArray(), emphasized);
    }

    public static TailoredResumeContent Materialize(TailoredResumeContent source, IReadOnlyCollection<ResumeTextReplacement> replacements)
    {
        string Text(string id, string original) => replacements.SingleOrDefault(x => x.TargetId == id)?.ReplacementText ?? original;
        var catalog = ResumeEvidenceCatalog.Create(source);
        string PathFor(ResumeTextReplacement x)
        {
            var fact = catalog.Single(e => e.Id == x.TargetId);
            if (fact.Scope == "summary") return "summary";
            if (fact.Scope == "additional") return $"additionalInfo/{int.Parse(x.TargetId[^3..], System.Globalization.CultureInfo.InvariantCulture) - 1}";
            var bullet = int.Parse(x.TargetId[^3..], System.Globalization.CultureInfo.InvariantCulture) - 1;
            return $"{fact.Scope}/{"bullets"}/{bullet}";
        }
        return source with
        {
            ProfessionalSummary = Text("SRC-SUMMARY-001", source.ProfessionalSummary),
            Experience = source.Experience.Select((x, i) => x with { Bullets = x.Bullets.Select((t, j) => Text($"SRC-EXP-{i + 1:D3}-BULLET-{j + 1:D3}", t)).ToArray() }).ToArray(),
            Projects = source.Projects.Select((x, i) => x with { Bullets = x.Bullets.Select((t, j) => Text($"SRC-PROJECT-{i + 1:D3}-BULLET-{j + 1:D3}", t)).ToArray() }).ToArray(),
            AdditionalInfo = source.AdditionalInfo.Select((t, i) => Text($"SRC-ADDITIONAL-{i + 1:D3}", t)).ToArray(),
            SummarySourceEvidenceIds = [],
            Evidence = replacements.Select(x => new ResumeProvenance(PathFor(x), x.ReplacementText, x.SourceEvidenceIds)).ToArray()
        };
    }

    private static readonly HashSet<string> Grammar = new("a an the and or with using to for of in on by from through that which while as at is are was were be been being developed maintained built implemented improved optimized reduced delivered worked created supported collaborated analyzed reducing improving optimizing".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static string Metric(string value) => string.Concat(value.Where(x => !char.IsWhiteSpace(x)));
    private static string NormalizedWhitespace(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    [GeneratedRegex(@"\bREST\s+APIs?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex RestApi();
    [GeneratedRegex(@"\bweb\s+APIs?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex WebApi();
    [GeneratedRegex(@"\d+(?:[.,]\d+)*(?:\s*%)?", RegexOptions.CultureInvariant)] private static partial Regex Metrics();
    [GeneratedRegex(@"\p{L}[\p{L}\p{M}\p{N}+#-]*(?:\.[\p{L}\p{M}\p{N}]+)*", RegexOptions.CultureInvariant)] private static partial Regex Words();
}
