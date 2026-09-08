using System.Text.Json;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.AIApply;

public sealed class AIApplyAuthorizationService(IAIApplyRepository repository, TimeProvider clock,
    IOptions<AIApplyOptions> options, JobPortal.Application.Abstractions.Payments.IMembershipPlanProvider plans) : IAIApplyAuthorizationService
{
    public async Task<AIApplyAccess> GetAccessAsync(Guid userId, CancellationToken ct = default)
    {
        var membership = await repository.GetMembershipAsync(userId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var active = membership is { Status: MembershipStatus.Active } && membership.StartsAtUtc <= now && (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > now);
        var expired = membership is { Status: MembershipStatus.Expired } || membership?.EndsAtUtc <= now;
        var plan = membership is null ? null : plans.FindByName(membership.PlanName);
        var tier = plan?.AIApplyProEnabled == true ? AIApplyPlanTier.Pro : plan?.AIApplyEnabled == true ? AIApplyPlanTier.Standard : AIApplyPlanTier.None;
        var pro = active && plan?.AIApplyProEnabled == true; var ai = active && plan?.AIApplyEnabled == true;
        return new(active, expired, tier, ai, pro, plan?.Code, membership?.Status, membership?.StartsAtUtc, membership?.EndsAtUtc, active, pro, pro, pro, pro ? options.Value.ProQueuePriority : ai ? options.Value.StandardQueuePriority : 0);
    }

    public async Task<AIApplyAccess> RequireAsync(Guid userId, bool pro = false, CancellationToken ct = default)
    {
        var access = await GetAccessAsync(userId, ct);
        if (!access.CanUseAIApply || pro && !access.CanUseAIApplyPro)
            throw new AppException(pro ? "An active AI Apply Pro subscription is required." : "An active AI Apply subscription is required.", 403, pro ? "ai_apply_pro_required" : "ai_apply_subscription_required");
        return access;
    }
}

public static class AIApplyStateMachine
{
    public static void Transition(AIApplyApplication application, AIApplyRunStatus next, DateTime now)
    {
        var allowed = application.Status switch
        {
            AIApplyRunStatus.Queued => next is AIApplyRunStatus.Processing or AIApplyRunStatus.Skipped or AIApplyRunStatus.Expired or AIApplyRunStatus.Cancelled,
            AIApplyRunStatus.Processing => next is AIApplyRunStatus.WaitingForUser or AIApplyRunStatus.Submitted or AIApplyRunStatus.Failed or AIApplyRunStatus.Queued or AIApplyRunStatus.Cancelled or AIApplyRunStatus.DeadLettered or AIApplyRunStatus.NeedsReview,
            AIApplyRunStatus.WaitingForUser => next is AIApplyRunStatus.Queued or AIApplyRunStatus.Cancelled or AIApplyRunStatus.Expired,
            AIApplyRunStatus.Failed => next is AIApplyRunStatus.Queued or AIApplyRunStatus.Cancelled,
            _ => false
        };
        if (!allowed) throw new ConflictException($"Cannot transition an AI application from {application.Status} to {next}.", "invalid_application_transition");
        application.Status = next;
        if (next == AIApplyRunStatus.Processing) application.StartedAtUtc ??= now;
        if (next is AIApplyRunStatus.Submitted or AIApplyRunStatus.Failed or AIApplyRunStatus.Skipped or AIApplyRunStatus.Expired or AIApplyRunStatus.Cancelled) application.CompletedAtUtc = now;
    }
}

public sealed class DeterministicAIApplyMatcher : IAIApplyMatcher
{
    public AIApplyMatchResult Match(User user, Job job, AIApplyPreference preferences, IReadOnlyCollection<AIApplyRule> rules)
    {
        var matched = new List<string>(); var missing = new List<string>(); var reasons = new List<string>(); var skips = new List<string>();
        var wantedSkills = Read<string>(preferences.SkillsJson);
        var jobText = $"{job.Title} {job.Description} {job.Requirements}".ToUpperInvariant();
        foreach (var skill in wantedSkills) if (jobText.Contains(skill.Trim().ToUpperInvariant(), StringComparison.Ordinal)) matched.Add(skill); else missing.Add(skill);
        var titleMatch = Read<string>(preferences.JobTitlesJson).Count == 0 || Read<string>(preferences.JobTitlesJson).Any(x => job.Title.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (titleMatch) reasons.Add("job_title_matched"); else skips.Add("job_title_not_matched");
        if (preferences.MinimumSalary.HasValue && (!job.MaximumSalary.HasValue || job.MaximumSalary < preferences.MinimumSalary)) skips.Add("salary_below_minimum");
        if (preferences.MinimumExperience.HasValue && job.MaximumExperienceYears.HasValue && job.MaximumExperienceYears < preferences.MinimumExperience) skips.Add("experience_below_minimum");
        var workplaces = Read<WorkplaceType>(preferences.WorkplaceTypesJson); if (workplaces.Count > 0 && !workplaces.Contains(job.WorkplaceType)) skips.Add("workplace_type_not_allowed");
        var employments = Read<EmploymentType>(preferences.EmploymentTypesJson); if (employments.Count > 0 && !employments.Contains(job.EmploymentType)) skips.Add("employment_type_not_allowed");
        foreach (var rule in rules.Where(x => x.IsEnabled))
        {
            if (rule.RuleType == AIApplyRuleType.ExcludeNightShift && $"{job.Title} {job.Description}".Contains("night shift", StringComparison.OrdinalIgnoreCase)) skips.Add("night_shift_excluded");
            if (rule.RuleType == AIApplyRuleType.RequiredSkill && !jobText.Contains(rule.Value.Trim().ToUpperInvariant(), StringComparison.Ordinal)) skips.Add("required_skill_missing");
        }
        var skillScore = wantedSkills.Count == 0 ? 50m : 70m * matched.Count / wantedSkills.Count;
        var score = Math.Min(100m, skillScore + (titleMatch ? 30m : 0m));
        return new(skips.Count == 0, score, matched, missing, reasons, skips.Distinct().ToList());
    }
    internal static List<T> Read<T>(string json) { try { return JsonSerializer.Deserialize<List<T>>(json) ?? []; } catch (JsonException) { return []; } }
}

public sealed class ApplicationQuestionMatcher(IAIApplyRepository repository) : IApplicationQuestionMatcher
{
    public string Normalize(string question) => string.Join(' ', new string(question.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public Task<UserApplicationAnswer?> FindAsync(Guid userId, string question, CancellationToken ct = default) => repository.FindAnswerAsync(userId, Normalize(question), ct);
}

public sealed class DeterministicApplicationFieldMapper : IApplicationFieldMapper
{
    public string? Map(string? label, string? name, string? placeholder, string? inputType, string? associatedText)
    {
        var value = $"{label} {name} {placeholder} {associatedText}".ToLowerInvariant();
        if (value.Contains("first name") || value.Contains("given name") || value.Contains("first_name") || value.Contains("fname")) return "FirstName";
        if (value.Contains("last name") || value.Contains("family name") || value.Contains("last_name") || value.Contains("lname")) return "LastName";
        if (value.Contains("full name") || value.Contains("your name") || value.Contains("candidate name")) return "FullName";
        if (value.Contains("email")) return "Email"; if (value.Contains("phone") || value.Contains("mobile")) return "Phone";
        if (value.Contains("resume") || value.Contains("cv") || inputType?.Equals("file", StringComparison.OrdinalIgnoreCase) == true) return "Resume";
        if (value.Contains("linkedin")) return "LinkedIn"; if (value.Contains("github")) return "GitHub";
        if (value.Contains("portfolio") || value.Contains("website")) return "Portfolio";
        if (value.Contains("postal") || value.Contains("zip")) return "PostalCode"; if (value.Contains("country")) return "Country"; if (value.Contains("state") || value.Contains("province")) return "State"; if (value.Contains("city")) return "City"; if (value.Contains("address")) return "Address";
        if (value.Contains("current company") || value.Contains("employer")) return "CurrentCompany"; if (value.Contains("job title") || value.Contains("current role")) return "CurrentJobTitle";
        if (value.Contains("expected") && value.Contains("salary")) return "ExpectedSalary"; if (value.Contains("current") && value.Contains("salary")) return "CurrentSalary";
        if (value.Contains("notice") || value.Contains("available to join")) return "NoticePeriod"; if (value.Contains("experience")) return "TotalExperience";
        if (value.Contains("work authorization") || value.Contains("authorised to work") || value.Contains("authorized to work")) return "WorkAuthorization";
        if (value.Contains("visa") || value.Contains("sponsorship")) return "VisaSponsorship"; if (value.Contains("relocat")) return "Relocation";
        if (value.Contains("cover letter")) return "CoverLetter"; if (value.Contains("location")) return "CurrentLocation";
        return null;
    }
}

public sealed class UnconfiguredJobApplicationBrowserAgent : IJobApplicationBrowserAgent
{
    public Task<BrowserApplicationResult> ApplyAsync(BrowserApplicationContext context, CancellationToken ct) =>
        Task.FromResult(new BrowserApplicationResult(false, AIApplyFailureKind.ApplicationUnavailable, "browser_adapter_not_configured"));
}
