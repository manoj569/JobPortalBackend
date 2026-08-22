using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.InterviewInsights;

public sealed class InterviewInsightService(
    IInterviewInsightRepository repository,
    IAuditWriter audit,
    IValidator<CreateInterviewInsightRequest> createValidator,
    IValidator<UpdateInterviewInsightRequest> updateValidator,
    IValidator<CreateInterviewScheduleRequest> scheduleValidator,
    IValidator<UpdateInterviewScheduleRequest> updateScheduleValidator,
    IValidator<CreateInsightFeedbackRequest> feedbackValidator,
    IValidator<CreateInsightReportRequest> reportValidator,
    TimeProvider timeProvider) : IInterviewInsightService
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<InterviewInsightResponse> CreateAsync(Guid candidateId, CreateInterviewInsightRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        await RequireCandidateAsync(candidateId, ct);
        await ValidateCompanyJobAsync(request.CompanyId, request.JobId, ct);
        if (request.InterviewDateMonth > DateOnly.FromDateTime(Now))
            throw new BadRequestException("Interview month cannot be in the future.", "future_interview_month");
        if (!await EligibleToAuthorAsync(candidateId, request.CompanyId, ct))
            throw new BadRequestException("An application or a past interview schedule at this company is required.", "interview_experience_not_established");
        if (await repository.CountInsightsSinceAsync(candidateId, Now.AddDays(-1), ct) >= 2)
            throw new AppException("You can share at most two interview insights per day.", 429, "insight_daily_limit");

        var insight = new InterviewInsight
        {
            AuthorCandidateId = candidateId,
            CompanyId = request.CompanyId,
            JobId = request.JobId,
            Status = InterviewInsightStatus.PendingReview
        };
        Apply(insight, request.RoleTitle, request.ExperienceLevel, request.InterviewDateMonth,
            request.OverallDifficulty, request.ProcessSummary, request.PreparationTips,
            request.Outcome, request.IsAnonymous, request.Rounds);
        await repository.AddInsightAsync(insight, ct);
        await audit.AppendAsync(new(AuditAction.Create, "InterviewInsight", insight.Id.ToString(),
            new Dictionary<string, string?> { ["status"] = "PendingReview" }, new(candidateId, "Candidate")), ct);
        await repository.SaveAsync(ct);
        var loaded = await repository.GetInsightAsync(insight.Id, false, ct) ?? insight;
        return Map(loaded, true, candidateId == loaded.AuthorCandidateId);
    }

    public async Task<PagedResponse<InterviewInsightResponse>> SearchAsync(Guid candidateId, InterviewInsightQuery query, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        ValidatePage(query.PageNumber, query.PageSize);
        if (query.Sort is not ("mostHelpful" or "newest")) throw new BadRequestException("Sort must be mostHelpful or newest.");
        var result = await repository.SearchPublishedAsync(query with { Role = query.Role?.Trim() }, ct);
        var mapped = new List<InterviewInsightResponse>(result.Items.Count);
        foreach (var item in result.Items)
        {
            var eligible = await EligibleForCompanyAsync(candidateId, item.CompanyId, ct);
            mapped.Add(Map(item, eligible, false));
        }
        return new(mapped, query.PageNumber, query.PageSize, result.Total);
    }

    public async Task<InterviewInsightResponse> GetAsync(Guid candidateId, Guid id, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        var item = await repository.GetInsightAsync(id, false, ct) ?? throw new NotFoundException("Interview insight was not found.");
        var owner = item.AuthorCandidateId == candidateId;
        if (!owner && item.Status != InterviewInsightStatus.Published) throw new NotFoundException("Interview insight was not found.");
        return Map(item, owner || await EligibleForCompanyAsync(candidateId, item.CompanyId, ct), owner);
    }

    public async Task<InterviewInsightResponse> UpdateAsync(Guid candidateId, Guid id, UpdateInterviewInsightRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        await RequireCandidateAsync(candidateId, ct);
        var item = await repository.GetInsightAsync(id, true, ct);
        if (item is null || item.AuthorCandidateId != candidateId) throw new NotFoundException("Interview insight was not found.");
        if (request.InterviewDateMonth > DateOnly.FromDateTime(Now)) throw new BadRequestException("Interview month cannot be in the future.");
        item.Rounds.Clear();
        Apply(item, request.RoleTitle, request.ExperienceLevel, request.InterviewDateMonth,
            request.OverallDifficulty, request.ProcessSummary, request.PreparationTips,
            request.Outcome, request.IsAnonymous, request.Rounds);
        item.Status = InterviewInsightStatus.PendingReview;
        item.PublishedAtUtc = null;
        item.ModerationReason = null;
        await audit.AppendAsync(new(AuditAction.Update, "InterviewInsight", id.ToString(),
            new Dictionary<string, string?> { ["status"] = "PendingReview" }, new(candidateId, "Candidate")), ct);
        await repository.SaveAsync(ct);
        return Map(item, true, true);
    }

    public async Task DeleteAsync(Guid candidateId, Guid id, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        var item = await repository.GetInsightAsync(id, true, ct);
        if (item is null || item.AuthorCandidateId != candidateId) throw new NotFoundException("Interview insight was not found.");
        item.IsDeleted = true;
        item.DeletedAtUtc = Now;
        await audit.AppendAsync(new(AuditAction.Delete, "InterviewInsight", id.ToString(), Actor: new(candidateId, "Candidate")), ct);
        await repository.SaveAsync(ct);
    }

    public async Task<InterviewScheduleResponse> CreateScheduleAsync(Guid candidateId, CreateInterviewScheduleRequest request, CancellationToken ct = default)
    {
        await scheduleValidator.ValidateAndThrowAsync(request, ct);
        await RequireCandidateAsync(candidateId, ct);
        await ValidateCompanyJobAsync(request.CompanyId, request.JobId, ct);
        var schedule = new CandidateInterviewSchedule
        {
            CandidateId = candidateId, CompanyId = request.CompanyId, JobId = request.JobId,
            RoleTitle = Clean(request.RoleTitle), InterviewAtUtc = Utc(request.InterviewAtUtc),
            ConfirmFeedbackAvailableAtUtc = Utc(request.InterviewAtUtc), Status = InterviewScheduleStatus.Scheduled
        };
        await repository.AddScheduleAsync(schedule, ct);
        await repository.SaveAsync(ct);
        return await ScheduleResponseAsync(schedule, ct);
    }

    public async Task<IReadOnlyCollection<InterviewScheduleResponse>> GetSchedulesAsync(Guid candidateId, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        return (await repository.GetSchedulesAsync(candidateId, ct)).Select(MapSchedule).ToArray();
    }

    public async Task<InterviewScheduleResponse> UpdateScheduleAsync(Guid candidateId, Guid id, UpdateInterviewScheduleRequest request, CancellationToken ct = default)
    {
        await updateScheduleValidator.ValidateAndThrowAsync(request, ct);
        await RequireCandidateAsync(candidateId, ct);
        var schedule = await repository.GetScheduleAsync(candidateId, id, true, ct) ?? throw new NotFoundException("Interview schedule was not found.");
        schedule.RoleTitle = Clean(request.RoleTitle);
        schedule.InterviewAtUtc = Utc(request.InterviewAtUtc);
        schedule.ConfirmFeedbackAvailableAtUtc = schedule.InterviewAtUtc;
        schedule.Status = request.Status;
        await repository.SaveAsync(ct);
        return MapSchedule(schedule);
    }

    public async Task<InsightFeedbackResponse> AddFeedbackAsync(Guid candidateId, Guid insightId, CreateInsightFeedbackRequest request, CancellationToken ct = default)
    {
        await feedbackValidator.ValidateAndThrowAsync(request, ct);
        await RequireCandidateAsync(candidateId, ct);
        if (await repository.CountFeedbackSinceAsync(candidateId, Now.AddDays(-1), ct) >= 10)
            throw new AppException("You can submit at most ten feedback responses per day.", 429, "feedback_daily_limit");
        var insight = await repository.GetInsightAsync(insightId, true, ct) ?? throw new NotFoundException("Interview insight was not found.");
        if (insight.Status != InterviewInsightStatus.Published) throw new NotFoundException("Interview insight was not found.");
        if (insight.AuthorCandidateId == candidateId) throw new BadRequestException("You cannot provide feedback on your own insight.", "self_feedback");
        var schedule = await repository.GetScheduleAsync(candidateId, request.CandidateInterviewScheduleId, false, ct)
            ?? throw new NotFoundException("Interview schedule was not found.");
        if (schedule.CompanyId != insight.CompanyId) throw new BadRequestException("The interview schedule is for another company.", "schedule_company_mismatch");
        if (schedule.Status == InterviewScheduleStatus.Cancelled || schedule.InterviewAtUtc > Now)
            throw new BadRequestException("Feedback is available only after your scheduled interview.", "feedback_not_available");
        if (await repository.FeedbackExistsAsync(candidateId, insightId, ct))
            throw new ConflictException("Feedback has already been submitted for this insight.", "duplicate_insight_feedback");
        var previousHelped = insight.HelpfulConfirmedCount;
        var feedback = new InsightHelpfulnessFeedback
        {
            InsightId = insightId, CandidateId = candidateId,
            CandidateInterviewScheduleId = schedule.Id, Helpfulness = request.Helpfulness,
            InterviewMatch = request.InterviewMatch, Feedback = Clean(request.Feedback)
        };
        if (request.Helpfulness == InsightHelpfulness.Helped) insight.HelpfulConfirmedCount++;
        insight.QualityScore += request.Helpfulness switch { InsightHelpfulness.Helped => 3, InsightHelpfulness.PartlyHelped => 1, _ => 0 };
        await repository.AddFeedbackAsync(feedback, ct);
        if (request.Helpfulness == InsightHelpfulness.Helped && IsMilestone(previousHelped, insight.HelpfulConfirmedCount))
            await repository.AddNotificationAsync(new Notification
            {
                UserId = insight.AuthorCandidateId, Type = NotificationType.Profile,
                Title = "Your insight helped candidates",
                Message = $"Your Interview Insight has now helped {insight.HelpfulConfirmedCount} candidate{(insight.HelpfulConfirmedCount == 1 ? string.Empty : "s")}.",
                ActionUrl = "/dashboard/interview-insights/my-contributions"
            }, ct);
        try { await repository.SaveAsync(ct); }
        catch (UniqueConstraintException) { throw new ConflictException("Feedback has already been submitted for this insight.", "duplicate_insight_feedback"); }
        return new(feedback.Id, feedback.Helpfulness, feedback.InterviewMatch, feedback.CreatedAtUtc,
            insight.HelpfulConfirmedCount, insight.QualityScore);
    }

    public async Task<InsightReportResponse> ReportAsync(Guid candidateId, Guid insightId, CreateInsightReportRequest request, CancellationToken ct = default)
    {
        await reportValidator.ValidateAndThrowAsync(request, ct);
        await RequireCandidateAsync(candidateId, ct);
        var insight = await repository.GetInsightAsync(insightId, false, ct);
        if (insight is null || insight.Status != InterviewInsightStatus.Published) throw new NotFoundException("Interview insight was not found.");
        if (await repository.ReportExistsAsync(candidateId, insightId, ct)) throw new ConflictException("You have already reported this insight.");
        var report = new InsightReport { InsightId = insightId, ReporterCandidateId = candidateId, Reason = request.Reason, Details = Clean(request.Details) };
        await repository.AddReportAsync(report, ct);
        try { await repository.SaveAsync(ct); }
        catch (UniqueConstraintException) { throw new ConflictException("You have already reported this insight."); }
        return new(report.Id, report.Status, report.CreatedAtUtc);
    }

    public async Task<MyInterviewContributionsResponse> ContributionsAsync(Guid candidateId, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        var x = await repository.ContributionsAsync(candidateId, ct);
        var badges = new List<string>();
        if (x.Published > 0) badges.Add("First Insight Shared");
        if (x.Helped >= 10) badges.Add("10 Candidates Helped");
        if (x.Helped >= 50) badges.Add("50 Candidates Helped");
        return new(x.Published, x.Helped, x.Score, badges);
    }

    public async Task<CompanyInterviewInsightSummaryResponse> CompanySummaryAsync(Guid candidateId, Guid companyId, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        var x = await repository.CompanySummaryAsync(companyId, ct);
        if (string.IsNullOrEmpty(x.Name)) throw new NotFoundException("Company was not found.");
        return new(companyId, x.Name, x.Published, x.Helped, await EligibleForCompanyAsync(candidateId, companyId, ct));
    }

    private async Task RequireCandidateAsync(Guid id, CancellationToken ct)
    {
        if (!await repository.IsCandidateAsync(id, ct)) throw new UnauthorizedException("An active Candidate account is required.");
    }
    private async Task<bool> EligibleForCompanyAsync(Guid id, Guid companyId, CancellationToken ct) =>
        await repository.HasApplicationAtCompanyAsync(id, companyId, ct) || await repository.HasScheduleAtCompanyAsync(id, companyId, ct);
    private async Task<bool> EligibleToAuthorAsync(Guid id, Guid companyId, CancellationToken ct) =>
        await repository.HasApplicationAtCompanyAsync(id, companyId, ct) || await repository.HasPastScheduleAtCompanyAsync(id, companyId, Now, ct);
    private async Task ValidateCompanyJobAsync(Guid companyId, Guid? jobId, CancellationToken ct)
    {
        if (!await repository.CompanyExistsAsync(companyId, ct)) throw new NotFoundException("Company was not found.");
        if (jobId.HasValue && !await repository.JobBelongsToCompanyAsync(jobId.Value, companyId, ct))
            throw new BadRequestException("The selected job does not belong to this company.", "job_company_mismatch");
    }
    private static void Apply(InterviewInsight item, string role, string? level, DateOnly month,
        InterviewDifficulty difficulty, string summary, string tips, InterviewOutcome? outcome,
        bool anonymous, IReadOnlyCollection<InterviewRoundRequest> rounds)
    {
        item.RoleTitle = role.Trim(); item.ExperienceLevel = Clean(level); item.InterviewDateMonth = month;
        item.OverallDifficulty = difficulty; item.ProcessSummary = summary.Trim(); item.PreparationTips = tips.Trim();
        item.Outcome = outcome; item.IsAnonymous = anonymous;
        var sequence = 1;
        foreach (var round in rounds) item.Rounds.Add(new InterviewRound
        {
            Sequence = sequence++, RoundType = round.RoundType, RoundTitle = Clean(round.RoundTitle),
            DurationMinutes = round.DurationMinutes, QuestionsOrTopics = round.QuestionsOrTopics.Trim(),
            CandidateAdvice = Clean(round.CandidateAdvice)
        });
    }
    internal static InterviewInsightResponse Map(InterviewInsight x, bool full, bool owner) => new(
        x.Id, x.CompanyId, x.Company?.Name ?? string.Empty, x.Company?.LogoUrl, x.JobId, x.RoleTitle,
        x.ExperienceLevel, x.InterviewDateMonth.Month, x.InterviewDateMonth.Year, x.OverallDifficulty,
        x.ProcessSummary, full ? x.PreparationTips : null, full ? x.Outcome : null, x.IsAnonymous,
        !x.IsAnonymous ? $"{x.AuthorCandidate?.FirstName} {x.AuthorCandidate?.LastName}".Trim() : null,
        x.Status, x.PublishedAtUtc, x.Rounds.Count, x.HelpfulConfirmedCount, x.QualityScore, full,
        full && !owner && x.Status == InterviewInsightStatus.Published,
        full ? x.Rounds.OrderBy(r => r.Sequence).Select(r => new InterviewRoundResponse(r.Id, r.Sequence,
            r.RoundType, r.RoundTitle, r.DurationMinutes, r.QuestionsOrTopics, r.CandidateAdvice)).ToArray() : []);
    private async Task<InterviewScheduleResponse> ScheduleResponseAsync(CandidateInterviewSchedule s, CancellationToken ct) =>
        MapSchedule(await repository.GetScheduleAsync(s.CandidateId, s.Id, false, ct) ?? s);
    private InterviewScheduleResponse MapSchedule(CandidateInterviewSchedule s) => new(s.Id, s.CompanyId,
        s.Company?.Name ?? string.Empty, s.JobId, s.RoleTitle, s.InterviewAtUtc, s.Status,
        s.ConfirmFeedbackAvailableAtUtc, s.Status != InterviewScheduleStatus.Cancelled && s.InterviewAtUtc <= Now);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static bool IsMilestone(int before, int after) => (before < 1 && after >= 1) || (before < 10 && after >= 10) || (before < 50 && after >= 50);
    private static void ValidatePage(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100) throw new BadRequestException("PageNumber must be positive and PageSize must be between 1 and 100.");
    }
}

