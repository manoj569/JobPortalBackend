using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class PublicJobRepository(
    JobPortalDbContext context,
    TimeProvider timeProvider) : IPublicJobRepository
{
    public async Task<(IReadOnlyCollection<PublicJobSummary> Items, int TotalCount)> SearchAsync(
        PublicJobQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = FilteredJobsQuery(query);
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await ApplySorting(source, query.SortBy, query.SortDirection)
            .Skip((query.EffectivePageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(PublicJobProjections.Summary)
            .ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<PublicJobDetails?> GetDetailsAsync(
        string slug,
        CancellationToken cancellationToken = default) =>
        AvailableJobs()
            .Where(job => job.Slug == slug)
            .Select(job => new PublicJobDetails(
                job.Id, job.ReferenceNumber, job.Title, job.Slug, job.Description,
                job.Responsibilities, job.Requirements, job.Benefits,
                job.CompanyId, job.Company.Name, job.Company.Slug, job.Company.LogoUrl,
                job.Company.Description, job.Company.WebsiteUrl,
                job.CategoryId, job.Category.Name, job.Location,
                job.MinimumSalary, job.MaximumSalary, job.CurrencyCode,
                job.EmploymentType, job.WorkplaceType, job.ExperienceLevel,
                job.IsFeatured, job.PublishedAtUtc!.Value, job.ExpiresAtUtc,
                job.MinimumExperienceYears, job.MaximumExperienceYears,
                job.InternshipDurationMonths, job.IsFlexibleDuration,
                job.Department, job.RoleCategory, job.EducationRequirement,
                job.PostedByType, job.Company.CompanyType, job.Company.Industry,
                job.ApplicationUrl))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<PublicJobSummary>> GetRelatedAsync(
        string slug,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var target = await AvailableJobs().Where(job => job.Slug == slug)
            .Select(job => new
            {
                job.Id,
                job.CategoryId,
                job.CompanyId,
                job.EmploymentType
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
            return Array.Empty<PublicJobSummary>();

        return await AvailableJobs()
            .Where(job => job.Id != target.Id &&
                (job.CategoryId == target.CategoryId ||
                 job.CompanyId == target.CompanyId ||
                 job.EmploymentType == target.EmploymentType))
            .OrderByDescending(job => job.CategoryId == target.CategoryId)
            .ThenByDescending(job => job.CompanyId == target.CompanyId)
            .ThenByDescending(job => job.IsFeatured)
            .ThenByDescending(job => job.PublishedAtUtc)
            .ThenBy(job => job.Id)
            .Take(limit)
            .Select(PublicJobProjections.Summary)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<PopularCompanyResponse>> GetPopularCompaniesAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await PopularCompaniesQuery(limit).ToArrayAsync(cancellationToken);

    public async Task<PublicJobFilterOptionsResponse> GetFilterOptionsAsync(
        PublicJobQuery query,
        CancellationToken cancellationToken = default)
    {
        var locations = await LocationFacetQuery(query).ToArrayAsync(cancellationToken);

        var workModes = await ApplyFilters(AvailableJobs(), query, FacetDimension.WorkMode)
            .GroupBy(job => job.WorkplaceType)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var departments = await ApplyFilters(AvailableJobs(), query, FacetDimension.Department)
            .Where(job => job.Department != null && job.Department != string.Empty)
            .GroupBy(job => job.Department!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var roleCategories = await ApplyFilters(
                AvailableJobs(), query, FacetDimension.RoleCategory)
            .Where(job => job.RoleCategory != null && job.RoleCategory != string.Empty)
            .GroupBy(job => job.RoleCategory!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var employmentTypes = await ApplyFilters(
                AvailableJobs(), query, FacetDimension.EmploymentType)
            .GroupBy(job => job.EmploymentType)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var companyTypes = await ApplyFilters(
                AvailableJobs(), query, FacetDimension.CompanyType)
            .Where(job => job.Company.CompanyType.HasValue)
            .GroupBy(job => job.Company.CompanyType!.Value)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var companies = await ApplyFilters(AvailableJobs(), query, FacetDimension.Company)
            .GroupBy(job => new { job.CompanyId, job.Company.Name })
            .Select(group => new
            {
                Id = group.Key.CompanyId,
                group.Key.Name,
                Count = group.Count()
            })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Name)
            .ToArrayAsync(cancellationToken);

        var industries = await ApplyFilters(AvailableJobs(), query, FacetDimension.Industry)
            .Where(job => job.Company.Industry != null && job.Company.Industry != string.Empty)
            .GroupBy(job => job.Company.Industry!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var education = await ApplyFilters(AvailableJobs(), query, FacetDimension.Education)
            .Where(job => job.EducationRequirement != null &&
                job.EducationRequirement != string.Empty)
            .GroupBy(job => job.EducationRequirement!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var postedBy = await ApplyFilters(AvailableJobs(), query, FacetDimension.PostedBy)
            .Where(job => job.PostedByType.HasValue)
            .GroupBy(job => job.PostedByType!.Value)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value)
            .ToArrayAsync(cancellationToken);

        var durations = await ApplyFilters(AvailableJobs(), query, FacetDimension.Duration)
            .Where(job => job.EmploymentType == EmploymentType.Internship &&
                (job.InternshipDurationMonths.HasValue || job.IsFlexibleDuration))
            .GroupBy(job => new { job.InternshipDurationMonths, job.IsFlexibleDuration })
            .Select(group => new
            {
                Months = group.Key.InternshipDurationMonths,
                IsFlexible = group.Key.IsFlexibleDuration,
                Count = group.Count()
            })
            .OrderBy(option => option.IsFlexible)
            .ThenBy(option => option.Months)
            .ToArrayAsync(cancellationToken);

        var salary = await ApplyFilters(AvailableJobs(), query, FacetDimension.Salary)
            .Where(job => job.MinimumSalary.HasValue || job.MaximumSalary.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Minimum = group.Min(job => job.MinimumSalary ?? job.MaximumSalary),
                Maximum = group.Max(job => job.MaximumSalary ?? job.MinimumSalary),
                Count = group.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var experience = await ApplyFilters(AvailableJobs(), query, FacetDimension.Experience)
            .Where(job => job.MinimumExperienceYears.HasValue ||
                job.MaximumExperienceYears.HasValue)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Minimum = group.Min(job =>
                    job.MinimumExperienceYears ?? job.MaximumExperienceYears),
                Maximum = group.Max(job =>
                    job.MaximumExperienceYears ?? job.MinimumExperienceYears),
                Count = group.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new(
            locations,
            workModes.Select(option =>
                new EnumFacetOption<WorkplaceType>(option.Value, option.Count)).ToArray(),
            departments.Select(option =>
                new StringFacetOption(option.Value, option.Count)).ToArray(),
            roleCategories.Select(option =>
                new StringFacetOption(option.Value, option.Count)).ToArray(),
            employmentTypes.Select(option =>
                new EnumFacetOption<EmploymentType>(option.Value, option.Count)).ToArray(),
            companyTypes.Select(option =>
                new EnumFacetOption<CompanyType>(option.Value, option.Count)).ToArray(),
            companies.Select(option =>
                new CompanyFacetOption(option.Id, option.Name, option.Count)).ToArray(),
            industries.Select(option =>
                new StringFacetOption(option.Value, option.Count)).ToArray(),
            education.Select(option =>
                new StringFacetOption(option.Value, option.Count)).ToArray(),
            postedBy.Select(option =>
                new EnumFacetOption<PostedByType>(option.Value, option.Count)).ToArray(),
            durations.Select(option => new InternshipDurationFacetOption(
                option.Months,
                option.IsFlexible,
                option.IsFlexible ? "Flexible" : $"{option.Months} month(s)",
                option.Count)).ToArray(),
            new DecimalRangeFacet(salary?.Minimum, salary?.Maximum, salary?.Count ?? 0),
            new IntegerRangeFacet(
                experience?.Minimum,
                experience?.Maximum,
                experience?.Count ?? 0));
    }

    internal IQueryable<PopularCompanyResponse> PopularCompaniesQuery(int limit)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var companies = context.Companies.AsNoTracking()
            .Select(company => new
            {
                company.Id,
                company.Name,
                company.Slug,
                company.LogoUrl,
                company.Industry,
                company.Location,
                company.IsVerified,
                ActiveJobCount = company.Jobs.Count(job =>
                    job.Status == JobStatus.Published &&
                    !job.IsHidden &&
                    !job.IsDeleted &&
                    job.PublishedAtUtc.HasValue &&
                    (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc > utcNow))
            });

        return companies
            .Where(company => company.ActiveJobCount > 0)
            .OrderByDescending(company => company.ActiveJobCount)
            .ThenByDescending(company => company.IsVerified)
            .ThenBy(company => company.Name)
            .Take(limit)
            .Select(company => new PopularCompanyResponse(
                company.Id,
                company.Name,
                company.Slug,
                company.LogoUrl,
                company.Industry,
                company.Location,
                company.IsVerified,
                company.ActiveJobCount));
    }

    internal IQueryable<Job> FilteredJobsQuery(PublicJobQuery query) =>
        ApplyFilters(AvailableJobs(), query);

    internal IQueryable<StringFacetOption> LocationFacetQuery(PublicJobQuery query)
    {
        var options = ApplyFilters(AvailableJobs(), query, FacetDimension.Location)
            .Where(job => job.Location != null && job.Location != string.Empty)
            .GroupBy(job => job.Location!)
            .Select(group => new { Value = group.Key, Count = group.Count() })
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Value);

        return options.Select(option => new StringFacetOption(option.Value, option.Count));
    }

    private IQueryable<Job> AvailableJobs()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return context.Jobs.AsNoTracking().Where(job =>
            job.Status == JobStatus.Published &&
            !job.IsHidden &&
            !job.IsDeleted &&
            job.PublishedAtUtc.HasValue &&
            (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc > utcNow));
    }

    private IQueryable<Job> ApplyFilters(
        IQueryable<Job> source,
        PublicJobQuery query,
        FacetDimension excluded = FacetDimension.None)
    {
        var keyword = FirstValue(query.Keyword, query.Search);
        if (keyword is not null)
        {
            source = source.Where(job =>
                job.Title.Contains(keyword) ||
                job.Company.Name.Contains(keyword) ||
                job.Category.Name.Contains(keyword) ||
                job.Description.Contains(keyword) ||
                job.JobSkills.Any(jobSkill => jobSkill.Skill.Name.Contains(keyword)));
        }

        if (query.CategoryId.HasValue)
            source = source.Where(job => job.CategoryId == query.CategoryId.Value);
#pragma warning disable CA1304, CA1311, CA1862 // Parameterless casing is translated by EF; comparison overloads are not.
        var categoryName = FirstValue(query.CategoryName);
        if (categoryName is not null)
        {
            var normalizedCategoryName = categoryName.ToLower();
            source = source.Where(job =>
                job.Category.Name.ToLower().Contains(normalizedCategoryName));
        }
        if (query.ExperienceLevel.HasValue)
            source = source.Where(job => job.ExperienceLevel == query.ExperienceLevel.Value);

        if (excluded != FacetDimension.Company)
        {
            var companyIds = Values(query.CompanyIds);
            if (query.CompanyId.HasValue)
                source = source.Where(job => job.CompanyId == query.CompanyId.Value);
            if (companyIds.Length > 0)
                source = source.Where(job => companyIds.Contains(job.CompanyId));
            var companyName = FirstValue(query.CompanyName);
            if (companyName is not null)
            {
                var normalizedCompanyName = companyName.ToLower();
                source = source.Where(job =>
                    job.Company.Name.ToLower().Contains(normalizedCompanyName));
            }
        }
#pragma warning restore CA1304, CA1311, CA1862

        if (excluded != FacetDimension.Location)
        {
            var location = FirstValue(query.Location);
            var locations = Values(query.Locations);
            if (location is not null)
                source = source.Where(job =>
                    job.Location != null && job.Location.Contains(location));
            if (locations.Length > 0)
                source = source.Where(job =>
                    job.Location != null && locations.Contains(job.Location));
        }

        if (excluded != FacetDimension.WorkMode)
        {
            var workModes = Values(query.WorkModes);
            if (query.WorkplaceType.HasValue)
                source = source.Where(job => job.WorkplaceType == query.WorkplaceType.Value);
            if (workModes.Length > 0)
                source = source.Where(job => workModes.Contains(job.WorkplaceType));
        }

        if (excluded != FacetDimension.EmploymentType)
        {
            var employmentTypes = Values(query.EmploymentTypes);
            if (query.EmploymentType.HasValue)
                source = source.Where(job => job.EmploymentType == query.EmploymentType.Value);
            if (employmentTypes.Length > 0)
                source = source.Where(job => employmentTypes.Contains(job.EmploymentType));
        }

        if (excluded != FacetDimension.Department)
        {
            var departments = Values(query.Departments);
            if (departments.Length > 0)
                source = source.Where(job =>
                    job.Department != null && departments.Contains(job.Department));
        }

        if (excluded != FacetDimension.RoleCategory)
        {
            var roleCategories = Values(query.RoleCategories);
            if (roleCategories.Length > 0)
                source = source.Where(job =>
                    job.RoleCategory != null && roleCategories.Contains(job.RoleCategory));
        }

        if (excluded != FacetDimension.Education)
        {
            var education = Values(query.EducationRequirements);
            if (education.Length > 0)
                source = source.Where(job => job.EducationRequirement != null &&
                    education.Contains(job.EducationRequirement));
        }

        if (excluded != FacetDimension.CompanyType)
        {
            var companyTypes = Values(query.CompanyTypes);
            if (companyTypes.Length > 0)
                source = source.Where(job => job.Company.CompanyType.HasValue &&
                    companyTypes.Contains(job.Company.CompanyType.Value));
        }

        if (excluded != FacetDimension.Industry)
        {
            var industries = Values(query.Industries);
            if (industries.Length > 0)
                source = source.Where(job => job.Company.Industry != null &&
                    industries.Contains(job.Company.Industry));
        }

        if (excluded != FacetDimension.PostedBy)
        {
            var postedByTypes = Values(query.PostedByTypes);
            if (postedByTypes.Length > 0)
                source = source.Where(job => job.PostedByType.HasValue &&
                    postedByTypes.Contains(job.PostedByType.Value));
        }

        if (excluded != FacetDimension.Experience)
        {
            if (query.MinExperienceYears.HasValue)
                source = source.Where(job =>
                    (job.MaximumExperienceYears ?? job.MinimumExperienceYears) >=
                    query.MinExperienceYears.Value);
            if (query.MaxExperienceYears.HasValue)
                source = source.Where(job =>
                    (job.MinimumExperienceYears ?? job.MaximumExperienceYears) <=
                    query.MaxExperienceYears.Value);
        }

        if (excluded != FacetDimension.Salary)
        {
            var minimum = query.MinAmount ?? query.MinimumSalary;
            var maximum = query.MaxAmount ?? query.MaximumSalary;
            if (minimum.HasValue)
                source = source.Where(job =>
                    (job.MaximumSalary ?? job.MinimumSalary) >= minimum.Value);
            if (maximum.HasValue)
                source = source.Where(job =>
                    (job.MinimumSalary ?? job.MaximumSalary) <= maximum.Value);
        }

        if (excluded != FacetDimension.Duration)
        {
            var durations = Values(query.InternshipDurationMonths);
            if (durations.Length > 0 && query.FlexibleDuration == true)
            {
                source = source.Where(job =>
                    (job.InternshipDurationMonths.HasValue &&
                        durations.Contains(job.InternshipDurationMonths.Value)) ||
                    job.IsFlexibleDuration);
            }
            else if (durations.Length > 0)
            {
                source = source.Where(job =>
                    job.InternshipDurationMonths.HasValue &&
                    durations.Contains(job.InternshipDurationMonths.Value) &&
                    (query.FlexibleDuration != false || !job.IsFlexibleDuration));
            }
            else if (query.FlexibleDuration.HasValue)
            {
                source = source.Where(job =>
                    job.IsFlexibleDuration == query.FlexibleDuration.Value);
            }
        }

        var featuredOnly = query.FeaturedOnly ?? query.IsFeatured;
        if (featuredOnly.HasValue)
            source = source.Where(job => job.IsFeatured == featuredOnly.Value);
        if (query.FreshnessDays.HasValue)
        {
            var publishedAfter = timeProvider.GetUtcNow().UtcDateTime
                .AddDays(-query.FreshnessDays.Value);
            source = source.Where(job => job.PublishedAtUtc >= publishedAfter);
        }

        return source;
    }

    private static IOrderedQueryable<Job> ApplySorting(
        IQueryable<Job> source,
        PublicJobSort sortBy,
        string sortDirection)
    {
        var descending = sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        return sortBy switch
        {
            PublicJobSort.LatestPublished => source
                .OrderByDescending(job => job.PublishedAtUtc)
                .ThenBy(job => job.Id),
            PublicJobSort.NewestAdded => source
                .OrderByDescending(job => job.CreatedAtUtc)
                .ThenBy(job => job.Id),
            PublicJobSort.ClosingSoon => source
                .OrderByDescending(job => job.ExpiresAtUtc.HasValue)
                .ThenBy(job => job.ExpiresAtUtc)
                .ThenBy(job => job.Id),
            PublicJobSort.SalaryHighToLow => source
                .OrderByDescending(job => job.MaximumSalary ?? job.MinimumSalary)
                .ThenByDescending(job => job.IsFeatured)
                .ThenByDescending(job => job.PublishedAtUtc)
                .ThenBy(job => job.Id),
            PublicJobSort.Title when !descending => source
                .OrderBy(job => job.Title)
                .ThenBy(job => job.Id),
            PublicJobSort.Title => source
                .OrderByDescending(job => job.Title)
                .ThenBy(job => job.Id),
            PublicJobSort.MinimumSalary when !descending => source
                .OrderBy(job => job.MinimumSalary)
                .ThenBy(job => job.Id),
            PublicJobSort.MinimumSalary => source
                .OrderByDescending(job => job.MinimumSalary)
                .ThenBy(job => job.Id),
            PublicJobSort.MaximumSalary when !descending => source
                .OrderBy(job => job.MaximumSalary)
                .ThenBy(job => job.Id),
            PublicJobSort.MaximumSalary => source
                .OrderByDescending(job => job.MaximumSalary)
                .ThenBy(job => job.Id),
            _ => source
                .OrderByDescending(job => job.IsFeatured)
                .ThenByDescending(job => job.PublishedAtUtc)
                .ThenBy(job => job.Id)
        };
    }

    private static string? FirstValue(params string?[] values) =>
        values.Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string[] Values(string[]? values) =>
        values?.Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static T[] Values<T>(T[]? values) where T : struct =>
        values?.Distinct().ToArray() ?? [];

    private enum FacetDimension
    {
        None,
        Location,
        WorkMode,
        Experience,
        Department,
        RoleCategory,
        EmploymentType,
        Salary,
        Duration,
        Education,
        CompanyType,
        Company,
        Industry,
        PostedBy
    }
}
