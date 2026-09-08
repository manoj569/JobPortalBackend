using System.Globalization;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.AIApply;

public sealed partial class DeterministicApplicationQuestionClassifier(IApplicationQuestionMatcher normalizer) : IApplicationQuestionClassifier
{
    private static readonly IReadOnlyDictionary<string, string[]> Skills = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["csharp"] = ["c#", "c sharp", "csharp"], ["aspnetcore"] = ["asp.net core", "asp net core", "aspnetcore"],
        ["dotnetcore"] = [".net core", "dot net core", "dotnet core"], ["dotnet"] = [".net", "dot net", "dotnet"],
        ["sqlserver"] = ["sql server", "ms sql", "mssql", "microsoft sql server"], ["entityframework"] = ["entity framework", "ef core"],
        ["javascript"] = ["javascript", "java script"], ["typescript"] = ["typescript", "type script"], ["webapi"] = ["web api"],
        ["java"] = ["java"],
        ["react"] = ["react", "reactjs"], ["angular"] = ["angular"], ["azure"] = ["azure"], ["aws"] = ["aws"],
        ["docker"] = ["docker"], ["kubernetes"] = ["kubernetes", "k8s"], ["kafka"] = ["kafka"], ["rabbitmq"] = ["rabbitmq", "rabbit mq"],
        ["microservices"] = ["microservices", "micro services"], ["redis"] = ["redis"], ["dapper"] = ["dapper"], ["restapi"] = ["rest api", "restful api"]
    };

    public ApplicationQuestionAnalysis Analyze(DetectedApplicationQuestion question)
    {
        var normalized = normalizer.Normalize(question.Text);
        var detectedSkill = FindSkill(question.Text);
        var category = detectedSkill is not null && Has(normalized, "experience", "worked", "used", "proficiency", "how long") ? ApplicationQuestionCategory.TechnicalExperience : Category(normalized);
        var subject = category == ApplicationQuestionCategory.TechnicalExperience ? detectedSkill :
            category is ApplicationQuestionCategory.CompanyMotivation or ApplicationQuestionCategory.RoleMotivation ? ScopeSubject(normalized) : null;
        var canonical = Canonical(category, subject);
        var risk = Risk(category);
        var answerType = AnswerType(question.Type, normalized, question.Options);
        var scope = category switch { ApplicationQuestionCategory.TechnicalExperience => ApplicationAnswerScope.Skill, ApplicationQuestionCategory.CompanyMotivation => ApplicationAnswerScope.Company, ApplicationQuestionCategory.RoleMotivation => ApplicationAnswerScope.Role, _ => ApplicationAnswerScope.Global };
        return new(question.Text, normalized, category, risk, canonical, subject, answerType, question.Options ?? [], question.Required, canonical ?? normalized, scope);
    }

    private static ApplicationQuestionCategory Category(string q)
    {
        if (Has(q, "visa", "sponsor")) return ApplicationQuestionCategory.VisaSponsorship;
        if (Has(q, "authorized to work", "authorised to work", "work authorization")) return ApplicationQuestionCategory.WorkAuthorization;
        if (Has(q, "non compete", "noncompete")) return ApplicationQuestionCategory.NonCompete;
        if (Has(q, "criminal", "conviction")) return ApplicationQuestionCategory.CriminalDeclaration;
        if (Has(q, "security clearance", "clearance")) return ApplicationQuestionCategory.SecurityClearance;
        if (Has(q, "disability", "disabled")) return ApplicationQuestionCategory.Disability;
        if (Has(q, "veteran")) return ApplicationQuestionCategory.VeteranStatus;
        if (Has(q, "race", "ethnicity")) return ApplicationQuestionCategory.RaceEthnicity;
        if (Has(q, "gender")) return ApplicationQuestionCategory.Gender;
        if (Has(q, "conflict of interest")) return ApplicationQuestionCategory.ConflictOfInterest;
        if (Has(q, "consent", "agree to", "certify", "declaration")) return ApplicationQuestionCategory.Consent;
        if (Has(q, "linkedin")) return ApplicationQuestionCategory.LinkedIn;
        if (Has(q, "github")) return ApplicationQuestionCategory.GitHub;
        if (Has(q, "portfolio", "website")) return ApplicationQuestionCategory.Portfolio;
        if (Has(q, "email", "phone", "mobile")) return ApplicationQuestionCategory.ContactInformation;
        if (Has(q, "location", "city", "country", "address")) return ApplicationQuestionCategory.Location;
        if (Has(q, "notice period")) return ApplicationQuestionCategory.NoticePeriod;
        if (Has(q, "salary", "compensation", "ctc")) return ApplicationQuestionCategory.Salary;
        if (Has(q, "relocat")) return ApplicationQuestionCategory.Relocation;
        if (Has(q, "why") && Has(q, "company", "organisation", "organization", "join us")) return ApplicationQuestionCategory.CompanyMotivation;
        if (Has(q, "why") && Has(q, "role", "position", "job")) return ApplicationQuestionCategory.RoleMotivation;
        if (Has(q, "career goal")) return ApplicationQuestionCategory.CareerGoals;
        if (Has(q, "experience", "worked", "used", "proficiency") && FindSkill(q) is not null) return ApplicationQuestionCategory.TechnicalExperience;
        if (Has(q, "total experience", "years of experience", "professional experience")) return ApplicationQuestionCategory.GeneralExperience;
        if (Has(q, "education", "degree", "qualification", "university")) return ApplicationQuestionCategory.Education;
        if (Has(q, "employer", "employment", "current company")) return ApplicationQuestionCategory.EmploymentHistory;
        if (Has(q, "available", "start date", "join")) return ApplicationQuestionCategory.Availability;
        if (Has(q, "resume", "cv")) return ApplicationQuestionCategory.Resume;
        if (Has(q, "name")) return ApplicationQuestionCategory.PersonalInformation;
        return ApplicationQuestionCategory.Unknown;
    }

    private static string? Canonical(ApplicationQuestionCategory category, string? subject) => category switch
    {
        ApplicationQuestionCategory.TechnicalExperience when subject is not null => $"experience.skill.{subject}", ApplicationQuestionCategory.GeneralExperience => "experience.total",
        ApplicationQuestionCategory.Salary => "salary.expected", ApplicationQuestionCategory.NoticePeriod => "notice_period", ApplicationQuestionCategory.Relocation => "relocation",
        ApplicationQuestionCategory.WorkAuthorization => "work_authorization", ApplicationQuestionCategory.VisaSponsorship => "visa_sponsorship", ApplicationQuestionCategory.Location => "location.current",
        ApplicationQuestionCategory.LinkedIn => "linkedin.url", ApplicationQuestionCategory.GitHub => "github.url", ApplicationQuestionCategory.Portfolio => "portfolio.url", _ => null
    };
    private static ApplicationQuestionRisk Risk(ApplicationQuestionCategory c) => c switch
    {
        ApplicationQuestionCategory.LegalDeclaration or ApplicationQuestionCategory.ConflictOfInterest or ApplicationQuestionCategory.NonCompete or ApplicationQuestionCategory.CriminalDeclaration or ApplicationQuestionCategory.SecurityClearance or ApplicationQuestionCategory.Consent => ApplicationQuestionRisk.Legal,
        ApplicationQuestionCategory.ProtectedDemographic or ApplicationQuestionCategory.Disability or ApplicationQuestionCategory.Gender or ApplicationQuestionCategory.RaceEthnicity or ApplicationQuestionCategory.VeteranStatus => ApplicationQuestionRisk.Sensitive,
        ApplicationQuestionCategory.WorkAuthorization or ApplicationQuestionCategory.VisaSponsorship => ApplicationQuestionRisk.HighRisk,
        ApplicationQuestionCategory.Salary or ApplicationQuestionCategory.Relocation or ApplicationQuestionCategory.Availability => ApplicationQuestionRisk.ModerateRisk, _ => ApplicationQuestionRisk.LowRisk
    };
    private static ApplicationAnswerType AnswerType(string type, string q, IReadOnlyList<string>? options) => options is { Count: > 0 } ? ApplicationAnswerType.SingleSelect : type.Equals("textarea", StringComparison.OrdinalIgnoreCase) ? ApplicationAnswerType.LongText : Has(q, "how many years", "how long", "months of experience") ? ApplicationAnswerType.Duration : Has(q, "yes or no") ? ApplicationAnswerType.Boolean : ApplicationAnswerType.ShortText;
    private static string? FindSkill(string text) { foreach (var pair in Skills) if (pair.Value.Any(x => Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(x)}(?![a-z0-9])", RegexOptions.IgnoreCase))) return pair.Key; return null; }
    private static string? ScopeSubject(string text) { var match = Regex.Match(text, @"(?:at|for|join)\s+([a-z0-9 ]+)$", RegexOptions.IgnoreCase); return match.Success ? match.Groups[1].Value.Trim() : text; }
    private static bool Has(string value, params string[] terms) => terms.Any(value.Contains);
}

public sealed class DeterministicApplicationSemanticMatcher(IApplicationQuestionClassifier classifier, IOptions<AIApplyOptions> options) : IApplicationSemanticMatcher
{
    public Task<SemanticMatchResult> FindBestMatchAsync(ApplicationQuestionAnalysis question, IReadOnlyCollection<UserApplicationAnswer> candidates, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var best = candidates.Select(x => (Answer: x, Analysis: classifier.Analyze(new(x.Question, "Text"))))
            .Where(x => x.Analysis.Category == question.Category && (question.Subject is null || x.Analysis.Subject == question.Subject) && x.Analysis.Scope == question.Scope)
            .Select(x => (x.Answer, Score: Similarity(question.NormalizedQuestion, x.Analysis.NormalizedQuestion))).OrderByDescending(x => x.Score).FirstOrDefault();
        return Task.FromResult(best.Answer is null ? new SemanticMatchResult(null, 0, 0, "no_candidate") : new(best.Answer.Id, best.Score, best.Score, best.Score >= options.Value.AI.SemanticMatchThreshold ? "token_similarity" : "below_threshold"));
    }
    private static decimal Similarity(string a, string b) { var left = a.Split(' ').ToHashSet(); var right = b.Split(' ').ToHashSet(); var union = left.Union(right).Count(); return union == 0 ? 0 : (decimal)left.Intersect(right).Count() / union; }
}

public sealed class AIApplicationAnswerPolicy : IAIApplicationAnswerPolicy
{
    public AIAnswerPolicyDecision Evaluate(ApplicationQuestionAnalysis question, bool allowAutoUse)
    {
        var allowed = question.Category is ApplicationQuestionCategory.RoleMotivation or ApplicationQuestionCategory.CompanyMotivation or ApplicationQuestionCategory.CareerGoals or ApplicationQuestionCategory.Behavioral;
        if (question.RiskLevel is not ApplicationQuestionRisk.LowRisk) allowed = false;
        var canGenerate = allowed && allowAutoUse;
        return new(canGenerate, false, true, canGenerate ? "low_risk_subjective" : "generation_prohibited");
    }
}

public sealed class DisabledAIApplyLanguageModel : IAIApplyLanguageModel
{
    public Task<AIApplyLanguageModelResult?> GenerateAsync(AIApplyLanguageModelRequest request, CancellationToken ct) => Task.FromResult<AIApplyLanguageModelResult?>(null);
}

public sealed class ApplicationAnswerResolver(IAIApplyRepository repository, IApplicationQuestionClassifier classifier,
    IApplicationSemanticMatcher semantic, IAIApplicationAnswerPolicy policy, IAIApplyLanguageModel languageModel,
    IOptions<AIApplyOptions> options) : IApplicationAnswerResolver
{
    public async Task<ApplicationAnswerResolution> ResolveAsync(Guid userId, DetectedApplicationQuestion question, bool allowGeneratedAnswers, CancellationToken ct)
    {
        var q = classifier.Analyze(question); var all = (await repository.GetAnswersAsync(userId, ct)).Where(x => x.IsActive).ToList();
        var verified = all.Where(x => x.IsVerified && x.Source == AIApplyAnswerSource.UserVerified).ToList();
        var canonical = verified.FirstOrDefault(x => classifier.Analyze(new(x.Question, "Text")).CanonicalKey == q.CanonicalKey && q.CanonicalKey is not null);
        if (canonical is not null) return Use(canonical, q, ApplicationAnswerResolutionSource.UserVerifiedCanonical, 1, "verified_canonical");
        var exact = verified.FirstOrDefault(x => x.NormalizedQuestion == q.NormalizedQuestion);
        if (exact is not null) return Use(exact, q, ApplicationAnswerResolutionSource.UserVerifiedExact, 1, "verified_exact");
        if (options.Value.AI.Enabled && options.Value.AI.SemanticMatchingEnabled && q.RiskLevel is ApplicationQuestionRisk.LowRisk or ApplicationQuestionRisk.ModerateRisk)
        {
            var candidates = verified.Where(x => { var candidate = classifier.Analyze(new(x.Question, "Text")); return candidate.Category == q.Category && candidate.Scope == q.Scope && (q.Subject is null || candidate.Subject == q.Subject); }).Take(Math.Clamp(options.Value.AI.MaxCandidateAnswers, 1, 100)).ToList();
            if (candidates.Count == 0) goto ProfileDerivation;
            var match = await semantic.FindBestMatchAsync(q, candidates, ct);
            if (match.MatchedAnswerId is Guid id && match.Confidence >= options.Value.AI.SemanticMatchThreshold)
            {
                var answer = candidates.Single(x => x.Id == id);
                var auto = match.Confidence >= options.Value.AI.AutoUseThreshold;
                return new(true, MapOption(answer.Answer, q.AvailableOptions), ApplicationAnswerResolutionSource.UserVerifiedSemantic, match.Confidence, q.CanonicalKey, answer.Question, !auto, auto, match.Reason);
            }
        }
        ProfileDerivation:
        var derived = await DeriveAsync(userId, q, ct); if (derived is not null) return new(true, MapOption(derived, q.AvailableOptions), ApplicationAnswerResolutionSource.ProfileDerived, 1, q.CanonicalKey, null, false, true, "profile_fact");
        var decision = policy.Evaluate(q, allowGeneratedAnswers);
        if (options.Value.AI.Enabled && options.Value.AI.AnswerGenerationEnabled && decision.CanGenerate)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.AI.RequestTimeoutSeconds, 1, 60)));
                var generated = await languageModel.GenerateAsync(new(q.OriginalQuestion, q.Category, q.AnswerType, Math.Clamp(options.Value.AI.MaxGeneratedAnswerCharacters, 50, 4000), new Dictionary<string, string>()), timeout.Token);
                if (generated is not null && generated.Confidence >= options.Value.AI.GeneratedAnswerThreshold && generated.Answer.Length <= options.Value.AI.MaxGeneratedAnswerCharacters)
                    return new(true, generated.Answer, ApplicationAnswerResolutionSource.AIGenerated, generated.Confidence, q.CanonicalKey, null, true, decision.CanAutoSubmit && !decision.RequiresConfirmation, "generated_subjective", new(1, generated.InputTokens, generated.OutputTokens, 0, 0));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception) { }
        }
        return new(false, null, ApplicationAnswerResolutionSource.None, 0, q.CanonicalKey, null, true, false, "ask_user");
    }

    private static ApplicationAnswerResolution Use(UserApplicationAnswer a, ApplicationQuestionAnalysis q, ApplicationAnswerResolutionSource source, decimal confidence, string reason) => new(true, MapOption(a.Answer, q.AvailableOptions), source, confidence, q.CanonicalKey, a.Question, false, true, reason);
    private async Task<string?> DeriveAsync(Guid userId, ApplicationQuestionAnalysis q, CancellationToken ct)
    {
        var user = await repository.GetUserProfileAsync(userId, ct); var profile = await repository.GetProfileAsync(userId, ct); if (user is null) return null;
        return q.Category switch { ApplicationQuestionCategory.ContactInformation when q.NormalizedQuestion.Contains("email") => user.Email, ApplicationQuestionCategory.ContactInformation => user.PhoneNumber, ApplicationQuestionCategory.Location => user.Location ?? user.CurrentCity, ApplicationQuestionCategory.LinkedIn => user.LinkedInUrl, ApplicationQuestionCategory.GitHub => profile?.GitHubUrl, ApplicationQuestionCategory.Portfolio => user.PortfolioUrl, ApplicationQuestionCategory.GeneralExperience => user.YearsOfExperience?.ToString(CultureInfo.InvariantCulture), ApplicationQuestionCategory.NoticePeriod => user.AvailabilityToJoin?.ToString(), ApplicationQuestionCategory.Salary when q.NormalizedQuestion.Contains("current") => user.CurrentAnnualSalary?.ToString(CultureInfo.InvariantCulture), ApplicationQuestionCategory.Salary => user.ExpectedAnnualSalary?.ToString(CultureInfo.InvariantCulture), ApplicationQuestionCategory.Relocation => profile?.WillingToRelocate?.ToString(), ApplicationQuestionCategory.WorkAuthorization => profile?.WorkAuthorization, ApplicationQuestionCategory.VisaSponsorship => profile?.VisaSponsorshipPreference, _ => null };
    }
    private static string MapOption(string answer, IReadOnlyList<string> options)
    {
        if (options.Count == 0) return answer; var exact = options.FirstOrDefault(x => string.Equals(x.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase)); if (exact is not null) return exact;
        var yes = answer.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "agree"; var no = answer.Trim().ToLowerInvariant() is "no" or "n" or "false"; if (yes || no) return options.FirstOrDefault(x => string.Equals(x, yes ? "yes" : "no", StringComparison.OrdinalIgnoreCase)) ?? answer;
        var number = Regex.Match(answer, @"\d+(?:\.\d+)?"); if (number.Success && decimal.TryParse(number.Value, CultureInfo.InvariantCulture, out var years)) foreach (var option in options) { var range = Regex.Matches(option, @"\d+(?:\.\d+)?").Select(x => decimal.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray(); if (range.Length == 2 && years >= range[0] && years <= range[1]) return option; if (range.Length == 1 && option.Contains('+') && years >= range[0]) return option; }
        return answer;
    }
}
