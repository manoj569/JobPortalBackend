using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Abstractions.Portfolios;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Portfolios;

public sealed partial class CandidatePortfolioService(
    ICandidatePortfolioRepository portfolios,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<CreatePortfolioRequest> createValidator,
    IValidator<UpdatePortfolioSettingsRequest> settingsValidator,
    IValidator<ExperienceRequest> experienceValidator,
    IValidator<EducationRequest> educationValidator,
    IValidator<ProjectRequest> projectValidator,
    IValidator<CertificationRequest> certificationValidator,
    IValidator<ProfessionalLinkRequest> linkValidator,
    IValidator<CustomSectionRequest> customSectionValidator,
    IValidator<CustomItemRequest> customItemValidator,
    TimeProvider timeProvider) : ICandidatePortfolioService
{
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "admin", "dashboard", "login", "register", "jobs", "portfolio",
        "portfolios", "settings", "terms-of-use", "privacy-policy"
    };

    public async Task<PortfolioEditorResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var data = await RequiredDataAsync(userId, false, cancellationToken);
        return MapEditor(data);
    }

    public async Task<PortfolioEditorResponse> CreateAsync(
        Guid userId, CreatePortfolioRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var data = await RequiredDataAsync(userId, true, cancellationToken);
        if (data.Portfolio is not null) return MapEditor(data);
        var slug = string.IsNullOrWhiteSpace(request.RequestedSlug)
            ? await GenerateSlugAsync(data.User, cancellationToken)
            : NormalizeAndValidateSlug(request.RequestedSlug);
        if (!string.IsNullOrWhiteSpace(request.RequestedSlug) &&
            await portfolios.SlugExistsAsync(slug, null, cancellationToken))
            throw new ConflictException("This portfolio URL is already in use.");
        var portfolio = new CandidatePortfolio
        {
            UserId = userId, Slug = slug, NormalizedSlug = slug,
            Template = request.Template, Status = CandidatePortfolioStatus.Draft,
            SectionSettings = DefaultSettings()
        };
        await portfolios.AddPortfolioAsync(portfolio, cancellationToken);
        await AuditAsync(AuditAction.Create, portfolio, userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapEditor(data with { Portfolio = portfolio });
    }

    public async Task<PortfolioEditorResponse> UpdateSettingsAsync(
        Guid userId, UpdatePortfolioSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await settingsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var data = await RequiredPortfolioDataAsync(userId, true, cancellationToken);
        var slug = NormalizeAndValidateSlug(request.Slug);
        if (await portfolios.SlugExistsAsync(slug, data.Portfolio!.Id, cancellationToken))
            throw new ConflictException("This portfolio URL is already in use.");
        data.Portfolio.Slug = slug;
        data.Portfolio.NormalizedSlug = slug;
        data.Portfolio.Template = request.Template;
        foreach (var setting in data.Portfolio.SectionSettings)
        {
            var requested = request.Sections.Single(x => x.SectionType == setting.SectionType);
            setting.IsVisible = requested.IsVisible;
            setting.DisplayOrder = requested.DisplayOrder;
        }
        await AuditAsync(AuditAction.Update, data.Portfolio, userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapEditor(data);
    }

    public async Task<PublicPortfolioResponse> PreviewAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        MapPublic(await RequiredPortfolioDataAsync(userId, false, cancellationToken), false);

    public async Task<PortfolioPublishResponse> PublishAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var data = await RequiredPortfolioDataAsync(userId, true, cancellationToken);
        var missing = MissingRequirements(data);
        if (missing.Count > 0) return new(false, missing, MapEditor(data));
        var portfolio = data.Portfolio!;
        if (await portfolios.SlugExistsAsync(portfolio.NormalizedSlug, portfolio.Id, cancellationToken))
            throw new ConflictException("This portfolio URL is already in use.");
        if (portfolio.Status != CandidatePortfolioStatus.Published)
        {
            portfolio.Status = CandidatePortfolioStatus.Published;
            portfolio.PublishedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await AuditAsync(AuditAction.Publish, portfolio, userId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return new(true, [], MapEditor(data));
    }

    public async Task<PortfolioEditorResponse> UnpublishAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var data = await RequiredPortfolioDataAsync(userId, true, cancellationToken);
        if (data.Portfolio!.Status == CandidatePortfolioStatus.Published)
        {
            data.Portfolio.Status = CandidatePortfolioStatus.Draft;
            data.Portfolio.PublishedAtUtc = null;
            await AuditAsync(AuditAction.Unpublish, data.Portfolio, userId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return MapEditor(data);
    }

    public async Task<PublicPortfolioResponse> GetPublicAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = NormalizeAndValidateSlug(slug); }
        catch (BadRequestException) { throw new NotFoundException("Portfolio was not found."); }
        var data = await portfolios.GetPublishedDataAsync(normalized, cancellationToken)
            ?? throw new NotFoundException("Portfolio was not found.");
        return MapPublic(data, true);
    }

    public async Task<IReadOnlyCollection<ExperienceResponse>> GetExperiencesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await RequiredDataAsync(userId, false, cancellationToken)).Experiences.Select(Map).ToArray();
    public async Task<ExperienceResponse> AddExperienceAsync(Guid userId, ExperienceRequest request, CancellationToken cancellationToken = default)
    {
        await experienceValidator.ValidateAndThrowAsync(request, cancellationToken); await RequiredDataAsync(userId, false, cancellationToken);
        var entity = new CandidateExperience { UserId = userId }; Apply(entity, request);
        return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<ExperienceResponse> UpdateExperienceAsync(Guid userId, Guid id, ExperienceRequest request, CancellationToken cancellationToken = default)
    {
        await experienceValidator.ValidateAndThrowAsync(request, cancellationToken); var data = await RequiredDataAsync(userId, true, cancellationToken);
        var entity = Owned(data.Experiences, id, "Experience"); Apply(entity, request);
        return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public Task DeleteExperienceAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) => DeleteAsync(userId, id, x => x.Experiences, "Experience", cancellationToken);

    public async Task<IReadOnlyCollection<EducationResponse>> GetEducationAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await RequiredDataAsync(userId, false, cancellationToken)).Education.Select(Map).ToArray();
    public async Task<EducationResponse> AddEducationAsync(Guid userId, EducationRequest request, CancellationToken cancellationToken = default)
    {
        await educationValidator.ValidateAndThrowAsync(request, cancellationToken); await RequiredDataAsync(userId, false, cancellationToken);
        var entity = new CandidateEducation { UserId = userId }; Apply(entity, request); return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<EducationResponse> UpdateEducationAsync(Guid userId, Guid id, EducationRequest request, CancellationToken cancellationToken = default)
    {
        await educationValidator.ValidateAndThrowAsync(request, cancellationToken); var entity = Owned((await RequiredDataAsync(userId, true, cancellationToken)).Education, id, "Education"); Apply(entity, request); return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public Task DeleteEducationAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) => DeleteAsync(userId, id, x => x.Education, "Education", cancellationToken);

    public async Task<IReadOnlyCollection<ProjectResponse>> GetProjectsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await RequiredDataAsync(userId, false, cancellationToken)).Projects.Select(Map).ToArray();
    public async Task<ProjectResponse> AddProjectAsync(Guid userId, ProjectRequest request, CancellationToken cancellationToken = default)
    {
        await projectValidator.ValidateAndThrowAsync(request, cancellationToken); await RequiredDataAsync(userId, false, cancellationToken);
        var entity = new CandidateProject { UserId = userId }; Apply(entity, request); return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<ProjectResponse> UpdateProjectAsync(Guid userId, Guid id, ProjectRequest request, CancellationToken cancellationToken = default)
    {
        await projectValidator.ValidateAndThrowAsync(request, cancellationToken); var entity = Owned((await RequiredDataAsync(userId, true, cancellationToken)).Projects, id, "Project"); Apply(entity, request); return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public Task DeleteProjectAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) => DeleteAsync(userId, id, x => x.Projects, "Project", cancellationToken);

    public async Task<IReadOnlyCollection<CertificationResponse>> GetCertificationsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await RequiredDataAsync(userId, false, cancellationToken)).Certifications.Select(Map).ToArray();
    public async Task<CertificationResponse> AddCertificationAsync(Guid userId, CertificationRequest request, CancellationToken cancellationToken = default)
    {
        await certificationValidator.ValidateAndThrowAsync(request, cancellationToken); await RequiredDataAsync(userId, false, cancellationToken);
        var entity = new CandidateCertification { UserId = userId }; Apply(entity, request); return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<CertificationResponse> UpdateCertificationAsync(Guid userId, Guid id, CertificationRequest request, CancellationToken cancellationToken = default)
    {
        await certificationValidator.ValidateAndThrowAsync(request, cancellationToken); var entity = Owned((await RequiredDataAsync(userId, true, cancellationToken)).Certifications, id, "Certification"); Apply(entity, request); return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public Task DeleteCertificationAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) => DeleteAsync(userId, id, x => x.Certifications, "Certification", cancellationToken);

    public async Task<IReadOnlyCollection<ProfessionalLinkResponse>> GetLinksAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await RequiredDataAsync(userId, false, cancellationToken)).ProfessionalLinks.Select(Map).ToArray();
    public async Task<ProfessionalLinkResponse> AddLinkAsync(Guid userId, ProfessionalLinkRequest request, CancellationToken cancellationToken = default)
    {
        await linkValidator.ValidateAndThrowAsync(request, cancellationToken); await RequiredDataAsync(userId, false, cancellationToken);
        var entity = new CandidateProfessionalLink { UserId = userId }; Apply(entity, request); return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<ProfessionalLinkResponse> UpdateLinkAsync(Guid userId, Guid id, ProfessionalLinkRequest request, CancellationToken cancellationToken = default)
    {
        await linkValidator.ValidateAndThrowAsync(request, cancellationToken); var entity = Owned((await RequiredDataAsync(userId, true, cancellationToken)).ProfessionalLinks, id, "Professional link"); Apply(entity, request); return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public Task DeleteLinkAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) => DeleteAsync(userId, id, x => x.ProfessionalLinks, "Professional link", cancellationToken);

    public async Task<IReadOnlyCollection<CustomSectionResponse>> GetCustomSectionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await RequiredDataAsync(userId, false, cancellationToken)).CustomSections.Select(Map).ToArray();
    public async Task<CustomSectionResponse> AddCustomSectionAsync(Guid userId, CustomSectionRequest request, CancellationToken cancellationToken = default)
    {
        await customSectionValidator.ValidateAndThrowAsync(request, cancellationToken); var data = await RequiredDataAsync(userId, false, cancellationToken);
        if (data.CustomSections.Count >= 5) throw new ConflictException("A portfolio can contain at most 5 custom sections.");
        var entity = new PortfolioCustomSection { UserId = userId, Title = request.Title.Trim(), DisplayOrder = request.DisplayOrder };
        return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<CustomSectionResponse> UpdateCustomSectionAsync(Guid userId, Guid id, CustomSectionRequest request, CancellationToken cancellationToken = default)
    {
        await customSectionValidator.ValidateAndThrowAsync(request, cancellationToken); var entity = Owned((await RequiredDataAsync(userId, true, cancellationToken)).CustomSections, id, "Custom section");
        entity.Title = request.Title.Trim(); entity.DisplayOrder = request.DisplayOrder; return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public Task DeleteCustomSectionAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) => DeleteAsync(userId, id, x => x.CustomSections, "Custom section", cancellationToken);

    public async Task<CustomItemResponse> AddCustomItemAsync(Guid userId, Guid sectionId, CustomItemRequest request, CancellationToken cancellationToken = default)
    {
        await customItemValidator.ValidateAndThrowAsync(request, cancellationToken); var section = Owned((await RequiredDataAsync(userId, true, cancellationToken)).CustomSections, sectionId, "Custom section");
        if (section.Items.Count(x => !x.IsDeleted) >= 10) throw new ConflictException("A custom section can contain at most 10 items.");
        var entity = new PortfolioCustomItem { SectionId = section.Id }; Apply(entity, request); return await AddAsync(userId, entity, Map, cancellationToken);
    }
    public async Task<CustomItemResponse> UpdateCustomItemAsync(Guid userId, Guid sectionId, Guid itemId, CustomItemRequest request, CancellationToken cancellationToken = default)
    {
        await customItemValidator.ValidateAndThrowAsync(request, cancellationToken); var section = Owned((await RequiredDataAsync(userId, true, cancellationToken)).CustomSections, sectionId, "Custom section");
        var entity = Owned(section.Items, itemId, "Custom item"); Apply(entity, request); return await UpdateAsync(userId, entity, Map, cancellationToken);
    }
    public async Task DeleteCustomItemAsync(Guid userId, Guid sectionId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var section = Owned((await RequiredDataAsync(userId, true, cancellationToken)).CustomSections, sectionId, "Custom section");
        var entity = Owned(section.Items, itemId, "Custom item"); portfolios.Remove(entity);
        await AuditAsync(AuditAction.Delete, entity, userId, cancellationToken); await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<TResponse> AddAsync<TEntity, TResponse>(Guid userId, TEntity entity,
        Func<TEntity, TResponse> map, CancellationToken ct) where TEntity : BaseEntity
    { await portfolios.AddAsync(entity, ct); await AuditAsync(AuditAction.Create, entity, userId, ct); await unitOfWork.SaveChangesAsync(ct); return map(entity); }
    private async Task<TResponse> UpdateAsync<TEntity, TResponse>(Guid userId, TEntity entity,
        Func<TEntity, TResponse> map, CancellationToken ct) where TEntity : BaseEntity
    { await AuditAsync(AuditAction.Update, entity, userId, ct); await unitOfWork.SaveChangesAsync(ct); return map(entity); }
    private async Task DeleteAsync<TEntity>(Guid userId, Guid id,
        Func<CandidatePortfolioData, IReadOnlyCollection<TEntity>> select, string label, CancellationToken ct) where TEntity : BaseEntity
    { var entity = Owned(select(await RequiredDataAsync(userId, true, ct)), id, label); portfolios.Remove(entity); await AuditAsync(AuditAction.Delete, entity, userId, ct); await unitOfWork.SaveChangesAsync(ct); }
    private static TEntity Owned<TEntity>(IEnumerable<TEntity> entities, Guid id, string label) where TEntity : BaseEntity =>
        entities.SingleOrDefault(x => x.Id == id) ?? throw new NotFoundException($"{label} was not found.");

    private Task AuditAsync(AuditAction action, BaseEntity entity, Guid userId, CancellationToken ct) =>
        auditWriter.AppendAsync(new(action, entity.GetType().Name, entity.Id.ToString(), Actor: new(userId, "Candidate")), ct);

    private async Task<CandidatePortfolioData> RequiredDataAsync(Guid userId, bool tracking, CancellationToken ct) =>
        await portfolios.GetCandidateDataAsync(userId, tracking, ct) ?? throw new UnauthorizedException();
    private async Task<CandidatePortfolioData> RequiredPortfolioDataAsync(Guid userId, bool tracking, CancellationToken ct)
    { var data = await RequiredDataAsync(userId, tracking, ct); return data.Portfolio is null ? throw new NotFoundException("Portfolio was not found.") : data; }

    private async Task<string> GenerateSlugAsync(User user, CancellationToken ct)
    {
        var baseSlug = SlugPart($"{user.FirstName}-{user.LastName}");
        if (baseSlug.Length < 3 || ReservedSlugs.Contains(baseSlug)) baseSlug = $"candidate-{user.Id:N}"[..18];
        baseSlug = baseSlug[..Math.Min(72, baseSlug.Length)].Trim('-');
        if (!await portfolios.SlugExistsAsync(baseSlug, null, ct)) return baseSlug;
        for (var suffix = 2; suffix <= 9999; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (!await portfolios.SlugExistsAsync(candidate, null, ct)) return candidate;
        }
        throw new ConflictException("A unique portfolio URL could not be generated.");
    }

    private static string NormalizeAndValidateSlug(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        if (slug.Length is < 3 or > 80 || !SlugRegex().IsMatch(slug) || ReservedSlugs.Contains(slug))
            throw new BadRequestException("Portfolio slug is invalid or reserved.", "invalid_portfolio_slug");
        return slug;
    }
    private static string SlugPart(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    private static List<PortfolioSectionSetting> DefaultSettings() =>
        Enum.GetValues<PortfolioSectionType>().Select((type, index) => new PortfolioSectionSetting
        { SectionType = type, DisplayOrder = index + 1, IsVisible = type != PortfolioSectionType.Resume }).ToList();

    private static List<string> MissingRequirements(CandidatePortfolioData data)
    {
        var missing = new List<string>(); var user = data.User;
        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName)) missing.Add("name");
        if (string.IsNullOrWhiteSpace(user.Headline)) missing.Add("resumeHeadline");
        if (string.IsNullOrWhiteSpace(user.Bio)) missing.Add("profileSummary");
        if (Skills(data).Length == 0) missing.Add("skills");
        try { NormalizeAndValidateSlug(data.Portfolio!.Slug); } catch (BadRequestException) { missing.Add("slug"); }
        return missing;
    }

    private static PortfolioEditorResponse MapEditor(CandidatePortfolioData data)
    {
        var p = data.Portfolio;
        return new(p is not null, p?.Id, p?.Slug, p?.Status, p?.Template, p?.PublishedAtUtc,
            DisplayName(data.User), data.User.Headline, data.User.Bio, Skills(data),
            !string.IsNullOrWhiteSpace(data.User.ResumeStorageKey),
            p?.SectionSettings.OrderBy(x => x.DisplayOrder).ThenBy(x => x.SectionType).Select(Map).ToArray() ?? [],
            data.Experiences.Select(Map).ToArray(), data.Education.Select(Map).ToArray(),
            data.Projects.Select(Map).ToArray(), data.Certifications.Select(Map).ToArray(),
            data.ProfessionalLinks.Select(Map).ToArray(), data.CustomSections.Select(Map).ToArray());
    }

    private static PublicPortfolioResponse MapPublic(CandidatePortfolioData data, bool requirePublished)
    {
        var p = data.Portfolio ?? throw new NotFoundException("Portfolio was not found.");
        if (requirePublished && p.Status != CandidatePortfolioStatus.Published) throw new NotFoundException("Portfolio was not found.");
        bool Visible(PortfolioSectionType type) => p.SectionSettings.Any(x => x.SectionType == type && x.IsVisible);
        var sectionOrder = p.SectionSettings.Where(x => x.IsVisible)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.SectionType)
            .Select(x => x.SectionType).ToArray();
        return new(p.Slug, p.Template, p.PublishedAtUtc,
            new(DisplayName(data.User), Visible(PortfolioSectionType.About) ? data.User.Headline : null,
                Visible(PortfolioSectionType.About) ? data.User.Bio : null),
            sectionOrder,
            new(Visible(PortfolioSectionType.Skills) ? Skills(data) : null,
                Visible(PortfolioSectionType.Experience) ? data.Experiences.Select(Map).ToArray() : null,
                Visible(PortfolioSectionType.Education) ? data.Education.Select(Map).ToArray() : null,
                Visible(PortfolioSectionType.Projects) ? data.Projects.Select(Map).ToArray() : null,
                Visible(PortfolioSectionType.Certifications) ? data.Certifications.Select(Map).ToArray() : null,
                Visible(PortfolioSectionType.ProfessionalLinks) ? data.ProfessionalLinks.Select(Map).ToArray() : null,
                Visible(PortfolioSectionType.CustomSections) ? data.CustomSections.Select(Map).ToArray() : null,
                Visible(PortfolioSectionType.Resume) ? !string.IsNullOrWhiteSpace(data.User.ResumeStorageKey) : null));
    }

    private static string DisplayName(User user) => $"{user.FirstName.Trim()} {user.LastName.Trim()}".Trim();
    private static string[] Skills(CandidatePortfolioData data) => data.StructuredSkills.Select(x => x.Name)
        .Concat(DeserializeStrings(data.User.SkillsJson)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    private static string[] DeserializeStrings(string json)
    { try { return JsonSerializer.Deserialize<string[]>(json) ?? []; } catch (JsonException) { return []; } }

    private static PortfolioSectionSettingResponse Map(PortfolioSectionSetting x) => new(x.Id, x.SectionType, x.IsVisible, x.DisplayOrder);
    private static ExperienceResponse Map(CandidateExperience x) => new(x.Id, x.JobTitle, x.CompanyName, x.Location, x.EmploymentType, x.StartDate, x.EndDate, x.IsCurrent, x.Description, x.DisplayOrder);
    private static EducationResponse Map(CandidateEducation x) => new(x.Id, x.Qualification, x.Institution, x.FieldOfStudy, x.StartYear, x.EndYear, x.Grade, x.Description, x.DisplayOrder);
    private static ProjectResponse Map(CandidateProject x) => new(x.Id, x.Name, x.Role, x.Description, DeserializeStrings(x.TechnologiesJson), x.SourceUrl, x.LiveUrl, x.StartDate, x.EndDate, x.DisplayOrder);
    private static CertificationResponse Map(CandidateCertification x) => new(x.Id, x.Name, x.Issuer, x.IssuedDate, x.ExpiryDate, x.DoesNotExpire, x.CredentialId, x.CredentialUrl, x.DisplayOrder);
    private static ProfessionalLinkResponse Map(CandidateProfessionalLink x) => new(x.Id, x.Type, x.Label, x.Url, x.DisplayOrder);
    private static CustomSectionResponse Map(PortfolioCustomSection x) => new(x.Id, x.Title, x.DisplayOrder, x.Items.OrderBy(y => y.DisplayOrder).ThenBy(y => y.Id).Select(Map).ToArray());
    private static CustomItemResponse Map(PortfolioCustomItem x) => new(x.Id, x.Title, x.Description, x.Date, x.Url, x.DisplayOrder);

    private static void Apply(CandidateExperience x, ExperienceRequest r) { x.JobTitle = r.JobTitle.Trim(); x.CompanyName = r.CompanyName.Trim(); x.Location = TextNormalizer.TrimOrNull(r.Location); x.EmploymentType = r.EmploymentType; x.StartDate = r.StartDate; x.EndDate = r.EndDate; x.IsCurrent = r.IsCurrent; x.Description = TextNormalizer.TrimOrNull(r.Description); x.DisplayOrder = r.DisplayOrder; }
    private static void Apply(CandidateEducation x, EducationRequest r) { x.Qualification = r.Qualification.Trim(); x.Institution = r.Institution.Trim(); x.FieldOfStudy = TextNormalizer.TrimOrNull(r.FieldOfStudy); x.StartYear = r.StartYear; x.EndYear = r.EndYear; x.Grade = TextNormalizer.TrimOrNull(r.Grade); x.Description = TextNormalizer.TrimOrNull(r.Description); x.DisplayOrder = r.DisplayOrder; }
    private static void Apply(CandidateProject x, ProjectRequest r) { x.Name = r.Name.Trim(); x.Role = TextNormalizer.TrimOrNull(r.Role); x.Description = r.Description.Trim(); x.TechnologiesJson = JsonSerializer.Serialize(r.Technologies.Select(y => y.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)); x.SourceUrl = TextNormalizer.TrimOrNull(r.SourceUrl); x.LiveUrl = TextNormalizer.TrimOrNull(r.LiveUrl); x.StartDate = r.StartDate; x.EndDate = r.EndDate; x.DisplayOrder = r.DisplayOrder; }
    private static void Apply(CandidateCertification x, CertificationRequest r) { x.Name = r.Name.Trim(); x.Issuer = TextNormalizer.TrimOrNull(r.Issuer); x.IssuedDate = r.IssuedDate; x.ExpiryDate = r.ExpiryDate; x.DoesNotExpire = r.DoesNotExpire; x.CredentialId = TextNormalizer.TrimOrNull(r.CredentialId); x.CredentialUrl = TextNormalizer.TrimOrNull(r.CredentialUrl); x.DisplayOrder = r.DisplayOrder; }
    private static void Apply(CandidateProfessionalLink x, ProfessionalLinkRequest r) { x.Type = r.Type; x.Label = TextNormalizer.TrimOrNull(r.Label); x.Url = r.Url.Trim(); x.DisplayOrder = r.DisplayOrder; }
    private static void Apply(PortfolioCustomItem x, CustomItemRequest r) { x.Title = r.Title.Trim(); x.Description = TextNormalizer.TrimOrNull(r.Description); x.Date = r.Date; x.Url = TextNormalizer.TrimOrNull(r.Url); x.DisplayOrder = r.DisplayOrder; }
}
