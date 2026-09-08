using System.Text.Json;
using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.AIApply;

#pragma warning disable CA1725

public sealed class AIApplyService(IAIApplyRepository repository, IAIApplyAuthorizationService authorization,
    IAIApplyMatcher matcher, IApplicationQuestionClassifier questionClassifier,
    ICandidateExternalApplicationLinkService externalLinks, TimeProvider clock) : IAIApplyService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AIApplyProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default)
    { await authorization.RequireAsync(userId, ct: ct); return MapProfile(await RequiredUser(userId, ct), await repository.GetProfileAsync(userId, ct)); }
    public async Task<AIApplyProfileResponse> UpdateProfileAsync(Guid userId, UpdateAIApplyProfileRequest request, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct);
        if (!SafeHttpsUrl.IsValid(request.GitHubUrl)) throw new BadRequestException("GitHubUrl must be a safe HTTPS URL.", "validation_error");
        GuardText(request.WorkAuthorization, 200, "workAuthorization"); GuardText(request.VisaSponsorshipPreference, 200, "visaSponsorshipPreference");
        var entity = await repository.GetProfileAsync(userId, ct) ?? new AIApplyProfile { UserId = userId };
        if (entity.CreatedAtUtc == default) await repository.AddAsync(entity, ct);
        entity.GitHubUrl = Clean(request.GitHubUrl); entity.WillingToRelocate = request.WillingToRelocate;
        entity.WorkAuthorization = Clean(request.WorkAuthorization); entity.VisaSponsorshipPreference = Clean(request.VisaSponsorshipPreference);
        await repository.SaveChangesAsync(ct); return MapProfile(await RequiredUser(userId, ct), entity);
    }

    public async Task<AIApplyPreferencesResponse> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    { await authorization.RequireAsync(userId, ct: ct); return MapPreferences(await repository.GetPreferencesAsync(userId, ct) ?? new AIApplyPreference { UserId = userId }); }
    public async Task<AIApplyPreferencesResponse> UpdatePreferencesAsync(Guid userId, UpdateAIApplyPreferencesRequest r, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct); ValidateRange(r.MinimumExperience, r.MaximumExperience, 0, 80, "experience"); ValidateRange(r.MinimumSalary, r.MaximumSalary, 0, 1_000_000_000, "salary");
        ValidateList(r.JobTitles, "jobTitles"); ValidateList(r.Skills, "skills"); ValidateList(r.PreferredLocations, "preferredLocations");
        var e = await repository.GetPreferencesAsync(userId, ct) ?? new AIApplyPreference { UserId = userId }; if (e.CreatedAtUtc == default) await repository.AddAsync(e, ct);
        e.JobTitlesJson = Write(r.JobTitles); e.SkillsJson = Write(r.Skills); e.PreferredLocationsJson = Write(r.PreferredLocations); e.WorkplaceTypesJson = Write(r.WorkplaceTypes.Distinct()); e.EmploymentTypesJson = Write(r.EmploymentTypes.Distinct()); e.MinimumExperience = r.MinimumExperience; e.MaximumExperience = r.MaximumExperience; e.MinimumSalary = r.MinimumSalary; e.MaximumSalary = r.MaximumSalary;
        await repository.SaveChangesAsync(ct); return MapPreferences(e);
    }

    public async Task<AIApplySettingsResponse> GetSettingsAsync(Guid userId, CancellationToken ct = default)
    { await authorization.RequireAsync(userId, ct: ct); return MapSettings(await repository.GetSettingsAsync(userId, ct) ?? Defaults(userId)); }
    public async Task<AIApplySettingsResponse> UpdateSettingsAsync(Guid userId, UpdateAIApplySettingsRequest r, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct); ValidateTimezone(r.Timezone); if (r.PreferredDays.Count > 7) throw Validation("preferredDays");
        var e = await repository.GetSettingsAsync(userId, ct) ?? Defaults(userId); if (e.CreatedAtUtc == default) await repository.AddAsync(e, ct);
        e.Enabled = r.Enabled; e.PreferredStartTime = r.PreferredStartTime; e.Timezone = r.Timezone.Trim(); e.PreferredDaysJson = Write(r.PreferredDays.Distinct()); e.AutoResumeApplications = r.AutoResumeApplications; e.AllowAIGeneratedAnswers = r.AllowAIGeneratedAnswers; e.RequireConfirmationBeforeSubmit = r.RequireConfirmationBeforeSubmit;
        await repository.SaveChangesAsync(ct); return MapSettings(e);
    }
    public async Task<AIApplySettingsResponse> SetModeAsync(Guid userId, AIApplyControlAction action, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct); var e = await repository.GetSettingsAsync(userId, ct) ?? Defaults(userId); if (e.CreatedAtUtc == default) await repository.AddAsync(e, ct);
        if (action == AIApplyControlAction.Enable) { e.Enabled = true; e.Paused = false; } else if (action == AIApplyControlAction.Disable) { e.Enabled = false; e.Paused = false; foreach (var item in await repository.GetApplicationsAsync(userId, null, null, ct)) if (item.Status == AIApplyRunStatus.Queued) AIApplyStateMachine.Transition(item, AIApplyRunStatus.Cancelled, Now); } else if (action == AIApplyControlAction.Pause) e.Paused = true; else if (action == AIApplyControlAction.Resume) { e.Enabled = true; e.Paused = false; }
        await repository.SaveChangesAsync(ct); return MapSettings(e);
    }

    public async Task<IReadOnlyList<AIApplyRuleResponse>> GetRulesAsync(Guid userId, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); return (await repository.GetRulesAsync(userId, ct)).Select(MapRule).ToList(); }
    public async Task<AIApplyRuleResponse> CreateRuleAsync(Guid userId, UpsertAIApplyRuleRequest r, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); ValidateRule(r); var e = new AIApplyRule { UserId = userId, RuleType = r.RuleType, Operator = r.Operator, Value = r.Value.Trim(), IsEnabled = r.IsEnabled }; await repository.AddAsync(e, ct); await repository.SaveChangesAsync(ct); return MapRule(e); }
    public async Task<AIApplyRuleResponse> UpdateRuleAsync(Guid userId, Guid id, UpsertAIApplyRuleRequest r, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); ValidateRule(r); var e = await repository.GetRuleAsync(userId, id, ct) ?? throw new NotFoundException("AI Apply rule was not found."); e.RuleType = r.RuleType; e.Operator = r.Operator; e.Value = r.Value.Trim(); e.IsEnabled = r.IsEnabled; await repository.SaveChangesAsync(ct); return MapRule(e); }
    public async Task DeleteRuleAsync(Guid userId, Guid id, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); var e = await repository.GetRuleAsync(userId, id, ct) ?? throw new NotFoundException("AI Apply rule was not found."); repository.Remove(e); await repository.SaveChangesAsync(ct); }

    public async Task<AIApplyApplicationResponse> QueueAsync(Guid userId, QueueAIApplyRequest r, CancellationToken ct = default)
    {
        var access = await authorization.RequireAsync(userId, ct: ct); var settings = await repository.GetSettingsAsync(userId, ct); if (settings is not { Enabled: true, Paused: false }) throw new ConflictException("AI Apply must be enabled and resumed before queueing.", "ai_apply_not_running");
        var job = await repository.GetJobAsync(r.JobId, ct) ?? throw new NotFoundException("Job was not found."); if (job.Status != JobStatus.Published || job.ExpiresAtUtc <= Now) throw new ConflictException("The job is not available.", "job_unavailable");
        if (!SafeHttpsUrl.IsValid(job.ApplicationUrl, false)) throw new ConflictException("The external application URL is unavailable.", "application_url_unavailable"); var normalizedUrl = NormalizeUrl(job.ApplicationUrl);
        if (await repository.IsDuplicateAsync(userId, job.Id, normalizedUrl, ct)) throw new ConflictException("This job has already been queued or applied to.", "duplicate_application");
        var prefs = await repository.GetPreferencesAsync(userId, ct) ?? new AIApplyPreference { UserId = userId }; var match = matcher.Match(await RequiredUser(userId, ct), job, prefs, await repository.GetRulesAsync(userId, ct));
        if (!match.IsEligible) throw new ConflictException(string.Join(",", match.SkipReasons), "job_not_eligible");
        var e = new AIApplyApplication { UserId = userId, JobId = job.Id, ExternalApplicationUrl = job.ApplicationUrl, NormalizedApplicationUrl = normalizedUrl, Priority = access.ProcessingPriority, ScheduledAtUtc = r.ScheduledAtUtc?.ToUniversalTime() ?? Now, MatchScore = match.MatchScore };
        await repository.AddAsync(e, ct); await repository.SaveChangesAsync(ct); return await MapApplicationAsync(e, job, ct);
    }
    public async Task<IReadOnlyList<AIApplyApplicationResponse>> GetApplicationsAsync(Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct); var applications = await repository.GetApplicationsAsync(userId, from, to, ct);
        var jobs = (await repository.GetJobsAsync(applications.Select(x => x.JobId).Distinct().ToArray(), ct)).ToDictionary(x => x.Id);
        var results = new List<AIApplyApplicationResponse>(applications.Count);
        foreach (var application in applications) { jobs.TryGetValue(application.JobId, out var job); results.Add(await MapApplicationAsync(application, job, ct)); }
        return results;
    }
    public async Task<AIApplyApplicationResponse> GetApplicationAsync(Guid userId, Guid id, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); var application = await RequiredApplication(userId, id, ct); return await MapApplicationAsync(application, await repository.GetJobAsync(application.JobId, ct), ct); }
    public async Task<AIApplyApplicationResponse> CancelAsync(Guid userId, Guid id, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); var e = await RequiredApplication(userId, id, ct); AIApplyStateMachine.Transition(e, AIApplyRunStatus.Cancelled, Now); await repository.SaveChangesAsync(ct); return await MapApplicationAsync(e, await repository.GetJobAsync(e.JobId, ct), ct); }
    public async Task<AIApplyApplicationResponse> ContinueAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct);
        var application = await RequiredApplication(userId, id, ct);
        var job = await repository.GetJobAsync(application.JobId, ct) ?? throw new ConflictException("The job is no longer available.", "job_unavailable");
        if (application.Status == AIApplyRunStatus.Queued && application.SubmissionAttemptedAtUtc is null)
            return (await MapApplicationAsync(application, job, ct)) with { Message = "The application is already queued." };
        if (application.SubmissionAttemptedAtUtc.HasValue && !application.SubmissionConfirmedAtUtc.HasValue)
            throw new ConflictException("This application may already have been submitted and requires review.", "submission_unconfirmed");
        if (application.Status != AIApplyRunStatus.Failed || application.FailureKind is not (AIApplyFailureKind.LoginRequired or AIApplyFailureKind.HumanVerificationRequired))
            throw new ConflictException("This application cannot be continued from its current state.", "application_not_continuable");
        if (job.Status != JobStatus.Published || job.ExpiresAtUtc <= Now)
            throw new ConflictException("The job is no longer available.", "job_unavailable");
        if (!string.Equals(job.ApplicationUrl.Trim(), application.ExternalApplicationUrl.Trim(), StringComparison.Ordinal))
            throw new ConflictException("The external application destination changed and requires review.", "application_destination_changed");
        var destination = await externalLinks.ResolveAsync(job.ApplicationUrl, ct);
        if (destination is null) throw new ConflictException("The external application URL is unavailable.", "application_url_unavailable");
        if (!destination.SupportsRestartFromUrl) throw new ConflictException("This application cannot be safely restarted from its external URL.", "site_continue_unsupported");
        if (!destination.OperationallyAvailable) throw new ConflictException("The external application site is temporarily unavailable.", "site_temporarily_unavailable");
        var expectedFailure = application.FailureKind;
        AIApplyStateMachine.Transition(new AIApplyApplication { Status = application.Status }, AIApplyRunStatus.Queued, Now);
        if (!await repository.TryContinueCandidateActionAsync(userId, id, expectedFailure, Now, ct))
        {
            var current = await RequiredApplication(userId, id, ct);
            if (current.Status == AIApplyRunStatus.Queued && current.SubmissionAttemptedAtUtc is null)
                return (await MapApplicationAsync(current, job, ct)) with { Message = "The application is already queued." };
            throw new ConflictException("The application state changed and cannot be continued.", "application_not_continuable");
        }
        application.ScheduledAtUtc = Now; application.RequiresUserInput = false; application.FailureKind = AIApplyFailureKind.None; application.LastErrorCode = null; application.FailureClassification = null; application.CompletedAtUtc = null; application.LeaseOwner = null; application.ClaimedAtUtc = null; application.LeaseExpiresAtUtc = null;
        return (await MapApplicationAsync(application, job, ct)) with { Message = "The application has been queued for a safe recheck." };
    }

    public async Task<IReadOnlyList<AIApplyQuestionResponse>> GetQuestionsAsync(Guid userId, AIApplyQuestionStatus? status, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); return (await repository.GetQuestionsAsync(userId, status, ct)).Select(MapQuestion).ToList(); }
    public async Task<AIApplyQuestionResponse> GetQuestionAsync(Guid userId, Guid id, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); return MapQuestion(await RequiredQuestion(userId, id, ct)); }
    public async Task<AIApplyQuestionResponse> AnswerAsync(Guid userId, Guid id, AnswerAIApplyQuestionRequest r, CancellationToken ct = default)
    {
        await authorization.RequireAsync(userId, ct: ct); GuardText(r.Answer, 4000, "answer", true); var q = await RequiredQuestion(userId, id, ct); if (q.Status != AIApplyQuestionStatus.Pending) throw new ConflictException("The question is no longer pending."); q.FinalAnswer = r.Answer.Trim(); q.Status = AIApplyQuestionStatus.Answered; q.AnsweredAtUtc = Now;
        if (r.SaveToMemory) { var existing = await repository.FindAnswerAsync(userId, q.NormalizedQuestion, ct); if (existing is null) { existing = new UserApplicationAnswer { UserId = userId, Question = q.Question, NormalizedQuestion = q.NormalizedQuestion }; await repository.AddAsync(existing, ct); } var analysis = questionClassifier.Analyze(new(q.Question, q.QuestionType)); existing.Answer = q.FinalAnswer; existing.Category = analysis.CanonicalKey ?? analysis.Category.ToString(); existing.Source = AIApplyAnswerSource.UserVerified; existing.Confidence = 1; existing.IsVerified = true; existing.IsActive = true; }
        var app = await RequiredApplication(userId, q.ApplicationId, ct); if (app.Status == AIApplyRunStatus.WaitingForUser && !(await repository.GetQuestionsAsync(userId, AIApplyQuestionStatus.Pending, ct)).Any(x => x.ApplicationId == app.Id && x.Id != q.Id)) { app.RequiresUserInput = false; AIApplyStateMachine.Transition(app, AIApplyRunStatus.Queued, Now); }
        await repository.SaveChangesAsync(ct); return MapQuestion(q);
    }
    public async Task<AIApplyQuestionResponse> SkipQuestionAsync(Guid userId, Guid id, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); var q = await RequiredQuestion(userId, id, ct); if (q.Status != AIApplyQuestionStatus.Pending) throw new ConflictException("The question is no longer pending."); q.Status = AIApplyQuestionStatus.Skipped; await repository.SaveChangesAsync(ct); return MapQuestion(q); }
    public async Task<IReadOnlyList<AIApplyAnswerResponse>> GetAnswersAsync(Guid userId, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); return (await repository.GetAnswersAsync(userId, ct)).Select(MapAnswer).ToList(); }
    public async Task<AIApplyAnswerResponse> UpdateAnswerAsync(Guid userId, Guid id, UpdateAIApplyAnswerRequest r, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); GuardText(r.Answer, 4000, "answer", true); var e = await repository.GetAnswerAsync(userId, id, ct) ?? throw new NotFoundException("Saved answer was not found."); e.Answer = r.Answer.Trim(); e.IsActive = r.IsActive; e.Source = AIApplyAnswerSource.UserVerified; e.IsVerified = true; e.Confidence = 1; await repository.SaveChangesAsync(ct); return MapAnswer(e); }
    public async Task DeleteAnswerAsync(Guid userId, Guid id, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); var e = await repository.GetAnswerAsync(userId, id, ct) ?? throw new NotFoundException("Saved answer was not found."); repository.Remove(e); await repository.SaveChangesAsync(ct); }
    public async Task<AIApplyAnalyticsResponse> AnalyticsAsync(Guid userId, DateTime from, DateTime to, CancellationToken ct = default) { await authorization.RequireAsync(userId, ct: ct); if (from >= to || to - from > TimeSpan.FromDays(366)) throw Validation("dateRange"); var items = await repository.GetApplicationsAsync(userId, from.ToUniversalTime(), to.ToUniversalTime(), ct); return new(from, to, items.Count, items.Count(x => x.Status == AIApplyRunStatus.Submitted), items.Count(x => x.Status == AIApplyRunStatus.WaitingForUser), items.Count(x => x.Status == AIApplyRunStatus.Failed), items.Count(x => x.Status == AIApplyRunStatus.Skipped), 0, items.Where(x => x.MatchScore.HasValue).Select(x => x.MatchScore!.Value).DefaultIfEmpty().Average(), items.Count(x => x.RequiresUserInput)); }

    private async Task<User> RequiredUser(Guid id, CancellationToken ct) => await repository.GetUserProfileAsync(id, ct) ?? throw new UnauthorizedException();
    private async Task<AIApplyApplication> RequiredApplication(Guid userId, Guid id, CancellationToken ct) => await repository.GetApplicationAsync(userId, id, ct) ?? throw new NotFoundException("AI Apply application was not found.");
    private async Task<AIApplyQuestion> RequiredQuestion(Guid userId, Guid id, CancellationToken ct) => await repository.GetQuestionAsync(userId, id, ct) ?? throw new NotFoundException("Application question was not found.");
    private static AIApplyProfileResponse MapProfile(User u, AIApplyProfile? p) => new(u.FirstName, u.LastName, u.Email, u.PhoneNumber, u.Location ?? u.CurrentCity, Read<string>(u.PreferredCitiesJson), u.LinkedInUrl, p?.GitHubUrl, u.PortfolioUrl, u.Degree, u.College, u.GraduationYear, u.YearsOfExperience, u.CandidateSkills.Select(x => x.Name).ToList(), u.CandidateCertifications.Select(x => x.Name).ToList(), u.AvailabilityToJoin, u.CurrentAnnualSalary, u.ExpectedAnnualSalary, p?.WillingToRelocate, p?.WorkAuthorization, p?.VisaSponsorshipPreference);
    private static AIApplyPreferencesResponse MapPreferences(AIApplyPreference e) => new(Read<string>(e.JobTitlesJson), Read<string>(e.SkillsJson), e.MinimumExperience, e.MaximumExperience, Read<string>(e.PreferredLocationsJson), Read<WorkplaceType>(e.WorkplaceTypesJson), e.MinimumSalary, e.MaximumSalary, Read<EmploymentType>(e.EmploymentTypesJson));
    private static AIApplySettingsResponse MapSettings(AIApplySetting e) => new(e.Enabled, e.Paused, e.PreferredStartTime, e.Timezone, Read<DayOfWeek>(e.PreferredDaysJson), e.AutoResumeApplications, e.AllowAIGeneratedAnswers, e.RequireConfirmationBeforeSubmit);
    private static AIApplyRuleResponse MapRule(AIApplyRule e) => new(e.Id, e.RuleType, e.Operator, e.Value, e.IsEnabled);
    private async Task<AIApplyApplicationResponse> MapApplicationAsync(AIApplyApplication e, Job? job, CancellationToken ct)
    {
        var candidateAction = e.FailureKind is AIApplyFailureKind.LoginRequired or AIApplyFailureKind.HumanVerificationRequired || e.Status == AIApplyRunStatus.NeedsReview;
        var destination = candidateAction && job is not null ? await externalLinks.ResolveAsync(job.ApplicationUrl, ct) : null;
        var jobEligible = job is { Status: JobStatus.Published } && job.ExpiresAtUtc > Now;
        var destinationUnchanged = job is not null && string.Equals(job.ApplicationUrl.Trim(), e.ExternalApplicationUrl.Trim(), StringComparison.Ordinal);
        var canContinue = e.Status == AIApplyRunStatus.Failed &&
            e.FailureKind is (AIApplyFailureKind.LoginRequired or AIApplyFailureKind.HumanVerificationRequired) &&
            e.SubmissionAttemptedAtUtc is null && jobEligible && destinationUnchanged && destination is { SupportsRestartFromUrl: true, OperationallyAvailable: true };
        var canCancel = e.Status is AIApplyRunStatus.Queued or AIApplyRunStatus.WaitingForUser or AIApplyRunStatus.Failed;
        var external = destination is null ? null : new CandidateExternalApplicationResponse(destination.Site, destination.DisplayName, destination.Url, true);
        return new(e.Id, e.JobId, e.Status, e.Priority, e.ScheduledAtUtc, e.StartedAtUtc, e.CompletedAtUtc, e.RetryCount, e.FailureKind, e.RequiresUserInput, e.MatchScore, e.CreatedAtUtc, external, new(external is not null, canContinue, canCancel, candidateAction), Message(e));
    }
    private static string? Message(AIApplyApplication e) => e.FailureKind switch { AIApplyFailureKind.LoginRequired => "Sign in on the external application site, then return to continue.", AIApplyFailureKind.HumanVerificationRequired => "Complete the external site's verification, then return to continue.", AIApplyFailureKind.SubmissionUnconfirmed => "We could not safely confirm whether this application was submitted. Review it before taking further action.", AIApplyFailureKind.Duplicate => "This application was identified as a duplicate.", AIApplyFailureKind.JobExpired => "This job is no longer available.", _ => null };
    private static AIApplyQuestionResponse MapQuestion(AIApplyQuestion e) => new(e.Id, e.ApplicationId, e.Question, e.QuestionType, e.SuggestedAnswer, e.FinalAnswer, e.Status, e.AnsweredAtUtc);
    private static AIApplyAnswerResponse MapAnswer(UserApplicationAnswer e) => new(e.Id, e.Question, e.Answer, e.Category, e.Source, e.Confidence, e.IsVerified, e.IsActive);
    private static AIApplySetting Defaults(Guid userId) => new() { UserId = userId, Timezone = "UTC" };
    private static string Write<T>(IEnumerable<T> value) => JsonSerializer.Serialize(value);
    private static List<T> Read<T>(string value) => DeterministicAIApplyMatcher.Read<T>(value);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeUrl(string value) { var u = new Uri(value.Trim()); return new UriBuilder(u) { Fragment = "", Query = "" }.Uri.AbsoluteUri.TrimEnd('/').ToLowerInvariant(); }
    private static void ValidateList(IReadOnlyList<string> values, string field) { if (values.Count > 50 || values.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 150 || x.Any(char.IsControl))) throw Validation(field); }
    private static void ValidateRange(decimal? min, decimal? max, decimal floor, decimal ceiling, string field) { if (min < floor || max < floor || min > ceiling || max > ceiling || min > max) throw Validation(field); }
    private static void ValidateTimezone(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 100) throw Validation("timezone"); try { TimeZoneInfo.FindSystemTimeZoneById(value); } catch (TimeZoneNotFoundException) { throw Validation("timezone"); } catch (InvalidTimeZoneException) { throw Validation("timezone"); } }
    private static void ValidateRule(UpsertAIApplyRuleRequest r) { GuardText(r.Value, 1000, "value", true); }
    private static void GuardText(string? value, int max, string field, bool required = false) { if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max || value?.Any(char.IsControl) == true) throw Validation(field); }
    private static BadRequestException Validation(string field) => new($"{field} is invalid.", "validation_error");
}
