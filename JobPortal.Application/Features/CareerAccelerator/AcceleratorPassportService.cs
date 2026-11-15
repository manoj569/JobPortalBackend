using System.Security.Cryptography;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorPassportService(IAcceleratorStore writes, IAcceleratorPhase2Store phase2,
    IAcceleratorPhase5Store store, AcceleratorService access, IAuditWriter audit, TimeProvider clock)
{
    public async Task<ReadinessResponse> ReadinessAsync(Guid actor, Guid goalId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        _ = await Goal(actor, goalId, ct);
        return await ReadinessCore(actor, goalId, ct);
    }

    public async Task<IReadOnlyList<ReadinessHistoryResponse>> ReadinessHistoryAsync(Guid actor, Guid goalId,
        int page, int pageSize, CancellationToken ct)
    {
        await access.Candidate(actor, ct); AcceleratorService.Page(page, pageSize);
        _ = await Goal(actor, goalId, ct);
        return (await store.ReadinessHistoryAsync(actor, goalId, (page - 1) * pageSize, pageSize, ct))
            .Select(x => new ReadinessHistoryResponse(x.Id, x.CalculatedAtUtc, x.Total, x.Explanation)).ToArray();
    }

    public async Task<SkillPassportResponse> PassportAsync(Guid actor, Guid goalId, CancellationToken ct)
    {
        await access.Candidate(actor, ct);
        var passport = await PassportCore(actor, goalId, ct);
        var share = await store.ShareAsync(actor, goalId, ct);
        return passport with { IsPublic = share?.IsEnabled == true,
            PublicToken = share?.IsEnabled == true ? share.PublicToken : null };
    }

    public async Task<PassportSkillResponse> SkillAsync(Guid actor, Guid goalId, Guid competencyId, CancellationToken ct) =>
        (await PassportAsync(actor, goalId, ct)).Skills.SingleOrDefault(x => x.CompetencyId == competencyId)
            ?? throw new NotFoundException("Passport competency not found.");

    public Task<PassportShareResponse> EnableShareAsync(Guid actor, PassportShareRequest request, CancellationToken ct) =>
        writes.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            _ = await Goal(actor, request.GoalId, ct);
            var share = await store.ShareAsync(actor, request.GoalId, ct);
            if (share is null)
            {
                if (request.ExpectedRevision.HasValue) throw new ConflictException("Passport share revision changed.");
                share = new AcceleratorPassportShare { UserId = actor, GoalId = request.GoalId };
                store.Add(share);
            }
            else if (request.ExpectedRevision != share.Revision) throw new ConflictException("Passport share revision changed.");
            if (!share.IsEnabled)
            {
                share.PublicToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                share.IsEnabled = true; share.EnabledAtUtc = clock.GetUtcNow().UtcDateTime;
                share.RevokedAtUtc = null; share.Revision = Guid.NewGuid();
                await Audit(actor, AuditAction.Publish, share.Id, ct);
            }
            return new PassportShareResponse(true, share.PublicToken, share.Revision);
        }, ct);

    public Task<PassportShareResponse> RevokeShareAsync(Guid actor, PassportShareRequest request, CancellationToken ct) =>
        writes.WriteAsync(async () =>
        {
            await access.Candidate(actor, ct);
            _ = await Goal(actor, request.GoalId, ct);
            var share = await store.ShareAsync(actor, request.GoalId, ct)
                ?? throw new NotFoundException("Passport share not found.");
            if (request.ExpectedRevision != share.Revision) throw new ConflictException("Passport share revision changed.");
            if (share.IsEnabled)
            {
                share.IsEnabled = false; share.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
                share.Revision = Guid.NewGuid();
                await Audit(actor, AuditAction.Update, share.Id, ct);
            }
            return new PassportShareResponse(false, null, share.Revision);
        }, ct);

    public async Task<PublicSkillPassportResponse> PublicAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64 || token.Any(x => !char.IsAsciiHexDigit(x)))
            throw new NotFoundException("Public passport not found.");
        var share = await store.PublicShareAsync(token, ct) ?? throw new NotFoundException("Public passport not found.");
        var passport = await PassportCore(share.UserId, share.GoalId, ct);
        var verified = passport.Skills.Where(x => x.TrustLevel is "AssessmentVerified" or "ProjectDemonstrated" or
            "InterviewDemonstrated").Select(x =>
        {
            var publicEvidence = x.Evidence.Where(e => e.Source is AcceleratorEvidenceSource.AssessmentDemonstrated or
                AcceleratorEvidenceSource.VirtualProject or AcceleratorEvidenceSource.MockInterview).ToArray();
            return x with { Evidence = publicEvidence, EvidenceCount = publicEvidence.Length,
                LastDemonstratedAtUtc = publicEvidence.Length == 0 ? x.LastDemonstratedAtUtc :
                    publicEvidence.Max(e => e.DateUtc) };
        }).ToArray();
        return new PublicSkillPassportResponse(passport.TargetRole, passport.Readiness.Total,
            verified, clock.GetUtcNow().UtcDateTime);
    }

    private async Task<SkillPassportResponse> PassportCore(Guid actor, Guid goalId, CancellationToken ct)
    {
        // Public-token lookup grants access only to this candidate's explicitly shared goal.
        var goal = await Goal(actor, goalId, ct);
        var role = await writes.RoleAsync(goal.RoleId, ct) ?? throw new ConflictException("Target role is missing.");
        var evidence = await phase2.EvidenceAsync(actor, goalId, ct);
        var competencies = (await store.CompetenciesAsync(evidence.Select(x => x.CompetencyId).Distinct().ToArray(), ct))
            .ToDictionary(x => x.Id);
        var skills = evidence.GroupBy(x => x.CompetencyId).Where(x => competencies.ContainsKey(x.Key))
            .Select(group =>
            {
                var verified = group.Where(Verified).OrderByDescending(x => x.Proficiency)
                    .ThenByDescending(x => x.Confidence).ThenByDescending(x => x.EvaluatedAtUtc).ToArray();
                var selected = verified.FirstOrDefault() ?? group.OrderByDescending(x => x.Confidence).First();
                return new PassportSkillResponse(group.Key, competencies[group.Key].SkillNameSnapshot,
                    selected.Proficiency, selected.Confidence, group.Count(), group.Max(x => x.EvaluatedAtUtc),
                    TrustLevel(selected),
                    group.OrderByDescending(x => x.EvaluatedAtUtc).Take(20).Select(x =>
                        new PassportEvidenceResponse(x.Source, x.EvaluatedAtUtc, x.Score, x.Confidence,
                            x.CandidateProjectId, x.InterviewSessionId)).ToArray());
            }).OrderBy(x => x.SkillName, StringComparer.Ordinal).ToArray();
        return new SkillPassportResponse(goalId, role.Name, await ReadinessCore(actor, goalId, ct), skills, false, null);
    }

    private async Task<ReadinessResponse> ReadinessCore(Guid actor, Guid goalId, CancellationToken ct)
    {
        var history = await store.ReadinessHistoryAsync(actor, goalId, 0, 1, ct);
        var latest = history.Count == 0 ? null : history[0];
        return latest is null ? new ReadinessResponse(goalId, CareerReadinessPolicy.Version, 0, 0, 0, 0, 0,
            "Complete a mock interview to establish a readiness snapshot.") :
            new ReadinessResponse(goalId, latest.CalculationVersion, latest.Total, latest.SkillCoverage,
                latest.PracticePerformance, latest.ProjectPerformance, latest.InterviewPerformance,
                new[] { ("Skills", latest.SkillCoverage), ("Practice", latest.PracticePerformance),
                    ("Projects", latest.ProjectPerformance), ("Interviews", latest.InterviewPerformance) }
                    .MinBy(x => x.Item2).Item1);
    }

    private static bool Verified(AcceleratorSkillEvidence evidence) =>
        evidence.Source is AcceleratorEvidenceSource.AssessmentDemonstrated or AcceleratorEvidenceSource.VirtualProject or
            AcceleratorEvidenceSource.MockInterview && evidence.Confidence >= .4m;
    private static string TrustLevel(AcceleratorSkillEvidence evidence) => evidence.Source switch
    {
        AcceleratorEvidenceSource.AssessmentDemonstrated when Verified(evidence) => "AssessmentVerified",
        AcceleratorEvidenceSource.VirtualProject when Verified(evidence) => "ProjectDemonstrated",
        AcceleratorEvidenceSource.MockInterview when Verified(evidence) => "InterviewDemonstrated",
        AcceleratorEvidenceSource.AssignmentDemonstrated => "LearningObserved",
        AcceleratorEvidenceSource.SelfReported => "SelfDeclared",
        _ => "Unverified"
    };
    private async Task<AcceleratorCareerGoal> Goal(Guid actor, Guid id, CancellationToken ct) =>
        await writes.GoalAsync(actor, id, ct) ?? throw new NotFoundException("Career goal not found.");
    private Task Audit(Guid actor, AuditAction action, Guid id, CancellationToken ct) =>
        audit.AppendAsync(new AuditEvent(action, "AcceleratorPassportShare", id.ToString(),
            Actor: new AuditActor(actor, "Candidate")), ct);
}
