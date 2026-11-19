using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AcceleratorLearningPathStore(JobPortalDbContext context) : IAcceleratorLearningPathStore
{
    public async Task<IReadOnlyList<AcceleratorLearningPath>> PublishedPathsAsync(AcceleratorLearningPathType? type, int skip, int take, CancellationToken ct) =>
        await context.AcceleratorLearningPaths.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == AcceleratorLearningCatalogStatus.Published && (!type.HasValue || x.Type == type.Value))
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Title).ThenBy(x => x.Id).Skip(skip).Take(take).ToArrayAsync(ct);

    public Task<AcceleratorLearningPath?> PublishedPathBySlugAsync(string slug, CancellationToken ct) => PathGraph()
        .SingleOrDefaultAsync(x => x.Slug == slug && x.Status == AcceleratorLearningCatalogStatus.Published && !x.IsDeleted, ct);

    public Task<AcceleratorLearningPath?> PathAsync(Guid id, CancellationToken ct) => PathGraph()
        .SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public Task<AcceleratorPathEnrollment?> EnrollmentAsync(Guid userId, Guid enrollmentId, CancellationToken ct) => EnrollmentGraph()
        .SingleOrDefaultAsync(x => x.UserId == userId && x.Id == enrollmentId && !x.IsDeleted, ct);

    public Task<AcceleratorPathEnrollment?> ActiveEnrollmentForPathAsync(Guid userId, Guid pathId, CancellationToken ct) => EnrollmentGraph()
        .OrderByDescending(x => x.LastActivityAtUtc).FirstOrDefaultAsync(x => x.UserId == userId && x.LearningPathId == pathId &&
            !x.IsDeleted && (x.Status == AcceleratorLearningEnrollmentStatus.Enrolled || x.Status == AcceleratorLearningEnrollmentStatus.InProgress || x.Status == AcceleratorLearningEnrollmentStatus.Paused), ct);

    public Task<AcceleratorPathEnrollment?> EnrollmentByRequestKeyAsync(Guid userId, Guid requestKey, CancellationToken ct) => EnrollmentGraph()
        .SingleOrDefaultAsync(x => x.UserId == userId && x.RequestKey == requestKey && !x.IsDeleted, ct);

    public Task<AcceleratorEnrollmentProgressEvent?> ProgressEventAsync(Guid enrollmentId, Guid requestKey, CancellationToken ct) =>
        context.AcceleratorEnrollmentProgressEvents.SingleOrDefaultAsync(x => x.EnrollmentId == enrollmentId && x.RequestKey == requestKey && !x.IsDeleted, ct);

    public void Add(object entity) => context.Add(entity);

    private IQueryable<AcceleratorLearningPath> PathGraph() => context.AcceleratorLearningPaths.AsSplitQuery()
        .Include(x => x.Modules.Where(m => !m.IsDeleted))
            .ThenInclude(x => x.Lessons.Where(l => !l.IsDeleted))
                .ThenInclude(x => x.Resources.Where(r => !r.IsDeleted));

    private IQueryable<AcceleratorPathEnrollment> EnrollmentGraph() => context.AcceleratorPathEnrollments.AsSplitQuery()
        .Include(x => x.LearningPath).ThenInclude(x => x.Modules.Where(m => !m.IsDeleted))
            .ThenInclude(x => x.Lessons.Where(l => !l.IsDeleted))
                .ThenInclude(x => x.Resources.Where(r => !r.IsDeleted))
        .Include(x => x.LearningPath).ThenInclude(x => x.ModulePrerequisites.Where(p => !p.IsDeleted))
        .Include(x => x.LessonProgress.Where(p => !p.IsDeleted));
}
