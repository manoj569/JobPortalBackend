using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed partial class FresherAcquisitionStore
{
    public async Task PopulateMissingLogoAsync(Guid companyId, string url, CancellationToken cancellationToken)
    {
        if (url.Length > 2048 || !FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(url)) return;
        // Conditional update protects concurrent administrator logo edits as well.
        await db.Companies.Where(x => x.Id == companyId && (x.LogoUrl == null || x.LogoUrl == ""))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LogoUrl, url), cancellationToken);
    }

    public Task<FresherCompanyResolution> ResolveCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken) => ResolveCompanyCoreAsync(candidate, false, cancellationToken);
    public Task<FresherCompanyResolution> ResolveWebsiteCompanyAsync(FresherImportCandidate candidate, CancellationToken cancellationToken) => ResolveCompanyCoreAsync(candidate, true, cancellationToken);
    private async Task<FresherCompanyResolution> ResolveCompanyCoreAsync(FresherImportCandidate candidate, bool websitePolicy, CancellationToken cancellationToken)
    {
        if (candidate.CompanyId is { } explicitId)
            return await db.Companies.AnyAsync(x => x.Id == explicitId, cancellationToken)
                ? new(explicitId) : new(null, "COMPANY_ID_NOT_FOUND");
        var name = websitePolicy ? CompanyNameNormalizer.Display(candidate.CompanyName ?? string.Empty)
            : FresherCompanyIdentity.ValidName(candidate.CompanyName);
        if (name is null) return new(null, "COMPANY_NAME_INVALID");
        if (websitePolicy && name.Length > 160) name = name[..160];
        var unknown = websitePolicy && CompanyNameNormalizer.Normalize(name) is
            "" or "confidential" or "unknown" or "n/a" or "na" or "undisclosed" or "not disclosed" or "company";
        if (unknown) name = string.Empty;
        // An empty display name represents unknown employer, not a fabricated company.
        // Keep the required FK/administrator owner using a source-scoped technical bucket.
        var normalized = unknown ? "WEBSITE-UNSPECIFIED:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(new Uri(candidate.DiscoveredUrl!).Host.ToUpperInvariant())))
            : CompanyNameNormalizer.Normalize(name);
        var website = websitePolicy && FresherAcquisitionEligibilityPolicy.IsSafePublicUrl(candidate.CompanyWebsiteUrl)
            ? new Uri(candidate.CompanyWebsiteUrl!).GetLeftPart(UriPartial.Authority)
            : FresherCompanyIdentity.EmployerWebsite(candidate.CompanyWebsiteUrl);
        var domain = CompanyDomainNormalizer.Normalize(website);
        if (domain is not null)
        {
            var matches = await db.Companies.Where(x => x.NormalizedWebsiteDomain == domain)
                .Select(x => x.Id).Take(2).ToArrayAsync(cancellationToken);
            if (matches.Length > 1) return new(null, "COMPANY_DOMAIN_AMBIGUOUS");
            if (matches.Length == 1) return new(matches[0]);
        }
        // A shared unknown-employer bucket must not acquire one vacancy's
        // employer domain; retain that claim on the job's source metadata.
        if (unknown) { website = null; domain = null; }
        var existing = await db.Companies.Where(x => x.NormalizedName == normalized)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing.HasValue) return new(existing);
        // Website source claims remain unverified company data. Other acquisition
        // providers retain their existing producer-attestation requirement.
        if (!websitePolicy && !candidate.CompanyWebsiteVerified) { website = null; domain = null; }

        // Match existing candidate-company onboarding: an active administrator is
        // the required owner; never invent a user or weaken the non-null owner FK.
        var owner = await db.Users.Where(x => x.RoleId == SystemRoleIds.Administrator && x.Status == UserStatus.Active)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (owner is null) return new(null, "COMPANY_OWNER_UNAVAILABLE");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Acquisition company creation requires the job/receipt chunk transaction.");
        var id = Guid.NewGuid();
        var slug = SlugGenerator.Generate(name, 180) + "-" + id.ToString("N");
        var now = DateTime.UtcNow;
        // Insert only the company, without flushing pending jobs/skills/receipts.
        // The chunk pre-acquires sorted domain/name locks. The existing unique
        // indexes also arbitrate races with writers outside acquisition. DO NOTHING
        // keeps PostgreSQL's transaction usable so we can read the winning row.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Companies" ("Id", "Name", "NormalizedName", "Slug", "WebsiteUrl", "NormalizedWebsiteDomain",
                "IsVerified", "OwnerUserId", "SubmissionSource", "CreatedAtUtc", "IsDeleted")
            VALUES ({id}, {name}, {normalized}, {slug}, {website}, {domain}, FALSE, {owner.Value},
                {(int)CompanySubmissionSource.Administrative}, {now}, FALSE)
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        existing = await db.Companies.Where(x => x.NormalizedName == normalized)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        return existing.HasValue ? new(existing) : new(null, "COMPANY_CREATION_CONFLICT");
    }
}
