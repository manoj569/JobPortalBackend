using JobPortal.Application.Features.AIResume;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AIResumeContentGuardTests
{
    [Theory]
    [InlineData("C#", "SRC-SKILL-001", true)]
    [InlineData("C#", "SRC-SKILL-002", false)]
    [InlineData("C#", "SRC-EXP-001-BULLET-001", false)]
    [InlineData("C# / .NET", "SRC-SKILL-001", false)]
    [InlineData("C# & .NET", "SRC-SKILL-001", false)]
    [InlineData("C# + .NET", "SRC-SKILL-001", false)]
    [InlineData("C Sharp", "SRC-SKILL-001", false)]
    [InlineData("Kubernetes", "SRC-SKILL-001", false)]
    [InlineData(" c# ", "SRC-SKILL-001", false)]
    public void GeneratedSkillsRemainLiteralAndSuppliedIdMustIdentifyThatExactSkill(string skill, string id, bool valid)
    {
        var source = ClaudeAIResumeProviderTests.Source with { Skills = ["C#", ".NET"] };
        var generated = source with { Skills = [skill], Evidence = [new("skills/0", skill, [id])] };
        Assert.Equal(valid, AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Fact]
    public void CombiningTwoExistingSkillIdsDoesNotAuthorizeACombinedLabelOrTheWrongLiteral()
    {
        var source = ClaudeAIResumeProviderTests.Source with { Skills = ["C#", ".NET"] };
        foreach (var skill in new[] { "C# / .NET", "C#" })
            Assert.False(AIResumeContentGuard.Validate(source, source with
            {
                Skills = [skill], Evidence = [new("skills/0", skill, ["SRC-SKILL-001", "SRC-SKILL-002"])]
            }).IsValid);
    }

    [Theory]
    [InlineData("Experienced backend developer with C# experience.", "SRC-SUMMARY-001", true)]
    [InlineData("Experienced backend developer with C# experience.", "", false)]
    [InlineData("Experienced backend developer with Kubernetes experience.", "SRC-SUMMARY-001", false)]
    [InlineData("Experienced backend developer with C# experience.", "SRC-PROJECT-001-IDENTITY-001", false)]
    [InlineData("Experienced backend developer with C# experience.", "SRC-SUMMARY-999", false)]
    public void ExplicitSummaryIdsStillRequireSupportedFacts(string text, string id, bool valid)
    {
        var source = ClaudeAIResumeProviderTests.Source;
        var generated = source with { ProfessionalSummary = text, SummarySourceEvidenceIds = id.Length == 0 ? [] : [id] };
        Assert.Equal(valid, AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Theory]
    [InlineData("SRC-SUMMARY-001", true)]
    [InlineData("SRC-SKILL-001", false)]
    public void LegacyAndExplicitSummaryProvenanceMustAgree(string legacyId, bool valid)
    {
        var source = ClaudeAIResumeProviderTests.Source;
        const string text = "Experienced backend developer with C# experience.";
        var generated = source with { ProfessionalSummary = text, SummarySourceEvidenceIds = ["SRC-SUMMARY-001"],
            Evidence = [new("summary", text, [legacyId])] };
        Assert.Equal(valid, AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Theory]
    [InlineData("Led")]
    [InlineData("Managed")]
    [InlineData("Owned")]
    [InlineData("Spearheaded")]
    [InlineData("Mentored")]
    [InlineData("Supervised")]
    [InlineData("Architected")]
    [InlineData("Directed")]
    [InlineData("Drove")]
    public void AdditionalInfoCannotUpgradeResponsibilityUnlessCitedSourceHasThatClaim(string verb)
    {
        var source = ClaudeAIResumeProviderTests.Source with { AdditionalInfo = ["Built C# APIs."] };
        var text = verb + " C# APIs.";
        var generated = source with { AdditionalInfo = [text], Evidence = [new("additionalInfo/0", text, ["SRC-ADDITIONAL-001"])] };
        var diagnostics = new List<ResumeGroundingDiagnostic>();
        Assert.False(AIResumeContentGuard.Validate(source, generated, diagnostics.Add).IsValid);
        Assert.Equal("leadership", Assert.Single(diagnostics).Category);
        source = source with { AdditionalInfo = [verb + " reliable C# APIs."] };
        Assert.True(AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Fact]
    public void LeadershipMustMatchAWholeSourceClaimRatherThanSubstringOfEnabledOrUnmanaged()
    {
        Assert.False(ResumeClaimGuard.IsSupported("Led C# APIs.", "Enabled C# APIs."));
        Assert.False(ResumeClaimGuard.IsSupported("Managed C# APIs.", "Built unmanaged C# APIs."));
    }

    [Theory]
    [InlineData("Developed reliable C# APIs.", "SRC-EXP-001-BULLET-001", true)]
    [InlineData("Developed Acme C# APIs.", "SRC-EXP-001-BULLET-001", false)]
    [InlineData("Developed Kubernetes APIs.", "SRC-EXP-001-BULLET-001", false)]
    [InlineData("Reduced processing time by 41%.", "SRC-EXP-001-BULLET-002", false)]
    [InlineData("Led reliable C# APIs.", "SRC-EXP-001-BULLET-001", false)]
    [InlineData("Developed reliable C# APIs.", "SRC-EXP-002-BULLET-001", false)]
    public void ConservativeBulletRewritesKeepEntitiesMetricsLeadershipAndExperienceScope(string text, string id, bool valid)
    {
        var original = ClaudeAIResumeProviderTests.Source.Experience[0];
        var source = ClaudeAIResumeProviderTests.Source with { Experience =
        [original with { Bullets = ["Built reliable C# APIs.", "Reduced processing time by 40%."] },
         original with { Employer = "Another employer", Bullets = ["Built reliable C# APIs."] }] };
        var generated = source with { Experience = [source.Experience[0] with { Bullets = [text] }],
            Evidence = [new("experience/0/bullets/0", text, [id])] };
        Assert.Equal(valid, AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Fact]
    public void MergingUnrelatedBulletsCannotTransferMetricsToAnotherContext()
    {
        var source = ClaudeAIResumeProviderTests.Source with { Experience =
            [ClaudeAIResumeProviderTests.Source.Experience[0] with { Bullets = ["Reduced processing time by 40%.", "Built C# APIs."] }] };
        const string text = "Reduced hosting costs by 40%.";
        var generated = source with { Experience = [source.Experience[0] with { Bullets = [text] }],
            Evidence = [new("experience/0/bullets/0", text, ["SRC-EXP-001-BULLET-001", "SRC-EXP-001-BULLET-002"])] };
        Assert.False(AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Fact]
    public void VerbatimAdditionalInformationIsGroundedWithoutProvenanceEvenWhenReordered()
    {
        var source = ClaudeAIResumeProviderTests.Source with { AdditionalInfo = ["Built C# APIs.", "Reduced processing time by 40%."] };
        Assert.True(AIResumeContentGuard.Validate(source, source).IsValid);
        Assert.True(AIResumeContentGuard.Validate(source, source with { AdditionalInfo = [source.AdditionalInfo[1], source.AdditionalInfo[0]] }).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, source with { AdditionalInfo = ["Built Kubernetes APIs."] }).IsValid);
    }

    [Fact]
    public void AdditionalInformationRewriteStillRequiresExistingScopedEvidenceAndSupportedClaims()
    {
        var source = ClaudeAIResumeProviderTests.Source with { AdditionalInfo = ["Built C# APIs."] };
        const string text = "Developed C# APIs.";
        var generated = source with { AdditionalInfo = [text] };
        Assert.False(AIResumeContentGuard.Validate(source, generated).IsValid);
        generated = generated with { Evidence = [new("additionalInfo/0", text, ["SRC-ADDITIONAL-001"])] };
        Assert.True(AIResumeContentGuard.Validate(source, generated).IsValid);
        Assert.False(AIResumeContentGuard.Validate(source, generated with { Evidence = [new("additionalInfo/0", text, ["SRC-EXP-001-BULLET-001"])] }).IsValid);
        const string invention = "Led Kubernetes delivery by 40%.";
        Assert.False(AIResumeContentGuard.Validate(source, generated with
        {
            AdditionalInfo = [invention], Evidence = [new("additionalInfo/0", invention, ["SRC-ADDITIONAL-001"])]
        }).IsValid);
    }

    [Theory]
    [InlineData("Reduced processing time by 40 %.", true)]
    [InlineData("Reduced processing time by 41 %.", false)]
    [InlineData("Reduced hosting costs by 40 %.", false)]
    public void PercentSpacingPreservesMetricValueAndContext(string text, bool valid)
    {
        var source = ClaudeAIResumeProviderTests.Source with
        {
            Experience = [ClaudeAIResumeProviderTests.Source.Experience[0] with { Bullets = ["Reduced processing time by 40%."] }]
        };
        var generated = source with
        {
            Experience = [source.Experience[0] with { Bullets = [text] }],
            Evidence = [new("experience/0/bullets/0", text, ["SRC-EXP-001-BULLET-001"])]
        };
        Assert.Equal(valid, AIResumeContentGuard.Validate(source, generated).IsValid);
    }

    [Theory]
    [InlineData("SRC-EXP-999-BULLET-001", "provenance", "unknown_evidence_id", false)]
    [InlineData("SRC-PROJECT-001-BULLET-001", "provenance", "evidence_scope_mismatch", true)]
    [InlineData("SRC-EXP-001-IDENTITY-001", "provenance", "evidence_scope_mismatch", true)]
    [InlineData("SRC-EXP-001-BULLET-001", "technology", "claim_not_supported_by_cited_evidence", true)]
    [InlineData("candidate@example.invalid", "provenance", "unknown_evidence_id", false)]
    public void DiagnosticsIdentifyFieldCategoryAndEvidenceWithoutClaimText(string id, string category, string reason, bool exists)
    {
        var source = ClaudeAIResumeProviderTests.Source;
        const string text = "Built Kubernetes services.";
        var generated = source with
        {
            Experience = [source.Experience[0] with { Bullets = [text] }],
            Evidence = [new("experience/0/bullets/0", text, [id])]
        };
        var diagnostics = new List<ResumeGroundingDiagnostic>();
        Assert.False(AIResumeContentGuard.Validate(source, generated, diagnostics.Add).IsValid);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("experience/0/bullets/0", diagnostic.Path);
        Assert.Equal(category, diagnostic.Category);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(exists, Assert.Single(diagnostic.EvidenceExists));
        Assert.Equal(id.StartsWith("SRC-", StringComparison.Ordinal) ? id : "[invalid-id-redacted]", Assert.Single(diagnostic.EvidenceIds));
    }

    [Fact]
    public void ClaimDiagnosticsPreserveRejectionsAndReportOffsetsWithoutClaimText()
    {
        foreach (var (text, category) in new[]
        {
            ("Built Kubernetes APIs.", "technology"),
            ("Led C# delivery.", "leadership"),
            ("Built C# APIs by 40%.", "metric"),
            ("Built Acme APIs.", "named_entity_or_wording"),
            ("Certified C# developer.", "certification")
        })
        {
            Assert.False(ResumeClaimGuard.IsSupported(text, "Built C# APIs."));
            Assert.Equal(category, ResumeClaimGuard.RejectionCategory(text, ["Built C# APIs."], out var offset));
            Assert.NotNull(offset);
            Assert.InRange(offset.Value, 0, text.Length - 1);
        }
        Assert.Equal("negation", ResumeClaimGuard.RejectionCategory("Kubernetes experience.", ["No Kubernetes experience."]));
    }

    [Fact]
    public void MissingOrMismatchedProvenanceIsRejected()
    {
        var source = ClaudeAIResumeProviderTests.Source;
        var generated = source with { ProfessionalSummary = "Developed C# APIs." };
        var diagnostics = new List<ResumeGroundingDiagnostic>();
        Assert.False(AIResumeContentGuard.Validate(source, generated, diagnostics.Add).IsValid);
        Assert.Equal("missing_evidence_path", Assert.Single(diagnostics).Reason);
        generated = generated with { Evidence = [new("summary", "Different text", ["SRC-SUMMARY-001"])] };
        diagnostics.Clear();
        Assert.False(AIResumeContentGuard.Validate(source, generated, diagnostics.Add).IsValid);
        Assert.Equal("evidence_text_mismatch", Assert.Single(diagnostics).Reason);
        Assert.True(Assert.Single(Assert.Single(diagnostics).EvidenceExists));
    }
}
