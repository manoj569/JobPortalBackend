namespace JobPortal.Application.Abstractions.AdminManagement;

public interface ICompanyLogoResolver
{
    Task<CompanyLogoResolutionResult> ResolveAsync(string companyName, string? companyWebsiteUrl,
        string? careerPageUrl, CancellationToken cancellationToken = default);
}

public sealed record CompanyLogoResolutionResult(string? LogoUrl, string Source)
{
    public bool Resolved => LogoUrl is not null;
}

public sealed record ResolveCompanyLogoRequest(string? WebsiteUrl = null, string? CareerPageUrl = null,
    bool ReplaceExisting = false);

public sealed record CompanyLogoPreviewResponse(Guid CompanyId, string CompanyName, string? WebsiteUrl,
    string? LogoUrl, string Source, bool Resolved);
