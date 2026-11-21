using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public enum AcceleratorCatalogReadinessLevel { NotReady=0,AssessmentReady=1,LearningReady=2,AcceleratorComplete=3 }
public sealed record AcceleratorMissingQuestionCoverage(Guid CompetencyId,string SkillName);
public sealed record AcceleratorRoleReadiness(Guid RoleId,string Code,string Name,Guid TaxonomyId,string TaxonomyVersion,
    bool RoleExists,bool RoleRequirementsConfigured,int RequiredCompetencyCount,int CoveredCompetencyCount,decimal ObjectiveQuestionCoveragePercent,
    int EligibleObjectiveQuestionCount,bool AssessmentReady,bool LearningPathExists,bool PublishedLearningPathExists,Guid? LearningPathId,
    int PublishedModuleCount,int PublishedLessonCount,bool BaselineAssessmentMapped,bool BaselineAssessmentUsable,int ProjectCount,int InterviewCount,
    bool LearningReady,bool AcceleratorReady,bool AcceleratorComplete,AcceleratorCatalogReadinessLevel Level,
    IReadOnlyList<string> Missing,IReadOnlyList<AcceleratorMissingQuestionCoverage> MissingCompetencyQuestionCoverage)
{
    public bool ProjectsAvailable => ProjectCount>0;
    public bool InterviewsAvailable => InterviewCount>0;
}
// Readiness filtering is applied within each bounded scan page. Follow NextScanPage even if Items is empty.
public sealed record AcceleratorReadinessQuery(int ScanPage=1,int PageSize=50,bool? Ready=null,string? Code=null,Guid? TaxonomyId=null,string? TaxonomyVersion=null);
public sealed record AcceleratorReadinessPage(IReadOnlyList<AcceleratorRoleReadiness> Items,int? NextScanPage,int ScannedRoleCount);
public sealed record AcceleratorCatalogBootstrapRequest(Guid TaxonomyId,bool Apply=false);
public sealed record AcceleratorCatalogBootstrapResult(Guid TaxonomyId,bool Applied,int PlannedQuestions,int AddedQuestions,int PreservedCoveredCompetencies,
    int ExistingNaturalKeys,IReadOnlyList<AcceleratorMissingQuestionCoverage> UnsupportedCompetencies);
public sealed record AcceleratorLaunchReadiness(string Code,string Name,bool RoleExists,IReadOnlyList<AcceleratorRoleReadiness> Roles);
public sealed record AcceleratorMarketReadiness(Guid MarketPathId,string State,bool CanProvisionConfiguration,string? DiagnosticReason,AcceleratorRoleReadiness? Role,AcceleratorAssessmentCoverage? AssessmentCoverage);
public interface IAcceleratorCatalogReadinessStore
{
    Task<AcceleratorReadinessPage> RolesAsync(AcceleratorReadinessQuery query,DateTime now,CancellationToken ct);
    Task<AcceleratorRoleReadiness?> RoleAsync(Guid id,DateTime now,CancellationToken ct);
    Task<IReadOnlyList<AcceleratorLaunchReadiness>> LaunchAsync(Guid taxonomyId,DateTime now,CancellationToken ct);
    Task<AcceleratorCatalogBootstrapResult> BootstrapAsync(Guid taxonomyId,bool apply,DateTime now,CancellationToken ct);
}
public sealed class AcceleratorCatalogReadinessService(IAcceleratorStore writes,IAcceleratorCatalogReadinessStore store,
    IAcceleratorJourneyProvisioningStore provisioning,AcceleratorService access,IAuditWriter audit,TimeProvider clock)
{
    public async Task<AcceleratorReadinessPage> RolesAsync(Guid actor,AcceleratorReadinessQuery query,CancellationToken ct)
    {
        await access.Administrator(actor,ct); AcceleratorService.Page(query.ScanPage,query.PageSize);
        if(query.Code?.Length>80 || query.TaxonomyVersion?.Length>80) throw new BadRequestException("Invalid readiness filter.");
        return await store.RolesAsync(query,clock.GetUtcNow().UtcDateTime,ct);
    }
    public async Task<AcceleratorRoleReadiness> RoleAsync(Guid actor,Guid id,CancellationToken ct)
    {
        await access.Administrator(actor,ct);
        return await store.RoleAsync(id,clock.GetUtcNow().UtcDateTime,ct) ?? throw new NotFoundException("Canonical role not found.");
    }
    public async Task<IReadOnlyList<AcceleratorLaunchReadiness>> LaunchAsync(Guid actor,Guid taxonomyId,CancellationToken ct)
    { await access.Administrator(actor,ct); return await store.LaunchAsync(taxonomyId,clock.GetUtcNow().UtcDateTime,ct); }
    public async Task<AcceleratorMarketReadiness> MarketAsync(Guid actor,Guid marketId,CancellationToken ct)
    {
        await access.Administrator(actor,ct);
        var plan=await provisioning.CheckEligibilityAsync(Guid.Empty,marketId,null,clock.GetUtcNow().UtcDateTime,ct);
        var role=plan.CanonicalRoleId.HasValue ? await store.RoleAsync(plan.CanonicalRoleId.Value,clock.GetUtcNow().UtcDateTime,ct) : null;
        var state=role is null ? "CanonicalRoleUnresolved" : !role.AssessmentReady ? "AssessmentCoverageInsufficient" : !role.LearningReady ? "LearningContentMissing" : "Ready";
        return new(marketId,state,plan.State=="ProvisioningRequired",plan.DiagnosticReason,role,plan.InternalCoverage);
    }
    public async Task<AcceleratorCatalogBootstrapResult> BootstrapAsync(Guid actor,AcceleratorCatalogBootstrapRequest request,CancellationToken ct)
    {
        await access.Administrator(actor,ct);
        if(!request.Apply) return await store.BootstrapAsync(request.TaxonomyId,false,clock.GetUtcNow().UtcDateTime,ct);
        return await writes.WriteAsync(async ()=>
        {
            await access.Administrator(actor,ct);
            var result=await store.BootstrapAsync(request.TaxonomyId,true,clock.GetUtcNow().UtcDateTime,ct);
            await audit.AppendAsync(new(AuditAction.Create,"AcceleratorBaselineCatalog",request.TaxonomyId.ToString(),
                new Dictionary<string,string?> { ["catalogVersion"]=AcceleratorBaselineQuestionCatalog.Version,["addedQuestions"]=result.AddedQuestions.ToString(System.Globalization.CultureInfo.InvariantCulture) },new(actor,"Administrator")),ct);
            return result;
        },ct);
    }
}
