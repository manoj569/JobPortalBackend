using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed class AcceleratorRecognitionService(IAcceleratorStore writes, IAcceleratorRecognitionStore store,
    AcceleratorJourneyContextService contexts, IAcceleratorJourneyStore journeys, IAcceleratorLearningPathStore learning,
    AcceleratorService access, IAuditWriter audit, TimeProvider clock)
{
    public async Task<EnrollmentRecognitionResponse> EnrollmentAsync(Guid actor, Guid enrollmentId, CancellationToken ct)
    {
        var context = await contexts.GetOrCreateForEnrollmentAsync(actor,enrollmentId,ct);
        var enrollment = await learning.EnrollmentAsync(actor,enrollmentId,ct) ?? throw new NotFoundException("Enrollment not found.");
        return new(enrollment.LearningPathId,enrollment.LearningPath.Title,await ProficiencyAsync(actor,context.Id,ct),
            await AchievementsAsync(actor,context.Id,ct),await CertificateAsync(actor,context.Id,ct));
    }
    public async Task<JourneyProficiencyResponse> ProficiencyAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        var context = await contexts.GetOwnedContextAsync(actor, contextId, ct);
        Guid? pathId = null;
        if (context.LearningPathEnrollmentId.HasValue)
            pathId = (await learning.EnrollmentAsync(actor, context.LearningPathEnrollmentId.Value, ct) ?? throw new NotFoundException("Enrollment not found.")).LearningPathId;
        var policy = await store.PolicyAsync(pathId,ct);
        var signals = await store.SignalsAsync(context,ct,policy?.MinimumEvidenceConfidence ?? 0);
        if (policy is null) return new(contextId,null,"PolicyUnavailable",null,null,signals.Assessments,signals.CompletedModules,signals.TotalModules,[],[]);
        var verified = (await journeys.EvidenceAsync(actor,contextId,ct)).Where(x => x.Confidence >= policy.MinimumEvidenceConfidence && x.Source != AcceleratorEvidenceSource.SelfReported && x.Source != AcceleratorEvidenceSource.ResumeInferred).ToArray();
        var canonical = (await journeys.CompetenciesAsync(verified.Select(x=>x.CompetencyId).Distinct().ToArray(),ct)).ToDictionary(x=>x.Id,x=>x.SkillNameSnapshot);
        verified = verified.Where(x=>canonical.ContainsKey(x.CompetencyId)).ToArray();
        var evidence = verified.Select(x=>x.Id).ToArray();
        var demonstrated = verified.GroupBy(x=>x.CompetencyId).Select(g=>g.OrderByDescending(x=>x.Confidence).ThenByDescending(x=>x.EvaluatedAtUtc).First())
            .Select(x=>new DemonstratedCompetencyResponse(x.CompetencyId,canonical[x.CompetencyId],x.Proficiency,x.Confidence)).ToArray();
        var configured = JsonSerializer.Deserialize<ProficiencyContribution[]>(policy.ComponentsJson) ?? [];
        var components = configured.Select(c =>
        {
            decimal? score = c.Component switch
            {
                AcceleratorProficiencyComponent.Assessment => signals.AssessmentScore,
                AcceleratorProficiencyComponent.Practice => signals.PracticeScore,
                AcceleratorProficiencyComponent.Project => signals.ProjectScore,
                AcceleratorProficiencyComponent.Interview => signals.InterviewScore,
                AcceleratorProficiencyComponent.ModuleCompletion when signals.TotalModules > 0 => decimal.Round(signals.CompletedModules * 100m / signals.TotalModules,2),
                _ => null
            };
            return new ProficiencyComponentResponse(c.Component,c.Weight,score,score.HasValue ? "Available" : "Unavailable");
        }).ToArray();
        var usable = components.Where(x => x.Score.HasValue).ToArray();
        var incomplete = configured.Any(x => x.Required && components.Single(c => c.Component == x.Component).Score == null) || usable.Length == 0 || evidence.Length == 0;
        decimal? overall = incomplete ? null : decimal.Round(usable.Sum(x => x.Score!.Value * x.Weight) / usable.Sum(x => x.Weight),2);
        var band = overall.HasValue ? policy.Bands.Where(x => x.IsActive && !x.IsDeleted && overall >= x.MinimumScore && (overall < x.MaximumScore || x.MaximumScore == 100 && overall == 100)).SingleOrDefault()?.Label : null;
        return new(contextId,policy.Id,incomplete ? "Incomplete" : "Calculated",overall,band,signals.Assessments,signals.CompletedModules,signals.TotalModules,evidence,components,demonstrated);
    }

    public async Task<IReadOnlyList<JourneyAchievementResponse>> AchievementsAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        await contexts.GetOwnedContextAsync(actor,contextId,ct);
        return await AwardDtosAsync(await store.AwardsAsync(actor,contextId,ct),ct);
    }

    public Task<IReadOnlyList<JourneyAchievementResponse>> EvaluateAchievementsAsync(Guid actor, Guid contextId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var result = await ProficiencyAsync(actor,contextId,ct);
        var existing = (await store.AwardsAsync(actor,contextId,ct)).ToList();
        foreach (var definition in await store.DefinitionsAsync(ct))
        {
            var eligible = definition.Rule switch
            {
                AcceleratorAchievementRule.MinimumProficiency => result.Score.HasValue && result.Score >= definition.Threshold && result.EvidenceIds.Count > 0,
                AcceleratorAchievementRule.CompletedModules => result.CompletedModules >= definition.Threshold,
                AcceleratorAchievementRule.CompletedJourney => result.TotalModules > 0 && result.CompletedModules == result.TotalModules && result.CompletedAssessments > 0 && result.Score.HasValue,
                _ => false
            };
            if (!eligible || existing.Any(x => x.AchievementDefinitionId == definition.Id)) continue;
            var award = new CandidateAcceleratorAchievement { UserId = actor,JourneyContextId = contextId,AchievementDefinitionId = definition.Id,AwardedAtUtc = clock.GetUtcNow().UtcDateTime,EvidenceJson = JsonSerializer.Serialize(result) };
            store.Add(award); existing.Add(award);
            await AuditAsync(actor,"CandidateAcceleratorAchievement",award.Id,ct);
        }
        return await AwardDtosAsync(existing,ct);
    },ct);

    public async Task<JourneyCertificateResponse> CertificateAsync(Guid actor, Guid contextId, CancellationToken ct)
    {
        await contexts.GetOwnedContextAsync(actor,contextId,ct);
        return CertificateDto(await store.CertificateAsync(actor,contextId,ct));
    }
    public Task<JourneyCertificateResponse> EvaluateCertificateAsync(Guid actor, Guid contextId, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        var result = await ProficiencyAsync(actor,contextId,ct);
        var record = await store.CertificateAsync(actor,contextId,ct);
        if (record?.IssuedAtUtc is not null) return CertificateDto(record);
        if (!result.PolicyId.HasValue) return CertificateDto(record);
        var context = await contexts.GetOwnedContextAsync(actor,contextId,ct);
        var pathId = context.LearningPathEnrollmentId.HasValue ? (await learning.EnrollmentAsync(actor,context.LearningPathEnrollmentId.Value,ct))?.LearningPathId : null;
        var policy = await store.PolicyAsync(pathId,ct) ?? throw new ConflictException("Proficiency policy is unavailable.");
        var eligible = result.Score.HasValue && result.Score >= policy.CertificateMinimumScore && result.CompletedAssessments >= policy.CertificateMinimumAssessments && result.EvidenceIds.Count > 0 &&
            (!policy.CertificateRequiresModules || result.TotalModules > 0 && result.CompletedModules == result.TotalModules);
        if (record is null) { record = new AcceleratorEnrollmentCertificate { UserId=actor,JourneyContextId=contextId,PolicyId=policy.Id }; store.Add(record); }
        record.PolicyId=policy.Id; record.State=eligible ? AcceleratorCertificateState.Eligible : AcceleratorCertificateState.NotEligible;
        record.EvaluatedAtUtc=clock.GetUtcNow().UtcDateTime; record.Revision=Guid.NewGuid();
        await AuditAsync(actor,"AcceleratorEnrollmentCertificate",record.Id,ct);
        return CertificateDto(record);
    },ct);

    public Task<Guid> CreatePolicyAsync(Guid actor, CreateProficiencyPolicyRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(actor,ct); ValidatePolicy(request);
        if (request.LearningPathId.HasValue && await learning.PathAsync(request.LearningPathId.Value,ct) is null) throw new NotFoundException("Learning path not found.");
        var policy = new AcceleratorSkillProficiencyPolicy { LearningPathId=request.LearningPathId,Code=request.Code,Version=request.Version,IsActive=true,
            ComponentsJson=JsonSerializer.Serialize(request.Components),MinimumEvidenceConfidence=request.MinimumEvidenceConfidence,
            CertificateMinimumAssessments=request.CertificateMinimumAssessments,CertificateMinimumScore=request.CertificateMinimumScore,CertificateRequiresModules=request.CertificateRequiresModules };
        foreach (var band in request.Bands) policy.Bands.Add(new AcceleratorProficiencyBand { Policy=policy,PolicyId=policy.Id,Label=band.Label,MinimumScore=band.MinimumScore,MaximumScore=band.MaximumScore,DisplayOrder=band.DisplayOrder,IsActive=true });
        store.Add(policy); await AuditAsync(actor,"AcceleratorSkillProficiencyPolicy",policy.Id,ct); return policy.Id;
    },ct);
    public Task<Guid> CreateAchievementAsync(Guid actor, CreateAchievementDefinitionRequest request, CancellationToken ct) => writes.WriteAsync(async () =>
    {
        await access.Administrator(actor,ct);
        if (!AcceleratorOptions.Identifier(request.Code) || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length>250 || request.Description is null || request.Description.Length>4000 || !Enum.IsDefined(request.Rule) || request.Threshold is <=0 or >100) throw new BadRequestException("Invalid achievement definition.");
        var definition=new AcceleratorAchievementDefinition { Code=request.Code,Title=request.Title,Description=request.Description,Rule=request.Rule,Threshold=request.Threshold,IsActive=true };
        store.Add(definition); await AuditAsync(actor,"AcceleratorAchievementDefinition",definition.Id,ct); return definition.Id;
    },ct);

    public static void ValidatePolicy(CreateProficiencyPolicyRequest request)
    {
        if (!AcceleratorOptions.Identifier(request.Code) || request.Version<=0 || request.MinimumEvidenceConfidence is <0 or >1 || request.CertificateMinimumAssessments<=0 || request.CertificateMinimumScore is <0 or >100 ||
            request.Components is not { Count: >0 and <=6 } || request.Components.Any(x => x is null || !Enum.IsDefined(x.Component) || x.Weight<=0) || request.Components.Select(x=>x.Component).Distinct().Count()!=request.Components.Count || request.Components.Sum(x=>x.Weight)!=100 ||
            request.Bands is not { Count: >0 and <=20 } || request.Bands.Any(x=>x is null || string.IsNullOrWhiteSpace(x.Label) || x.Label.Length>80 || x.MinimumScore<0 || x.MaximumScore>100 || x.MinimumScore>=x.MaximumScore)) throw new BadRequestException("Invalid proficiency policy.");
        var bands=request.Bands.OrderBy(x=>x.MinimumScore).ToArray();
        if (bands[0].MinimumScore!=0 || bands[^1].MaximumScore!=100 || bands.Zip(bands.Skip(1)).Any(x=>x.First.MaximumScore!=x.Second.MinimumScore)) throw new BadRequestException("Proficiency bands must cover 0–100 without gaps or overlaps.");
    }
    private static JourneyCertificateResponse CertificateDto(AcceleratorEnrollmentCertificate? c) => c is null ? new(null,AcceleratorCertificateState.NotEligible,null,null,null) : new(c.Id,c.State,c.EvaluatedAtUtc,c.IssuedAtUtc,c.CertificateIdentifier);
    private async Task<IReadOnlyList<JourneyAchievementResponse>> AwardDtosAsync(IReadOnlyList<CandidateAcceleratorAchievement> awards,CancellationToken ct)
    {
        var definitions=(await store.DefinitionsByIdsAsync(awards.Select(x=>x.AchievementDefinitionId).Distinct().ToArray(),ct)).ToDictionary(x=>x.Id);
        return awards.Select(x=>new JourneyAchievementResponse(x.Id,x.AchievementDefinitionId,x.AwardedAtUtc,
            definitions[x.AchievementDefinitionId].Code,definitions[x.AchievementDefinitionId].Title,definitions[x.AchievementDefinitionId].Description)).ToArray();
    }
    private Task AuditAsync(Guid actor,string entity,Guid id,CancellationToken ct) => audit.AppendAsync(new AuditEvent(AuditAction.Update,entity,id.ToString(),Actor:new AuditActor(actor,"Accelerator")),ct);
}
