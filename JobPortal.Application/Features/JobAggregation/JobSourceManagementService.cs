using FluentValidation;
using JobPortal.Application.Abstractions.AdminManagement;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.JobAggregation;

public sealed class JobSourceManagementService(
    IJobSourceManagementRepository sources,
    ICompanyManagementRepository companies,
    ICategoryManagementRepository categories,
    IJobSourceCategoryResolver categoryResolver,
    IJobSourceRunner runner,
    JobSourceRunGuard runGuard,
    IUnitOfWork unitOfWork,
    IAuditWriter audit,
    IValidator<SaveJobSourceRequest> saveValidator,
    IValidator<JobSourceSearchQuery> searchValidator,
    IJobSourceExecutionLock executionLock) : IJobSourceManagementService
{
    public async Task<PagedResponse<JobSourceResponse>> SearchAsync(
        JobSourceSearchQuery query, CancellationToken cancellationToken = default)
    {
        await searchValidator.ValidateAndThrowAsync(query, cancellationToken);
        var (items, total) = await sources.SearchAsync(query, cancellationToken);
        var responses = new List<JobSourceResponse>(items.Count);
        foreach (var source in items)
            responses.Add(await ToResponseAsync(source, cancellationToken));
        return new(responses, query.PageNumber, query.PageSize, total);
    }

    public async Task<JobSourceResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ToResponseAsync(await RequiredSourceAsync(id, cancellationToken), cancellationToken);

    public async Task<JobSourceResponse> CreateAsync(SaveJobSourceRequest request, CancellationToken cancellationToken = default)
    {
        await saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        var company = await RequiredCompanyAsync(request.CompanyId, cancellationToken);
        var identifier = TextNormalizer.TrimOrNull(request.AtsIdentifier);
        await EnsureUniqueAsync(request, identifier, null, cancellationToken);
        var source = new JobSource();
        Apply(source, request, company, identifier);
        await sources.AddAsync(source, cancellationToken);
        await audit.AppendAsync(new(AuditAction.Create, "JobSource", source.Id.ToString()), cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToResponseAsync(source, cancellationToken);
    }

    public async Task<JobSourceResponse> UpdateAsync(Guid id, SaveJobSourceRequest request, CancellationToken cancellationToken = default)
    {
        using var lease = runGuard.Acquire(id);
        await using var distributed = await AcquireExecutionAsync(id, cancellationToken);
        await saveValidator.ValidateAndThrowAsync(request, cancellationToken);
        var source = await RequiredSourceAsync(id, cancellationToken);
        var company = await RequiredCompanyAsync(request.CompanyId, cancellationToken);
        var identifier = TextNormalizer.TrimOrNull(request.AtsIdentifier);
        await EnsureUniqueAsync(request, identifier, id, cancellationToken);
        Apply(source, request, company, identifier);
        // The source is tracked; do not call the runner's bookkeeping-only Update.
        await audit.AppendAsync(new(AuditAction.Update, "JobSource", id.ToString()), cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToResponseAsync(source, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var lease = runGuard.Acquire(id);
        await using var distributed = await AcquireExecutionAsync(id, cancellationToken);
        var source = await RequiredSourceAsync(id, cancellationToken);
        source.IsActive = false;
        sources.Remove(source); // DbContext converts deletion into BaseEntity soft-delete.
        await audit.AppendAsync(new(AuditAction.Delete, "JobSource", id.ToString()), cancellationToken);
        await SaveAsync(cancellationToken);
    }

    public async Task<JobSourceRunResult> RunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var lease = runGuard.Acquire(id);
        await using var distributed = await AcquireExecutionAsync(id, cancellationToken);
        _ = await RequiredSourceAsync(id, cancellationToken);
        await audit.AppendAsync(new(AuditAction.Submit, "JobSource", id.ToString(),
            new Dictionary<string, string?> { ["result"] = "manual_run_requested" }), cancellationToken);
        // Persist the request before the runner can clear its context after a bad record.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var result = await runner.RunAsync(id, cancellationToken);
        await audit.AppendAsync(new(AuditAction.Update, "JobSource", id.ToString(),
            new Dictionary<string, string?>
            {
                ["result"] = result.Succeeded ? "manual_run_succeeded" : "manual_run_failed",
                ["received"] = result.TotalReceived.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["created"] = result.Created.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["matched"] = result.Matched.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["skipped"] = result.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["failed"] = result.Failed.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<JobSource> RequiredSourceAsync(Guid id, CancellationToken cancellationToken) =>
        await sources.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Job source '{id}' was not found.");

    private async Task<IAsyncDisposable> AcquireExecutionAsync(Guid id, CancellationToken token) =>
        await executionLock.TryAcquireAsync(id, token)
        ?? throw new ConflictException("Job source is already running or being updated.", "job_source_busy");

    private async Task<Company> RequiredCompanyAsync(Guid id, CancellationToken cancellationToken) =>
        await companies.GetByIdAsync(id, cancellationToken)
        ?? throw new BadRequestException("Company must reference an existing company.", "invalid_company");

    private async Task EnsureUniqueAsync(SaveJobSourceRequest request, string? identifier, Guid? excludingId, CancellationToken cancellationToken)
    {
        if (await sources.ConfigurationExistsAsync(request.CompanyId, request.AtsType, identifier, excludingId, cancellationToken))
            throw DuplicateSource();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (UniqueConstraintException)
        {
            unitOfWork.ResetAfterFailure();
            throw DuplicateSource();
        }
    }

    private static ConflictException DuplicateSource() =>
        new("A job source with this company, ATS type and identifier already exists.", "duplicate_job_source");

    private static void Apply(JobSource source, SaveJobSourceRequest request, Company company, string? identifier)
    {
        source.CompanyId = company.Id;
        source.Company = company;
        source.CareerPageUrl = request.CareerPageUrl.Trim();
        source.AtsType = request.AtsType;
        source.AtsIdentifier = identifier;
        source.IsActive = request.IsActive;
        source.ScanIntervalMinutes = request.ScanIntervalMinutes;
    }

    private async Task<JobSourceResponse> ToResponseAsync(JobSource source, CancellationToken cancellationToken)
    {
        var categoryId = await categoryResolver.ResolveCategoryIdAsync(source, cancellationToken);
        var category = categoryId.HasValue ? await categories.GetByIdAsync(categoryId.Value, cancellationToken) : null;
        return new(source.Id, source.CompanyId, source.Company.Name, source.CareerPageUrl,
            source.AtsType, source.AtsIdentifier, source.IsActive, source.ScanIntervalMinutes,
            source.LastRunAtUtc, source.LastSuccessfulRunAtUtc,
            source.LastError is null ? null : "External job source run failed.",
            source.ConsecutiveFailures, category?.Id, category?.Name, source.CreatedAtUtc, source.UpdatedAtUtc);
    }
}
