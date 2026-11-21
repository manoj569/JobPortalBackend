using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using System.Diagnostics;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed partial class MarketIntelligenceService(IMarketIntelligenceStore store,
    AcceleratorService access, IAuditWriter audit, IUnitOfWork unitOfWork,
    IOptions<MarketIntelligenceOptions> options,
    TimeProvider clock, ILogger<MarketIntelligenceService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<MarketBatchResult> ProcessBatchAsync(int? batchSize, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled) return new(0, 0, 0, 0, 0);
        if (!settings.IsValid()) throw new InvalidOperationException("Invalid market intelligence configuration.");
        var timer = Stopwatch.StartNew();
        var limit = Math.Clamp(batchSize ?? settings.BatchSize, 1, 200);
        // Taxonomy must be available before a checkpoint lease is acquired. Otherwise a
        // fresh installation could mark real jobs as processed without any public paths.
        await store.BootstrapTaxonomyAsync(Now, ct);
        var skills = await store.SkillsAsync(ct);
        var roles = await store.RolesAsync(ct);
        if (skills.Count == 0)
        {
            NoCanonicalTerms(logger, roles.Count);
            return new(0, 0, 0, 0, 0);
        }
        var batch = await store.NextJobsAsync(limit, ct);
        if (batch.Count == 0)
        {
            NoJobsSelected(logger, skills.Count, roles.Count);
            return new(0, 0, 0, 0, 0);
        }
        BatchSelected(logger, batch.Count, skills.Count, roles.Count);
        var processed = 0; var skillsMapped = 0; var rolesMapped = 0;
        var unknownSkills = 0; var unknownRoles = 0;
        try
        {
            foreach (var job in batch)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var extraction = MarketExtractionPolicy.Extract(job, skills, roles);
                    if (!await store.ApplyExtractionAsync(job, extraction, Now, ct)) continue;
                    processed++; skillsMapped += extraction.SkillIds.Length;
                    rolesMapped += extraction.PrimaryRoleId.HasValue ? 1 : 0;
                    unknownSkills += extraction.UnknownSkills.Length;
                    unknownRoles += extraction.UnknownRole is null ? 0 : 1;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    JobProcessingFailed(logger, job.Id, exception);
                    await store.RecordFailureAsync(job.Id, job.CreatedAtUtc, ct);
                }
            }
        }
        finally { await store.CompleteBatchAsync(CancellationToken.None); }
        BatchProcessed(logger, processed, skillsMapped, rolesMapped, unknownSkills, unknownRoles,
            timer.ElapsedMilliseconds);
        return new(processed, skillsMapped, rolesMapped, unknownSkills, unknownRoles);
    }

    public Task<IReadOnlyList<MarketPathCard>> CareerPathsAsync(string? search, string? family,
        string? location, int? experience, string? sort, int page, int pageSize, CancellationToken ct) =>
        PathsAsync(true, search, family, null, location, experience, sort, page, pageSize, ct);

    public Task<IReadOnlyList<MarketPathCard>> SkillPathsAsync(string? search, string? category,
        Guid? roleId, string? location, int? experience, string? sort, int page, int pageSize,
        CancellationToken ct) => PathsAsync(false, search, category, roleId, location, experience,
            sort, page, pageSize, ct);

    private Task<IReadOnlyList<MarketPathCard>> PathsAsync(bool roles, string? search, string? category,
        Guid? roleId, string? location, int? experience, string? sort, int page, int pageSize,
        CancellationToken ct)
    {
        AcceleratorService.Page(page, pageSize);
        if (search?.Length > 100 || category?.Length > 100 || location?.Length > 100 ||
            experience is < 0 or > 10 || sort is not (null or "Recommended" or "MostJobs" or
            "Trending" or "Alphabetical"))
            throw new BadRequestException("Invalid market path filter.");
        return store.PathsAsync(roles, search, category, roleId, location, experience,
            sort ?? "MostJobs", (page - 1) * pageSize, pageSize, Now, ct);
    }

    public Task<MarketPathDetail?> CareerPathAsync(Guid id, CancellationToken ct) =>
        store.DetailAsync(true, id, Now, ct);

    public Task<MarketPathDetail?> SkillPathAsync(Guid id, CancellationToken ct) =>
        store.DetailAsync(false, id, Now, ct);

    public async Task<MarketSearchResult> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 100)
            throw new BadRequestException("Search query must contain 1 to 100 characters.");
        var roles = await CareerPathsAsync(query, null, null, null, "MostJobs", 1, 10, ct);
        var skills = await SkillPathsAsync(query, null, null, null, null, "MostJobs", 1, 10, ct);
        return new(roles, skills);
    }

    public async Task<IReadOnlyList<MarketRecommendation>> RecommendAsync(Guid candidateId,
        bool roles, CancellationToken ct)
    {
        await access.Candidate(candidateId, ct);
        var signals = await store.CandidateSignalsAsync(candidateId, ct);
        var paths = await store.PathsAsync(roles, null, null,
            roles ? null : signals.GoalMarketRoleId, null, null, "MostJobs", 0, 40, Now, ct);
        return paths.Select(path =>
        {
            var goal = roles && signals.GoalMarketRoleId == path.Id;
            var proficiency = signals.VerifiedProficiencyByMarketSkill.GetValueOrDefault(path.Id);
            var gap = !roles && proficiency is < 3;
            var score = Math.Min(50, path.Demand.ActiveJobCount) + (goal ? 100 : 0) +
                (gap ? 20 : 0);
            var reason = goal ? "Matches your active career goal." : gap
                ? "Relevant to your career goal and not yet supported by advanced verified evidence."
                : "Demand is based on active CareerHarbor jobs.";
            return new MarketRecommendation(path, score, reason);
        }).OrderByDescending(x => x.Score).ThenBy(x => x.Path.Name).Take(20).ToArray();
    }

    public async Task<IReadOnlyList<MarketRoleGap>> RoleGapAsync(Guid candidateId,
        Guid roleId, CancellationToken ct)
    {
        await access.Candidate(candidateId, ct);
        var detail = await store.DetailAsync(true, roleId, Now, ct)
            ?? throw new NotFoundException("Career path was not found.");
        var signals = await store.CandidateSignalsAsync(candidateId, ct);
        var requirements = await store.PublishedRoleRequirementsAsync(roleId, ct);
        return detail.Associations.Select(x =>
        {
            var expected = requirements.TryGetValue(x.Id, out var value) ? value : (int?)null;
            var current = signals.VerifiedProficiencyByMarketSkill.GetValueOrDefault(x.Id);
            return new MarketRoleGap(x.Id, x.Name, x.JobCount, x.Percentage, expected,
                current, expected.HasValue ? Math.Max(0, expected.Value - current) : null);
        }).OrderByDescending(x => x.Gap).ThenByDescending(x => x.PercentageOfRoleJobs).ToArray();
    }

    public async Task<MarketProcessingStatus> StatusAsync(Guid adminId, CancellationToken ct)
    { await access.Administrator(adminId, ct); return await store.StatusAsync(ct); }

    public async Task<MarketBatchResult> BackfillAsync(Guid adminId, MarketBackfillRequest request,
        CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        if (request.BatchSize is < 1 or > 200) throw new BadRequestException("Batch size must be 1 to 200.");
        await AuditAsync(adminId, AuditAction.Create, "MarketBackfill", Guid.Empty,
            $"batch-size:{request.BatchSize}", ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await ProcessBatchAsync(request.BatchSize, ct);
    }

    public async Task<IReadOnlyList<MarketDiscoveryItem>> DiscoveriesAsync(Guid adminId, bool roles,
        int page, int pageSize, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); AcceleratorService.Page(page, pageSize);
        return await store.DiscoveriesAsync(roles, (page - 1) * pageSize, pageSize, ct);
    }

    public async Task<MarketDiscoveryItem?> DiscoveryAsync(Guid adminId, bool role, Guid id,
        CancellationToken ct)
    { await access.Administrator(adminId, ct); return await store.DiscoveryAsync(role, id, ct); }

    public async Task ApproveSkillAsync(Guid adminId, Guid id, MarketApproveSkillRequest request,
        CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        ValidateReview(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Length > 100 ||
            request.Description.Length > 2000) throw new BadRequestException("Invalid skill metadata.");
        await RequireEvidenceAsync(false, id, ct);
        await AuditAsync(adminId, AuditAction.Create, "MarketSkill", id, request.Reason, ct);
        await store.ApproveSkillAsync(id, request, Now, ct);
    }

    public async Task ApproveRoleAsync(Guid adminId, Guid id, MarketApproveRoleRequest request,
        CancellationToken ct)
    {
        await access.Administrator(adminId, ct);
        ValidateReview(request.Reason);
        if (string.IsNullOrWhiteSpace(request.Family) || request.Family.Length > 100 ||
            request.Description.Length > 2000) throw new BadRequestException("Invalid role metadata.");
        await RequireEvidenceAsync(true, id, ct);
        await AuditAsync(adminId, AuditAction.Create, "MarketRole", id, request.Reason, ct);
        await store.ApproveRoleAsync(id, request, Now, ct);
    }

    public async Task MergeAsync(Guid adminId, bool role, Guid id, MarketMergeRequest request,
        CancellationToken ct)
    {
        await access.Administrator(adminId, ct); ValidateReview(request.Reason);
        await AuditAsync(adminId, AuditAction.Update, role ? "MarketRoleDiscovery" : "MarketSkillDiscovery",
            id, request.Reason, ct);
        if (role) await store.MergeRoleAsync(id, request, Now, ct);
        else await store.MergeSkillAsync(id, request, Now, ct);
    }

    public async Task ReviewAsync(Guid adminId, bool role, Guid id,
        MarketDiscoveryStatus status, MarketReviewRequest request, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); ValidateReview(request.Reason);
        await AuditAsync(adminId, AuditAction.Update, role ? "MarketRoleDiscovery" : "MarketSkillDiscovery",
            id, request.Reason, ct);
        await store.ReviewAsync(role, id, status, request.Reason.Trim(), ct);
    }

    public async Task MergeCanonicalAsync(Guid adminId, bool role, Guid sourceId,
        MarketMergeRequest request, CancellationToken ct)
    {
        await access.Administrator(adminId, ct); ValidateReview(request.Reason);
        if (sourceId == request.TargetId)
            throw new BadRequestException("Source and target must differ.");
        await AuditAsync(adminId, AuditAction.Update, role ? "MarketRole" : "MarketSkill",
            sourceId, request.Reason, ct);
        await store.MergeCanonicalAsync(role, sourceId, request.TargetId, Now, ct);
    }

    public async Task<MarketAcceleratorRoleMapping> AcceleratorRoleMappingAsync(Guid adminId,Guid id,CancellationToken ct)
    {
        await access.Administrator(adminId,ct);
        return await store.AcceleratorRoleMappingAsync(id,ct) ?? throw new NotFoundException("Market role was not found.");
    }
    public async Task<MarketAcceleratorRoleMapping> MapAcceleratorRoleAsync(Guid adminId,Guid id,MarketMapAcceleratorRoleRequest request,CancellationToken ct)
    {
        await access.Administrator(adminId,ct); ValidateReview(request.Reason);
        if(request.AcceleratorRoleId==Guid.Empty || request.ExpectedRevision==Guid.Empty) throw new BadRequestException("Canonical role and revision are required.");
        await AuditAsync(adminId,AuditAction.Update,"MarketRole",id,request.Reason,ct);
        return await store.MapAcceleratorRoleAsync(id,request,Now,ct);
    }

    public async Task AddAliasAsync(Guid adminId, bool role, Guid id, MarketAliasRequest request,
        CancellationToken ct)
    {
        await access.Administrator(adminId, ct); ValidateReview(request.Reason);
        var normalized = MarketExtractionPolicy.Normalize(request.Text);
        if (normalized.Length is < 2 or > 160) throw new BadRequestException("Invalid alias.");
        await AuditAsync(adminId, AuditAction.Update, role ? "MarketRoleAlias" : "MarketSkillAlias",
            id, request.Reason, ct);
        await store.AddAliasAsync(role, id, request.Text.Trim(), normalized, ct);
    }

    private async Task RequireEvidenceAsync(bool role, Guid id, CancellationToken ct)
    {
        var discovery = await store.DiscoveryAsync(role, id, ct)
            ?? throw new NotFoundException("Discovery was not found.");
        var settings = options.Value;
        if (discovery.DistinctJobCount < settings.DiscoveryMinimumJobs ||
            discovery.DistinctCompanyCount < settings.DiscoveryMinimumCompanies)
            throw new ConflictException("Discovery has insufficient independent job evidence.");
    }

    private Task AuditAsync(Guid adminId, AuditAction action, string entity, Guid id,
        string reason, CancellationToken ct) => audit.AppendAsync(new(action, entity, id.ToString(),
            new Dictionary<string, string?> { ["reason"] = reason }, new(adminId, "Administrator")), ct);

    private static void ValidateReview(string reason)
    { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        throw new BadRequestException("A review reason of up to 1000 characters is required."); }

    [LoggerMessage(8851, LogLevel.Warning, "Market job {JobId} processing failed; next jobs remain available.")]
    private static partial void JobProcessingFailed(ILogger logger, Guid jobId, Exception exception);
    [LoggerMessage(8852, LogLevel.Information,
        "Market batch processed {Jobs} jobs, {Skills} skills, {Roles} roles, {UnknownSkills} skill candidates and {UnknownRoles} role candidates in {ElapsedMilliseconds} ms.")]
    private static partial void BatchProcessed(ILogger logger, int jobs, int skills, int roles,
        int unknownSkills, int unknownRoles, long elapsedMilliseconds);
    [LoggerMessage(8853, LogLevel.Information,
        "Market batch selected {Jobs} jobs using {Skills} canonical skills and {Roles} canonical roles.")]
    private static partial void BatchSelected(ILogger logger, int jobs, int skills, int roles);
    [LoggerMessage(8854, LogLevel.Warning,
        "Market processing skipped because no canonical skills are available; {Roles} canonical roles exist.")]
    private static partial void NoCanonicalTerms(ILogger logger, int roles);
    [LoggerMessage(8855, LogLevel.Information,
        "Market processing selected no jobs; {Skills} canonical skills and {Roles} canonical roles are available.")]
    private static partial void NoJobsSelected(ILogger logger, int skills, int roles);
}
