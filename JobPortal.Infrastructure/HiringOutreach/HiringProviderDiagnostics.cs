using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.HiringOutreach;

public sealed class HiringProviderDiagnostics(CoresignalHiringProvider coresignal,HiringProviderOrchestrator orchestrator,IAuditWriter audit) : IHiringProviderDiagnostics
{
    private static readonly SemaphoreSlim ProbeGate=new(1,1);
    public async Task<HiringProviderProbeResult> ProbeCoresignalAsync(Guid administrator,HiringProviderProbeRequest request,CancellationToken ct)
    {
        if(administrator==Guid.Empty) throw new UnauthorizedException();
        if(!request.ConfirmProviderCreditUse || !Valid(request.CompanyName,200) || !Valid(request.CompanyDomain,253) ||
            !Valid(request.JobTitle,200) || !Valid(request.Location,200) || CoresignalHiringProvider.Domain(request.CompanyDomain).Length==0)
            throw new BadRequestException("Supply bounded company, domain, job and location values and explicitly confirm provider credit use.");
        if(!coresignal.IsAvailable) throw new AppException("Coresignal diagnostics are disabled or unconfigured.",503,"CoresignalConfigurationRequired");
        if(!await ProbeGate.WaitAsync(0,ct)) throw new AppException("A provider diagnostic is already running on this instance.",409,"DiagnosticInProgress");
        try
        {
            await Audit(administrator,"Requested",ct);
            var context=new HiringDiscoveryContext(request.CompanyName,CoresignalHiringProvider.Domain(request.CompanyDomain),request.JobTitle,"",request.Location);
            var identities=new List<HiringPersonIdentity>();
            var result=await orchestrator.DiscoverForDiagnosticAsync(context,coresignal,identities.Add,ct);
            var relevant=identities.DistinctBy(x=>x.Reference).Select(x=>new HiringProviderProbeContact(x.Title,x.Location,x.Domain,HiringContactPolicy.ContactRole(x.Title),
                HiringContactPolicy.Score(new(x.FullName,x.FirstName,"",x.Title,x.Department,"unknown",0,null,x.Reference,x.Location,"EmployeeSearch","Coresignal"),context),
                result.Contacts.Any(c=>CoresignalHiringOptions.IsDerived(c.Provider) && c.Reference==x.Reference)))
                .Where(x=>x.MatchScore>0).OrderByDescending(x=>x.MatchScore).ToArray();
            await Audit(administrator,result.Uncertain ? "Uncertain" : "Completed",CancellationToken.None);
            return new("Coresignal",result.Uncertain ? "Uncertain" : "Completed",identities.Count,relevant.Length,result.Contacts.Count,relevant.Take(20).ToArray(),result.Failures ?? []);
        }
        catch
        {
            await Audit(administrator,"Failed",CancellationToken.None);
            throw;
        }
        finally { ProbeGate.Release(); }
    }
    private Task Audit(Guid actor,string state,CancellationToken ct)=>audit.AppendAsync(new(AuditAction.Update,"HiringProviderDiagnostic","Coresignal",
        new Dictionary<string,string?> { ["state"]=state },new(actor,"Administrator")),ct);
    private static bool Valid(string? value,int length)=>!string.IsNullOrWhiteSpace(value) && value.Length<=length && !value.Any(char.IsControl);
}
