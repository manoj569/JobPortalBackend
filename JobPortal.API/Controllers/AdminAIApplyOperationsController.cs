using JobPortal.Application.Abstractions.AIApply;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JobPortal.Application.Features.AIApply;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/ai-apply")]
[Produces("application/json")]
public sealed class AdminAIApplyOperationsController(JobPortalDbContext db, IAIApplyAuthorizationService authorization,
    IAIApplyOperationalStore operations, IJobSiteAdapterHealthService siteHealth, IOptions<AIApplyOptions> options, TimeProvider clock) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<object>>> Overview([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var query = Period(db.AIApplyApplications.AsNoTracking(), from, to);
        var statuses = await query.GroupBy(x => x.Status).Select(x => new { x.Key, Count = x.Count() }).ToListAsync(ct);
        var costs = await Period(db.AIApplyCosts.AsNoTracking(), from, to).GroupBy(_ => 1).Select(x => new { BrowserSeconds = x.Sum(c => c.BrowserExecutionSeconds), EstimatedCost = x.Sum(c => c.EstimatedCost) }).SingleOrDefaultAsync(ct);
        int Count(AIApplyRunStatus status) => statuses.FirstOrDefault(x => x.Key == status)?.Count ?? 0;
        var workers = await operations.GetWorkersAsync(clock.GetUtcNow().UtcDateTime.AddSeconds(-options.Value.Observability.WorkerStaleSeconds), ct);
        var confirmed = Count(AIApplyRunStatus.Submitted); var estimated = costs?.EstimatedCost ?? 0;
        return Ok(new ApiResponse<object>(new { queued = Count(AIApplyRunStatus.Queued), processing = Count(AIApplyRunStatus.Processing), waitingForUser = Count(AIApplyRunStatus.WaitingForUser), submitted = confirmed, failed = Count(AIApplyRunStatus.Failed), deadLettered = Count(AIApplyRunStatus.DeadLettered), needsReview = Count(AIApplyRunStatus.NeedsReview), activeWorkers = workers.Count(x => x.Status == "Running"), staleWorkers = workers.Count(x => x.IsStale), browserExecutionSeconds = costs?.BrowserSeconds ?? 0, estimatedCost = estimated, costPerConfirmedApplication = confirmed == 0 ? (decimal?)null : estimated / confirmed }));
    }

    [HttpGet("applications")]
    public async Task<ActionResult<ApiResponse<object>>> Applications([FromQuery] AIApplyRunStatus? status, [FromQuery] string? failureCode, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var query = Period(db.AIApplyApplications.AsNoTracking(), from, to);
        if (status.HasValue) query = query.Where(x => x.Status == status); if (!string.IsNullOrWhiteSpace(failureCode)) query = query.Where(x => x.LastErrorCode == failureCode);
        var size = Math.Clamp(pageSize, 1, 100); var number = Math.Max(1, page); var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).Skip((number - 1) * size).Take(size).Select(x => new { x.Id, x.Status, x.RetryCount, failureCode = x.LastErrorCode, x.FailureClassification, x.FirstFailureAtUtc, x.LastFailureAtUtc, x.DeadLetteredAtUtc, x.CreatedAtUtc }).ToListAsync(ct);
        return Ok(new ApiResponse<object>(new { items, page = number, pageSize = size, total }));
    }

    [HttpGet("failures")]
    public async Task<ActionResult<ApiResponse<object>>> Failures([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var items = await Period(db.AIApplyApplications.AsNoTracking(), from, to).Where(x => x.LastErrorCode != null).GroupBy(x => new { x.LastErrorCode, x.FailureClassification }).Select(x => new { failureCode = x.Key.LastErrorCode, failureClassification = x.Key.FailureClassification, count = x.Count(), lastFailureAtUtc = x.Max(a => a.LastFailureAtUtc) }).OrderByDescending(x => x.count).Take(100).ToListAsync(ct);
        return Ok(new ApiResponse<object>(items));
    }

    [HttpGet("costs")]
    public async Task<ActionResult<ApiResponse<object>>> Costs([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var totals = await Period(db.AIApplyCosts.AsNoTracking(), from, to).GroupBy(_ => 1).Select(x => new { aiRequests = x.Sum(c => c.AIRequestCount), inputTokens = x.Sum(c => c.AIInputTokens), outputTokens = x.Sum(c => c.AIOutputTokens), browserSeconds = x.Sum(c => c.BrowserExecutionSeconds), proxyCost = x.Sum(c => c.ProxyCost), estimatedCost = x.Sum(c => c.EstimatedCost) }).SingleOrDefaultAsync(ct);
        var confirmed = await Period(db.AIApplyApplications.AsNoTracking(), from, to).CountAsync(x => x.Status == AIApplyRunStatus.Submitted, ct); var cost = totals?.estimatedCost ?? 0;
        return Ok(new ApiResponse<object>(new { totals, confirmedSubmissions = confirmed, costPerConfirmedApplication = confirmed == 0 ? (decimal?)null : cost / confirmed }));
    }

    [HttpGet("workers")]
    public async Task<ActionResult<ApiResponse<object>>> Workers(CancellationToken ct) => Ok(new ApiResponse<object>(await operations.GetWorkersAsync(clock.GetUtcNow().UtcDateTime.AddSeconds(-options.Value.Observability.WorkerStaleSeconds), ct)));

    [HttpGet("sites")]
    public async Task<ActionResult<ApiResponse<object>>> Sites(CancellationToken ct)
    {
        var persisted = (await operations.GetSitesAsync(ct)).ToDictionary(x => x.Site);
        var result = new List<object>();
        foreach (var site in Enum.GetValues<JobSiteIdentifier>()) result.Add(persisted.TryGetValue(site, out var state) ? state : new AIApplySiteOperationalSnapshot(site, AIApplyCircuitState.Closed, 0, 0, null, null, null, null));
        return Ok(new ApiResponse<object>(result));
    }

    [HttpPost("sites/{site}/open")]
    public async Task<ActionResult<ApiResponse<object>>> OpenSite(JobSiteIdentifier site, CancellationToken ct) { await siteHealth.OpenCircuitAsync(site, clock.GetUtcNow().UtcDateTime, ct); return Ok(new ApiResponse<object>(new { site, circuitState = "Open" })); }

    [HttpPost("sites/{site}/close")]
    public async Task<ActionResult<ApiResponse<object>>> CloseSite(JobSiteIdentifier site, CancellationToken ct) { await siteHealth.CloseCircuitAsync(site, ct); return Ok(new ApiResponse<object>(new { site, circuitState = "Closed" })); }

    [HttpGet("alerts")]
    public async Task<ActionResult<ApiResponse<object>>> Alerts(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime; var o = options.Value.Observability; var since = now.AddMinutes(-o.AlertEvaluationWindowMinutes);
        var workers = await operations.GetWorkersAsync(now.AddSeconds(-o.WorkerStaleSeconds), ct);
        var apps = db.AIApplyApplications.AsNoTracking().Where(x => x.CreatedAtUtc >= since);
        var total = await apps.CountAsync(ct); var queued = await db.AIApplyApplications.CountAsync(x => x.Status == AIApplyRunStatus.Queued, ct);
        var dead = await apps.CountAsync(x => x.Status == AIApplyRunStatus.DeadLettered, ct); var review = await apps.CountAsync(x => x.Status == AIApplyRunStatus.NeedsReview, ct);
        var retried = await apps.CountAsync(x => x.RetryCount > 0, ct); var browserFailures = await apps.CountAsync(x => x.FailureClassification == AIApplyFailureClassification.SiteUnavailable.ToString() || x.FailureClassification == AIApplyFailureClassification.Transient.ToString(), ct);
        var oldestQueued = await db.AIApplyApplications.AsNoTracking().Where(x => x.Status == AIApplyRunStatus.Queued).MinAsync(x => (DateTime?)x.CreatedAtUtc, ct);
        var cost = await db.AIApplyCosts.AsNoTracking().Where(x => x.CreatedAtUtc >= since).SumAsync(x => x.EstimatedCost, ct); var alerts = new List<object>();
        void Add(string code, string severity, string summary, object evidence, string? site = null) => alerts.Add(new { code, severity, status = "Active", detectedAtUtc = now, summary, evidence, site });
        if (options.Value.Enabled && workers.All(x => x.Status != "Running")) Add("AIAPPLY_NO_HEALTHY_WORKERS", "Critical", "No healthy AI Apply worker is reporting.", new { activeWorkers = 0 });
        if (workers.Any(x => x.IsStale)) Add("AIAPPLY_STALE_WORKER", "Warning", "One or more AI Apply workers are stale.", new { staleWorkers = workers.Count(x => x.IsStale) });
        if (queued >= o.QueueDepthWarningThreshold) Add("AIAPPLY_QUEUE_DEPTH_HIGH", "Warning", "AI Apply queue depth exceeds its configured threshold.", new { queueDepth = queued, threshold = o.QueueDepthWarningThreshold });
        if (oldestQueued.HasValue && now - oldestQueued.Value >= TimeSpan.FromMinutes(o.OldestQueueAgeWarningMinutes)) Add("AIAPPLY_QUEUE_STALLED", "Critical", "The oldest queued AI Apply application exceeds the configured processing age.", new { oldestQueuedAgeMinutes = (now - oldestQueued.Value).TotalMinutes, threshold = o.OldestQueueAgeWarningMinutes });
        if (dead >= o.DeadLetterWarningThreshold) Add("AIAPPLY_DEADLETTER_HIGH", "Critical", "Dead-letter growth exceeds its configured threshold.", new { deadLettered = dead, threshold = o.DeadLetterWarningThreshold });
        if (total > 0 && retried * 100m / total >= o.RetryRateWarningPercent) Add("AIAPPLY_RETRY_RATE_HIGH", "Warning", "AI Apply retry rate is elevated.", new { percent = retried * 100m / total, threshold = o.RetryRateWarningPercent });
        if (total > 0 && review * 100m / total >= o.NeedsReviewWarningPercent) Add("AIAPPLY_NEEDS_REVIEW_RATE_HIGH", "Warning", "AI Apply needs-review rate is elevated.", new { percent = review * 100m / total, threshold = o.NeedsReviewWarningPercent });
        if (total > 0 && review * 100m / total >= o.SubmissionUnconfirmedWarningPercent) Add("AIAPPLY_UNCONFIRMED_RATE_HIGH", "Warning", "AI Apply unconfirmed-submission rate is elevated.", new { percent = review * 100m / total, threshold = o.SubmissionUnconfirmedWarningPercent });
        if (total > 0 && browserFailures * 100m / total >= o.BrowserFailureWarningPercent) Add("AIAPPLY_BROWSER_FAILURE_RATE_HIGH", "Warning", "AI Apply browser failure rate is elevated.", new { percent = browserFailures * 100m / total, threshold = o.BrowserFailureWarningPercent });
        if (total > 0 && cost / total >= o.CostPerApplicationWarning) Add("AIAPPLY_COST_THRESHOLD_EXCEEDED", "Warning", "Estimated AI Apply cost per application exceeds its configured threshold.", new { costPerApplication = cost / total, threshold = o.CostPerApplicationWarning });
        foreach (var state in await operations.GetSitesAsync(ct)) if (state.CircuitState == AIApplyCircuitState.Open) Add("AIAPPLY_SITE_CIRCUIT_OPEN", "Warning", "An AI Apply site circuit is open.", new { state.SampleCount, state.FailureCount }, state.Site.ToString());
        return Ok(new ApiResponse<object>(alerts));
    }

    [HttpPost("applications/{id:guid}/requeue")]
    public async Task<ActionResult<ApiResponse<object>>> Requeue(Guid id, CancellationToken ct)
    {
        var item = await db.AIApplyApplications.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("AI Apply application was not found.");
        if (item.Status == AIApplyRunStatus.NeedsReview || item.SubmissionAttemptedAtUtc.HasValue && !item.SubmissionConfirmedAtUtc.HasValue) throw new ConflictException("An unconfirmed submission requires manual resolution.", "submission_unconfirmed");
        if (item.Status != AIApplyRunStatus.DeadLettered) throw new ConflictException("Only dead-lettered applications can be requeued.", "invalid_application_transition");
        await authorization.RequireAsync(item.UserId, ct: ct); item.Status = AIApplyRunStatus.Queued; item.RetryCount = 0; item.ScheduledAtUtc = clock.GetUtcNow().UtcDateTime; item.DeadLetteredAtUtc = null; item.LeaseOwner = null; item.LeaseExpiresAtUtc = null;
        db.AIApplyExecutionLogs.Add(new AIApplyExecutionLog { ApplicationId = item.Id, EventType = AIApplyExecutionEvent.DeadLetterRequeued, Status = item.Status, MessageCode = "admin_dead_letter_requeue", StartedAtUtc = clock.GetUtcNow().UtcDateTime, CompletedAtUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct); return Ok(new ApiResponse<object>(new { item.Id, item.Status }));
    }

    private static IQueryable<T> Period<T>(IQueryable<T> query, DateTime? from, DateTime? to) where T : BaseEntity { if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= from); if (to.HasValue) query = query.Where(x => x.CreatedAtUtc < to); return query; }
}
