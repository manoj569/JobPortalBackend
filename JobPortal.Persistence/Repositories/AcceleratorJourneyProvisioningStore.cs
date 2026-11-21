using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorJourneyProvisioningStore(JobPortalDbContext db,IOptions<MarketIntelligenceOptions>? options=null) : IAcceleratorJourneyProvisioningStore
{
    private readonly MarketIntelligenceOptions settings=options?.Value ?? new();
    public Task<ProvisionAcceleratorJourneyResponse> CheckEligibilityAsync(Guid actor,Guid? marketPathId,Guid? marketSkillPathId,DateTime now,CancellationToken ct) =>
        PrepareAsync(actor,new(Guid.Empty,marketPathId,marketSkillPathId),now,true,ct);
    public Task<ProvisionAcceleratorJourneyResponse> EnsureAsync(Guid actor,ProvisionAcceleratorJourneyRequest request,DateTime now,CancellationToken ct) =>
        PrepareAsync(actor,request,now,false,ct);
    private async Task<ProvisionAcceleratorJourneyResponse> PrepareAsync(Guid actor,ProvisionAcceleratorJourneyRequest request,DateTime now,bool readOnly,CancellationToken ct)
    {
        // Caller holds AcceleratorStore's cross-instance lock and transaction first. The second
        // lock also serializes canonical links/checkpoint revisions with the market bootstrap.
        if(!readOnly && db.Database.ProviderName=="Npgsql.EntityFrameworkCore.PostgreSQL")
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(82419731)",ct);
        var previous=readOnly ? null : await db.AcceleratorJourneyProvisioningReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==actor && x.RequestKey==request.RequestKey,ct);
        if(previous is not null)
        {
            if(previous.MarketRoleId!=request.MarketPathId || previous.MarketSkillId!=request.MarketSkillPathId)
                throw new ConflictException("Provisioning request key belongs to another Market Path.");
            return await ReplayAsync(previous,now,ct);
        }

        MarketRole? marketRole=null; MarketSkill? marketSkill=null; AcceleratorCareerRole? role=null;
        Guid taxonomyId; List<Requirement> requirements;
        var createRole=false;
        if(request.MarketPathId.HasValue)
        {
            marketRole=await db.MarketRoles.Include(x=>x.Aliases).SingleOrDefaultAsync(x=>x.Id==request.MarketPathId && !x.IsDeleted && x.Status==MarketConceptStatus.Active,ct);
            if(marketRole is null) throw new NotFoundException("Active Market Career Path not found.");
            if(marketRole.AcceleratorRoleId.HasValue)
            {
                role=await Roles(now).SingleOrDefaultAsync(x=>x.Id==marketRole.AcceleratorRoleId,ct);
                if(role is null) return Unavailable("CuratedCanonicalRoleUnavailable");
            }
            else
            {
                var code=MarketFoundationTaxonomyPolicy.TryResolveRole(marketRole.NormalizedName,out var known)
                    ? known.Code : AcceleratorJourneyProvisioningPolicy.CanonicalCode(marketRole.NormalizedName);
                var slug=AcceleratorJourneyProvisioningPolicy.CanonicalCode(marketRole.Slug);
                var candidates=await Roles(now).Where(x=>x.Code==code || x.Code==slug).ToArrayAsync(ct);
                var latest=await LatestTaxonomy(now,ct);
                var current=candidates.Where(x=>x.TaxonomyId==latest).ToArray();
                if(current.Length==1) role=current[0];
                else if(candidates.Length==1) role=candidates[0];
                else if(candidates.Length>1) return Unavailable("AmbiguousCanonicalRole");
                else
                {
                    var aliases=marketRole.Aliases.Where(x=>!x.IsDeleted).Select(x=>AcceleratorJourneyProvisioningPolicy.CanonicalCode(x.NormalizedText)).Where(x=>x is not null).Distinct().Take(64).ToArray();
                    var matches=await Roles(now).Where(x=>aliases.Contains(x.Code)).ToArrayAsync(ct);
                    if(matches.Length==1) role=matches[0];
                    else if(matches.Length>1) return Unavailable("AmbiguousCanonicalRoleAlias");
                }
                if(role is null)
                {
                    if(code is null) return Unavailable("UnsupportedCanonicalRoleCode");
                    if(!latest.HasValue) return Unavailable("PublishedTaxonomyMissing");
                    role=new AcceleratorCareerRole { TaxonomyId=latest.Value,Code=code,Name=marketRole.Name[..Math.Min(120,marketRole.Name.Length)],
                        Description="System-provisioned from approved canonical Market Intelligence demand; journey-provision-v1.",ConfigurationSource=AcceleratorConfigurationSource.AutoProvisioned };
                    createRole=true;
                }
            }
            taxonomyId=role.TaxonomyId;
            requirements=createRole ? await DemandRequirements(marketRole.Id,taxonomyId,now,ct) :
                await db.AcceleratorRoleRequirements.AsNoTracking().Where(x=>x.RoleId==role.Id && !x.IsDeleted)
                    .OrderBy(x=>x.CompetencyId).Select(x=>new Requirement(x.CompetencyId,x.ExpectedProficiency)).ToListAsync(ct);
            if(requirements.Count==0) return Unavailable(createRole ? "InsufficientCanonicalRoleDemand" : "CanonicalRoleRequirementsMissing") with { CanonicalRoleId=createRole ? null : role.Id };
        }
        else
        {
            marketSkill=await db.MarketSkills.Include(x=>x.Skill).SingleOrDefaultAsync(x=>x.Id==request.MarketSkillPathId && !x.IsDeleted && x.Status==MarketConceptStatus.Active,ct);
            if(marketSkill is null || marketSkill.Skill.IsDeleted) throw new NotFoundException("Active Market Skill Path not found.");
            AcceleratorCompetency? competency;
            if(marketSkill.CompetencyId.HasValue)
                competency=await Competencies(now).SingleOrDefaultAsync(x=>x.Id==marketSkill.CompetencyId && x.SkillId==marketSkill.SkillId,ct);
            else
            {
                var candidates=await Competencies(now).Where(x=>x.SkillId==marketSkill.SkillId).ToArrayAsync(ct);
                var latest=await LatestTaxonomy(now,ct); var current=candidates.Where(x=>x.TaxonomyId==latest).ToArray();
                competency=current.Length==1 ? current[0] : candidates.Length==1 ? candidates[0] : null;
            }
            if(competency is null) return Unavailable("CanonicalSkillMappingMissingOrAmbiguous");
            taxonomyId=competency.TaxonomyId; requirements=[new(competency.Id,3)];
        }
        var competencyIds=requirements.Select(x=>x.Id).ToArray();
        var validIds=await Competencies(now).Where(x=>x.TaxonomyId==taxonomyId && competencyIds.Contains(x.Id)).Select(x=>x.Id).ToArrayAsync(ct);
        if(validIds.Length!=requirements.Count || requirements.Any(x=>x.Target is <1 or >5)) return Unavailable("IncompatibleCanonicalRequirements") with { CanonicalRoleId=createRole ? null : role?.Id };

        var isCareer=marketRole is not null; var roleId=role?.Id; var skillId=marketSkill?.SkillId;
        var paths=db.AcceleratorLearningPaths.Where(x=>!x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published &&
            ((isCareer && x.Type==AcceleratorLearningPathType.Career && x.CanonicalCareerRoleId==roleId) || (!isCareer && x.Type==AcceleratorLearningPathType.Skill && x.CanonicalSkillId==skillId)));
        // Reuse the candidate's existing compatible enrollment first; never replace their progress.
        var enrollment=await db.AcceleratorPathEnrollments.Where(x=>x.UserId==actor && !x.IsDeleted && x.Status!=AcceleratorLearningEnrollmentStatus.Abandoned && paths.Any(p=>p.Id==x.LearningPathId))
            .OrderByDescending(x=>x.LastActivityAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        var path=enrollment is not null ? await paths.SingleAsync(x=>x.Id==enrollment.LearningPathId,ct) :
            await paths.OrderBy(x=>x.ConfigurationSource).ThenBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        var createPath=path is null;
        if(path is null) path=new AcceleratorLearningPath { Type=marketRole is not null ? AcceleratorLearningPathType.Career : AcceleratorLearningPathType.Skill,
            CanonicalCareerRoleId=role?.Id,CanonicalSkillId=marketSkill?.SkillId,Title=marketRole?.Name ?? marketSkill!.Skill.Name,
            Slug=$"auto-{(role is not null ? "career" : "skill")}-{(role?.Id ?? marketSkill!.SkillId):N}-{taxonomyId:N}",Difficulty=1,
            Status=AcceleratorLearningCatalogStatus.Published,ConfigurationSource=AcceleratorConfigurationSource.AutoProvisioned,
            ShortDescription="Assessment-led learning journey from canonical competencies.",Description="AutoProvisioned / journey-provision-v1. Detailed roadmap follows the candidate's real assessment; no generated lesson or completion data." };
        if(marketSkill is not null && !createPath)
        {
            var pathRequirements=await db.AcceleratorLearningPathCompetencyRequirements.AsNoTracking().Where(x=>x.LearningPathId==path.Id && !x.IsDeleted).ToArrayAsync(ct);
            if(!pathRequirements.Any(x=>x.CompetencyId==requirements[0].Id) || pathRequirements.Any(x=>x.Weight<=0)) return Unavailable("CuratedSkillPathRequirementsInvalid");
            requirements=pathRequirements.Select(x=>new Requirement(x.CompetencyId,x.TargetProficiency)).ToList();
            competencyIds=requirements.Select(x=>x.Id).ToArray();
            if(await Competencies(now).CountAsync(x=>x.TaxonomyId==taxonomyId && competencyIds.Contains(x.Id),ct)!=requirements.Count) return Unavailable("CuratedSkillPathTaxonomyMismatch");
        }
        var mapping=createPath ? null : await db.AcceleratorLearningPathAssessments.SingleOrDefaultAsync(x=>x.LearningPathId==path.Id && x.Purpose==AcceleratorLearningPathAssessmentPurpose.Baseline && !x.IsDeleted,ct);
        var template=mapping is not null ? await db.AcceleratorAssessmentTemplates.Include(x=>x.Role).SingleOrDefaultAsync(x=>x.Id==mapping.AssessmentTemplateId && !x.IsDeleted,ct) :
            role is null ? null : await db.AcceleratorAssessmentTemplates.Include(x=>x.Role).Where(x=>x.RoleId==role.Id && x.IsActive && !x.IsDeleted)
                .OrderBy(x=>x.SelectionStrategy==AcceleratorJourneyProvisioningPolicy.ObjectiveSelection).ThenByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        var questions=await db.AcceleratorQuestions.AsNoTracking().Where(x=>x.TaxonomyId==taxonomyId && competencyIds.Contains(x.CompetencyId) && x.IsActive && !x.IsDeleted).ToArrayAsync(ct);
        var compatible=template is not null && template.IsActive && template.TimeLimitMinutes is >=1 and <=240 && template.MaximumQuestions is >=1 and <=100 &&
            (template.TaxonomyId ?? template.Role?.TaxonomyId)==taxonomyId && (!template.RoleId.HasValue || template.RoleId==role?.Id);
        var selected=Select(questions,template?.SelectionStrategy,template?.MaximumQuestions ?? 100);
        if(mapping is not null && (!compatible || selected.Any(x=>!AcceleratorJourneyProvisioningPolicy.ValidQuestion(x)))) return Unavailable("CuratedBaselineAssessmentInvalid") with { CanonicalRoleId=createRole ? null : role?.Id };
        if(mapping is null && (!compatible || selected.Any(x=>!AcceleratorJourneyProvisioningPolicy.ValidQuestion(x)) ||
            !AcceleratorJourneyProvisioningPolicy.SufficientCoverage(requirements.Count,selected.Count(AcceleratorJourneyProvisioningPolicy.ValidQuestion))))
        {
            var version=$"auto-baseline-{(role?.Id ?? requirements[0].Id):N}";
            template=await db.AcceleratorAssessmentTemplates.SingleOrDefaultAsync(x=>x.TaxonomyId==taxonomyId && x.RoleId==(role==null ? null : role.Id) && x.Version==version && !x.IsDeleted,ct);
            if(template is not null && !template.IsActive) return Unavailable("AutoBaselineRetired") with { CanonicalRoleId=createRole ? null : role?.Id };
            selected=Select(questions,AcceleratorJourneyProvisioningPolicy.ObjectiveSelection,100);
            template ??= new AcceleratorAssessmentTemplate { RoleId=role?.Id,TaxonomyId=taxonomyId,Version=version,IsActive=true,
                MaximumQuestions=Math.Clamp(selected.Length,1,100),TimeLimitMinutes=Math.Clamp(selected.Length*2,5,120),SelectionStrategy=AcceleratorJourneyProvisioningPolicy.ObjectiveSelection };
        }
        if(template is null) return Unavailable("BaselineAssessmentUnavailable");
        selected=Select(questions,template.SelectionStrategy,template.MaximumQuestions);
        var covered=selected.Where(AcceleratorJourneyProvisioningPolicy.ValidQuestion).Select(x=>x.CompetencyId).Distinct().ToArray();
        var coverage=AcceleratorAssessmentReadinessPolicy.Evaluate(taxonomyId,competencyIds,questions,template.MaximumQuestions,template.SelectionStrategy);
        if(!coverage.AssessmentReady) return Unavailable("InsufficientObjectiveQuestionCoverage") with { CanonicalRoleId=createRole ? null : role?.Id,InternalCoverage=coverage };
        if(!readOnly && await db.AcceleratorPathEnrollments.AnyAsync(x=>x.UserId==actor && x.RequestKey==request.RequestKey && x.LearningPathId!=path.Id,ct))
            throw new ConflictException("Request key belongs to another enrollment.");

        // No canonical/path mutations occur until all viability checks above pass.
        if(readOnly) return new("ProvisioningRequired",null,null,createPath ? null : path.Id,enrollment?.Id,AcceleratorNextAction.ProvisionJourney,
            requirements.Count,covered.Length,competencyIds.Except(covered).Order().ToArray(),AcceleratorJourneyProvisioningPolicy.Version,createRole ? null : role?.Id,coverage);
        if(createRole)
        {
            db.Add(role!);
            foreach(var requirement in requirements) db.Add(new AcceleratorRoleRequirement { RoleId=role!.Id,TaxonomyId=taxonomyId,CompetencyId=requirement.Id,ExpectedProficiency=requirement.Target });
        }
        var canonicalChanged=false;
        if(marketRole is not null && !marketRole.AcceleratorRoleId.HasValue)
        { marketRole.AcceleratorRoleId=role!.Id; marketRole.Revision=Guid.NewGuid(); canonicalChanged=true; }
        if(marketSkill is not null && !marketSkill.CompetencyId.HasValue)
        { marketSkill.CompetencyId=validIds[0]; marketSkill.Revision=Guid.NewGuid(); canonicalChanged=true; }
        if(canonicalChanged)
        {
            var checkpoint=await db.MarketProcessingCheckpoints.SingleOrDefaultAsync(x=>x.Name=="career-accelerator-market-v1",ct);
            if(checkpoint is null) db.Add(new MarketProcessingCheckpoint { Name="career-accelerator-market-v1",TaxonomyRevision=1 });
            else { checkpoint.TaxonomyRevision++; checkpoint.Revision=Guid.NewGuid(); }
        }
        if(createPath)
        {
            db.Add(path);
            if(marketSkill is not null) foreach(var requirement in requirements)
                db.Add(new AcceleratorLearningPathCompetencyRequirement { LearningPathId=path.Id,CompetencyId=requirement.Id,TargetProficiency=requirement.Target,Weight=1,IsRequired=true });
        }
        if(db.Entry(template).State==EntityState.Detached) db.Add(template);
        if(mapping is null) db.Add(new AcceleratorLearningPathAssessment { LearningPathId=path.Id,AssessmentTemplateId=template.Id,Purpose=AcceleratorLearningPathAssessmentPurpose.Baseline });
        if(enrollment is null)
        {
            var weekly=await db.AcceleratorProfiles.Where(x=>x.UserId==actor && !x.IsDeleted).Select(x=>(int?)x.WeeklyStudyMinutes).SingleOrDefaultAsync(ct) ?? 300;
            var enrollmentRequest=new EnrollLearningPathRequest(request.RequestKey,1,3,weekly,now.AddMonths(3));
            enrollment=new AcceleratorPathEnrollment { UserId=actor,LearningPathId=path.Id,RequestKey=request.RequestKey,
                RequestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(enrollmentRequest)))),CurrentLevel=1,TargetLevel=3,WeeklyCommitmentMinutes=weekly,
                TargetCompletionAtUtc=enrollmentRequest.TargetCompletionAtUtc,Status=AcceleratorLearningEnrollmentStatus.Enrolled,EnrolledAtUtc=now,LastActivityAtUtc=now };
            db.Add(enrollment);
        }
        var context=await db.AcceleratorJourneyContexts.SingleOrDefaultAsync(x=>x.UserId==actor && x.LearningPathEnrollmentId==enrollment.Id && !x.IsDeleted,ct);
        if(context is null)
        { context=new AcceleratorJourneyContext { UserId=actor,TaxonomyId=taxonomyId,ScopeType=AcceleratorJourneyScopeType.LearningPathEnrollment,LearningPathEnrollmentId=enrollment.Id }; db.Add(context); }
        if(context.TaxonomyId!=taxonomyId) throw new ConflictException("Existing enrollment taxonomy cannot change.");
        var receipt=new AcceleratorJourneyProvisioningReceipt { UserId=actor,RequestKey=request.RequestKey,MarketRoleId=request.MarketPathId,MarketSkillId=request.MarketSkillPathId,
            LearningPathId=path.Id,EnrollmentId=enrollment.Id,JourneyContextId=context.Id,PolicyVersion=AcceleratorJourneyProvisioningPolicy.Version,
            RequiredCompetencies=requirements.Count,AssessedCompetencies=covered.Length,UnassessedCompetencyIdsJson=JsonSerializer.Serialize(competencyIds.Except(covered).Order().ToArray()) };
        db.Add(receipt); return Response(receipt);
    }
    private IQueryable<AcceleratorCareerRole> Roles(DateTime now) => db.AcceleratorCareerRoles.Where(x=>!x.IsDeleted && !x.Taxonomy.IsDeleted && x.Taxonomy.PublishedAtUtc<=now);
    private async Task<ProvisionAcceleratorJourneyResponse> ReplayAsync(AcceleratorJourneyProvisioningReceipt receipt,DateTime now,CancellationToken ct)
    {
        var path=await db.AcceleratorLearningPaths.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receipt.LearningPathId && !x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published,ct);
        var context=await db.AcceleratorJourneyContexts.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receipt.JourneyContextId && x.UserId==receipt.UserId && !x.IsDeleted,ct);
        if(path is null || context is null || !await db.AcceleratorPathEnrollments.AnyAsync(x=>x.Id==receipt.EnrollmentId && x.UserId==receipt.UserId && !x.IsDeleted && x.Status!=AcceleratorLearningEnrollmentStatus.Abandoned,ct))
            return Unavailable("ProvisionedPathUnavailable");
        var requirements=path.Type==AcceleratorLearningPathType.Career
            ? await db.AcceleratorRoleRequirements.Where(x=>x.RoleId==path.CanonicalCareerRoleId && x.TaxonomyId==context.TaxonomyId && !x.IsDeleted).Select(x=>x.CompetencyId).ToArrayAsync(ct)
            : await db.AcceleratorLearningPathCompetencyRequirements.Where(x=>x.LearningPathId==path.Id && !x.IsDeleted).Select(x=>x.CompetencyId).ToArrayAsync(ct);
        var mapping=await db.AcceleratorLearningPathAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.LearningPathId==path.Id && x.Purpose==AcceleratorLearningPathAssessmentPurpose.Baseline && !x.IsDeleted,ct);
        var template=mapping is null ? null : await db.AcceleratorAssessmentTemplates.AsNoTracking().Include(x=>x.Role).SingleOrDefaultAsync(x=>x.Id==mapping.AssessmentTemplateId && x.IsActive && !x.IsDeleted,ct);
        if(template is null || (template.TaxonomyId ?? template.Role?.TaxonomyId)!=context.TaxonomyId || template.RoleId.HasValue && template.RoleId!=path.CanonicalCareerRoleId)
            return Unavailable("BaselineConfigurationChanged");
        var questions=await db.AcceleratorQuestions.AsNoTracking().Where(x=>x.TaxonomyId==context.TaxonomyId && requirements.Contains(x.CompetencyId) && x.IsActive && !x.IsDeleted).ToArrayAsync(ct);
        var selected=Select(questions,template.SelectionStrategy,template.MaximumQuestions);
        var covered=selected.Where(AcceleratorJourneyProvisioningPolicy.ValidQuestion).Select(x=>x.CompetencyId).Distinct().ToArray();
        if(selected.Any(x=>!AcceleratorJourneyProvisioningPolicy.ValidQuestion(x)) || !AcceleratorJourneyProvisioningPolicy.SufficientCoverage(requirements.Length,covered.Length))
            return Unavailable("InsufficientObjectiveQuestionCoverage");
        if(await Competencies(now).CountAsync(x=>x.TaxonomyId==context.TaxonomyId && requirements.Contains(x.Id),ct)!=requirements.Length)
            return Unavailable("IncompatibleCanonicalRequirements");
        return Response(receipt) with { RequiredCompetencies=requirements.Length,CoveredCompetencies=covered.Length,UncoveredCompetencyIds=requirements.Except(covered).Order().ToArray() };
    }
    private IQueryable<AcceleratorCompetency> Competencies(DateTime now) => db.AcceleratorCompetencies.Where(x=>!x.IsDeleted && !x.Skill.IsDeleted && !x.Taxonomy.IsDeleted && x.Taxonomy.PublishedAtUtc<=now);
    private Task<Guid?> LatestTaxonomy(DateTime now,CancellationToken ct) => db.AcceleratorTaxonomies.AsNoTracking().Where(x=>!x.IsDeleted && x.PublishedAtUtc<=now)
        .OrderByDescending(x=>x.PublishedAtUtc).ThenBy(x=>x.Id).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
    private async Task<List<Requirement>> DemandRequirements(Guid marketRole,Guid taxonomy,DateTime now,CancellationToken ct)
    {
        var evidence=from role in db.MarketJobRoles join skill in db.MarketJobSkills on role.JobId equals skill.JobId
            join canonical in db.AcceleratorCompetencies on skill.MarketSkill.SkillId equals canonical.SkillId
            where role.MarketRoleId==marketRole && !role.IsDeleted && !skill.IsDeleted && role.Confidence>=.8m && skill.Confidence>=.8m &&
                !role.Job.IsDeleted && !role.Job.IsHidden && role.Job.Status==JobStatus.Published && role.Job.PublishedAtUtc<=now && (role.Job.ExpiresAtUtc==null || role.Job.ExpiresAtUtc>now) &&
                !skill.MarketSkill.IsDeleted && skill.MarketSkill.Status==MarketConceptStatus.Active && !canonical.IsDeleted && canonical.SkillId==skill.MarketSkill.SkillId && canonical.TaxonomyId==taxonomy
            select new { canonical.Id,role.JobId,role.Job.CompanyId };
        return await evidence.GroupBy(x=>x.Id).Select(x=>new { Id=x.Key,Jobs=x.Select(y=>y.JobId).Distinct().Count(),Companies=x.Select(y=>y.CompanyId).Distinct().Count() })
            .Where(x=>x.Jobs>=settings.DiscoveryMinimumJobs && x.Companies>=settings.DiscoveryMinimumCompanies)
            .OrderByDescending(x=>x.Jobs).ThenBy(x=>x.Id).Take(100).Select(x=>new Requirement(x.Id,3)).ToListAsync(ct);
    }
    private static AcceleratorQuestion[] Select(IEnumerable<AcceleratorQuestion> questions,string? strategy,int maximum) =>
        AcceleratorAssessmentReadinessPolicy.SelectQuestions(questions,strategy,maximum);
    private static ProvisionAcceleratorJourneyResponse Response(AcceleratorJourneyProvisioningReceipt row) => new("Ready",null,row.JourneyContextId,row.LearningPathId,row.EnrollmentId,
        AcceleratorNextAction.StartAssessment,row.RequiredCompetencies,row.AssessedCompetencies,JsonSerializer.Deserialize<Guid[]>(row.UnassessedCompetencyIdsJson)!,row.PolicyVersion);
    private static ProvisionAcceleratorJourneyResponse Unavailable(string reason) => new("ProvisioningUnavailable",reason,null,null,null,AcceleratorNextAction.Unavailable,0,0,[],AcceleratorJourneyProvisioningPolicy.Version);
    private sealed record Requirement(Guid Id,int Target);
}
