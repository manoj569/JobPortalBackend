using System.Globalization;
using System.Text;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Jobs;

public sealed class JobService(
    IJobRepository jobs,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreateJobRequest> createValidator,
    IValidator<UpdateJobRequest> updateValidator,
    IValidator<UpdateRecruiterContactRequest> recruiterContactValidator,
    IValidator<JobSearchQuery> searchValidator,
    TimeProvider timeProvider,
    ICompanyManagementRepository? companies = null,
    ICategoryManagementRepository? categories = null) : IJobService
{
    public async Task<ComposeJobResponse> ComposeAsync(Guid administratorUserId, ComposeJobRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Job is null || string.IsNullOrWhiteSpace(request.Job.Title))
            throw new BadRequestException("Job title is required.", "validation_error");
        if (request.Job.Title.Trim().Length > 250)
            throw new BadRequestException("Job title cannot exceed 250 characters.", "validation_error");
        ValidateRelation(request.Company?.ExistingId, request.Company?.New, "company");
        ValidateRelation(request.Category?.ExistingId, request.Category?.New, "category");
        if (request.Company is null || (!request.Company.ExistingId.HasValue && request.Company.New is null))
            throw new BadRequestException("A company is required by the current job schema.", "validation_error");
        if (request.Category is null || (!request.Category.ExistingId.HasValue && request.Category.New is null))
            throw new BadRequestException("A category is required by the current job schema.", "validation_error");
        if (request.Job.MinimumSalary < 0 || request.Job.MaximumSalary < 0 ||
            request.Job.MinimumSalary > request.Job.MaximumSalary)
            throw new BadRequestException("Maximum salary must be greater than or equal to minimum salary.", "validation_error");
        if (request.Job.ExpiresAtUtc.HasValue && request.Job.ExpiresAtUtc <= UtcNow)
            throw new BadRequestException("Closing date must be in the future.", "validation_error");
        if (request.Job.ApplicationUrl is { Length: > 0 } url &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new BadRequestException("ApplicationUrl must be an absolute HTTP or HTTPS URL.", "validation_error");
        var recruiterRequest = ToRecruiterContactRequest(request.RecruiterContact);
        if (recruiterRequest is not null)
            await recruiterContactValidator.ValidateAndThrowAsync(recruiterRequest, cancellationToken);

        var (company, companyCreated) = await ResolveCompanyAsync(administratorUserId, request.Company, cancellationToken);
        var (category, categoryCreated) = await ResolveCategoryAsync(request.Category, cancellationToken);
        var id = Guid.NewGuid();
        var job = new Job
        {
            Id = id,
            ReferenceNumber = $"JOB-{UtcNow:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            Title = request.Job.Title.Trim(),
            Slug = $"{SlugGenerator.Generate(request.Job.Title, 240)}-{id.ToString("N")[..8]}",
            Description = TextNormalizer.TrimOrNull(request.Job.Description) ?? string.Empty,
            ApplicationUrl = TextNormalizer.TrimOrNull(request.Job.ApplicationUrl) ?? string.Empty,
            EmploymentType = request.Job.EmploymentType ?? default,
            WorkplaceType = request.Job.WorkplaceType ?? default,
            ExperienceLevel = request.Job.ExperienceLevel ?? default,
            Location = TextNormalizer.TrimOrNull(request.Job.Location),
            MinimumSalary = request.Job.MinimumSalary,
            MaximumSalary = request.Job.MaximumSalary,
            CurrencyCode = TextNormalizer.TrimOrNull(request.Job.CurrencyCode)?.ToUpperInvariant() ?? "USD",
            ExpiresAtUtc = request.Job.ExpiresAtUtc,
            Responsibilities = TextNormalizer.TrimOrNull(request.Job.Responsibilities),
            Requirements = TextNormalizer.TrimOrNull(request.Job.Requirements),
            Benefits = TextNormalizer.TrimOrNull(request.Job.Benefits),
            Company = company!,
            CompanyId = company!.Id,
            Category = category!,
            CategoryId = category!.Id,
            Status = JobStatus.Draft
        };
        if (recruiterRequest is not null)
        {
            job.RecruiterContact = new JobRecruiterContact
            {
                JobId = job.Id,
                ContactName = recruiterRequest.ContactName.Trim(),
                ContactRole = recruiterRequest.ContactRole.Trim(),
                Email = recruiterRequest.Email.Trim(),
                PhoneNumber = TextNormalizer.TrimOrNull(recruiterRequest.PhoneNumber),
                IsSharingApproved = recruiterRequest.IsSharingApproved
            };
        }
        await jobs.AddAsync(job, cancellationToken);
        if (companyCreated) await auditWriter.AppendAsync(new(AuditAction.Create, "Company", company!.Id.ToString()), cancellationToken);
        if (categoryCreated) await auditWriter.AppendAsync(new(AuditAction.Create, "Category", category!.Id.ToString()), cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Create, "Job", job.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["status"] = JobStatus.Draft.ToString(),
                ["recruiterContactCreated"] = (job.RecruiterContact is not null).ToString()
            }), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(job.Id, job.Slug, job.Status,
            company is null ? null : new(company.Id, company.Name, companyCreated),
            category is null ? null : new(category.Id, category.Name, categoryCreated),
            job.RecruiterContact is not null);
    }

    private static UpdateRecruiterContactRequest? ToRecruiterContactRequest(
        ComposeRecruiterContactRequest? value)
    {
        if (value is null || (string.IsNullOrWhiteSpace(value.Name) &&
            string.IsNullOrWhiteSpace(value.Role) && string.IsNullOrWhiteSpace(value.Email) &&
            string.IsNullOrWhiteSpace(value.PhoneNumber) && !value.SharingApproved))
            return null;
        return new(value.Name ?? string.Empty, value.Role ?? string.Empty,
            value.Email ?? string.Empty, value.PhoneNumber, value.SharingApproved);
    }

    private static void ValidateRelation<T>(Guid? existingId, T? inline, string relation) where T : class
    {
        if (existingId.HasValue && inline is not null)
            throw new BadRequestException($"Provide either an existing {relation} ID or a new {relation}, not both.", "validation_error");
        if (existingId == Guid.Empty)
            throw new BadRequestException($"Existing {relation} ID cannot be empty.", "validation_error");
    }

    private async Task<(Company?, bool)> ResolveCompanyAsync(Guid actorId,
        ComposeRelationRequest<CreateInlineCompanyRequest>? relation, CancellationToken cancellationToken)
    {
        if (relation?.ExistingId is Guid id)
            return (await companies!.GetByIdAsync(id, cancellationToken) ??
                throw new BadRequestException($"Company '{id}' does not exist.", "invalid_company"), false);
        if (relation?.New is not { } value) return (null, false);
        var name = value.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new BadRequestException("Company name is required.", "validation_error");
        var slug = TextNormalizer.TrimOrNull(value.Slug) ?? SlugGenerator.Generate(name);
        var normalized = name;
        var existing = await companies!.FindByNameOrSlugAsync(normalized, slug, cancellationToken);
        if (existing is not null) return (existing, false);
        var company = new Company { Id = Guid.NewGuid(), Name = name, Slug = slug,
            Description = TextNormalizer.TrimOrNull(value.Description), WebsiteUrl = TextNormalizer.TrimOrNull(value.WebsiteUrl),
            LogoUrl = TextNormalizer.TrimOrNull(value.LogoUrl), Industry = TextNormalizer.TrimOrNull(value.Industry),
            Location = TextNormalizer.TrimOrNull(value.Location), EmployeeCount = value.EmployeeCount,
            IsVerified = value.IsVerified, OwnerUserId = actorId };
        await companies!.AddAsync(company, cancellationToken);
        return (company, true);
    }

    private async Task<(Category?, bool)> ResolveCategoryAsync(
        ComposeRelationRequest<CreateInlineCategoryRequest>? relation, CancellationToken cancellationToken)
    {
        if (relation?.ExistingId is Guid id)
            return (await categories!.GetByIdAsync(id, cancellationToken) ??
                throw new BadRequestException($"Category '{id}' does not exist.", "invalid_category"), false);
        if (relation?.New is not { } value) return (null, false);
        var name = value.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new BadRequestException("Category name is required.", "validation_error");
        var slug = TextNormalizer.TrimOrNull(value.Slug) ?? SlugGenerator.Generate(name);
        var existing = await categories!.FindByNameOrSlugAsync(name, slug, cancellationToken);
        if (existing is not null) return (existing, false);
        if (value.ParentCategoryId.HasValue && !await categories!.ExistsAsync(value.ParentCategoryId.Value, cancellationToken))
            throw new BadRequestException($"Parent category '{value.ParentCategoryId}' does not exist.", "invalid_category");
        var category = new Category { Id = Guid.NewGuid(), Name = name, Slug = slug,
            Description = TextNormalizer.TrimOrNull(value.Description), DisplayOrder = value.DisplayOrder,
            ParentCategoryId = value.ParentCategoryId };
        await categories!.AddAsync(category, cancellationToken);
        return (category, true);
    }
    public async Task<JobResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ValidateReferencesAsync(request.CompanyId, request.CategoryId, cancellationToken);

        var id = Guid.NewGuid();
        var job = new Job
        {
            Id = id,
            ReferenceNumber = $"JOB-{UtcNow:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            Slug = $"{Slugify(request.Title)}-{id.ToString("N")[..8]}",
            Status = JobStatus.Draft
        };
        job.Apply(new UpdateJobRequest(request.Title, request.Description, request.CompanyId, request.CategoryId, request.ApplicationUrl,
            request.Responsibilities, request.Requirements, request.Benefits, request.Location,
            request.MinimumSalary, request.MaximumSalary, request.CurrencyCode, request.EmploymentType,
            request.WorkplaceType, request.ExperienceLevel, request.ExpiresAtUtc,
            request.MinimumExperienceYears, request.MaximumExperienceYears,
            request.InternshipDurationMonths, request.IsFlexibleDuration, request.Department,
            request.RoleCategory, request.EducationRequirement, request.PostedByType));

        await jobs.AddAsync(job, cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Create,
            "Job",
            job.Id.ToString(),
            new Dictionary<string, string?> { ["status"] = JobStatus.Draft.ToString() }),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(job.Id, false, cancellationToken)).ToResponse();
    }

    public async Task<JobResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await RequiredJobAsync(id, true, cancellationToken)).ToResponse();

    public async Task<JobResponse> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        await ValidateReferencesAsync(request.CompanyId, request.CategoryId, cancellationToken);
        var job = await RequiredJobAsync(id, false, cancellationToken);
        if (job.Status == JobStatus.Archived)
            throw new ConflictException("An archived job cannot be updated.");
        if (job.Status == JobStatus.Published &&
            (!request.ExpiresAtUtc.HasValue || request.ExpiresAtUtc <= UtcNow))
            throw new BadRequestException(
                "A published job must have an expiration date in the future.");
        job.Apply(request);
        job.Slug = $"{Slugify(request.Title)}-{job.Id.ToString("N")[..8]}";
        jobs.Update(job);
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "Job",
            job.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["companyId"] = job.CompanyId.ToString(),
                ["categoryId"] = job.CategoryId.ToString(),
                ["status"] = job.Status.ToString()
            }), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(id, false, cancellationToken)).ToResponse();
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        jobs.Remove(job);
        await auditWriter.AppendAsync(new(
            AuditAction.Delete, "Job", job.Id.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, true, cancellationToken);
        if (!job.IsDeleted)
            throw new ConflictException("A job must be soft-deleted before it can be permanently deleted.");
        await jobs.DeletePermanentlyAsync(id, cancellationToken);
    }

    public async Task<JobResponse> PublishAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        if (job.Status == JobStatus.Published)
            throw new ConflictException("The job is already published.");
        if (job.Status == JobStatus.Archived)
            throw new ConflictException("An archived job cannot be published.");

        await updateValidator.ValidateAndThrowAsync(ToUpdateRequest(job), cancellationToken);
        await ValidateReferencesAsync(job.CompanyId, job.CategoryId, cancellationToken);
        if (!job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc <= UtcNow)
            throw new BadRequestException(
                "A job must have an expiration date in the future before it can be published.");

        job.Status = JobStatus.Published;
        job.PublishedAtUtc = UtcNow;
        job.IsFeatured = false;
        job.IsHidden = false;
        return await SaveStateChangeAsync(job, AuditAction.Publish, cancellationToken);
    }

    public Task<JobResponse> UnpublishAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, AuditAction.Unpublish, job =>
        {
            if (job.Status != JobStatus.Published)
                throw new ConflictException("Only a published job can be unpublished.");
            job.Status = JobStatus.Draft;
            job.PublishedAtUtc = null;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> CloseAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, AuditAction.Close, job =>
        {
            if (job.Status != JobStatus.Published)
                throw new ConflictException("Only a published job can be closed.");
            job.Status = JobStatus.Closed;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, AuditAction.Archive, job =>
        {
            if (job.Status == JobStatus.Archived)
                throw new ConflictException("The job is already archived.");
            job.Status = JobStatus.Archived;
            job.IsFeatured = false;
        }, cancellationToken);

    public Task<JobResponse> SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            id,
            isFeatured ? AuditAction.Feature : AuditAction.Unfeature,
            job =>
        {
            if (isFeatured && (job.Status != JobStatus.Published ||
                job.IsHidden || !job.ExpiresAtUtc.HasValue || job.ExpiresAtUtc <= UtcNow))
                throw new ConflictException(
                    "Only visible, unexpired published jobs can be featured.");
            job.IsFeatured = isFeatured;
        }, cancellationToken);

    public Task<JobResponse> SetHiddenAsync(Guid id, bool isHidden, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, null, job =>
        {
            job.IsHidden = isHidden;
            if (isHidden)
                job.IsFeatured = false;
        }, cancellationToken);
    public async Task<AdminRecruiterContactResponse> GetRecruiterContactAsync(
    Guid jobId,
    CancellationToken cancellationToken = default)
    {
        var job = await RequiredJobAsync(jobId, false, cancellationToken);

        var contact = job.RecruiterContact
            ?? throw new NotFoundException(
                "Recruiter contact details have not been added for this job.");

        return new AdminRecruiterContactResponse(
            job.Id,
            contact.ContactName,
            contact.ContactRole,
            contact.Email,
            contact.PhoneNumber,
            contact.IsSharingApproved);
    }

    public async Task<AdminRecruiterContactResponse> UpdateRecruiterContactAsync(
        Guid jobId,
        UpdateRecruiterContactRequest request,
        CancellationToken cancellationToken = default)
    {
        await recruiterContactValidator.ValidateAndThrowAsync(request, cancellationToken);

        var job = await RequiredJobAsync(jobId, false, cancellationToken);

        var contact = job.RecruiterContact ?? new JobRecruiterContact
        {
            JobId = job.Id
        };

        contact.ContactName = request.ContactName.Trim();
        contact.ContactRole = request.ContactRole.Trim();
        contact.Email = request.Email.Trim();
        contact.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : request.PhoneNumber.Trim();
        contact.IsSharingApproved = request.IsSharingApproved;

        job.RecruiterContact = contact;
        jobs.Update(job);

        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "RecruiterContact",
            job.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["jobId"] = job.Id.ToString(),
                ["isSharingApproved"] = contact.IsSharingApproved.ToString()
            }),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AdminRecruiterContactResponse(
            job.Id,
            contact.ContactName,
            contact.ContactRole,
            contact.Email,
            contact.PhoneNumber,
            contact.IsSharingApproved);
    }
    public async Task<PagedResponse<JobResponse>> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await jobs.SearchAsync(query, cancellationToken);
        return new PagedResponse<JobResponse>(result.Items.Select(x => x.ToResponse()).ToArray(),
            query.PageNumber, query.PageSize, result.TotalCount);
    }

    private async Task<JobResponse> ChangeAsync(
        Guid id,
        AuditAction? auditAction,
        Action<Job> change,
        CancellationToken cancellationToken)
    {
        var job = await RequiredJobAsync(id, false, cancellationToken);
        change(job);
        return await SaveStateChangeAsync(job, auditAction, cancellationToken);
    }

    private async Task<JobResponse> SaveStateChangeAsync(
        Job job,
        AuditAction? auditAction,
        CancellationToken cancellationToken)
    {
        jobs.Update(job);
        if (auditAction.HasValue)
        {
            await auditWriter.AppendAsync(new(
                auditAction.Value,
                "Job",
                job.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["status"] = job.Status.ToString(),
                    ["isFeatured"] = job.IsFeatured.ToString()
                }), cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await RequiredJobAsync(job.Id, false, cancellationToken)).ToResponse();
    }

    private async Task<Job> RequiredJobAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken) =>
        await jobs.GetByIdAsync(id, includeDeleted, cancellationToken)
        ?? throw new NotFoundException($"Job '{id}' was not found.");

    private async Task ValidateReferencesAsync(Guid companyId, Guid categoryId, CancellationToken cancellationToken)
    {
        if (!await jobs.CompanyExistsAsync(companyId, cancellationToken))
            throw new BadRequestException($"Company '{companyId}' does not exist.", "invalid_company");
        if (!await jobs.CategoryExistsAsync(categoryId, cancellationToken))
            throw new BadRequestException($"Category '{categoryId}' does not exist.", "invalid_category");
    }

    private static UpdateJobRequest ToUpdateRequest(Job job) => new(
        job.Title, job.Description, job.CompanyId, job.CategoryId, job.ApplicationUrl,
        job.Responsibilities, job.Requirements, job.Benefits, job.Location,
        job.MinimumSalary, job.MaximumSalary, job.CurrencyCode, job.EmploymentType,
        job.WorkplaceType, job.ExperienceLevel, job.ExpiresAtUtc,
        job.MinimumExperienceYears, job.MaximumExperienceYears,
        job.InternshipDurationMonths, job.IsFlexibleDuration, job.Department,
        job.RoleCategory, job.EducationRequirement, job.PostedByType);

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            var isAlphaNumeric = char.IsLetterOrDigit(character);
            if (isAlphaNumeric) { builder.Append(character); previousDash = false; }
            else if (!previousDash && builder.Length > 0) { builder.Append('-'); previousDash = true; }
        }
        var slug = builder.ToString().Trim('-');
        return slug[..Math.Min(slug.Length, 240)];
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

public sealed class JobExpiryService(
    IJobRepository jobs,
    TimeProvider timeProvider) : IJobExpiryService
{
    public Task<int> ExpireOverdueAsync(
        CancellationToken cancellationToken = default) =>
        jobs.ExpireOverduePublishedAsync(
            timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
}
