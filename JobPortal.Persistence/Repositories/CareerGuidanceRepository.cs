using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class CareerGuidanceRepository(JobPortalDbContext db) : ICareerGuidanceRepository
{
    private IQueryable<CareerConsultant> PublicQuery() => db.Set<CareerConsultant>().AsNoTracking()
        .Where(p => p.VerificationStatus == ConsultantVerificationStatus.Verified && !p.User.IsDeleted && p.User.Status == UserStatus.Active);

    private static IQueryable<CareerConsultant> IncludeDetails(IQueryable<CareerConsultant> query) =>
        query.Include(p => p.Tags).Include(p => p.Services.Where(s => !s.IsDeleted)).AsSplitQuery();

    public Task<CareerConsultant?> FindAsync(Guid id, bool publicOnly, CancellationToken ct) =>
        IncludeDetails(publicOnly ? PublicQuery() : db.Set<CareerConsultant>().IgnoreQueryFilters().Where(p => !p.IsDeleted))
            .SingleOrDefaultAsync(p => p.Id == id, ct);

    // Include tombstoned tags to revive them without violating their immutable unique identity.
    public Task<CareerConsultant?> FindByUserAsync(Guid userId, CancellationToken ct) =>
        IncludeDetails(db.Set<CareerConsultant>().IgnoreQueryFilters()).SingleOrDefaultAsync(p => p.UserId == userId, ct);

    public Task AddAsync(CareerConsultant profile, CancellationToken ct) => db.Set<CareerConsultant>().AddAsync(profile, ct).AsTask();

    public async Task<IReadOnlyDictionary<Guid, CareerRatingSummary>> RatingsAsync(Guid[] consultantIds, CancellationToken ct)
    {
        var rows = await CareerTrustRepository.Published(db).Where(r => consultantIds.Contains(r.ConsultantId))
            .GroupBy(r => r.ConsultantId).Select(g => new { Id = g.Key, Sum = g.Sum(r => (decimal)r.Rating), Count = g.Count() }).ToArrayAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new CareerRatingSummary(r.Id, decimal.Round(r.Sum / r.Count, 2, MidpointRounding.AwayFromZero), r.Count));
    }

    public async Task<(IReadOnlyCollection<CareerConsultant> Items, int Total)> AdminSearchAsync(ConsultantAdminQuery query, CancellationToken ct)
    {
        var rows = db.Set<CareerConsultant>().AsNoTracking();
        if (query.Status.HasValue) rows = rows.Where(p => p.VerificationStatus == query.Status);
        var total = await rows.CountAsync(ct);
        var page = await IncludeDetails(rows.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)).ToArrayAsync(ct);
        return (page, total);
    }

    public async Task<(IReadOnlyCollection<CareerConsultant> Items, int Total)> SearchAsync(ConsultantSearchQuery query, CancellationToken ct)
    {
        var rows = FilteredQuery(query);
        var total = await rows.CountAsync(ct);
        var page = await IncludeDetails(rows.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)).ToArrayAsync(ct);
        return (page, total);
    }

    // Internal for offline PostgreSQL translation tests.
    internal IQueryable<CareerConsultant> FilteredQuery(ConsultantSearchQuery query)
    {
        var rows = PublicQuery();
        if (query.CompanyId.HasValue) rows = rows.Where(p => p.CompanyId == query.CompanyId);
        if (query.ProfessionalType.HasValue) rows = rows.Where(p => p.ProfessionalType == query.ProfessionalType);
#pragma warning disable CA1304, CA1311, CA1862 // SQL-translatable UPPER; no culture-specific user casing in parameters.
        if (!string.IsNullOrWhiteSpace(query.Company))
        { var term = query.Company.Trim().ToUpperInvariant(); rows = rows.Where(p => p.CompanyName.ToUpper().Contains(term)); }
        if (!string.IsNullOrWhiteSpace(query.Role))
        { var term = query.Role.Trim().ToUpperInvariant(); rows = rows.Where(p => p.CurrentRole.ToUpper().Contains(term)); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            rows = rows.Where(p => p.DisplayName.ToUpper().Contains(term) || p.ProfessionalHeadline.ToUpper().Contains(term) ||
                p.CompanyName.ToUpper().Contains(term) || p.CurrentRole.ToUpper().Contains(term));
        }
#pragma warning restore CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(query.Language))
        { var value = query.Language.Trim().ToUpperInvariant(); rows = rows.Where(p => p.Tags.Any(t => t.Kind == ConsultantTagKind.Language && t.Value == value)); }
        if (!string.IsNullOrWhiteSpace(query.Expertise))
        { var value = query.Expertise.Trim().ToUpperInvariant(); rows = rows.Where(p => p.Tags.Any(t => t.Kind == ConsultantTagKind.Expertise && t.Value == value)); }
        var type = query.ServiceType?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(type) || query.Currency is not null || query.MinPrice.HasValue || query.MaxPrice.HasValue)
            rows = rows.Where(p => p.Services.Any(s => s.IsActive && (string.IsNullOrEmpty(type) || s.ServiceType == type) &&
                (query.Currency == null || s.Currency == query.Currency) && (!query.MinPrice.HasValue || s.Price >= query.MinPrice) &&
                (!query.MaxPrice.HasValue || s.Price <= query.MaxPrice)));
        return query.Sort switch
        {
            "experience" => rows.OrderByDescending(p => p.YearsOfExperience).ThenBy(p => p.Id),
            "price-asc" => rows.OrderBy(p => p.Services.Where(s => s.IsActive && s.Currency == query.Currency &&
                (string.IsNullOrEmpty(type) || s.ServiceType == type) && (!query.MinPrice.HasValue || s.Price >= query.MinPrice) &&
                (!query.MaxPrice.HasValue || s.Price <= query.MaxPrice)).Min(s => (decimal?)s.Price)).ThenBy(p => p.Id),
            "price-desc" => rows.OrderByDescending(p => p.Services.Where(s => s.IsActive && s.Currency == query.Currency &&
                (string.IsNullOrEmpty(type) || s.ServiceType == type) && (!query.MinPrice.HasValue || s.Price >= query.MinPrice) &&
                (!query.MaxPrice.HasValue || s.Price <= query.MaxPrice)).Min(s => (decimal?)s.Price)).ThenBy(p => p.Id),
            _ => rows.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id)
        };
    }
}
