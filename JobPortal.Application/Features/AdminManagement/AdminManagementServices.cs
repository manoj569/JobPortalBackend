using FluentValidation;
using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.AdminManagement;

public sealed class CompanyManagementService(
    ICompanyManagementRepository companies,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreateCompanyRequest> createValidator,
    IValidator<UpdateCompanyRequest> updateValidator,
    IValidator<CompanySearchQuery> searchValidator) : ICompanyManagementService
{
    public async Task<PagedResponse<CompanyResponse>> SearchAsync(CompanySearchQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await companies.SearchAsync(query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<CompanyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await companies.GetResponseAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Company '{id}' was not found.");

    public async Task<CompanyResponse> CreateAsync(
        Guid administratorUserId, CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        await EnsureUniqueNameAsync(request.Name, null, cancellationToken);
        var slug = RequiredSlug(request.Slug, request.Name);
        await EnsureUniqueSlugAsync(slug, null, cancellationToken);
        var company = new Company { OwnerUserId = administratorUserId };
        Apply(company, request.Name, slug, request.Description, request.WebsiteUrl, request.LogoUrl,
            request.Industry, request.Location, request.EmployeeCount, request.IsVerified,
            request.CompanyType);
        await companies.AddAsync(company, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Create,
            "Company",
            company.Id.ToString(),
            Actor: new(administratorUserId, "Administrator")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(company.Id, cancellationToken);
    }

    public async Task<CompanyResponse> UpdateAsync(Guid id, UpdateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var company = await companies.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Company '{id}' was not found.");
        await EnsureUniqueNameAsync(request.Name, id, cancellationToken);
        var slug = RequiredSlug(request.Slug, request.Name);
        await EnsureUniqueSlugAsync(slug, id, cancellationToken);
        Apply(company, request.Name, slug, request.Description, request.WebsiteUrl, request.LogoUrl,
            request.Industry, request.Location, request.EmployeeCount, request.IsVerified,
            request.CompanyType);
        await auditWriter.AppendAsync(new(
            AuditAction.Update, "Company", company.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await companies.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Company '{id}' was not found.");
        if (await companies.HasJobsAsync(id, cancellationToken))
            throw new ConflictException("A company referenced by jobs cannot be deleted.");
        companies.Remove(company);
        await auditWriter.AppendAsync(new(
            AuditAction.Delete, "Company", company.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
        companies.GetOptionsAsync(cancellationToken);

    private async Task EnsureUniqueSlugAsync(string slug, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (await companies.SlugExistsAsync(slug, excludingId, cancellationToken))
            throw new ConflictException($"An active company with slug '{slug}' already exists.");
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (await companies.NameExistsAsync(CompanyNameNormalizer.Normalize(name), excludingId, cancellationToken))
            throw new ConflictException("An active company with this name already exists.", "duplicate_company_name");
    }

    private static string RequiredSlug(string? requested, string name)
    {
        var slug = SlugGenerator.Generate(string.IsNullOrWhiteSpace(requested) ? name : requested);
        if (slug.Length == 0) throw new BadRequestException("A valid company slug could not be generated.", "invalid_slug");
        return slug;
    }

    private static void Apply(Company company, string name, string slug, string? description,
        string? websiteUrl, string? logoUrl, string? industry, string? location,
        int? employeeCount, bool isVerified, CompanyType? companyType)
    {
        company.Name = name.Trim();
        company.Slug = slug;
        company.Description = TextNormalizer.TrimOrNull(description);
        company.WebsiteUrl = TextNormalizer.TrimOrNull(websiteUrl);
        company.LogoUrl = TextNormalizer.TrimOrNull(logoUrl);
        company.Industry = TextNormalizer.TrimOrNull(industry);
        company.Location = TextNormalizer.TrimOrNull(location);
        company.EmployeeCount = employeeCount;
        company.IsVerified = isVerified;
        company.CompanyType = companyType;
    }
}

public sealed class CategoryManagementService(
    ICategoryManagementRepository categories,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreateCategoryRequest> createValidator,
    IValidator<UpdateCategoryRequest> updateValidator,
    IValidator<CategorySearchQuery> searchValidator) : ICategoryManagementService
{
    public async Task<PagedResponse<CategoryResponse>> SearchAsync(CategorySearchQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await categories.SearchAsync(query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await categories.GetResponseAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Category '{id}' was not found.");

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ValidateParentAsync(null, request.ParentCategoryId, cancellationToken);
        var slug = RequiredSlug(request.Slug, request.Name);
        await EnsureUniqueSlugAsync(slug, null, cancellationToken);
        var category = new Category();
        Apply(category, request.Name, slug, request.Description, request.DisplayOrder, request.ParentCategoryId);
        await categories.AddAsync(category, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Create, "Category", category.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(category.Id, cancellationToken);
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var category = await categories.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Category '{id}' was not found.");
        await ValidateParentAsync(id, request.ParentCategoryId, cancellationToken);
        var slug = RequiredSlug(request.Slug, request.Name);
        await EnsureUniqueSlugAsync(slug, id, cancellationToken);
        Apply(category, request.Name, slug, request.Description, request.DisplayOrder, request.ParentCategoryId);
        await auditWriter.AppendAsync(new(
            AuditAction.Update, "Category", category.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Category '{id}' was not found.");
        if (await categories.HasChildrenOrJobsAsync(id, cancellationToken))
            throw new ConflictException("A category referenced by active children or jobs cannot be deleted.");
        categories.Remove(category);
        await auditWriter.AppendAsync(new(
            AuditAction.Delete, "Category", category.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
        categories.GetOptionsAsync(cancellationToken);

    private async Task ValidateParentAsync(Guid? id, Guid? parentId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue) return;
        if (id == parentId) throw new BadRequestException("A category cannot be its own parent.", "invalid_parent");
        if (!await categories.ExistsAsync(parentId.Value, cancellationToken))
            throw new BadRequestException($"Parent category '{parentId}' does not exist.", "invalid_parent");
        if (id.HasValue && await categories.IsDescendantAsync(id.Value, parentId.Value, cancellationToken))
            throw new BadRequestException("The selected parent would create a category cycle.", "category_cycle");
    }

    private async Task EnsureUniqueSlugAsync(string slug, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (await categories.SlugExistsAsync(slug, excludingId, cancellationToken))
            throw new ConflictException($"An active category with slug '{slug}' already exists.");
    }

    private static string RequiredSlug(string? requested, string name)
    {
        var slug = SlugGenerator.Generate(string.IsNullOrWhiteSpace(requested) ? name : requested, 170);
        if (slug.Length == 0) throw new BadRequestException("A valid category slug could not be generated.", "invalid_slug");
        return slug;
    }

    private static void Apply(Category category, string name, string slug, string? description,
        int displayOrder, Guid? parentCategoryId)
    {
        category.Name = name.Trim();
        category.Slug = slug;
        category.Description = TextNormalizer.TrimOrNull(description);
        category.DisplayOrder = displayOrder;
        category.ParentCategoryId = parentCategoryId;
    }
}
