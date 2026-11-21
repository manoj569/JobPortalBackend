using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorCatalogReadinessStore(JobPortalDbContext db) : IAcceleratorCatalogReadinessStore
{
    private static readonly (string Code,string Name)[] LaunchRoles=[("full-stack-developer","Full Stack Developer"),("backend-developer","Backend Developer"),
        ("frontend-developer","Frontend Developer"),("dotnet-developer",".NET Developer"),("java-developer","Java Developer"),("react-developer","React Developer"),
        ("data-analyst","Data Analyst"),("data-engineer","Data Engineer"),("qa-automation-engineer","QA Automation Engineer"),("devops-engineer","DevOps Engineer"),
        ("cloud-engineer","Cloud Engineer"),("product-manager","Product Manager"),("associate-product-manager","Associate Product Manager"),
        ("business-analyst","Business Analyst"),("ui-ux-designer","UI/UX Designer")];
    private IQueryable<AcceleratorCareerRole> Roles(DateTime now) => db.AcceleratorCareerRoles.AsNoTracking()
        .Where(x=>!x.IsDeleted && !x.Taxonomy.IsDeleted && x.Taxonomy.PublishedAtUtc<=now);
    public async Task<AcceleratorReadinessPage> RolesAsync(AcceleratorReadinessQuery query,DateTime now,CancellationToken ct)
    {
        var roles=Roles(now);
        if(query.Code is not null) roles=roles.Where(x=>x.Code==query.Code);
        if(query.TaxonomyId.HasValue) roles=roles.Where(x=>x.TaxonomyId==query.TaxonomyId);
        if(query.TaxonomyVersion is not null) roles=roles.Where(x=>x.Taxonomy.Version==query.TaxonomyVersion);
        var rows=await Headers(roles.OrderBy(x=>x.Code).ThenBy(x=>x.TaxonomyId).ThenBy(x=>x.Id).Skip((query.ScanPage-1)*query.PageSize).Take(query.PageSize+1)).ToArrayAsync(ct);
        var reports=await BuildAsync(rows.Take(query.PageSize).ToArray(),ct);
        return new(reports.Where(x=>!query.Ready.HasValue || x.AcceleratorReady==query.Ready.Value).ToArray(),rows.Length>query.PageSize ? query.ScanPage+1 : null,reports.Count);
    }
    public async Task<AcceleratorRoleReadiness?> RoleAsync(Guid id,DateTime now,CancellationToken ct)
    { var rows=await Headers(Roles(now).Where(x=>x.Id==id)).ToArrayAsync(ct); return (await BuildAsync(rows,ct)).SingleOrDefault(); }
    public async Task<IReadOnlyList<AcceleratorLaunchReadiness>> LaunchAsync(Guid taxonomyId,DateTime now,CancellationToken ct)
    {
        var codes=LaunchRoles.Select(x=>x.Code).ToArray(); var names=LaunchRoles.Select(x=>x.Name).ToArray();
        var rows=await Headers(Roles(now).Where(x=>x.TaxonomyId==taxonomyId && (codes.Contains(x.Code) || names.Contains(x.Name)))).ToArrayAsync(ct);
        var reports=await BuildAsync(rows,ct);
        return LaunchRoles.Select(x=>new AcceleratorLaunchReadiness(x.Code,x.Name,reports.Any(r=>r.Code==x.Code || r.Name==x.Name),reports.Where(r=>r.Code==x.Code || r.Name==x.Name).ToArray())).ToArray();
    }
    private static IQueryable<RoleHeader> Headers(IQueryable<AcceleratorCareerRole> roles) => roles.Select(x=>new RoleHeader(x.Id,x.Code,x.Name,x.TaxonomyId,x.Taxonomy.Version));

    // Validation needs options/key structure, which is not a persisted validity flag. Fetch that
    // validation inputs once per scan batch, never rubrics/explanations or per-role body queries.
    // Actual prompt text preserves whitespace validation exactly; none of these inputs leave this store.
    private async Task<AcceleratorQuestionAvailability[]> QuestionBank(Guid[] competencyIds,CancellationToken ct)
    {
        var projection=db.AcceleratorQuestions.AsNoTracking()
        .Where(x=>competencyIds.Contains(x.CompetencyId) && x.IsActive && !x.IsDeleted)
        .Select(x=>new AcceleratorQuestion { Id=x.Id,TaxonomyId=x.TaxonomyId,CompetencyId=x.CompetencyId,Type=x.Type,Difficulty=x.Difficulty,Weight=x.Weight,
            IsActive=x.IsActive,Prompt=x.Prompt,OptionsJson=x.OptionsJson,AnswerKeyJson=x.AnswerKeyJson });
        var result=new List<AcceleratorQuestionAvailability>();
        await foreach(var question in projection.AsAsyncEnumerable().WithCancellation(ct)) result.Add(AcceleratorQuestionAvailability.From(question));
        return result.ToArray();
    }
    private async Task<IReadOnlyList<AcceleratorRoleReadiness>> BuildAsync(RoleHeader[] roles,CancellationToken ct)
    {
        if(roles.Length==0) return [];
        var roleIds=roles.Select(x=>x.Id).ToArray();
        var requirements=await db.AcceleratorRoleRequirements.AsNoTracking().Where(x=>roleIds.Contains(x.RoleId) && !x.IsDeleted)
            .Select(x=>new {x.RoleId,x.CompetencyId,x.TaxonomyId,x.ExpectedProficiency}).ToArrayAsync(ct);
        var competencyIds=requirements.Select(x=>x.CompetencyId).Distinct().ToArray();
        var competencies=await db.AcceleratorCompetencies.AsNoTracking().Where(x=>competencyIds.Contains(x.Id))
            .Select(x=>new {x.Id,x.TaxonomyId,x.SkillNameSnapshot,Valid=!x.IsDeleted && !x.Skill.IsDeleted}).ToDictionaryAsync(x=>x.Id,ct);
        var questions=await QuestionBank(competencyIds,ct);
        var paths=await db.AcceleratorLearningPaths.AsNoTracking().Where(x=>x.Type==AcceleratorLearningPathType.Career && x.CanonicalCareerRoleId.HasValue && roleIds.Contains(x.CanonicalCareerRoleId.Value) && !x.IsDeleted)
            .Select(x=>new {x.Id,x.CanonicalCareerRoleId,x.Status,x.ConfigurationSource,x.DisplayOrder}).ToArrayAsync(ct);
        var selectedPaths=roles.Select(r=>paths.Where(x=>x.CanonicalCareerRoleId==r.Id && x.Status==AcceleratorLearningCatalogStatus.Published)
            .OrderBy(x=>x.ConfigurationSource).ThenBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).FirstOrDefault()).Where(x=>x is not null).Select(x=>x!.Id).ToArray();
        var modules=await db.AcceleratorLearningModules.AsNoTracking().Where(x=>selectedPaths.Contains(x.LearningPathId) && !x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published)
            .GroupBy(x=>x.LearningPathId).Select(x=>new {Id=x.Key,Count=x.Count()}).ToDictionaryAsync(x=>x.Id,x=>x.Count,ct);
        var lessons=await db.AcceleratorLessons.AsNoTracking().Where(x=>selectedPaths.Contains(x.Module.LearningPathId) && !x.IsDeleted && x.Status==AcceleratorLearningCatalogStatus.Published && !x.Module.IsDeleted && x.Module.Status==AcceleratorLearningCatalogStatus.Published)
            .GroupBy(x=>x.Module.LearningPathId).Select(x=>new {Id=x.Key,Count=x.Count()}).ToDictionaryAsync(x=>x.Id,x=>x.Count,ct);
        var mappings=await db.AcceleratorLearningPathAssessments.AsNoTracking().Where(x=>selectedPaths.Contains(x.LearningPathId) && x.Purpose==AcceleratorLearningPathAssessmentPurpose.Baseline && !x.IsDeleted).ToArrayAsync(ct);
        var templateIds=mappings.Select(x=>x.AssessmentTemplateId).ToArray();
        var templates=await db.AcceleratorAssessmentTemplates.AsNoTracking().Where(x=>templateIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x=>new {x.Id,x.RoleId,TaxonomyId=x.TaxonomyId ?? (x.Role==null ? null : (Guid?)x.Role.TaxonomyId),x.IsActive,x.TimeLimitMinutes,x.MaximumQuestions,x.SelectionStrategy}).ToDictionaryAsync(x=>x.Id,ct);
        var projects=await db.AcceleratorProjectVersions.AsNoTracking().Where(x=>roleIds.Contains(x.TargetRoleId) && !x.IsDeleted && x.IsActive && x.CatalogStatus==AcceleratorProjectCatalogStatus.Published && !x.Definition.IsDeleted && x.Definition.IsActive)
            .GroupBy(x=>x.TargetRoleId).Select(x=>new {Id=x.Key,Count=x.Count()}).ToDictionaryAsync(x=>x.Id,x=>x.Count,ct);
        var interviews=await db.AcceleratorInterviewVersions.AsNoTracking().Where(x=>roleIds.Contains(x.Definition.TargetRoleId) && !x.IsDeleted && x.Status==AcceleratorInterviewCatalogStatus.Published && !x.Definition.IsDeleted && x.Definition.IsActive)
            .GroupBy(x=>x.Definition.TargetRoleId).Select(x=>new {Id=x.Key,Count=x.Count()}).ToDictionaryAsync(x=>x.Id,x=>x.Count,ct);
        var result=new List<AcceleratorRoleReadiness>();
        foreach(var role in roles)
        {
            var required=requirements.Where(x=>x.RoleId==role.Id).ToArray();
            var ids=required.Select(x=>x.CompetencyId).ToArray();
            var configured=required.Length>0 && required.All(x=>x.TaxonomyId==role.TaxonomyId && x.ExpectedProficiency is >=1 and <=5 &&
                competencies.TryGetValue(x.CompetencyId,out var c) && c.Valid && c.TaxonomyId==role.TaxonomyId);
            var coverage=AcceleratorAssessmentReadinessPolicy.EvaluateValidated(role.TaxonomyId,ids,questions);
            var assessment=configured && coverage.AssessmentReady;
            var rolePaths=paths.Where(x=>x.CanonicalCareerRoleId==role.Id).ToArray();
            var path=rolePaths.Where(x=>x.Status==AcceleratorLearningCatalogStatus.Published).OrderBy(x=>x.ConfigurationSource).ThenBy(x=>x.DisplayOrder).ThenBy(x=>x.Id).FirstOrDefault();
            var mapping=path is null ? null : mappings.SingleOrDefault(x=>x.LearningPathId==path.Id);
            var template=mapping is null ? null : templates.GetValueOrDefault(mapping.AssessmentTemplateId);
            var baseline=template is not null && template.IsActive && template.TaxonomyId==role.TaxonomyId && (!template.RoleId.HasValue || template.RoleId==role.Id) &&
                template.TimeLimitMinutes is >=1 and <=240 && template.MaximumQuestions is >=1 and <=100 &&
                AcceleratorAssessmentReadinessPolicy.EvaluateValidated(role.TaxonomyId,ids,questions,template.MaximumQuestions,template.SelectionStrategy).AssessmentReady;
            var moduleCount=path is null ? 0 : modules.GetValueOrDefault(path.Id); var lessonCount=path is null ? 0 : lessons.GetValueOrDefault(path.Id);
            var projectCount=projects.GetValueOrDefault(role.Id); var interviewCount=interviews.GetValueOrDefault(role.Id);
            var learning=assessment && path is not null && baseline && moduleCount>0 && lessonCount>0;
            var complete=learning && projectCount>0 && interviewCount>0;
            // AcceleratorReady means Foundation can begin via self-service. Detailed learning content
            // and projects/interviews are separately reported and never fabricated to satisfy this flag.
            var ready=assessment && (mapping is null || baseline);
            var missing=new List<string>();
            if(!configured) missing.Add("RoleRequirementsNotConfigured");
            if(coverage.MissingCompetencyIds.Count>0) missing.Add("MissingCompetencyQuestionCoverage");
            if(rolePaths.Length==0) missing.Add("MissingLearningPath"); else if(path is null) missing.Add("MissingPublishedLearningPath");
            if(moduleCount==0) missing.Add("MissingPublishedModules"); if(lessonCount==0) missing.Add("MissingPublishedLessons");
            if(mapping is null) missing.Add("MissingBaselineAssessment"); else if(!baseline) missing.Add("InvalidBaselineAssessment");
            if(projectCount==0) missing.Add("MissingProjects"); if(interviewCount==0) missing.Add("MissingInterviews");
            result.Add(new(role.Id,role.Code,role.Name,role.TaxonomyId,role.Version,true,configured,coverage.RequiredCompetencyCount,coverage.CoveredCompetencyCount,
                coverage.ObjectiveQuestionCoveragePercent,coverage.EligibleObjectiveQuestionCount,assessment,rolePaths.Length>0,path is not null,path?.Id,moduleCount,lessonCount,
                mapping is not null,baseline,projectCount,interviewCount,learning,ready,complete,
                complete ? AcceleratorCatalogReadinessLevel.AcceleratorComplete : learning ? AcceleratorCatalogReadinessLevel.LearningReady : assessment ? AcceleratorCatalogReadinessLevel.AssessmentReady : AcceleratorCatalogReadinessLevel.NotReady,
                missing,coverage.MissingCompetencyIds.Select(id=>new AcceleratorMissingQuestionCoverage(id,competencies.GetValueOrDefault(id)?.SkillNameSnapshot ?? "Canonical competency unavailable")).ToArray()));
        }
        return result;
    }
    public async Task<AcceleratorCatalogBootstrapResult> BootstrapAsync(Guid taxonomyId,bool apply,DateTime now,CancellationToken ct)
    {
        if(!await db.AcceleratorTaxonomies.AnyAsync(x=>x.Id==taxonomyId && !x.IsDeleted && x.PublishedAtUtc<=now,ct)) throw new NotFoundException("Published taxonomy not found.");
        var competencies=await db.AcceleratorCompetencies.AsNoTracking().Where(x=>x.TaxonomyId==taxonomyId && !x.IsDeleted && !x.Skill.IsDeleted)
            .Select(x=>new {x.Id,x.SkillNameSnapshot}).ToArrayAsync(ct);
        var ids=competencies.Select(x=>x.Id).ToArray(); var bank=await QuestionBank(ids,ct);
        var covered=bank.Where(x=>x.Eligible && x.TaxonomyId==taxonomyId).Select(x=>x.CompetencyId).ToHashSet();
        // Retired content still owns its natural key; never recreate a soft-deleted seed.
        var keys=await db.AcceleratorQuestions.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TaxonomyId==taxonomyId && ids.Contains(x.CompetencyId))
            .Select(x=>new {x.CompetencyId,x.Version}).ToArrayAsync(ct);
        var existing=keys.Select(x=>(x.CompetencyId,x.Version)).ToHashSet();
        var unsupported=new List<AcceleratorMissingQuestionCoverage>(); var planned=0; var collisions=0;
        foreach(var competency in competencies.Where(x=>!covered.Contains(x.Id)))
        {
            // Exact approved canonical names preserve C# versus C and Java versus JavaScript.
            var definitions=AcceleratorBaselineQuestionCatalog.Questions.Where(x=>x.CanonicalSkillNames.Contains(competency.SkillNameSnapshot.Trim().ToUpperInvariant(),StringComparer.Ordinal)).ToArray();
            if(definitions.Length==0) { unsupported.Add(new(competency.Id,competency.SkillNameSnapshot)); continue; }
            foreach(var definition in definitions)
            {
                if(existing.Contains((competency.Id,definition.QuestionVersion))) { collisions++; continue; }
                var question=definition.Create(taxonomyId,competency.Id);
                if(!AcceleratorAssessmentReadinessPolicy.IsEligible(question)) throw new InvalidOperationException("Baseline catalog contains an invalid objective question.");
                planned++; if(apply) db.Add(question);
            }
        }
        return new(taxonomyId,apply,planned,apply ? planned : 0,covered.Count,collisions,unsupported);
    }
    private sealed record RoleHeader(Guid Id,string Code,string Name,Guid TaxonomyId,string Version);
}
