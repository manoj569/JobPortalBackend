using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
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
    private static readonly string[] FoundationAssessmentMethods = ["mcq"];

    public async Task BootstrapTaxonomyAsync(DateTime nowUtc, CancellationToken ct)
    {
        // The hosted worker can run in more than one application instance. PostgreSQL's
        // transaction-scoped advisory lock makes this one-time initialization atomic
        // without introducing a new table or weakening the existing checkpoint lease.
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            await BootstrapTaxonomyCoreAsync(nowUtc, ct);
            return;
        }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A transient error can cause this delegate to run again. Detaching state
            // left by a failed attempt makes the database rows, protected by the lock,
            // the sole source of truth for the next idempotent attempt.
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(82419731)", ct);
            await BootstrapTaxonomyCoreAsync(nowUtc, ct);
            await transaction.CommitAsync(ct);
        });
    }

    private async Task BootstrapTaxonomyCoreAsync(DateTime nowUtc, CancellationToken ct)
    {
        var taxonomy = await db.AcceleratorTaxonomies.AsNoTracking()
            .OrderByDescending(x => x.PublishedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var foundationCreated = false;
        if (taxonomy is null)
        {
            taxonomy = await CreateFoundationalTaxonomyAsync(nowUtc, ct);
            foundationCreated = taxonomy is not null;
            // The remaining bootstrap reads the persisted taxonomy through no-tracking
            // queries, so write the new immutable foundation before materializing terms.
            if (foundationCreated) await db.SaveChangesAsync(ct);
        }
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
            var foundation = MarketFoundationTaxonomyPolicy.Definitions
                .SingleOrDefault(x => x.Code == role.Code);
            var market = new MarketRole { AcceleratorRoleId = role.Id, Name = role.Name,
                NormalizedName = normalizedName, Slug = Slug(role.Name, role.Id),
                Family = foundation?.Family ?? "Other", Description = role.Description, CreatedAtUtc = nowUtc };
            db.MarketRoles.Add(market);
            rolesByName.Add(normalizedName, market);
            db.MarketRoleAliases.Add(new MarketRoleAlias { MarketRole = market, Text = role.Name,
                NormalizedText = market.NormalizedName, CreatedAtUtc = nowUtc });
            if (foundation is not null)
                foreach (var alias in foundation.Aliases.Where(x =>
                    MarketExtractionPolicy.Normalize(x) != market.NormalizedName))
                    db.MarketRoleAliases.Add(new MarketRoleAlias { MarketRole = market, Text = alias,
                        NormalizedText = MarketExtractionPolicy.Normalize(alias), CreatedAtUtc = nowUtc });
        }
        if (db.ChangeTracker.HasChanges())
        {
            var checkpoint = await db.MarketProcessingCheckpoints.SingleOrDefaultAsync(
                x => x.Name == CheckpointName, ct);
            if (checkpoint is null)
                db.MarketProcessingCheckpoints.Add(new MarketProcessingCheckpoint
                {
                    Name = CheckpointName, TaxonomyRevision = 1, CreatedAtUtc = nowUtc
                });
            else
            {
                checkpoint.TaxonomyRevision++;
                checkpoint.Revision = Guid.NewGuid();
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<Guid?> CreateFoundationalTaxonomyAsync(DateTime nowUtc, CancellationToken ct)
    {
        // This is a one-time bootstrap, not automatic publication of raw job titles.
        // Only a small reviewed role catalog is used; unfamiliar titles remain discoveries.
        var jobs = await db.Jobs.AsNoTracking().Include(x => x.JobSkills).ThenInclude(x => x.Skill)
            .Where(x => !x.IsDeleted && x.Status == JobStatus.Published && x.PublishedAtUtc <= nowUtc &&
                (x.ExpiresAtUtc == null || x.ExpiresAtUtc > nowUtc) &&
                x.JobSkills.Any(s => !s.IsDeleted)).ToArrayAsync(ct);
        var observations = jobs.SelectMany(job => job.JobSkills.Where(skill => !skill.IsDeleted)
            .Select(skill => new { job.Id, job.CompanyId, skill.SkillId })).ToArray();
        var credibleSkillIds = observations.GroupBy(x => x.SkillId)
            .Where(x => x.Select(y => y.Id).Distinct().Count() >= settings.DiscoveryMinimumJobs &&
                x.Select(y => y.CompanyId).Distinct().Count() >= settings.DiscoveryMinimumCompanies)
            .Select(x => x.Key).ToHashSet();

        // A small installation may not yet meet discovery evidence thresholds. In that
        // case, allow skills from a precisely recognized foundation role only. This gives
        // a new installation a useful, bounded start without promoting skills from an
        // arbitrary one-off title; all other low-evidence skills remain discoveries.
        if (credibleSkillIds.Count == 0)
            foreach (var job in jobs.Where(x => MarketFoundationTaxonomyPolicy.TryResolveRole(x.Title,
                out _)))
                foreach (var skill in job.JobSkills.Where(x => !x.IsDeleted))
                    credibleSkillIds.Add(skill.SkillId);

        var skills = await db.Skills.Where(x => credibleSkillIds.Contains(x.Id))
            .OrderBy(x => x.NormalizedName).Take(200).ToArrayAsync(ct);
        if (skills.Length == 0) return null;

        var skillsById = skills.ToDictionary(x => x.Id);
        var roleInputs = new List<(FoundationRole Definition, Skill[] Requirements)>();
        foreach (var definition in MarketFoundationTaxonomyPolicy.Definitions)
        {
            var requirements = jobs.Where(x => MarketFoundationTaxonomyPolicy.TryResolveRole(x.Title,
                    out var matched) && matched.Code == definition.Code)
                .SelectMany(x => x.JobSkills).Where(x => !x.IsDeleted)
                .GroupBy(x => x.SkillId).OrderByDescending(x => x.Count()).ThenBy(x => x.Key)
                .Select(x => x.Key).Take(8).Where(skillsById.ContainsKey)
                .Select(x => skillsById[x]).ToArray();
            if (requirements.Length > 0) roleInputs.Add((definition, requirements));
        }
        var existing = await db.AcceleratorTaxonomies.SingleOrDefaultAsync(x => x.Version ==
            MarketFoundationTaxonomyPolicy.Version, ct);
        if (existing is not null) return existing.Id;

        var material = string.Join('\n', skills.Select(x => $"S:{x.Id:D}").Concat(roleInputs
            .OrderBy(x => x.Definition.Code).Select(x => $"R:{x.Definition.Code}:{string.Join(',', x.Requirements.Select(s => s.Id).Order())}")));
        var taxonomy = new AcceleratorTaxonomy
        {
            Version = MarketFoundationTaxonomyPolicy.Version,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))),
            PublishedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc
        };
        var competencies = skills.ToDictionary(x => x.Id, x => new AcceleratorCompetency
        {
            Taxonomy = taxonomy, TaxonomyId = taxonomy.Id, Skill = x, SkillId = x.Id,
            SkillNameSnapshot = x.Name, Description = "Canonical skill observed in CareerHarbor job demand.",
            Category = "Market demand", Difficulty = 1,
            AssessmentMethodsJson = JsonSerializer.Serialize(FoundationAssessmentMethods), ResourcesJson = "[]",
            CreatedAtUtc = nowUtc
        });
        foreach (var competency in competencies.Values) taxonomy.Competencies.Add(competency);
        foreach (var input in roleInputs)
        {
            var role = new AcceleratorCareerRole
            {
                Taxonomy = taxonomy, TaxonomyId = taxonomy.Id, Code = input.Definition.Code,
                Name = input.Definition.Name,
                Description = "Canonical role supported by observed CareerHarbor job demand.",
                CreatedAtUtc = nowUtc
            };
            foreach (var requirement in input.Requirements)
                role.Requirements.Add(new AcceleratorRoleRequirement
                {
                    TaxonomyId = taxonomy.Id, Role = role, RoleId = role.Id,
                    Competency = competencies[requirement.Id], CompetencyId = competencies[requirement.Id].Id,
                    ExpectedProficiency = 1, CreatedAtUtc = nowUtc
                });
            taxonomy.Roles.Add(role);
        }
        db.AcceleratorTaxonomies.Add(taxonomy);
        return taxonomy.Id;
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
