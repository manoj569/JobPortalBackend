using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed record TalentOpportunity(Guid JobId, string Title, string Company, string? Location,
    WorkplaceType WorkMode, int MarketMatchScore, IReadOnlyList<string> MatchReasons,
    IReadOnlyList<string> SkillMatches, IReadOnlyList<string> SkillGaps, decimal? CareerReadinessScore,
    string EmployerType, string OutreachAvailability);
public sealed record TalentShareCreated(Guid Id, string Token, DateTime ExpiresAtUtc);
public sealed record TalentPublicPackage(string TargetRole, decimal Readiness,
    IReadOnlyList<string> VerifiedSkills, DateTime ExpiresAtUtc);
public sealed record TalentSubmissionView(Guid Id, Guid JobId, string JobTitle, string Company,
    TalentSubmissionStatus Status, DateTime SubmittedAtUtc, string? InterviewMessage);
public sealed record TalentOpportunityQuery(Guid? RoleId, Guid? SkillId, string? Location,
    WorkplaceType? WorkMode, ExperienceLevel? Experience, int? MinimumMatchScore,
    string? Sort, int Page = 1, int PageSize = 20);

public sealed partial class AcceleratorTalentService(JobPortalDbContext db, AcceleratorPassportService passport,
    IAuditWriter audit, TimeProvider clock, IOptions<TalentOutreachOptions> outreachOptions,
    IEnumerable<ICandidateMailboxProvider> mailboxProviders, AcceleratorQuotaService quota)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private TalentOutreachOptions Outreach => outreachOptions.Value;

    private async Task Candidate(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actor && !x.IsDeleted &&
            x.Status == UserStatus.Active && x.Role.Name == "Candidate", ct))
            throw new UnauthorizedException();
    }

    private async Task Employer(Guid actor, Guid companyId, CancellationToken ct)
    {
        if (!await db.Companies.AnyAsync(x => x.Id == companyId && !x.IsDeleted &&
            x.OwnerUserId == actor && x.OwnerUser.Status == UserStatus.Active &&
            x.OwnerUser.Role.Name == "Employer", ct))
            throw new AppException("Employer access required.", 403, "forbidden");
    }

    private Task Audit(Guid actor, AuditAction action, string type, Guid id, string role,
        CancellationToken ct) => audit.AppendAsync(new AuditEvent(action, type, id.ToString(),
            Actor: new AuditActor(actor, role)), ct);

    public async Task<IReadOnlyList<TalentOpportunity>> OpportunitiesAsync(Guid actor,
        TalentOpportunityQuery query, CancellationToken ct)
    {
        await Candidate(actor, ct);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new BadRequestException("Invalid page size.");
        if (query.MinimumMatchScore is < 0 or > 100 || query.Location is { Length: > 100 })
            throw new BadRequestException("Invalid opportunity filters.");
        var goal = await db.AcceleratorCareerGoals.AsNoTracking()
            .Where(x => x.UserId == actor && !x.IsDeleted && x.Status == AcceleratorGoalStatus.Active)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, x.RoleId })
            .FirstOrDefaultAsync(ct);
        var verified = await (from evidence in db.AcceleratorSkillEvidence.AsNoTracking()
            join competency in db.AcceleratorCompetencies.AsNoTracking()
                on evidence.CompetencyId equals competency.Id
            join skill in db.Skills.AsNoTracking() on competency.SkillId equals skill.Id
            where evidence.UserId == actor && !evidence.IsDeleted && evidence.Confidence >= .4m &&
                (evidence.Source == AcceleratorEvidenceSource.AssessmentDemonstrated ||
                 evidence.Source == AcceleratorEvidenceSource.VirtualProject ||
                 evidence.Source == AcceleratorEvidenceSource.MockInterview)
            select skill.Name).Distinct().ToArrayAsync(ct);
        var jobs = PublicJobRepository.AvailableJobs(db.Jobs.AsNoTracking(), Now);
        if (query.RoleId.HasValue) jobs = jobs.Where(x => db.MarketJobRoles.Any(r =>
            r.JobId == x.Id && !r.IsDeleted && r.MarketRoleId == query.RoleId));
        if (query.SkillId.HasValue) jobs = jobs.Where(x => db.MarketJobSkills.Any(s =>
            s.JobId == x.Id && !s.IsDeleted && s.MarketSkillId == query.SkillId));
        if (!string.IsNullOrWhiteSpace(query.Location))
        {
            var location = query.Location.Trim();
            jobs = jobs.Where(x => x.Location != null && x.Location.Contains(location));
        }
        if (query.WorkMode.HasValue) jobs = jobs.Where(x => x.WorkplaceType == query.WorkMode);
        if (query.Experience.HasValue) jobs = jobs.Where(x => x.ExperienceLevel == query.Experience);
        // Bounded recommendation window. Pagination is applied after deterministic scoring.
        var window = await jobs.OrderByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id)
            .Take(500).Select(x => new { x.Id, x.Title, Company = x.Company.Name, x.CompanyId,
                Registered = x.Company.IsVerified,
                x.Location, x.WorkplaceType, x.PublishedAtUtc }).ToArrayAsync(ct);
        var ids = window.Select(x => x.Id).ToArray();
        var requirements = await (from mapping in db.MarketJobSkills.AsNoTracking()
            where ids.Contains(mapping.JobId) && !mapping.IsDeleted
            select new { mapping.JobId, Name = mapping.MarketSkill.Skill.Name }).ToArrayAsync(ct);
        var roles = await db.MarketJobRoles.AsNoTracking().Where(x => ids.Contains(x.JobId) &&
            !x.IsDeleted && x.IsPrimary).Select(x => new { x.JobId, x.MarketRoleId,
                x.MarketRole.AcceleratorRoleId })
            .ToArrayAsync(ct);
        var roleIds = roles.Select(x => x.MarketRoleId).Distinct().ToArray();
        var activeJobIds = PublicJobRepository.AvailableJobs(db.Jobs, Now).Select(x => x.Id);
        var roleDemand = await db.MarketJobRoles.AsNoTracking().Where(x =>
            roleIds.Contains(x.MarketRoleId) && !x.IsDeleted && x.IsPrimary &&
            activeJobIds.Contains(x.JobId))
            .GroupBy(x => x.MarketRoleId).Select(x => new { Id = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var readiness = goal is null ? null : await db.AcceleratorReadinessSnapshots.AsNoTracking()
            .Where(x => x.UserId == actor && x.GoalId == goal.Id && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => (decimal?)x.Total)
            .FirstOrDefaultAsync(ct);
        var scored = window.Select(job =>
        {
            var roleMatches = goal is not null && roles.Any(r => r.JobId == job.Id &&
                r.AcceleratorRoleId == goal.RoleId);
            var match = TalentMatchPolicy.Evaluate(roleMatches,
                requirements.Where(r => r.JobId == job.Id).Select(r => r.Name).ToArray(), verified);
            return new TalentOpportunity(job.Id, job.Title, job.Company, job.Location,
                job.WorkplaceType, match.MarketMatchScore, match.Reasons, match.SkillMatches,
                match.SkillGaps, readiness, job.Registered ? "Registered" : "External",
                job.Registered ? "TalentSubmission" : "MailboxNotConfigured");
        }).Where(x => !query.MinimumMatchScore.HasValue || x.MarketMatchScore >= query.MinimumMatchScore.Value);
        var sorted = query.Sort switch
        {
            "Newest" => scored.OrderByDescending(x => window.Single(j => j.Id == x.JobId).PublishedAtUtc),
            "MarketDemand" => scored.OrderByDescending(x => roles.Where(r => r.JobId == x.JobId)
                .Select(r => roleDemand.GetValueOrDefault(r.MarketRoleId)).DefaultIfEmpty().Max())
                .ThenByDescending(x => x.MarketMatchScore),
            "BestMatch" or null or "" => scored.OrderByDescending(x => x.MarketMatchScore)
                .ThenBy(x => x.Title),
            _ => throw new BadRequestException("Unsupported opportunity sort.")
        };
        return sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
    }

    public async Task<TalentShareCreated> CreateShareAsync(Guid actor, Guid goalId, Guid? jobId,
        int validHours, CancellationToken ct)
    {
        await Candidate(actor, ct);
        if (validHours is < 1 or > 168) throw new BadRequestException("Share expiry must be 1–168 hours.");
        if (!await db.AcceleratorCareerGoals.AnyAsync(x => x.Id == goalId && x.UserId == actor && !x.IsDeleted, ct))
            throw new NotFoundException("Career goal not found.");
        Guid? companyId = null;
        if (jobId.HasValue)
        {
            companyId = await PublicJobRepository.AvailableJobs(db.Jobs, Now)
                .Where(x => x.Id == jobId.Value).Select(x => (Guid?)x.CompanyId).FirstOrDefaultAsync(ct);
            if (!companyId.HasValue) throw new NotFoundException("Job not found.");
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var link = new TalentShareLink { CandidateId = actor, GoalId = goalId,
            JobId = jobId, CompanyId = companyId, TokenHash = HashToken(token),
            ExpiresAtUtc = Now.AddHours(validHours) };
        db.TalentShareLinks.Add(link);
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Create, "TalentShareLink", link.Id, "Candidate", ct);
        return new(link.Id, token, link.ExpiresAtUtc);
    }

    public async Task RevokeShareAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var link = await db.TalentShareLinks.SingleOrDefaultAsync(x => x.Id == id &&
            x.CandidateId == actor && !x.IsDeleted, ct) ?? throw new NotFoundException("Share link not found.");
        if (link.RevokedAtUtc.HasValue) return;
        link.RevokedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Update, "TalentShareLink", id, "Candidate", ct);
    }

    public async Task<TalentPublicPackage> PublicShareAsync(string token, CancellationToken ct)
    {
        if (token.Length != 64 || token.Any(x => !char.IsAsciiHexDigit(x)))
            throw new NotFoundException("Share link not found.");
        var link = await db.TalentShareLinks.SingleOrDefaultAsync(x => x.TokenHash == HashToken(token) &&
            !x.IsDeleted && x.RevokedAtUtc == null && x.ExpiresAtUtc > Now, ct)
            ?? throw new NotFoundException("Share link not found.");
        var result = await passport.PassportAsync(link.CandidateId, link.GoalId, ct);
        link.AccessCount++;
        link.LastAccessedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEvent(AuditAction.Update, "TalentShareLinkAccess",
            link.Id.ToString(), Actor: new AuditActor(null, "PublicViewer")), ct);
        return new(result.TargetRole, result.Readiness.Total,
            result.Skills.Where(x => x.TrustLevel is "AssessmentVerified" or "ProjectDemonstrated" or
                "InterviewDemonstrated").Select(x => x.SkillName).ToArray(), link.ExpiresAtUtc);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
