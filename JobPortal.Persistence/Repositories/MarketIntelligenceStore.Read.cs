using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed partial class MarketIntelligenceStore
{
    public async Task<MarketCandidateSignals> CandidateSignalsAsync(Guid candidateId, CancellationToken ct)
    {
        var goalRoleId = await (from goal in db.AcceleratorCareerGoals.AsNoTracking()
            join role in db.MarketRoles.AsNoTracking() on goal.RoleId equals role.AcceleratorRoleId
            where goal.UserId == candidateId && !goal.IsDeleted && goal.Status == AcceleratorGoalStatus.Active
            orderby goal.CreatedAtUtc descending
            select (Guid?)role.Id).FirstOrDefaultAsync(ct);
        var evidence = await (from item in db.AcceleratorSkillEvidence.AsNoTracking()
            join market in db.MarketSkills.AsNoTracking() on item.CompetencyId equals market.CompetencyId
            where item.UserId == candidateId && !item.IsDeleted &&
                item.Source != AcceleratorEvidenceSource.SelfReported &&
                item.Source != AcceleratorEvidenceSource.ResumeInferred
            select new { market.Id, Level = (int)item.Proficiency }).ToArrayAsync(ct);
        return new(goalRoleId, evidence.GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.Max(y => y.Level)));
    }

    public async Task<IReadOnlyDictionary<Guid, int>> PublishedRoleRequirementsAsync(Guid marketRoleId,
        CancellationToken ct)
    {
        var rows = await (from role in db.MarketRoles.AsNoTracking()
            join requirement in db.AcceleratorRoleRequirements.AsNoTracking()
                on role.AcceleratorRoleId equals requirement.RoleId
            join skill in db.MarketSkills.AsNoTracking()
                on requirement.CompetencyId equals skill.CompetencyId
            where role.Id == marketRoleId && !role.IsDeleted
            select new { skill.Id, requirement.ExpectedProficiency }).ToArrayAsync(ct);
        return rows.ToDictionary(x => x.Id, x => x.ExpectedProficiency);
    }

    public async Task<IReadOnlyList<MarketPathCard>> PathsAsync(bool roles, string? search,
        string? category, Guid? relatedRoleId, string? location, int? experience, string sort,
        int skip, int take, DateTime nowUtc, CancellationToken ct)
    {
        var jobs = ActiveJobs(nowUtc, location, experience);
        var term = MarketExtractionPolicy.Normalize(search);
        if (roles)
        {
            var aliases = db.MarketRoleAliases.Where(x => !x.IsDeleted && x.NormalizedText.Contains(term))
                .Select(x => x.MarketRoleId);
            var matchingSkills = db.MarketSkills.Where(x => !x.IsDeleted &&
                x.Status == MarketConceptStatus.Active && (x.Skill.NormalizedName.Contains(term) ||
                db.MarketSkillAliases.Any(a => a.MarketSkillId == x.Id && !a.IsDeleted &&
                    a.NormalizedText.Contains(term)))).Select(x => x.Id);
            var relatedRoles = db.MarketJobRoles.Where(x => !x.IsDeleted && jobs.Any(j => j.Id == x.JobId) &&
                db.MarketJobSkills.Any(s => !s.IsDeleted && s.JobId == x.JobId && matchingSkills.Contains(s.MarketSkillId)))
                .Select(x => x.MarketRoleId);
            var query = db.MarketRoles.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Status == MarketConceptStatus.Active &&
                (term == "" || x.NormalizedName.Contains(term) || aliases.Contains(x.Id) ||
                    relatedRoles.Contains(x.Id)) &&
                (category == null || x.Family == category));
            query = sort switch
            {
                "MostJobs" or "Recommended" => query.OrderByDescending(x => db.MarketJobRoles.Count(m =>
                    !m.IsDeleted && m.MarketRoleId == x.Id && m.IsPrimary && jobs.Any(j => j.Id == m.JobId))).ThenBy(x => x.Name),
                "Trending" => query.OrderByDescending(x => db.MarketJobRoles.Count(m =>
                    !m.IsDeleted && m.MarketRoleId == x.Id && m.IsPrimary && jobs.Any(j => j.Id == m.JobId &&
                    j.PublishedAtUtc >= nowUtc.AddDays(-30)))).ThenBy(x => x.Name),
                _ => query.OrderBy(x => x.Name)
            };
            var page = await query.Skip(skip).Take(take).Select(x => new
            { x.Id, x.Name, x.Family, x.Description, x.AcceleratorRoleId }).ToArrayAsync(ct);
            var ids = page.Select(x => x.Id).ToArray();
            var demand = await RoleDemandAsync(ids, jobs, nowUtc, ct);
            var top = await TopRoleSkillsAsync(ids, jobs, demand, ct);
            return page.Select(x => new MarketPathCard(x.Id, x.Name, x.Family, x.Description,
                demand.GetValueOrDefault(x.Id) ?? EmptyDemand(nowUtc),
                x.AcceleratorRoleId.HasValue ? "Available" : "ComingSoon", JobFilter(true, x.Id),
                top.GetValueOrDefault(x.Id) ?? [])).ToArray();
        }
        else
        {
            var aliases = db.MarketSkillAliases.Where(x => !x.IsDeleted && x.NormalizedText.Contains(term))
                .Select(x => x.MarketSkillId);
            var query = db.MarketSkills.AsNoTracking().Where(x => !x.IsDeleted &&
                x.Status == MarketConceptStatus.Active &&
                (term == "" || x.Skill.NormalizedName.Contains(term) || aliases.Contains(x.Id)) &&
                (category == null || x.Category == category) &&
                (relatedRoleId == null || db.MarketJobSkills.Any(s => !s.IsDeleted && s.MarketSkillId == x.Id &&
                    db.MarketJobRoles.Any(r => !r.IsDeleted && r.JobId == s.JobId && r.MarketRoleId == relatedRoleId))));
            query = sort switch
            {
                "MostJobs" or "Recommended" => query.OrderByDescending(x => db.MarketJobSkills.Count(m =>
                    !m.IsDeleted && m.MarketSkillId == x.Id && jobs.Any(j => j.Id == m.JobId))).ThenBy(x => x.Skill.Name),
                "Trending" => query.OrderByDescending(x => db.MarketJobSkills.Count(m =>
                    !m.IsDeleted && m.MarketSkillId == x.Id && jobs.Any(j => j.Id == m.JobId &&
                    j.PublishedAtUtc >= nowUtc.AddDays(-30)))).ThenBy(x => x.Skill.Name),
                _ => query.OrderBy(x => x.Skill.Name)
            };
            var page = await query.Skip(skip).Take(take).Select(x => new
            { x.Id, Name = x.Skill.Name, x.Category, x.Description, x.CompetencyId }).ToArrayAsync(ct);
            var ids = page.Select(x => x.Id).ToArray();
            var demand = await SkillDemandAsync(ids, jobs, nowUtc, ct);
            var top = await TopSkillRolesAsync(ids, jobs, demand, ct);
            return page.Select(x => new MarketPathCard(x.Id, x.Name, x.Category, x.Description,
                demand.GetValueOrDefault(x.Id) ?? EmptyDemand(nowUtc),
                x.CompetencyId.HasValue ? "Available" : "ComingSoon", JobFilter(false, x.Id),
                top.GetValueOrDefault(x.Id) ?? [])).ToArray();
        }
    }

    public async Task<MarketPathDetail?> DetailAsync(bool role, Guid id, DateTime nowUtc, CancellationToken ct)
    {
        var path = await PathByIdAsync(role, id, nowUtc, ct);
        if (path is null) return null;
        var active = ActiveJobs(nowUtc, null, null);
        if (role)
        {
            var jobIds = db.MarketJobRoles.Where(x => !x.IsDeleted && x.MarketRoleId == id && x.IsPrimary &&
                active.Any(j => j.Id == x.JobId)).Select(x => x.JobId);
            var top = await db.MarketJobSkills.Where(x => !x.IsDeleted && jobIds.Contains(x.JobId))
                .GroupBy(x => new { x.MarketSkillId, x.MarketSkill.Skill.Name })
                .Select(x => new { x.Key.MarketSkillId, x.Key.Name, Count = x.Count() })
                .OrderByDescending(x => x.Count).Take(12).ToArrayAsync(ct);
            var denominator = path.Demand.ActiveJobCount;
            var associations = top.Select(x => new MarketSkillAssociation(x.MarketSkillId, x.Name,
                x.Count, denominator == 0 ? 0 : decimal.Round(100m * x.Count / denominator, 1))).ToArray();
            var aliases = await db.MarketRoleAliases.Where(x => x.MarketRoleId == id && !x.IsDeleted)
                .OrderBy(x => x.Text).Select(x => x.Text).Take(30).ToArrayAsync(ct);
            var related = await db.MarketJobRoles.Where(x => !x.IsDeleted && x.MarketRoleId != id &&
                jobIds.Contains(x.JobId) && !x.MarketRole.IsDeleted &&
                x.MarketRole.Status == MarketConceptStatus.Active)
                .GroupBy(x => new { x.MarketRoleId, x.MarketRole.Name })
                .Select(x => new { x.Key.MarketRoleId, x.Key.Name, Count = x.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Name).Take(12).ToArrayAsync(ct);
            var relatedRoles = related.Select(x => new MarketSkillAssociation(x.MarketRoleId, x.Name,
                x.Count, denominator == 0 ? 0 : decimal.Round(100m * x.Count / denominator, 1)))
                .ToArray();
            return new(path, associations, relatedRoles, await CompaniesAsync(jobIds, ct), aliases);
        }
        else
        {
            var jobIds = db.MarketJobSkills.Where(x => !x.IsDeleted && x.MarketSkillId == id &&
                active.Any(j => j.Id == x.JobId)).Select(x => x.JobId);
            var roles = await db.MarketJobRoles.Where(x => !x.IsDeleted && jobIds.Contains(x.JobId) && x.IsPrimary)
                .GroupBy(x => new { x.MarketRoleId, x.MarketRole.Name })
                .Select(x => new { x.Key.MarketRoleId, x.Key.Name, Count = x.Count() })
                .OrderByDescending(x => x.Count).Take(12).ToArrayAsync(ct);
            var denominator = path.Demand.ActiveJobCount;
            var associations = roles.Select(x => new MarketSkillAssociation(x.MarketRoleId, x.Name,
                x.Count, denominator == 0 ? 0 : decimal.Round(100m * x.Count / denominator, 1))).ToArray();
            var related = await db.MarketJobSkills.Where(x => !x.IsDeleted && x.MarketSkillId != id && jobIds.Contains(x.JobId))
                .GroupBy(x => new { x.MarketSkillId, x.MarketSkill.Skill.Name })
                .Select(x => new { x.Key.MarketSkillId, x.Key.Name, Count = x.Count() })
                .OrderByDescending(x => x.Count).Take(12).ToArrayAsync(ct);
            var aliases = await db.MarketSkillAliases.Where(x => x.MarketSkillId == id && !x.IsDeleted)
                .OrderBy(x => x.Text).Select(x => x.Text).Take(30).ToArrayAsync(ct);
            return new(path, associations, related.Select(x => new MarketSkillAssociation(x.MarketSkillId,
                x.Name, x.Count, denominator == 0 ? 0 : decimal.Round(100m * x.Count / denominator, 1)))
                .ToArray(), await CompaniesAsync(jobIds, ct), aliases);
        }
    }

    private async Task<MarketPathCard?> PathByIdAsync(bool role, Guid id, DateTime now, CancellationToken ct)
    {
        var jobs = ActiveJobs(now, null, null);
        if (role)
        {
            var item = await db.MarketRoles.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted &&
                x.Status == MarketConceptStatus.Active).Select(x => new
                { x.Id, x.Name, x.Family, x.Description, x.AcceleratorRoleId }).SingleOrDefaultAsync(ct);
            if (item is null) return null;
            var demand = await RoleDemandAsync([id], jobs, now, ct);
            return new(item.Id, item.Name, item.Family, item.Description,
                demand.GetValueOrDefault(id) ?? EmptyDemand(now),
                item.AcceleratorRoleId.HasValue ? "Available" : "ComingSoon", JobFilter(true, item.Id),
                (await TopRoleSkillsAsync([id], jobs, demand, ct)).GetValueOrDefault(id) ?? []);
        }
        var skill = await db.MarketSkills.AsNoTracking().Where(x => x.Id == id && !x.IsDeleted &&
            x.Status == MarketConceptStatus.Active).Select(x => new
            { x.Id, Name = x.Skill.Name, x.Category, x.Description, x.CompetencyId }).SingleOrDefaultAsync(ct);
        if (skill is null) return null;
        var skillsDemand = await SkillDemandAsync([id], jobs, now, ct);
        return new(skill.Id, skill.Name, skill.Category, skill.Description,
            skillsDemand.GetValueOrDefault(id) ?? EmptyDemand(now),
            skill.CompetencyId.HasValue ? "Available" : "ComingSoon", JobFilter(false, skill.Id),
            (await TopSkillRolesAsync([id], jobs, skillsDemand, ct)).GetValueOrDefault(id) ?? []);
    }

    private IQueryable<Job> ActiveJobs(DateTime now, string? location, int? experience)
    {
        var query = PublicJobRepository.AvailableJobs(db.Jobs, now);
        if (!string.IsNullOrWhiteSpace(location))
        {
            var pattern = "%" + location.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(x => x.Location != null && EF.Functions.ILike(x.Location, pattern, "\\"));
        }
        if (experience.HasValue) query = query.Where(x => (int)x.ExperienceLevel == experience.Value);
        return query;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<MarketSkillAssociation>>> TopRoleSkillsAsync(
        Guid[] ids, IQueryable<Job> jobs, Dictionary<Guid, MarketDemand> demand,
        CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var aggregates = from role in db.MarketJobRoles
            join skill in db.MarketJobSkills on role.JobId equals skill.JobId
            where !role.IsDeleted && role.IsPrimary && !skill.IsDeleted &&
                ids.Contains(role.MarketRoleId) && jobs.Any(j => j.Id == role.JobId)
            group skill by new { role.MarketRoleId, skill.MarketSkillId, skill.MarketSkill.Skill.Name } into g
            select new { g.Key.MarketRoleId, g.Key.MarketSkillId, g.Key.Name, Count = g.Count() };
        var limit = Math.Min(50000, ids.Length * 1000);
        var rows = await aggregates.OrderBy(x => x.MarketRoleId).ThenByDescending(x => x.Count)
            .Take(limit + 1).ToArrayAsync(ct);
        if (rows.Length > limit) throw new InvalidOperationException(
            "Market role-skill aggregation exceeds its bounded response limit.");
        return rows.GroupBy(x => x.MarketRoleId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<MarketSkillAssociation>)x.OrderByDescending(y => y.Count)
                .ThenBy(y => y.Name).Take(3).Select(y => new MarketSkillAssociation(y.MarketSkillId,
                    y.Name, y.Count, Percent(y.Count, demand.GetValueOrDefault(x.Key)))).ToArray());
    }

    private async Task<Dictionary<Guid, IReadOnlyList<MarketSkillAssociation>>> TopSkillRolesAsync(
        Guid[] ids, IQueryable<Job> jobs, Dictionary<Guid, MarketDemand> demand,
        CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var aggregates = from skill in db.MarketJobSkills
            join role in db.MarketJobRoles on skill.JobId equals role.JobId
            where !skill.IsDeleted && !role.IsDeleted && role.IsPrimary &&
                ids.Contains(skill.MarketSkillId) && jobs.Any(j => j.Id == skill.JobId)
            group role by new { skill.MarketSkillId, role.MarketRoleId, role.MarketRole.Name } into g
            select new { g.Key.MarketSkillId, g.Key.MarketRoleId, g.Key.Name, Count = g.Count() };
        var limit = Math.Min(50000, ids.Length * 1000);
        var rows = await aggregates.OrderBy(x => x.MarketSkillId).ThenByDescending(x => x.Count)
            .Take(limit + 1).ToArrayAsync(ct);
        if (rows.Length > limit) throw new InvalidOperationException(
            "Market skill-role aggregation exceeds its bounded response limit.");
        return rows.GroupBy(x => x.MarketSkillId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<MarketSkillAssociation>)x.OrderByDescending(y => y.Count)
                .ThenBy(y => y.Name).Take(3).Select(y => new MarketSkillAssociation(y.MarketRoleId,
                    y.Name, y.Count, Percent(y.Count, demand.GetValueOrDefault(x.Key)))).ToArray());
    }

    private static decimal Percent(int count, MarketDemand? demand) =>
        demand is null || demand.ActiveJobCount == 0 ? 0 :
            decimal.Round(100m * count / demand.ActiveJobCount, 1);

    private async Task<Dictionary<Guid, MarketDemand>> SkillDemandAsync(Guid[] ids, IQueryable<Job> jobs,
        DateTime now, CancellationToken ct)
    {
        var rows = await db.MarketJobSkills.Where(x => !x.IsDeleted && ids.Contains(x.MarketSkillId) &&
            jobs.Any(j => j.Id == x.JobId)).GroupBy(x => x.MarketSkillId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Companies = g.Select(x => x.Job.CompanyId).Distinct().Count(),
                New7 = g.Count(x => x.Job.PublishedAtUtc >= now.AddDays(-7)),
                New30 = g.Count(x => x.Job.PublishedAtUtc >= now.AddDays(-30)) }).ToArrayAsync(ct);
        var previous = await db.MarketJobSkills.Where(x => !x.IsDeleted && ids.Contains(x.MarketSkillId) &&
            !x.Job.IsDeleted && x.Job.PublishedAtUtc >= now.AddDays(-60) &&
            x.Job.PublishedAtUtc < now.AddDays(-30))
            .GroupBy(x => x.MarketSkillId).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return rows.ToDictionary(x => x.Id, x => new MarketDemand(x.Count, x.Companies, x.New7,
            x.New30, Trend(x.New30, previous.GetValueOrDefault(x.Id)), now));
    }

    private async Task<Dictionary<Guid, MarketDemand>> RoleDemandAsync(Guid[] ids, IQueryable<Job> jobs,
        DateTime now, CancellationToken ct)
    {
        var rows = await db.MarketJobRoles.Where(x => !x.IsDeleted && ids.Contains(x.MarketRoleId) && x.IsPrimary &&
            jobs.Any(j => j.Id == x.JobId)).GroupBy(x => x.MarketRoleId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Companies = g.Select(x => x.Job.CompanyId).Distinct().Count(),
                New7 = g.Count(x => x.Job.PublishedAtUtc >= now.AddDays(-7)),
                New30 = g.Count(x => x.Job.PublishedAtUtc >= now.AddDays(-30)) }).ToArrayAsync(ct);
        var previous = await db.MarketJobRoles.Where(x => !x.IsDeleted && ids.Contains(x.MarketRoleId) && x.IsPrimary &&
            !x.Job.IsDeleted && x.Job.PublishedAtUtc >= now.AddDays(-60) &&
            x.Job.PublishedAtUtc < now.AddDays(-30))
            .GroupBy(x => x.MarketRoleId).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return rows.ToDictionary(x => x.Id, x => new MarketDemand(x.Count, x.Companies, x.New7,
            x.New30, Trend(x.New30, previous.GetValueOrDefault(x.Id)), now));
    }

    private async Task<IReadOnlyList<MarketCompanyDemand>> CompaniesAsync(IQueryable<Guid> jobIds,
        CancellationToken ct)
    {
        var rows = await db.Jobs.Where(x => jobIds.Contains(x.Id))
            .GroupBy(x => new { x.CompanyId, x.Company.Name })
            .Select(x => new { x.Key.CompanyId, x.Key.Name, Count = x.Count() })
            .OrderByDescending(x => x.Count).Take(10).ToArrayAsync(ct);
        return rows.Select(x => new MarketCompanyDemand(x.CompanyId, x.Name, x.Count)).ToArray();
    }

    private static decimal? Trend(int current, int previous) => previous == 0 ? null :
        decimal.Round(100m * (current - previous) / previous, 2);
    private static MarketDemand EmptyDemand(DateTime now) => new(0, 0, 0, 0, null, now);
    private static string JobFilter(bool role, Guid id) => role
        ? $"/api/jobs?marketRoleId={id:D}" : $"/api/jobs?marketSkillId={id:D}";
}
