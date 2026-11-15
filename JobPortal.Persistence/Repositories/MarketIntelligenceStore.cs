using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed partial class MarketIntelligenceStore(JobPortalDbContext db,
    IOptions<MarketIntelligenceOptions>? options = null) : IMarketIntelligenceStore
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly MarketIntelligenceOptions settings = options?.Value ?? new();
    private const string CheckpointName = "career-accelerator-market-v1";

    public async Task BootstrapTaxonomyAsync(DateTime nowUtc, CancellationToken ct)
    {
        var taxonomy = await db.AcceleratorTaxonomies.AsNoTracking()
            .OrderByDescending(x => x.PublishedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (taxonomy is null) return;
        var competencies = await db.AcceleratorCompetencies.AsNoTracking().Include(x => x.Skill)
            .Where(x => x.TaxonomyId == taxonomy && !x.IsDeleted).ToArrayAsync(ct);
        var roles = await db.AcceleratorCareerRoles.AsNoTracking()
            .Where(x => x.TaxonomyId == taxonomy && !x.IsDeleted).ToArrayAsync(ct);
        var skillIds = competencies.Select(x => x.SkillId).ToArray();
        var existingSkills = await db.MarketSkills.Where(x => skillIds.Contains(x.SkillId)).ToArrayAsync(ct);
        var knownSkills = existingSkills.Select(x => x.SkillId).ToHashSet();
        foreach (var market in existingSkills)
        {
            var current = competencies.Single(x => x.SkillId == market.SkillId);
            if (market.CompetencyId != current.Id)
            { market.CompetencyId = current.Id; market.Revision = Guid.NewGuid(); }
        }
        foreach (var competency in competencies.Where(x => !knownSkills.Contains(x.SkillId)))
        {
            var market = new MarketSkill { SkillId = competency.SkillId, CompetencyId = competency.Id,
                Slug = Slug(competency.Skill.Name, competency.SkillId), Category = competency.Category,
                Description = competency.Description, CreatedAtUtc = nowUtc };
            db.MarketSkills.Add(market);
            db.MarketSkillAliases.Add(new MarketSkillAlias { MarketSkill = market,
                Text = competency.Skill.Name, NormalizedText = MarketExtractionPolicy.Normalize(competency.Skill.Name),
                CreatedAtUtc = nowUtc });
        }
        var roleNames = roles.Select(x => MarketExtractionPolicy.Normalize(x.Name)).ToArray();
        var existingRoles = await db.MarketRoles.Where(x => roleNames.Contains(x.NormalizedName)).ToArrayAsync(ct);
        var rolesByName = existingRoles.ToDictionary(x => x.NormalizedName, StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var normalizedName = MarketExtractionPolicy.Normalize(role.Name);
            if (rolesByName.TryGetValue(normalizedName, out var existing))
            {
                if (existing.AcceleratorRoleId != role.Id)
                { existing.AcceleratorRoleId = role.Id; existing.Revision = Guid.NewGuid(); }
                continue;
            }
            var market = new MarketRole { AcceleratorRoleId = role.Id, Name = role.Name,
                NormalizedName = normalizedName, Slug = Slug(role.Name, role.Id),
                Family = "Other", Description = role.Description, CreatedAtUtc = nowUtc };
            db.MarketRoles.Add(market);
            rolesByName.Add(normalizedName, market);
            db.MarketRoleAliases.Add(new MarketRoleAlias { MarketRole = market, Text = role.Name,
                NormalizedText = market.NormalizedName, CreatedAtUtc = nowUtc });
        }
        if (db.ChangeTracker.HasChanges())
        {
            var checkpoint = await db.MarketProcessingCheckpoints.SingleOrDefaultAsync(x => x.Name == CheckpointName, ct);
            if (checkpoint is not null)
            { checkpoint.TaxonomyRevision++; checkpoint.Revision = Guid.NewGuid(); }
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<MarketProcessingStatus> StatusAsync(CancellationToken ct)
    {
        var checkpoint = await db.MarketProcessingCheckpoints.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Name == CheckpointName, ct);
        return checkpoint is null ? new(null, null, 0, 0) :
            new(checkpoint.LastJobId, checkpoint.LastSuccessfulAtUtc,
                checkpoint.JobsProcessed, checkpoint.Failures);
    }

    public async Task<IReadOnlyList<MarketJobSnapshot>> NextJobsAsync(int take, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"MarketProcessingCheckpoints\" (\"Id\", \"Name\", \"Revision\", \"CreatedAtUtc\", \"IsDeleted\", \"JobsProcessed\", \"Failures\", \"TaxonomyRevision\") VALUES ({Guid.NewGuid()}, {CheckpointName}, {Guid.NewGuid()}, {now}, FALSE, 0, 0, 0) ON CONFLICT (\"Name\") DO NOTHING", ct);
        var checkpoint = await db.MarketProcessingCheckpoints.SingleAsync(x => x.Name == CheckpointName, ct);
        var claimed = await db.MarketProcessingCheckpoints.Where(x => x.Id == checkpoint.Id &&
            (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseOwner, owner)
                .SetProperty(x => x.Revision, Guid.NewGuid())
                .SetProperty(x => x.LeaseExpiresAtUtc, now.AddMinutes(5)), ct);
        if (claimed != 1) return [];
        await db.Entry(checkpoint).ReloadAsync(ct);
        var jobs = await AfterCursorQuery(db, checkpoint).Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .Take(take).ToArrayAsync(ct);
        if (jobs.Length == 0)
        {
            checkpoint.LastJobId = null;
            checkpoint.LastJobCreatedAtUtc = null;
            checkpoint.LeaseOwner = null;
            checkpoint.LeaseExpiresAtUtc = null;
            checkpoint.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            return [];
        }
        return jobs.Select(x => new MarketJobSnapshot(x.Id, x.CompanyId, x.CreatedAtUtc, x.Title, x.Description,
            x.Requirements, x.Responsibilities, x.Location, x.PublishedAtUtc, x.ExpiresAtUtc,
            x.JobSkills.Where(s => !s.IsDeleted).Select(s => s.Skill.Name).ToArray(),
            Hash(x))).ToArray();
    }

    internal static IQueryable<Job> AfterCursorQuery(JobPortalDbContext context,
        MarketProcessingCheckpoint checkpoint) => context.Jobs.AsNoTracking()
            .Where(x => !x.IsDeleted && (checkpoint.LastJobId == null ||
                EF.Functions.GreaterThan(ValueTuple.Create(x.CreatedAtUtc, x.Id),
                    ValueTuple.Create(checkpoint.LastJobCreatedAtUtc!.Value, checkpoint.LastJobId.Value))))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id);

    public async Task CompleteBatchAsync(CancellationToken ct)
    {
        await db.MarketProcessingCheckpoints.Where(x => x.Name == CheckpointName && x.LeaseOwner == owner)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseOwner, (Guid?)null)
                .SetProperty(x => x.Revision, Guid.NewGuid())
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null), ct);
    }

    public async Task<IReadOnlyList<MarketTerm>> SkillsAsync(CancellationToken ct) =>
        await db.MarketSkills.AsNoTracking().Include(x => x.Skill).Include(x => x.Aliases)
            .Where(x => !x.IsDeleted && x.Status == MarketConceptStatus.Active)
            .Select(x => new MarketTerm(x.Id, x.Skill.Name, x.Skill.NormalizedName,
                x.Aliases.Where(a => !a.IsDeleted).Select(a => a.Text).ToArray(), true))
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<MarketTerm>> RolesAsync(CancellationToken ct) =>
        await db.MarketRoles.AsNoTracking().Include(x => x.Aliases)
            .Where(x => !x.IsDeleted && x.Status == MarketConceptStatus.Active)
            .Select(x => new MarketTerm(x.Id, x.Name, x.NormalizedName,
                x.Aliases.Where(a => !a.IsDeleted).Select(a => a.Text).ToArray(), true))
            .ToArrayAsync(ct);

    public async Task<bool> ApplyExtractionAsync(MarketJobSnapshot job, MarketExtraction extraction,
        DateTime nowUtc, CancellationToken ct)
    {
        var renewed = await db.MarketProcessingCheckpoints.Where(x => x.Name == CheckpointName &&
            x.LeaseOwner == owner && x.LeaseExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseExpiresAtUtc,
                nowUtc.AddMinutes(5)).SetProperty(x => x.Revision, Guid.NewGuid()), ct);
        if (renewed != 1) throw new ConflictException("Market processing claim expired.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            () => ApplyExtractionCoreAsync(job, extraction, nowUtc, ct));
    }

    private async Task<bool> ApplyExtractionCoreAsync(MarketJobSnapshot job,
        MarketExtraction extraction, DateTime nowUtc, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var checkpoint = await db.MarketProcessingCheckpoints.SingleAsync(x => x.Name == CheckpointName, ct);
        if (checkpoint.LeaseOwner != owner || checkpoint.LeaseExpiresAtUtc <= nowUtc)
            throw new ConflictException("Market processing claim expired.");
        var processed = await db.MarketJobProcessingStates.SingleOrDefaultAsync(x => x.JobId == job.Id, ct);
        if (processed?.ContentHash == job.ContentHash &&
            processed.TaxonomyRevision == checkpoint.TaxonomyRevision)
        {
            checkpoint.LastJobId = job.Id;
            checkpoint.LastJobCreatedAtUtc = job.CreatedAtUtc;
            checkpoint.LastSuccessfulAtUtc = nowUtc;
            checkpoint.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return false;
        }
        var skillMappings = await db.MarketJobSkills.Where(x => x.JobId == job.Id && !x.IsDeleted).ToArrayAsync(ct);
        var requestedSkills = extraction.SkillIds.ToHashSet();
        var structured = extraction.StructuredSkillIds.ToHashSet();
        foreach (var stale in skillMappings.Where(x => !requestedSkills.Contains(x.MarketSkillId)))
        { stale.IsDeleted = true; stale.DeletedAtUtc = nowUtc; }
        foreach (var mapping in skillMappings.Where(x => requestedSkills.Contains(x.MarketSkillId)))
        {
            mapping.LastObservedAtUtc = nowUtc;
            mapping.Source = structured.Contains(mapping.MarketSkillId)
                ? MarketMappingSource.StructuredSource : MarketMappingSource.PhraseRule;
            mapping.Confidence = structured.Contains(mapping.MarketSkillId) ? 1m : 0.8m;
        }
        var roleMappings = await db.MarketJobRoles.Where(x => x.JobId == job.Id && !x.IsDeleted).ToArrayAsync(ct);
        foreach (var stale in roleMappings.Where(x => x.MarketRoleId != extraction.PrimaryRoleId))
        { stale.IsDeleted = true; stale.DeletedAtUtc = nowUtc; }
        // Clear the filtered primary/skill unique slots before inserting replacements.
        // Both saves share this transaction, including the processing checkpoint.
        await db.SaveChangesAsync(ct);
        foreach (var id in requestedSkills.Except(skillMappings.Select(x => x.MarketSkillId)))
            db.MarketJobSkills.Add(new MarketJobSkill { JobId = job.Id, MarketSkillId = id,
                Source = structured.Contains(id) ? MarketMappingSource.StructuredSource : MarketMappingSource.PhraseRule,
                Confidence = structured.Contains(id) ? 1m : 0.8m,
                FirstObservedAtUtc = nowUtc, LastObservedAtUtc = nowUtc, CreatedAtUtc = nowUtc });
        foreach (var retained in roleMappings.Where(x => x.MarketRoleId == extraction.PrimaryRoleId))
            retained.ObservedAtUtc = nowUtc;
        if (extraction.PrimaryRoleId is { } roleId && roleMappings.All(x => x.MarketRoleId != roleId))
            db.MarketJobRoles.Add(new MarketJobRole { JobId = job.Id, MarketRoleId = roleId,
                Source = MarketMappingSource.Alias, Confidence = 1m, IsPrimary = true,
                ObservedAtUtc = nowUtc, CreatedAtUtc = nowUtc });
        foreach (var term in extraction.UnknownSkills.Take(25))
            await ObserveSkillAsync(job, term, nowUtc, ct);
        if (extraction.UnknownRole is { } title)
            await ObserveRoleAsync(job, title, nowUtc, ct);
        if (processed is null)
            db.MarketJobProcessingStates.Add(new MarketJobProcessingState { JobId = job.Id,
                ContentHash = job.ContentHash, TaxonomyRevision = checkpoint.TaxonomyRevision,
                ProcessedAtUtc = nowUtc, CreatedAtUtc = nowUtc });
        else { processed.ContentHash = job.ContentHash;
            processed.TaxonomyRevision = checkpoint.TaxonomyRevision; processed.ProcessedAtUtc = nowUtc; }
        checkpoint.LastJobId = job.Id;
        checkpoint.LastJobCreatedAtUtc = job.CreatedAtUtc;
        checkpoint.LastSuccessfulAtUtc = nowUtc;
        checkpoint.JobsProcessed++;
        checkpoint.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task RecordFailureAsync(Guid jobId, DateTime createdAtUtc, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await db.MarketProcessingCheckpoints.Where(x => x.Name == CheckpointName && x.LeaseOwner == owner)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastJobId, jobId)
                .SetProperty(x => x.LastJobCreatedAtUtc, createdAtUtc)
                .SetProperty(x => x.Revision, Guid.NewGuid())
                .SetProperty(x => x.Failures, x => x.Failures + 1), ct);
    }

    private async Task ObserveSkillAsync(MarketJobSnapshot job, string term, DateTime now, CancellationToken ct)
    {
        var discovery = await db.MarketSkillDiscoveries.SingleOrDefaultAsync(x => x.NormalizedTerm == term, ct);
        if (discovery is null)
        {
            discovery = new MarketSkillDiscovery { RawTerm = term, NormalizedTerm = term,
                FirstSeenAtUtc = now, LastSeenAtUtc = now, CreatedAtUtc = now };
            db.MarketSkillDiscoveries.Add(discovery);
        }
        if (discovery.Status is not (MarketDiscoveryStatus.CollectingEvidence or MarketDiscoveryStatus.PendingReview)) return;
        discovery.LastSeenAtUtc = now;
        discovery.Revision = Guid.NewGuid();
        if (!discovery.SuggestedSkillId.HasValue)
        {
            var structuredName = job.StructuredSkills.FirstOrDefault(x =>
                MarketExtractionPolicy.Normalize(x) == term);
            if (structuredName is not null)
            {
                var normalizedStructuredName = structuredName.ToUpperInvariant();
                discovery.SuggestedSkillId = await db.Skills.Where(x =>
                    x.NormalizedName == normalizedStructuredName)
                    .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            }
        }
        var observation = await db.MarketDiscoveryObservations.SingleOrDefaultAsync(x =>
            x.SkillDiscoveryId == discovery.Id && x.JobId == job.Id, ct);
        var observed = observation is not null;
        if (observation is not null) observation.CompanyId = job.CompanyId;
        else
            db.MarketDiscoveryObservations.Add(new MarketDiscoveryObservation { JobId = job.Id,
                CompanyId = job.CompanyId, SkillDiscoveryId = discovery.Id, ObservedAtUtc = now,
                CreatedAtUtc = now });
        if (discovery.Status == MarketDiscoveryStatus.CollectingEvidence)
        {
            var jobs = await db.MarketDiscoveryObservations.CountAsync(x => x.SkillDiscoveryId == discovery.Id, ct);
            var companies = await db.MarketDiscoveryObservations.Where(x => x.SkillDiscoveryId == discovery.Id)
                .Select(x => x.CompanyId).Distinct().CountAsync(ct);
            var companySeen = await db.MarketDiscoveryObservations.AnyAsync(x => x.SkillDiscoveryId == discovery.Id &&
                x.CompanyId == job.CompanyId, ct);
            if (MarketDiscoveryPolicy.ReadyForReview(jobs + (observed ? 0 : 1),
                companies + (companySeen ? 0 : 1), settings))
                discovery.Status = MarketDiscoveryStatus.PendingReview;
        }
    }

    private async Task ObserveRoleAsync(MarketJobSnapshot job, string title, DateTime now, CancellationToken ct)
    {
        var discovery = await db.MarketRoleDiscoveries.SingleOrDefaultAsync(x => x.NormalizedTitle == title, ct);
        if (discovery is null)
        {
            discovery = new MarketRoleDiscovery { RawTitle = job.Title, NormalizedTitle = title,
                FirstSeenAtUtc = now, LastSeenAtUtc = now, CreatedAtUtc = now };
            db.MarketRoleDiscoveries.Add(discovery);
        }
        if (discovery.Status is not (MarketDiscoveryStatus.CollectingEvidence or MarketDiscoveryStatus.PendingReview)) return;
        discovery.LastSeenAtUtc = now;
        discovery.Revision = Guid.NewGuid();
        var observation = await db.MarketDiscoveryObservations.SingleOrDefaultAsync(x =>
            x.RoleDiscoveryId == discovery.Id && x.JobId == job.Id, ct);
        var observed = observation is not null;
        if (observation is not null) observation.CompanyId = job.CompanyId;
        else
            db.MarketDiscoveryObservations.Add(new MarketDiscoveryObservation { JobId = job.Id,
                CompanyId = job.CompanyId, RoleDiscoveryId = discovery.Id,
                ObservedAtUtc = now, CreatedAtUtc = now });
        if (discovery.Status == MarketDiscoveryStatus.CollectingEvidence)
        {
            var jobs = await db.MarketDiscoveryObservations.CountAsync(x => x.RoleDiscoveryId == discovery.Id, ct);
            var companies = await db.MarketDiscoveryObservations.Where(x => x.RoleDiscoveryId == discovery.Id)
                .Select(x => x.CompanyId).Distinct().CountAsync(ct);
            var companySeen = await db.MarketDiscoveryObservations.AnyAsync(x => x.RoleDiscoveryId == discovery.Id &&
                x.CompanyId == job.CompanyId, ct);
            if (MarketDiscoveryPolicy.ReadyForReview(jobs + (observed ? 0 : 1),
                companies + (companySeen ? 0 : 1), settings))
                discovery.Status = MarketDiscoveryStatus.PendingReview;
        }
    }

    private static string Hash(Job job)
    {
        var value = string.Join('\n', job.CompanyId.ToString("D"), job.Title, job.Description, job.Requirements,
            job.Responsibilities, string.Join('|', job.JobSkills.Select(x => x.Skill.Name).Order(StringComparer.Ordinal)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string Slug(string text, Guid id) =>
        $"{MarketExtractionPolicy.Normalize(text).Replace(' ', '-').ToLowerInvariant()}-{id.ToString("N")[..8]}";
}
