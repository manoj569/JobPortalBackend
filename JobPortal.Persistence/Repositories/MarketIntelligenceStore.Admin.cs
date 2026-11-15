using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed partial class MarketIntelligenceStore
{
    public async Task<IReadOnlyList<MarketDiscoveryItem>> DiscoveriesAsync(bool roles, int skip,
        int take, CancellationToken ct)
    {
        if (roles)
        {
            var rows = await db.MarketRoleDiscoveries.AsNoTracking()
                .Where(x => x.Status == MarketDiscoveryStatus.PendingReview)
                .OrderByDescending(x => x.LastSeenAtUtc).Skip(skip).Take(take).ToArrayAsync(ct);
            return await RoleEvidenceAsync(rows, false, ct);
        }
        var skills = await db.MarketSkillDiscoveries.AsNoTracking()
            .Where(x => x.Status == MarketDiscoveryStatus.PendingReview)
            .OrderByDescending(x => x.LastSeenAtUtc).Skip(skip).Take(take).ToArrayAsync(ct);
        return await SkillEvidenceAsync(skills, false, ct);
    }

    public async Task<MarketDiscoveryItem?> DiscoveryAsync(bool role, Guid id, CancellationToken ct)
    {
        if (role)
        {
            var row = await db.MarketRoleDiscoveries.AsNoTracking().Where(x => x.Id == id).ToArrayAsync(ct);
            return (await RoleEvidenceAsync(row, true, ct)).SingleOrDefault();
        }
        var skill = await db.MarketSkillDiscoveries.AsNoTracking().Where(x => x.Id == id).ToArrayAsync(ct);
        return (await SkillEvidenceAsync(skill, true, ct)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<MarketDiscoveryItem>> SkillEvidenceAsync(
        MarketSkillDiscovery[] rows, bool includeExamples, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var counts = await db.MarketDiscoveryObservations.AsNoTracking()
            .Where(x => x.SkillDiscoveryId.HasValue && ids.Contains(x.SkillDiscoveryId.Value))
            .GroupBy(x => x.SkillDiscoveryId!.Value)
            .Select(x => new { Id = x.Key, Jobs = x.Count(),
                Companies = x.Select(y => y.CompanyId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Id, ct);
        var result = new List<MarketDiscoveryItem>(rows.Length);
        foreach (var row in rows)
        {
            counts.TryGetValue(row.Id, out var evidence);
            Guid[] examples = includeExamples ? await db.MarketDiscoveryObservations.AsNoTracking()
                .Where(x => x.SkillDiscoveryId == row.Id).OrderBy(x => x.ObservedAtUtc)
                .Select(x => x.JobId).Take(5).ToArrayAsync(ct) : [];
            result.Add(new(row.Id, row.RawTerm, evidence?.Jobs ?? 0,
                evidence?.Companies ?? 0,
                row.FirstSeenAtUtc, row.LastSeenAtUtc, row.Status,
                examples, row.SuggestedSkillId));
        }
        return result;
    }

    private async Task<IReadOnlyList<MarketDiscoveryItem>> RoleEvidenceAsync(
        MarketRoleDiscovery[] rows, bool includeExamples, CancellationToken ct)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var counts = await db.MarketDiscoveryObservations.AsNoTracking()
            .Where(x => x.RoleDiscoveryId.HasValue && ids.Contains(x.RoleDiscoveryId.Value))
            .GroupBy(x => x.RoleDiscoveryId!.Value)
            .Select(x => new { Id = x.Key, Jobs = x.Count(),
                Companies = x.Select(y => y.CompanyId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Id, ct);
        var result = new List<MarketDiscoveryItem>(rows.Length);
        foreach (var row in rows)
        {
            counts.TryGetValue(row.Id, out var evidence);
            Guid[] examples = includeExamples ? await db.MarketDiscoveryObservations.AsNoTracking()
                .Where(x => x.RoleDiscoveryId == row.Id).OrderBy(x => x.ObservedAtUtc)
                .Select(x => x.JobId).Take(5).ToArrayAsync(ct) : [];
            result.Add(new(row.Id, row.RawTitle, evidence?.Jobs ?? 0,
                evidence?.Companies ?? 0,
                row.FirstSeenAtUtc, row.LastSeenAtUtc, row.Status,
                examples));
        }
        return result;
    }

    public async Task ApproveSkillAsync(Guid id, MarketApproveSkillRequest request, DateTime nowUtc,
        CancellationToken ct)
    {
        var discovery = await db.MarketSkillDiscoveries.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Skill discovery was not found.");
        if (discovery.Status != MarketDiscoveryStatus.PendingReview)
            throw new ConflictException("Skill discovery has already been reviewed.");
        var skill = discovery.SuggestedSkillId.HasValue
            ? await db.Skills.SingleOrDefaultAsync(x => x.Id == discovery.SuggestedSkillId.Value, ct)
            : await db.Skills.SingleOrDefaultAsync(x => x.NormalizedName == discovery.NormalizedTerm, ct);
        if (skill is null)
        {
            skill = new Skill { Name = discovery.RawTerm, NormalizedName = discovery.NormalizedTerm,
                CreatedAtUtc = nowUtc };
            db.Skills.Add(skill);
        }
        if (await db.MarketSkills.AnyAsync(x => x.SkillId == skill.Id, ct))
            throw new ConflictException("Skill already has a market concept. Merge the discovery instead.");
        if (request.CompetencyId is { } competencyId && !await db.AcceleratorCompetencies
            .AnyAsync(x => x.Id == competencyId && x.SkillId == skill.Id, ct))
            throw new ConflictException("Competency must reference the same canonical skill.");
        var market = new MarketSkill { Skill = skill, SkillId = skill.Id,
            Slug = Slug(skill.Name, skill.Id), Category = request.Category.Trim(),
            Description = request.Description.Trim(), CompetencyId = request.CompetencyId,
            CreatedAtUtc = nowUtc };
        db.MarketSkills.Add(market);
        db.MarketSkillAliases.Add(new MarketSkillAlias { MarketSkill = market,
            Text = skill.Name, NormalizedText = discovery.NormalizedTerm, CreatedAtUtc = nowUtc });
        discovery.Status = MarketDiscoveryStatus.Approved;
        discovery.Revision = Guid.NewGuid();
        discovery.ResolvedMarketSkillId = market.Id;
        discovery.ReviewReason = request.Reason.Trim();
        await InvalidateObservedJobsAsync(false, id, ct);
        await BumpTaxonomyRevisionAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveRoleAsync(Guid id, MarketApproveRoleRequest request, DateTime nowUtc,
        CancellationToken ct)
    {
        var discovery = await db.MarketRoleDiscoveries.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Role discovery was not found.");
        if (discovery.Status != MarketDiscoveryStatus.PendingReview)
            throw new ConflictException("Role discovery has already been reviewed.");
        if (request.AcceleratorRoleId is { } roleId && !await db.AcceleratorCareerRoles.AnyAsync(x => x.Id == roleId, ct))
            throw new ConflictException("Accelerator role does not exist.");
        if (await db.MarketRoles.AnyAsync(x => x.NormalizedName == discovery.NormalizedTitle, ct))
            throw new ConflictException("Role already exists. Map the discovery instead.");
        var market = new MarketRole { Name = discovery.RawTitle, NormalizedName = discovery.NormalizedTitle,
            Slug = Slug(discovery.RawTitle, id), Family = request.Family.Trim(),
            Description = request.Description.Trim(), AcceleratorRoleId = request.AcceleratorRoleId,
            CreatedAtUtc = nowUtc };
        db.MarketRoles.Add(market);
        db.MarketRoleAliases.Add(new MarketRoleAlias { MarketRole = market, Text = market.Name,
            NormalizedText = market.NormalizedName, CreatedAtUtc = nowUtc });
        discovery.Status = MarketDiscoveryStatus.Approved;
        discovery.Revision = Guid.NewGuid();
        discovery.ResolvedMarketRoleId = market.Id;
        discovery.ReviewReason = request.Reason.Trim();
        await InvalidateObservedJobsAsync(true, id, ct);
        await BumpTaxonomyRevisionAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MergeSkillAsync(Guid id, MarketMergeRequest request, DateTime nowUtc,
        CancellationToken ct)
    {
        var discovery = await db.MarketSkillDiscoveries.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Skill discovery was not found.");
        if (discovery.Status != MarketDiscoveryStatus.PendingReview)
            throw new ConflictException("Skill discovery has already been reviewed.");
        var target = await db.MarketSkills.SingleOrDefaultAsync(x => x.Id == request.TargetId &&
            x.Status == MarketConceptStatus.Active && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Target skill was not found.");
        await StageAliasAsync(false, target.Id, discovery.RawTerm, discovery.NormalizedTerm, ct);
        discovery.Status = MarketDiscoveryStatus.Merged;
        discovery.Revision = Guid.NewGuid();
        discovery.ResolvedMarketSkillId = target.Id;
        discovery.ReviewReason = request.Reason.Trim();
        discovery.UpdatedAtUtc = nowUtc;
        await InvalidateObservedJobsAsync(false, id, ct);
        await BumpTaxonomyRevisionAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MergeRoleAsync(Guid id, MarketMergeRequest request, DateTime nowUtc,
        CancellationToken ct)
    {
        var discovery = await db.MarketRoleDiscoveries.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Role discovery was not found.");
        if (discovery.Status != MarketDiscoveryStatus.PendingReview)
            throw new ConflictException("Role discovery has already been reviewed.");
        var target = await db.MarketRoles.SingleOrDefaultAsync(x => x.Id == request.TargetId &&
            x.Status == MarketConceptStatus.Active && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Target role was not found.");
        await StageAliasAsync(true, target.Id, discovery.RawTitle, discovery.NormalizedTitle, ct);
        discovery.Status = MarketDiscoveryStatus.Merged;
        discovery.Revision = Guid.NewGuid();
        discovery.ResolvedMarketRoleId = target.Id;
        discovery.ReviewReason = request.Reason.Trim();
        discovery.UpdatedAtUtc = nowUtc;
        await InvalidateObservedJobsAsync(true, id, ct);
        await BumpTaxonomyRevisionAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MergeCanonicalAsync(bool role, Guid sourceId, Guid targetId,
        DateTime nowUtc, CancellationToken ct)
    {
        if (sourceId == targetId) throw new ConflictException("A concept cannot merge into itself.");
        if (role)
        {
            var source = await db.MarketRoles.Include(x => x.Aliases)
                .SingleOrDefaultAsync(x => x.Id == sourceId && !x.IsDeleted &&
                    x.Status == MarketConceptStatus.Active, ct)
                ?? throw new NotFoundException("Source role was not found.");
            var target = await db.MarketRoles.SingleOrDefaultAsync(x => x.Id == targetId &&
                !x.IsDeleted && x.Status == MarketConceptStatus.Active, ct)
                ?? throw new NotFoundException("Target role was not found.");
            if (source.AcceleratorRoleId.HasValue && source.AcceleratorRoleId != target.AcceleratorRoleId)
                throw new ConflictException("A published Accelerator role cannot be silently merged into another role.");
            foreach (var alias in source.Aliases.Where(x => !x.IsDeleted))
            { alias.MarketRole = target; alias.MarketRoleId = target.Id; }
            source.Status = MarketConceptStatus.Merged;
            source.MergedIntoId = target.Id;
            source.Revision = Guid.NewGuid();
            source.UpdatedAtUtc = nowUtc;
        }
        else
        {
            var source = await db.MarketSkills.Include(x => x.Aliases)
                .SingleOrDefaultAsync(x => x.Id == sourceId && !x.IsDeleted &&
                    x.Status == MarketConceptStatus.Active, ct)
                ?? throw new NotFoundException("Source skill was not found.");
            var target = await db.MarketSkills.SingleOrDefaultAsync(x => x.Id == targetId &&
                !x.IsDeleted && x.Status == MarketConceptStatus.Active, ct)
                ?? throw new NotFoundException("Target skill was not found.");
            if (source.CompetencyId.HasValue && source.CompetencyId != target.CompetencyId)
                throw new ConflictException("A published Accelerator skill path cannot be silently merged into another skill.");
            foreach (var alias in source.Aliases.Where(x => !x.IsDeleted))
            { alias.MarketSkill = target; alias.MarketSkillId = target.Id; }
            source.Status = MarketConceptStatus.Merged;
            source.MergedIntoId = target.Id;
            source.Revision = Guid.NewGuid();
            source.UpdatedAtUtc = nowUtc;
        }
        await BumpTaxonomyRevisionAsync(ct);
        await db.SaveChangesAsync(ct);
        // Old job mappings remain as historical rows. The versioned worker will
        // soft-delete them and create canonical target mappings on reprocessing.
    }

    public async Task ReviewAsync(bool role, Guid id, MarketDiscoveryStatus status,
        string reason, CancellationToken ct)
    {
        if (status is not (MarketDiscoveryStatus.Rejected or MarketDiscoveryStatus.Ignored))
            throw new ConflictException("Invalid discovery review state.");
        if (role)
        {
            var row = await db.MarketRoleDiscoveries.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Role discovery was not found.");
            if (row.Status != MarketDiscoveryStatus.PendingReview)
                throw new ConflictException("Role discovery has already been reviewed.");
            row.Status = status; row.ReviewReason = reason; row.Revision = Guid.NewGuid();
        }
        else
        {
            var row = await db.MarketSkillDiscoveries.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Skill discovery was not found.");
            if (row.Status != MarketDiscoveryStatus.PendingReview)
                throw new ConflictException("Skill discovery has already been reviewed.");
            row.Status = status; row.ReviewReason = reason; row.Revision = Guid.NewGuid();
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task AddAliasAsync(bool role, Guid id, string alias, string normalized,
        CancellationToken ct)
    {
        await StageAliasAsync(role, id, alias, normalized, ct);
        await BumpTaxonomyRevisionAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task StageAliasAsync(bool role, Guid id, string alias, string normalized,
        CancellationToken ct)
    {
        if (role)
        {
            if (!await db.MarketRoles.AnyAsync(x => x.Id == id && x.Status == MarketConceptStatus.Active, ct))
                throw new NotFoundException("Role was not found.");
            var existing = await db.MarketRoleAliases.SingleOrDefaultAsync(x => x.NormalizedText == normalized, ct);
            if (existing is not null)
            {
                if (existing.MarketRoleId != id) throw new ConflictException("Alias belongs to another role.");
                return;
            }
            db.MarketRoleAliases.Add(new MarketRoleAlias { MarketRoleId = id, Text = alias,
                NormalizedText = normalized, CreatedAtUtc = DateTime.UtcNow });
        }
        else
        {
            if (!await db.MarketSkills.AnyAsync(x => x.Id == id && x.Status == MarketConceptStatus.Active, ct))
                throw new NotFoundException("Skill was not found.");
            var existing = await db.MarketSkillAliases.SingleOrDefaultAsync(x => x.NormalizedText == normalized, ct);
            if (existing is not null)
            {
                if (existing.MarketSkillId != id) throw new ConflictException("Alias belongs to another skill.");
                return;
            }
            db.MarketSkillAliases.Add(new MarketSkillAlias { MarketSkillId = id, Text = alias,
                NormalizedText = normalized, CreatedAtUtc = DateTime.UtcNow });
        }
    }

    private async Task BumpTaxonomyRevisionAsync(CancellationToken ct)
    {
        var checkpoint = await db.MarketProcessingCheckpoints.SingleOrDefaultAsync(x => x.Name == CheckpointName, ct);
        if (checkpoint is not null)
        { checkpoint.TaxonomyRevision++; checkpoint.Revision = Guid.NewGuid(); }
    }

    private async Task InvalidateObservedJobsAsync(bool role, Guid discoveryId, CancellationToken ct)
    {
        var ids = db.MarketDiscoveryObservations.Where(x => role
            ? x.RoleDiscoveryId == discoveryId : x.SkillDiscoveryId == discoveryId).Select(x => x.JobId);
        if (!await ids.AnyAsync(ct)) return;
        await db.MarketJobProcessingStates.Where(x => ids.Contains(x.JobId)).ExecuteDeleteAsync(ct);
    }
}