public sealed class AdminInterviewInsightService(IInterviewInsightRepository repository, IAuditWriter audit, TimeProvider timeProvider) : IAdminInterviewInsightService
{
    public async Task<PagedResponse<InterviewInsightResponse>> SearchAsync(AdminInterviewInsightQuery query, CancellationToken ct = default)
    {
        ValidatePage(query.PageNumber, query.PageSize);
        var x = await repository.SearchAdminAsync(query, ct);
        return new(x.Items.Select(i => InterviewInsightService.Map(i, true, false)).ToArray(), query.PageNumber, query.PageSize, x.Total);
    }
    public async Task<InterviewInsightResponse> ModerateAsync(Guid administratorId, Guid id, ModerateInterviewInsightRequest request, CancellationToken ct = default)
    {
        if (request.Status is not (InterviewInsightStatus.Published or InterviewInsightStatus.Rejected or InterviewInsightStatus.Hidden))
            throw new BadRequestException("Moderation status must be Published, Rejected, or Hidden.");
        if (request.Status != InterviewInsightStatus.Published && string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("A moderation reason is required.");
        var item = await repository.GetInsightAsync(id, true, ct) ?? throw new NotFoundException("Interview insight was not found.");
        item.Status = request.Status;
        item.ModerationReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        item.PublishedAtUtc = request.Status == InterviewInsightStatus.Published ? timeProvider.GetUtcNow().UtcDateTime : null;
        await audit.AppendAsync(new(AuditAction.Update, "InterviewInsight", id.ToString(),
            new Dictionary<string, string?> { ["moderationStatus"] = request.Status.ToString() }, new(administratorId, "Administrator")), ct);
        await repository.SaveAsync(ct);
        return InterviewInsightService.Map(item, true, false);
    }
    public async Task<PagedResponse<AdminInsightReportResponse>> ReportsAsync(AdminInsightReportQuery query, CancellationToken ct = default)
    {
        ValidatePage(query.PageNumber, query.PageSize);
        var x = await repository.SearchReportsAsync(query, ct);
        return new(x.Items.Select(MapReport).ToArray(), query.PageNumber, query.PageSize, x.Total);
    }
    public async Task<AdminInsightReportResponse> ModerateReportAsync(Guid administratorId, Guid id, ModerateInsightReportRequest request, CancellationToken ct = default)
    {
        if (request.Status == InsightReportStatus.Open) throw new BadRequestException("Select a completed report status.");
        var report = await repository.GetReportAsync(id, ct) ?? throw new NotFoundException("Interview insight report was not found.");
        report.Status = request.Status;
        if (request.Status == InsightReportStatus.Actioned)
        {
            var insight = await repository.GetInsightAsync(report.InsightId, true, ct);
            if (insight is not null) { insight.Status = InterviewInsightStatus.Hidden; insight.PublishedAtUtc = null; insight.ModerationReason = "Hidden following an actioned candidate report."; }
        }
        await audit.AppendAsync(new(AuditAction.Update, "InsightReport", id.ToString(),
            new Dictionary<string, string?> { ["status"] = request.Status.ToString() }, new(administratorId, "Administrator")), ct);
        await repository.SaveAsync(ct);
        return MapReport(report);
    }
    private static AdminInsightReportResponse MapReport(InsightReport x) => new(x.Id, x.InsightId, x.Reason, x.Details, x.Status, x.CreatedAtUtc);
    private static void ValidatePage(int page, int size) { if (page < 1 || size is < 1 or > 100) throw new BadRequestException("Invalid pagination."); }
}
