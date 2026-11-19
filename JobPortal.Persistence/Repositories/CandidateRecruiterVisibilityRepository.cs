using JobPortal.Application.Abstractions.CandidateRecruiterVisibility;
using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CandidateRecruiterVisibilityRepository(
    JobPortalDbContext context)
    : ICandidateRecruiterVisibilityRepository
{
    public async Task<bool?> GetVisibilityAsync(
        Guid candidateUserId,
        CancellationToken cancellationToken = default)
    {
        return await context.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == candidateUserId &&
                x.RoleId == SystemRoleIds.Candidate &&
                x.Status == UserStatus.Active)
            .Select(x => (bool?)x.IsRecruiterVisible)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UpdateVisibilityAsync(
        Guid candidateUserId,
        bool isRecruiterVisible,
        CancellationToken cancellationToken = default)
    {
        var candidate = await context.Users
            .SingleOrDefaultAsync(
                x =>
                    x.Id == candidateUserId &&
                    x.RoleId == SystemRoleIds.Candidate &&
                    x.Status == UserStatus.Active,
                cancellationToken);

        if (candidate is null)
        {
            return false;
        }

        candidate.IsRecruiterVisible = isRecruiterVisible;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
