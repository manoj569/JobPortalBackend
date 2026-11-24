using System.Security.Cryptography;
using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.HiringOutreach;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPortal.Persistence.Repositories;

public sealed partial class HiringOutreachService
{
    [LoggerMessage(Level=LogLevel.Information,Message="Hiring discovery candidates {Candidates} relevant {Relevant} providerCalled {ProviderCalled}")]
    private static partial void DiscoveryDiagnostics(ILogger logger,int candidates,int relevant,bool providerCalled);
    [LoggerMessage(Level=LogLevel.Information,Message="Hiring provider raw {Raw} professional {Professional} verified {Verified} roleRelevant {RoleRelevant} returned {Returned}")]
    private static partial void ProviderDiagnostics(ILogger logger,int raw,int professional,int verified,int roleRelevant,int returned);
    private static HiringDiscoveryContext Context(HiringOutreachSession s)=>new(s.CompanyName,s.CompanyDomain,s.JobTitle,s.Department,s.Location,s.JobDescription);
    private void AddContacts(HiringOutreachSession session,IReadOnlyList<HiringProviderContact> selected)
    {
        for(var rank=0;rank<selected.Count;rank++)
        {
            var item=selected[rank];
            db.Add(new HiringOutreachContact { SessionId=session.Id,Rank=rank,FullName=item.FullName,FirstName=item.FirstName,Email=item.Email,
                Title=item.Title,Department=item.Department,Type=HiringContactPolicy.Type(item.Title),Verification=item.Verification,Confidence=item.Confidence,
                LinkedInUrl=item.LinkedInUrl,ProviderReference=item.Reference,VerifiedAtUtc=Now,Location=item.Location,
                Provider=item.Provider,Source=item.Source,MatchScore=HiringContactPolicy.Score(item,Context(session)) });
        }
    }
    private async Task<string> AllowanceSource(Guid actor,CancellationToken ct)
    {
        var used=await Sessions.IgnoreQueryFilters().CountAsync(x=>x.CandidateId==actor && x.DispatchedAtUtc!=null && x.UsageState!="Released",ct);
        if(used<Limits.PromotionalLifetimeDiscoveries) return "Promotion";
        var plans=await db.Memberships.AsNoTracking().Where(x=>x.UserId==actor && x.Status==MembershipStatus.Active && x.StartsAtUtc<=Now && (x.EndsAtUtc==null || x.EndsAtUtc>Now)).Select(x=>x.PlanCode).ToArrayAsync(ct);
        return plans.OrderByDescending(x=>Limits.PlanMonthlyDiscoveries.GetValueOrDefault(x)).FirstOrDefault(x=>Limits.PlanMonthlyDiscoveries.GetValueOrDefault(x)>0) ?? "";
    }
    private async Task<IReadOnlyList<HiringProviderContact>> SharedContacts(HiringOutreachSession session,CancellationToken ct)
    {
        if(!Limits.SharedContacts.Enabled) return [];
        if(sharedProtection is null) throw new AppException("Shared contact protection is unavailable.",503,"shared_contact_protection_unavailable");
        var rows=await db.Set<HiringContactIntelligence>().Where(x=>x.CompanyId==session.CompanyId && x.CompanyDomain==session.CompanyDomain &&
            x.IsActive && x.ExpiresAtUtc>Now && x.LastVerifiedAtUtc>Now.AddDays(-Limits.SharedContacts.CacheDays) && x.Verification=="valid")
            .OrderByDescending(x=>x.LastVerifiedAtUtc).ThenByDescending(x=>x.Confidence).ThenBy(x=>x.Id).Take(200).ToArrayAsync(ct);
        var usable=new List<(HiringContactIntelligence Row,HiringProviderContact Person,int Score)>();
        foreach(var row in rows)
        {
            if(!CanShare(row.Provider)) continue;
            try
            {
                var person=JsonSerializer.Deserialize<HiringProviderContact>(sharedProtection.Unprotect(session.CompanyDomain,row.ProtectedContact));
                if(person is null || !CanShare(person.Provider) || person.Verification!="valid" || Hash(person.Email.Trim().ToLowerInvariant())!=row.EmailHash || !HiringContactPolicy.ProfessionalEmail(person.Email,session.CompanyDomain)) continue;
                var score=HiringContactPolicy.Score(person,Context(session));
                if(score>=Limits.SharedContacts.MinimumScore) usable.Add((row,person,score));
            }
            catch(Exception ex) when(ex is CryptographicException or JsonException) { /* Unreadable data is never returned as a contact. */ }
        }
        var selected=usable.OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Row.LastVerifiedAtUtc).ThenByDescending(x=>x.Row.SuccessfulReuseCount)
            .ThenBy(x=>x.Row.EmailHash,StringComparer.Ordinal).Take(Math.Min(4,Limits.MaximumContacts)).ToArray();
        if(selected.Length>=Math.Min(Limits.SharedContacts.MinimumContacts,Limits.MaximumContacts))
        {
            foreach(var item in selected) { item.Row.SuccessfulReuseCount=Math.Min(item.Row.SuccessfulReuseCount,int.MaxValue-1)+1; item.Row.Revision=Guid.NewGuid(); }
        }
        // Including partial cache enrichment: a snapshot must not outlive its oldest source.
        if(selected.Length>0)
        {
            var earliest=selected.Min(x=>x.Row.ExpiresAtUtc);
            if(earliest<session.ExpiresAtUtc) session.ExpiresAtUtc=earliest;
        }
        DiscoveryDiagnostics(logger,rows.Length,selected.Length,false);
        return selected.Select(x=>x.Person).ToArray();
    }
    private async Task StoreSharedContacts(HiringOutreachSession session,IReadOnlyList<HiringProviderContact> selected,CancellationToken ct)
    {
        if(!Limits.SharedContacts.Enabled || sharedProtection is null) return;
        foreach(var person in selected.Where(x=>x.Verification=="valid" && CanShare(x.Provider)))
        {
            var hash=Hash(person.Email.Trim().ToLowerInvariant());
            var row=await db.Set<HiringContactIntelligence>().IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyDomain==session.CompanyDomain && x.EmailHash==hash,ct);
            // Explicitly deactivated/deleted identities stay suppressed. A different company cannot take ownership.
            if(row is not null && (row.IsDeleted || !row.IsActive || row.CompanyId!=session.CompanyId)) continue;
            if(row is null)
            {
                row=new HiringContactIntelligence { CompanyId=session.CompanyId,CompanyDomain=session.CompanyDomain,EmailHash=hash,FirstDiscoveredAtUtc=Now };
                db.Add(row);
            }
            row.LastSeenAtUtc=Now;
            // Fresh stronger data is never replaced by lower-confidence metadata. Expired rows may
            // be refreshed only from a new explicitly verified observation.
            if(row.ProtectedContact.Length>0 && row.ExpiresAtUtc>Now)
            {
                var previous=person with { Title=row.Title,Department=row.Department,Location=row.Location,Confidence=row.Confidence };
                var lessRelevant=HiringContactPolicy.Score(person,Context(session))<HiringContactPolicy.Score(previous,Context(session));
                var losesMetadata=row.Title.Length>0 && string.IsNullOrWhiteSpace(person.Title) ||
                    row.Department.Length>0 && string.IsNullOrWhiteSpace(person.Department) ||
                    row.Location.Length>0 && string.IsNullOrWhiteSpace(person.Location);
                if(row.Confidence>person.Confidence || losesMetadata || lessRelevant) continue;
            }
            row.ProtectedContact=sharedProtection.Protect(session.CompanyDomain,JsonSerializer.Serialize(person));
            row.Title=HiringContactPolicy.Normalize(person.Title); row.Department=HiringContactPolicy.Normalize(person.Department);
            row.Location=HiringContactPolicy.Normalize(person.Location); row.JobFamily=HiringContactPolicy.Family(person.Title,person.Department);
            row.ContactType=HiringContactPolicy.Type(person.Title); row.Confidence=person.Confidence; row.Verification="valid";
            row.Provider=Bound(person.Provider,30); row.Source=person.Source is "PersonEnrichment" or "PeopleSearch" or "Verification" or "EmailFinder" or "EmployeeSearch" or "EmployeeEnrichment" ? person.Source : "DomainSearch";
            row.LastVerifiedAtUtc=Now; row.ExpiresAtUtc=Now.AddDays(Limits.SharedContacts.CacheDays); row.Revision=Guid.NewGuid();
        }
    }
    private async Task<HiringProviderContact[]> UnsuppressedContacts(HiringOutreachSession session,IEnumerable<HiringProviderContact> candidates,CancellationToken ct)
    {
        var bounded=candidates.Take(8).ToArray();
        if(!Limits.SharedContacts.Enabled || bounded.Length==0) return bounded;
        var hashes=bounded.Select(x=>Hash(x.Email.Trim().ToLowerInvariant())).ToArray();
        var suppressed=await db.Set<HiringContactIntelligence>().IgnoreQueryFilters().Where(x=>x.CompanyDomain==session.CompanyDomain &&
            hashes.Contains(x.EmailHash) && (x.IsDeleted || !x.IsActive)).Select(x=>x.EmailHash).ToArrayAsync(ct);
        return bounded.Where(x=>!suppressed.Contains(Hash(x.Email.Trim().ToLowerInvariant()),StringComparer.Ordinal)).ToArray();
    }
    private static string Scope(HiringOutreachSession session)=>Hash(session.CompanyDomain+"|"+HiringContactPolicy.Family(session.JobTitle,session.Department)+"|"+HiringLocationPolicy.Normalize(session.Location));
    private async Task ClaimSharedDiscovery(HiringOutreachSession session,CancellationToken ct)
    {
        if(!Limits.SharedContacts.Enabled) return;
        var scope=Scope(session);
        var lease=await db.Set<HiringContactDiscoveryLease>().IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.ScopeHash==scope,ct);
        if(lease is not null && lease.ExpiresAtUtc>Now) throw new AppException("Hiring-team discovery is in progress; retry shortly.",409,"DiscoveryInProgress");
        if(lease is null) { lease=new HiringContactDiscoveryLease { ScopeHash=scope }; db.Add(lease); }
        lease.OwnerSessionId=session.Id; lease.ExpiresAtUtc=Now.AddMinutes(3); lease.Revision=Guid.NewGuid();
    }
    private async Task ReleaseSharedDiscovery(HiringOutreachSession session,bool uncertain,CancellationToken ct)
    {
        if(!Limits.SharedContacts.Enabled) return;
        var scope=Scope(session);
        var lease=await db.Set<HiringContactDiscoveryLease>().SingleOrDefaultAsync(x=>x.ScopeHash==scope && x.OwnerSessionId==session.Id,ct);
        if(lease is not null) { lease.ExpiresAtUtc=uncertain ? Now.AddMinutes(5) : Now; lease.Revision=Guid.NewGuid(); }
    }
}
