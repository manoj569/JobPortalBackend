using System.Text.Json;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerAccelerator;

public sealed record ProvisionAcceleratorJourneyRequest(Guid RequestKey,Guid? MarketPathId=null,Guid? MarketSkillPathId=null);
public sealed record ProvisionAcceleratorJourneyResponse(string State,string? DiagnosticReason,Guid? JourneyContextId,Guid? LearningPathId,Guid? EnrollmentId,
    AcceleratorNextAction NextAction,int RequiredCompetencies,int CoveredCompetencies,IReadOnlyList<Guid> UncoveredCompetencyIds,string PolicyVersion,Guid? CanonicalRoleId=null,
    [property:System.Text.Json.Serialization.JsonIgnore] AcceleratorAssessmentCoverage? InternalCoverage=null)
{
    public bool IsPartialBaseline => CoveredCompetencies<RequiredCompetencies;
    public decimal CompetencyCoveragePercent => RequiredCompetencies==0 ? 0 : decimal.Round(CoveredCompetencies*100m/RequiredCompetencies,2);
}
public interface IAcceleratorJourneyProvisioningStore
{
    Task<ProvisionAcceleratorJourneyResponse> CheckEligibilityAsync(Guid actor,Guid? marketPathId,Guid? marketSkillPathId,DateTime now,CancellationToken ct);
    // Invoked inside the existing Accelerator cross-instance transaction/lock only.
    Task<ProvisionAcceleratorJourneyResponse> EnsureAsync(Guid actor,ProvisionAcceleratorJourneyRequest request,DateTime now,CancellationToken ct);
}
public static class AcceleratorJourneyProvisioningPolicy
{
    public const string Version="journey-provision-v1";
    public const string ObjectiveSelection="objective-provision-v1";
    // Existing selection takes one question per competency. One valid objective question is the
    // floor; at least half the role must be represented, capped by the existing 100-question limit.
    // Unrepresented skills remain NotAssessed, with no fabricated evidence or inferred mastery.
    public static bool SufficientCoverage(int required,int covered) => AcceleratorAssessmentReadinessPolicy.SufficientCoverage(required,covered);
    public static bool ValidQuestion(AcceleratorQuestion question)
    {
        if(!question.IsActive || question.IsDeleted || question.Weight<=0 || question.Difficulty is <1 or >5 || string.IsNullOrWhiteSpace(question.Prompt) ||
            question.Type is not (AcceleratorQuestionType.MultipleChoice or AcceleratorQuestionType.MultipleSelect or AcceleratorQuestionType.TrueFalse)) return false;
        try
        {
            using var key=JsonDocument.Parse(question.AnswerKeyJson); using var options=JsonDocument.Parse(question.OptionsJson);
            if(key.RootElement.ValueKind!=JsonValueKind.Object || key.RootElement.EnumerateObject().Count()!=1 || !key.RootElement.TryGetProperty("answer",out var answer)) return false;
            if(question.Type==AcceleratorQuestionType.TrueFalse) return answer.ValueKind is JsonValueKind.True or JsonValueKind.False;
            if(options.RootElement.ValueKind!=JsonValueKind.Array || options.RootElement.GetArrayLength()<2) return false;
            var choices=options.RootElement.EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.String ? x.GetString() :
                x.ValueKind==JsonValueKind.Object && x.TryGetProperty("id",out var id) ? id.ToString() : null).Where(x=>x is not null).ToHashSet(StringComparer.Ordinal);
            if(choices.Count<2) return false;
            if(question.Type==AcceleratorQuestionType.MultipleChoice) return answer.ValueKind==JsonValueKind.String && choices.Contains(answer.GetString());
            return answer.ValueKind==JsonValueKind.Array && answer.GetArrayLength()>0 && answer.EnumerateArray().All(x=>x.ValueKind==JsonValueKind.String && choices.Contains(x.GetString()));
        }
        catch(JsonException) { return false; }
        catch(InvalidOperationException) { return false; }
    }
    public static string? CanonicalCode(string name)
    {
        var value=MarketExtractionPolicy.Normalize(name).ToLowerInvariant().Replace(' ','-');
        return value.Length is >0 and <=80 && value.All(x=>char.IsAsciiLetterOrDigit(x) || x=='-') ? value : null;
    }
}
public sealed class AcceleratorJourneyProvisioningService(IAcceleratorStore writes,IAcceleratorJourneyProvisioningStore store,
    AcceleratorService access,IAuditWriter audit,TimeProvider clock,AcceleratorJourneySummaryService summary)
{
    public async Task<ProvisionAcceleratorJourneyResponse> ProvisionAsync(Guid actor,ProvisionAcceleratorJourneyRequest request,CancellationToken ct)
    {
        var result=await writes.WriteAsync(async ()=>
        {
            await access.Candidate(actor,ct);
            if(request.RequestKey==Guid.Empty || request.MarketPathId.HasValue==request.MarketSkillPathId.HasValue || request.MarketPathId==Guid.Empty || request.MarketSkillPathId==Guid.Empty)
                throw new BadRequestException("A request key and exactly one Market Career or Skill Path are required.");
            var cycle=await access.ResolveAccessCycleAsync(actor,ct);
            if(cycle is null || cycle.Payment.IsDeleted || cycle.Payment.Status!=PaymentStatus.Paid)
                throw new AppException("An active Accelerator subscription is required.",403,"accelerator_subscription_required");
            var response=await store.EnsureAsync(actor,request,clock.GetUtcNow().UtcDateTime,ct);
            await audit.AppendAsync(new(AuditAction.Create,"AcceleratorJourneyProvisioning",request.RequestKey.ToString(),
                new Dictionary<string,string?> { ["state"]=response.State,["reason"]=response.DiagnosticReason,["policy"]=response.PolicyVersion },new(actor,"Candidate")),ct);
            return response;
        },ct);
        if(!result.JourneyContextId.HasValue) return result;
        // Read after commit, outside the provisioning lock. Replaying provisioning must resume
        // the existing journey, not suggest another assessment or reset completed work.
        var current=await summary.GetAsync(actor,null,result.JourneyContextId,ct);
        return result with { State=current.State,NextAction=current.NextAction };
    }
}
