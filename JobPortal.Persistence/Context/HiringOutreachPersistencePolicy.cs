using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Context;

internal static class HiringOutreachPersistencePolicy
{
    public static void Enforce(JobPortalDbContext db)
    {
        foreach(var entry in db.ChangeTracker.Entries())
        {
            if(entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            var allowed=entry.Entity switch
            {
                HiringOutreachDraft or HiringOutreachContact or HiringOutreachDiscoveryRequest=>Array.Empty<string>(),
                HiringOutreachSession=>SessionChanges,
                HiringOutreachSend=>SendChanges,
                _=>null
            };
            if(allowed is null) continue;
            if(entry.State==EntityState.Deleted || entry.Properties.Any(p=>p.IsModified && !Equals(p.OriginalValue,p.CurrentValue) &&
                p.Metadata.Name is not ("UpdatedAtUtc" or "IsDeleted" or "DeletedAtUtc") && !allowed.Contains(p.Metadata.Name,StringComparer.Ordinal)))
                throw new InvalidOperationException("Outreach review content, identity and usage provenance are immutable.");
        }
    }
    private static readonly string[] SessionChanges=["State","UsageState","FailureCode","Revision"];
    private static readonly string[] SendChanges=["State","FailureCode","Revision","DispatchedAtUtc","SentAtUtc","ProviderMessageId","ProviderThreadId"];
}
