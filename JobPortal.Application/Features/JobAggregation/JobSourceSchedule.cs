using System.Linq.Expressions;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.JobAggregation;

public static class JobSourceSchedule
{
    // Shared SQL-translatable predicate, also used for the post-lock scheduler recheck.
    public static Expression<Func<JobSource, bool>> DuePredicate(DateTime nowUtc, int cooldownMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cooldownMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cooldownMinutes, 10080);
        return source => source.IsActive && !source.IsDeleted &&
            (source.LastSuccessfulRunAtUtc.HasValue &&
             (!source.LastRunAtUtc.HasValue || source.LastSuccessfulRunAtUtc >= source.LastRunAtUtc)
                ? source.LastSuccessfulRunAtUtc.Value.AddMinutes(source.ScanIntervalMinutes) <= nowUtc
                : !source.LastRunAtUtc.HasValue ||
                  (source.LastRunAtUtc.Value.AddMinutes(source.ScanIntervalMinutes) <= nowUtc &&
                   source.LastRunAtUtc.Value.AddMinutes(cooldownMinutes) <= nowUtc));
    }
}
