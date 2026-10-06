using System.Text.Json;

namespace JobPortal.Application.Features.AIResume;

public static class AIResumeContentGuard
{
    public static TailoredResumeValidation Validate(TailoredResumeContent source, TailoredResumeContent generated,
        Action<ResumeGroundingDiagnostic>? diagnostic = null)
    {
        var errors = new List<string>();
        void Reject(string code, string path, string category, string reason)
        {
            errors.Add(code);
            Report(source, generated, path, category, reason, diagnostic);
        }
        if (!WellFormed(source) || !WellFormed(generated))
        {
            diagnostic?.Invoke(new("content", "structure", "invalid_content", [], []));
            return new(false, ["invalid_content"]);
        }
        if (JsonSerializer.Serialize(source.Contact) != JsonSerializer.Serialize(generated.Contact)) Reject("contact_changed", "contact", "identity", "contact_changed");
        if (!Grounded(source, generated, "summary", generated.ProfessionalSummary, source.ProfessionalSummary, diagnostic: diagnostic)) errors.Add("unsupported_summary");
        for (var i = 0; i < generated.Skills.Length; i++)
        {
            if (!Subset([generated.Skills[i]], source.Skills)) Reject("unsupported_skill", $"skills/{i}", "technology_skill", "not_in_source");
            else if (!ValidSkillEvidence(source, generated, i, diagnostic)) errors.Add("unsupported_skill");
        }
        for (var index = 0; index < generated.AdditionalInfo.Length; index++)
            if (!Grounded(source, generated, $"additionalInfo/{index}", generated.AdditionalInfo[index],
                source.AdditionalInfo.Contains(generated.AdditionalInfo[index], StringComparer.Ordinal) ? generated.AdditionalInfo[index] : "", "additional", diagnostic))
                errors.Add("unsupported_additional_info");
        for (var index = 0; index < generated.Experience.Length; index++)
        {
            var item = generated.Experience[index];
            var original = source.Experience.SingleOrDefault(x => x.Employer == item.Employer && x.Role == item.Role && x.StartDate == item.StartDate && x.EndDate == item.EndDate);
            if (original is null)
            {
                var path = $"experience/{index}";
                if (!source.Experience.Any(x => x.Employer == item.Employer)) Reject("unsupported_experience", path + "/employer", "employer", "not_in_source");
                if (!source.Experience.Any(x => x.Employer == item.Employer && x.Role == item.Role)) Reject("unsupported_experience", path + "/role", "role", "role_not_in_source_employer");
                if (source.Experience.Any(x => x.Employer == item.Employer && x.Role == item.Role))
                {
                    if (!source.Experience.Any(x => x.Employer == item.Employer && x.Role == item.Role && x.StartDate == item.StartDate))
                        Reject("unsupported_experience", path + "/startDate", "date", "not_in_source_role");
                    if (!source.Experience.Any(x => x.Employer == item.Employer && x.Role == item.Role && x.EndDate == item.EndDate))
                        Reject("unsupported_experience", path + "/endDate", "date", "not_in_source_role");
                }
                Reject("unsupported_experience", path, "experience_identity", "employer_role_dates_tuple_not_in_source");
                continue;
            }
            var sourceIndex = Array.IndexOf(source.Experience, original);
            for (var bullet = 0; bullet < item.Bullets.Length; bullet++)
                if (!Grounded(source, generated, $"experience/{index}/bullets/{bullet}", item.Bullets[bullet],
                    string.Join('\n', original.Bullets), $"experience/{sourceIndex}", diagnostic)) errors.Add("unsupported_experience");
        }
        for (var index = 0; index < generated.Projects.Length; index++)
        {
            var item = generated.Projects[index];
            var original = source.Projects.SingleOrDefault(x => x.Name == item.Name);
            if (original is null) { Reject("unsupported_project", $"projects/{index}/name", "project", "not_in_source"); continue; }
            if (!Subset(item.Technologies, original.Technologies))
            {
                for (var i = 0; i < item.Technologies.Length; i++)
                    if (!Subset([item.Technologies[i]], original.Technologies)) Reject("unsupported_project", $"projects/{index}/technologies/{i}", "technology", "not_in_source_project");
                continue;
            }
            var sourceIndex = Array.IndexOf(source.Projects, original);
            for (var bullet = 0; bullet < item.Bullets.Length; bullet++)
                if (!Grounded(source, generated, $"projects/{index}/bullets/{bullet}", item.Bullets[bullet],
                    string.Join('\n', original.Bullets), $"projects/{sourceIndex}", diagnostic)) errors.Add("unsupported_project");
        }
        for (var i = 0; i < generated.Education.Length; i++)
            if (!source.Education.Contains(generated.Education[i]))
            {
                var item = generated.Education[i];
                var matches = source.Education.Where(x => x.Institution == item.Institution).ToArray();
                if (matches.Length == 0) Reject("unsupported_education", $"education/{i}/institution", "education", "not_in_source");
                else
                {
                    if (!matches.Any(x => x.Qualification == item.Qualification)) Reject("unsupported_education", $"education/{i}/qualification", "degree", "not_in_source_institution");
                    if (!matches.Any(x => x.StartDate == item.StartDate)) Reject("unsupported_education", $"education/{i}/startDate", "date", "not_in_source_institution");
                    if (!matches.Any(x => x.EndDate == item.EndDate)) Reject("unsupported_education", $"education/{i}/endDate", "date", "not_in_source_institution");
                }
                Reject("unsupported_education", $"education/{i}", "education", "education_record_not_in_source");
            }
        for (var i = 0; i < generated.Certifications.Length; i++)
            if (!source.Certifications.Contains(generated.Certifications[i]))
            {
                var item = generated.Certifications[i];
                var matches = source.Certifications.Where(x => x.Name == item.Name).ToArray();
                if (matches.Length == 0) Reject("unsupported_certification", $"certifications/{i}/name", "certification", "not_in_source");
                else
                {
                    if (!matches.Any(x => x.Issuer == item.Issuer)) Reject("unsupported_certification", $"certifications/{i}/issuer", "certification", "not_in_source_certificate");
                    if (!matches.Any(x => x.Date == item.Date)) Reject("unsupported_certification", $"certifications/{i}/date", "date", "not_in_source_certificate");
                }
                Reject("unsupported_certification", $"certifications/{i}", "certification", "certification_record_not_in_source");
            }
        return new(errors.Count == 0, errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    public static bool ValidAnalysis(ResumeAnalysis analysis) => AIResumeAnalysisValidator.Rejections(analysis).Length == 0;

    public static bool WellFormed(TailoredResumeContent content) => content is not null && content.Contact is not null &&
        new[] { content.Contact.Name, content.Contact.Email, content.Contact.Phone, content.Contact.Location, content.ProfessionalSummary }
            .All(x => x is not null && x.Length <= 2000 && !x.Contains('\0')) &&
        Strings(content.Contact.Links, 5, 500) && Strings(content.Skills, 100, 100) && Strings(content.AdditionalInfo, 20, 1000) &&
        content.Experience is { Length: <= 30 } && content.Experience.All(x => x is not null &&
            Strings([x.Employer, x.Role, x.StartDate, x.EndDate], 4, 250) && Strings(x.Bullets, 20, 1000)) &&
        content.Experience.Select(x => (x.Employer, x.Role, x.StartDate, x.EndDate)).Distinct().Count() == content.Experience.Length &&
        content.Projects is { Length: <= 30 } && content.Projects.All(x => x is not null && Strings([x.Name], 1, 250) && Strings(x.Technologies, 50, 100) && Strings(x.Bullets, 20, 1000)) &&
        content.Projects.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == content.Projects.Length &&
        content.Education is { Length: <= 20 } && content.Education.All(x => x is not null && Strings([x.Institution, x.Qualification, x.StartDate, x.EndDate], 4, 250)) &&
        content.Certifications is { Length: <= 30 } && content.Certifications.All(x => x is not null && Strings([x.Name, x.Issuer, x.Date], 3, 250)) &&
        !string.IsNullOrWhiteSpace(content.Contact.Name) &&
        (!string.IsNullOrWhiteSpace(content.ProfessionalSummary) || content.Skills.Length > 0 || content.Experience.Length > 0 ||
            content.Projects.Length > 0 || content.Education.Length > 0 || content.Certifications.Length > 0);

    private static bool Strings(string[]? values, int count, int length) => values is not null && values.Length <= count &&
        values.All(x => x is not null && x.Length <= length && !x.Contains('\0'));
    private static bool Subset(string[] selected, string[] source) => selected.All(x => source.Contains(x, StringComparer.Ordinal));
    private static bool SupportedText(string selected, string source) => selected.Length == 0 || string.Equals(source, selected, StringComparison.Ordinal);
    private static bool Grounded(TailoredResumeContent source, TailoredResumeContent generated, string path, string text, string original, string? scope = null,
        Action<ResumeGroundingDiagnostic>? diagnostic = null)
    {
        var pathReferences = References(generated, path).ToArray();
        // Verbatim source text is already evidence. Supplied provenance must still be valid, even for copies.
        if (pathReferences.Length == 0 && (SupportedText(text, original) || original.Split('\n').Contains(text, StringComparer.Ordinal))) return true;
        var references = pathReferences.Where(x => x.Text == text).ToArray();
        string? reason = references.Length != 1 ? pathReferences.Length == 0 ? "missing_evidence_path" :
            references.Length == 0 ? "evidence_text_mismatch" : "duplicate_matching_evidence" :
            references[0].SourceEvidenceIds is not { Length: >= 1 and <= 10 } ? "invalid_evidence_id_count" : null;
        if (reason is not null) { Report(source, generated, path, "provenance", reason, diagnostic); return false; }
        var catalog = ResumeEvidenceCatalog.Create(source);
        var facts = references[0].SourceEvidenceIds.Select(id => catalog.SingleOrDefault(x => x.Id == id)).ToArray();
        reason = facts.Any(x => x is null) ? "unknown_evidence_id" :
            facts.Any(x => scope is not null && x!.Scope != scope) ? "evidence_scope_mismatch" : null;
        if (reason is not null) { Report(source, generated, path, "provenance", reason, diagnostic); return false; }
        var category = ResumeClaimGuard.RejectionCategory(text, facts.Select(x => x!.Text).ToArray(), out var claimOffset);
        if (category is null) return true;
        Report(source, generated, path, category, category == "metric" ? "metric_value_or_context_not_supported" :
            category == "negation" ? "source_negation_removed" : "claim_not_supported_by_cited_evidence", diagnostic, claimOffset);
        return false;
    }

    private static IEnumerable<ResumeProvenance> References(TailoredResumeContent generated, string path)
    {
        var legacy = (generated.Evidence ?? []).Where(x => x is not null && x.Path == path).ToArray();
        foreach (var item in legacy) yield return item;
        // Identical old/new summary references are one assertion. Conflicting references still reject.
        if (path == "summary" && generated.SummarySourceEvidenceIds is { Length: > 0 } ids &&
            !legacy.Any(x => x.Text == generated.ProfessionalSummary && x.SourceEvidenceIds is not null &&
                x.SourceEvidenceIds.SequenceEqual(ids, StringComparer.Ordinal)))
            yield return new("summary", generated.ProfessionalSummary, generated.SummarySourceEvidenceIds);
    }

    private static bool ValidSkillEvidence(TailoredResumeContent source, TailoredResumeContent generated, int index,
        Action<ResumeGroundingDiagnostic>? diagnostic)
    {
        var path = $"skills/{index}";
        var references = References(generated, path).ToArray();
        // Existing verbatim snapshots/editor content need no new provenance. Never ignore incorrect supplied IDs.
        if (references.Length == 0) return true;
        var skill = generated.Skills[index];
        if (references.Length == 1 && references[0].Text == skill && references[0].SourceEvidenceIds is { Length: 1 } ids &&
            ResumeEvidenceCatalog.Create(source).Any(x => x.Id == ids[0] && x.Scope == "skills" && x.Text == skill)) return true;
        Report(source, generated, path, "technology_skill", "evidence_not_exact_source_skill", diagnostic);
        return false;
    }

    private static void Report(TailoredResumeContent source, TailoredResumeContent generated, string path, string category,
        string reason, Action<ResumeGroundingDiagnostic>? diagnostic, int? claimOffset = null)
    {
        if (diagnostic is null) return;
        var catalog = ResumeEvidenceCatalog.Create(source);
        var ids = References(generated, path)
            .SelectMany(x => x.SourceEvidenceIds ?? []).Take(20).ToArray();
        // Only catalog-shaped IDs may leave the guard. Arbitrary model strings could contain PII/secrets.
        var safeIds = ids.Select(id => System.Text.RegularExpressions.Regex.IsMatch(id ?? "", @"^SRC-(?:CONTACT|SUMMARY|SKILL|EXP|PROJECT|EDU|CERT|ADDITIONAL)-[0-9]{3}(?:-(?:BULLET|IDENTITY|TECH)-[0-9]{3})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            ? id! : "[invalid-id-redacted]").ToArray();
        diagnostic(new(path, category, reason, safeIds, ids.Select(id => catalog.Any(x => x.Id == id)).ToArray(), claimOffset));
    }
}

public sealed record ResumeGroundingDiagnostic(string Path, string Category, string Reason, string[] EvidenceIds, bool[] EvidenceExists, int? ClaimOffset = null);
