using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CandidateCompanies;

public sealed partial class CandidateCompanyService(
    ICandidateCompanyRepository companies,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICandidateCompanyService
{
    public async Task<IReadOnlyCollection<CompanyOption>> SearchAsync(Guid candidateId, string query, int limit = 10, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        var display = CompanyNameNormalizer.Display(query ?? string.Empty);
        if (display.Length is < 2 or > 160)
            throw new BadRequestException("Query must contain between 2 and 160 characters.", "invalid_query");
        if (limit is < 1 or > 20)
            throw new BadRequestException("Limit must be between 1 and 20.", "invalid_limit");
        return await companies.SearchAsync(CompanyNameNormalizer.Normalize(display), limit, ct);
    }

    public async Task<CreateCandidateCompanyResponse> CreateAsync(Guid candidateId, CreateCandidateCompanyRequest request, CancellationToken ct = default)
    {
        await RequireCandidateAsync(candidateId, ct);
        var name = ValidateName(request.CompanyName);
        var normalized = CompanyNameNormalizer.Normalize(name);
        var existing = await companies.FindByNormalizedNameAsync(normalized, ct);
        if (existing is not null) return new(existing.Id, existing.Name, false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (await companies.CountCreatedSinceAsync(candidateId, now.AddHours(-24), ct) >= 10)
            throw new AppException("You can add at most ten new companies in 24 hours.", 429, "company_creation_daily_limit");
        var ownerId = await companies.FindActiveAdministratorIdAsync(ct)
            ?? throw new AppException("A company owner is not currently available.", 503, "company_owner_unavailable");
        var company = new Company
        {
            Name = name,
            NormalizedName = normalized,
            Slug = $"{SlugGenerator.Generate(name, 180)}-{Guid.NewGuid():N}"[..Math.Min(220, SlugGenerator.Generate(name, 180).Length + 33)],
            OwnerUserId = ownerId,
            SubmissionSource = CompanySubmissionSource.CandidateSubmitted,
            SubmittedByCandidateId = candidateId,
            IsVerified = false
        };
        await companies.AddAsync(company, ct);
        try
        {
            await companies.SaveAsync(ct);
        }
        catch (UniqueConstraintException)
        {
            companies.DiscardPendingChanges();
            existing = await companies.FindByNormalizedNameAsync(normalized, ct);
            if (existing is null) throw;
            return new(existing.Id, existing.Name, false);
        }
        await audit.AppendAsync(new(AuditAction.Create, "Company", company.Id.ToString(), Actor: new(candidateId, "Candidate")), ct);
        await companies.SaveAsync(ct);
        return new(company.Id, company.Name, true);
    }

    private async Task RequireCandidateAsync(Guid id, CancellationToken ct)
    {
        if (!await companies.IsCandidateAsync(id, ct)) throw new UnauthorizedException();
    }

    private static string ValidateName(string? value)
    {
        var name = CompanyNameNormalizer.Display(value ?? string.Empty);
        if (name.Length is < 2 or > 160) throw Invalid();
        if (name.Any(char.IsControl) || HtmlLike().IsMatch(name) || UrlOnly().IsMatch(name) || !PlainName().IsMatch(name) || !name.Any(char.IsLetterOrDigit))
            throw Invalid();
        return name;
    }

    private static BadRequestException Invalid() => new("CompanyName must be valid plain text between 2 and 160 characters.", "invalid_company_name");

    [GeneratedRegex(@"[<>]|&(?:lt|gt|#\d+);|\b(?:script|iframe|object|embed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlLike();
    [GeneratedRegex(@"^(?:(?:https?|ftp)://|www\.)?\S+\.[a-z]{2,}(?:[/?:#]\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlOnly();
    [GeneratedRegex(@"^[\p{L}\p{M}\p{N}][\p{L}\p{M}\p{N}\p{Zs}.,&'’()\-+/@]*$")]
    private static partial Regex PlainName();
}
