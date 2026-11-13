using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JobPortal.Persistence.Context;

internal static class AcceleratorPersistencePolicy
{
    public static void Enforce(ChangeTracker tracker)
    {
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.Entity is AcceleratorTaxonomy or AcceleratorCareerRole or AcceleratorCompetency or AcceleratorPrerequisite or
                AcceleratorRoleRequirement or AcceleratorBillingCycle or AcceleratorAllowance or AcceleratorUsageEvent or
                AcceleratorProgressEvent or AcceleratorSkillEvidence or AcceleratorTutorMessage or AcceleratorPracticeAttempt or
                AcceleratorPracticeEvaluation)
                throw new InvalidOperationException("Published Accelerator taxonomy, billing allocations and usage events are immutable.");
            if (entry.Entity is AcceleratorUsageReservation)
            {
                if (entry.State == EntityState.Deleted || entry.Property("IsDeleted").IsModified)
                    throw new InvalidOperationException("Accelerator usage accounting cannot be deleted.");
                foreach (var property in entry.Properties.Where(p => p.IsModified))
                {
                    if (property.Metadata.Name is not ("State" or "Revision" or "FinishedAtUtc" or "ActualCostInr" or
                        "InputTokens" or "OutputTokens" or "PolicyViolation" or "UpdatedAtUtc"))
                        throw new InvalidOperationException("Accelerator usage reservation identity and estimate are immutable.");
                }
            }
            if (entry.Entity is AcceleratorCareerGoal && (entry.Property("UserId").IsModified || entry.Property("RoleId").IsModified || entry.Property("TaxonomyId").IsModified))
                throw new InvalidOperationException("Career goal identity and taxonomy history are immutable.");
        }
    }
}
