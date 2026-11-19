using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.EmployerCompanies;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.EmployerCompanies;

public sealed class EmployerCompanyService(
    IEmployerCompanyRepository companies,
    IAuditWriter audit,
    IValidator<EmployerCompanySetupRequest> setupValidator,
    IValidator<EmployerCompanyUpdateRequest> updateValidator)
    : IEmployerCompanyService
{
    public async Task<EmployerCompanyResponse?> GetMineAsync(
        Guid employerUserId,
        CancellationToken cancellationToken = default)
    {
        await RequireEmployerAsync(
            employerUserId,
            cancellationToken);

        var company =
            await companies.GetByOwnerUserIdAsync(
                employerUserId,
                cancellationToken);

        return company is null
            ? null
            : Map(company);
    }

    public async Task<EmployerCompanyResponse> SetupAsync(
        Guid employerUserId,
        EmployerCompanySetupRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireEmployerAsync(
            employerUserId,
            cancellationToken);

        await setupValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var ownedCompany =
            await companies.GetByOwnerUserIdAsync(
                employerUserId,
                cancellationToken);

        if (ownedCompany is not null)
        {
            throw new ConflictException(
                "Your employer account already has a company.",
                "employer_company_exists");
        }

        var name =
            CompanyNameNormalizer.Display(
                request.Name);

        var normalizedName =
            CompanyNameNormalizer.Normalize(name);

        var existingByName =
            await companies.FindByNormalizedNameAsync(
                normalizedName,
                cancellationToken);

        if (existingByName is not null)
        {
            throw new ConflictException(
                "A company with this name already exists.",
                "company_name_exists");
        }

        var normalizedWebsiteDomain =
            NormalizeWebsiteDomain(request.WebsiteUrl);

        if (normalizedWebsiteDomain is not null)
        {
            var existingByDomain =
                await companies.FindByWebsiteDomainAsync(
                    normalizedWebsiteDomain,
                    cancellationToken);

            if (existingByDomain is not null)
            {
                throw new ConflictException(
                    "A company with this website already exists.",
                    "company_website_exists");
            }
        }

        var slug =
            await GenerateUniqueSlugAsync(
                name,
                cancellationToken);

        var company = new Company
        {
            Id = Guid.NewGuid(),

            Name = name,
            NormalizedName = normalizedName,

            Slug = slug,

            Description =
                Clean(request.Description),

            WebsiteUrl =
                Clean(request.WebsiteUrl),

            NormalizedWebsiteDomain =
                normalizedWebsiteDomain,

            Industry =
                Clean(request.Industry),

            Location =
                Clean(request.Location),

            EmployeeCount =
                request.EmployeeCount,

            CompanyType =
                request.CompanyType,

            /*
             * Employers cannot self-verify.
             */
            IsVerified = false,

            /*
             * Never accept this value from the browser.
             */
            OwnerUserId = employerUserId,

            SubmissionSource =
                CompanySubmissionSource.EmployerSubmitted,

            SubmittedByCandidateId = null
        };

        await companies.AddAsync(
            company,
            cancellationToken);

        await audit.AppendAsync(
            new(
                AuditAction.Create,
                "Company",
                company.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["source"] =
                        "employerCompanySetup"
                },
                new(
                    employerUserId,
                    "Employer")),
            cancellationToken);

        await companies.SaveAsync(
            cancellationToken);

        return Map(company);
    }

    public async Task<EmployerCompanyResponse> UpdateAsync(
        Guid employerUserId,
        EmployerCompanyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireEmployerAsync(
            employerUserId,
            cancellationToken);

        await updateValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var company =
            await companies.GetByOwnerUserIdAsync(
                employerUserId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Employer company was not found.");

        var name =
            CompanyNameNormalizer.Display(
                request.Name);

        var normalizedName =
            CompanyNameNormalizer.Normalize(name);

        var existingByName =
            await companies.FindByNormalizedNameAsync(
                normalizedName,
                cancellationToken);

        if (existingByName is not null &&
            existingByName.Id != company.Id)
        {
            throw new ConflictException(
                "A company with this name already exists.",
                "company_name_exists");
        }

        var normalizedWebsiteDomain =
            NormalizeWebsiteDomain(
                request.WebsiteUrl);

        if (normalizedWebsiteDomain is not null)
        {
            var existingByDomain =
                await companies.FindByWebsiteDomainAsync(
                    normalizedWebsiteDomain,
                    cancellationToken);

            if (existingByDomain is not null &&
                existingByDomain.Id != company.Id)
            {
                throw new ConflictException(
                    "A company with this website already exists.",
                    "company_website_exists");
            }
        }

        company.Name = name;
        company.NormalizedName = normalizedName;

        company.Description =
            Clean(request.Description);

        company.WebsiteUrl =
            Clean(request.WebsiteUrl);

        company.NormalizedWebsiteDomain =
            normalizedWebsiteDomain;

        company.Industry =
            Clean(request.Industry);

        company.Location =
            Clean(request.Location);

        company.EmployeeCount =
            request.EmployeeCount;

        company.CompanyType =
            request.CompanyType;

        /*
         * Employer cannot modify:
         *
         * OwnerUserId
         * IsVerified
         * SubmissionSource
         * SubmittedByCandidateId
         */

        await audit.AppendAsync(
            new(
                AuditAction.Update,
                "Company",
                company.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["source"] =
                        "employerCompanyProfile"
                },
                new(
                    employerUserId,
                    "Employer")),
            cancellationToken);

        await companies.SaveAsync(
            cancellationToken);

        return Map(company);
    }

    private async Task RequireEmployerAsync(
        Guid employerUserId,
        CancellationToken cancellationToken)
    {
        if (!await companies.IsEmployerAsync(
                employerUserId,
                cancellationToken))
        {
            throw new UnauthorizedException();
        }
    }

    private async Task<string> GenerateUniqueSlugAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var baseSlug =
            SlugGenerator.Generate(name, 180);

        if (!await companies.SlugExistsAsync(
                baseSlug,
                null,
                cancellationToken))
        {
            return baseSlug;
        }

        for (var attempt = 0;
             attempt < 5;
             attempt++)
        {
            var suffix =
                Guid.NewGuid()
                    .ToString("N")[..8];

            var slug =
                $"{baseSlug}-{suffix}";

            if (!await companies.SlugExistsAsync(
                    slug,
                    null,
                    cancellationToken))
            {
                return slug;
            }
        }

        throw new ConflictException(
            "Unable to generate a unique company URL.",
            "company_slug_conflict");
    }

    private static string? NormalizeWebsiteDomain(
        string? websiteUrl)
    {
        var value = Clean(websiteUrl);

        if (value is null)
            return null;

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            return null;
        }

        var host =
            uri.Host
                .Trim()
                .TrimEnd('.')
                .ToLowerInvariant();

        if (host.StartsWith(
                "www.",
                StringComparison.Ordinal))
        {
            host = host[4..];
        }

        return host.Length == 0
            ? null
            : host;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static EmployerCompanyResponse Map(
        Company company) =>
        new(
            company.Id,
            company.Name,
            company.Slug,
            company.Description,
            company.WebsiteUrl,
            company.LogoUrl,
            company.Industry,
            company.Location,
            company.EmployeeCount,
            company.CompanyType,
            company.IsVerified,
            company.OwnerUserId,
            company.CreatedAtUtc,
            company.UpdatedAtUtc);
}
