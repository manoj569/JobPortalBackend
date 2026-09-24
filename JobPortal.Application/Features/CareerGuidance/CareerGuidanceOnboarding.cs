using System.Net.Mail;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed partial class CareerGuidanceService
{
    public async Task<OnboardingResponse> StartOnboardingAsync(Guid actor, CancellationToken ct)
    {
        await RequireActor(actor, false, ct);
        var p = await profiles.FindByUserAsync(actor, ct);
        if (p is { IsDeleted: true }) throw new ConflictException("A consultant profile already exists.");
        if (p is null)
        {
            p = new() { UserId = actor, VerificationStatus = ConsultantVerificationStatus.Draft };
            await profiles.AddAsync(p, ct);
            try { await Save(p, AuditAction.Create, "onboarding_started", ct); }
            catch (ConflictException)
            {
                // Concurrent create is idempotent; the unique owner key chooses the winner.
                unitOfWork.ResetAfterFailure();
                p = await profiles.FindByUserAsync(actor, ct);
                if (p is null || p.IsDeleted) throw;
            }
        }
        return await OnboardingDto(p, actor, ct);
    }

    public async Task<OnboardingResponse> OnboardingAsync(Guid actor, CancellationToken ct) =>
        await OnboardingDto(await Own(actor, ct), actor, ct);

    public async Task<OnboardingResponse> SaveBasicAsync(Guid actor, OnboardingBasicRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        var name = Patch(request.DisplayName, p.DisplayName, 120) ?? "";
        var headline = Patch(request.ProfessionalHeadline, p.ProfessionalHeadline, 200) ?? "";
        var bio = Patch(request.Bio, p.Bio, 4000) ?? "";
        var photo = Patch(request.ProfileImageUrl, p.ProfileImageUrl, 2048);
        var location = Patch(request.Location, p.Location, 200);
        var email = Patch(request.ProfessionalEmail, p.ProfessionalEmail, 254);
        if (request.ProfileImageUrl.IsSpecified) ValidatePhoto(photo);
        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            throw new BadRequestException("Invalid professional email.");
        if (name != p.DisplayName || headline != p.ProfessionalHeadline || bio != p.Bio || photo != p.ProfileImageUrl || location != p.Location)
            await MaterialEdit(p, ct);
        p.DisplayName = name; p.ProfessionalHeadline = headline; p.Bio = bio;
        p.ProfileImageUrl = photo; p.Location = location; p.ProfessionalEmail = email;
        return await Finish(p, actor, "onboarding_basic_saved", ct);
    }

    public async Task<OnboardingResponse> SaveProfessionalAsync(Guid actor, OnboardingProfessionalRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        var companyId = request.CompanyId.IsSpecified ? request.CompanyId.Value : p.CompanyId;
        if (companyId == Guid.Empty) throw new BadRequestException("Invalid company.");
        var companyName = Patch(request.CompanyName, p.CompanyName, 200) ?? "";
        if (companyId.HasValue)
        {
            var company = await companies.GetByIdAsync(companyId.Value, ct) ?? throw new BadRequestException("Company must exist.");
            companyName = company.Name;
        }
        var role = Patch(request.CurrentRole, p.CurrentRole, 160) ?? "";
        var years = request.YearsOfExperience.IsSpecified ? request.YearsOfExperience.Value : p.YearsOfExperience;
        var type = request.ProfessionalType.IsSpecified ? request.ProfessionalType.Value : p.ProfessionalType;
        if (years.HasValue && (years < 0 || years > 70 || decimal.Round(years.Value, 1) != years))
            throw new BadRequestException("Experience must be 0–70 years, with at most one decimal place.");
        if (type.HasValue && !Enum.IsDefined(type.Value)) throw new BadRequestException("Invalid professional type.");
        var linkedIn = Patch(request.LinkedInUrl, p.LinkedInUrl, 500) ?? "";
        if (linkedIn.Length > 0 && !ValidLinkedIn(linkedIn)) throw new BadRequestException("Use an HTTPS LinkedIn /in/ profile without credentials, query or fragment.");
        var industry = Patch(request.Industry, p.Industry, 120);
        var area = Patch(request.FunctionalArea, p.FunctionalArea, 120);
        if (companyId != p.CompanyId || companyName != p.CompanyName || role != p.CurrentRole || years != p.YearsOfExperience ||
            type != p.ProfessionalType || linkedIn != p.LinkedInUrl || industry != p.Industry || area != p.FunctionalArea)
            await MaterialEdit(p, ct);
        p.CompanyId = companyId; p.CompanyName = companyName; p.CurrentRole = role; p.YearsOfExperience = years;
        p.ProfessionalType = type; p.LinkedInUrl = linkedIn; p.Industry = industry; p.FunctionalArea = area;
        return await Finish(p, actor, "onboarding_professional_saved", ct);
    }

    public async Task<OnboardingResponse> SaveExpertiseAsync(Guid actor, OnboardingExpertiseRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        var languages = Tags(request.Languages, p, ConsultantTagKind.Language, 10);
        var expertise = Tags(request.Expertise, p, ConsultantTagKind.Expertise, 20);
        if (!languages.SequenceEqual(TagValues(p, ConsultantTagKind.Language)) || !expertise.SequenceEqual(TagValues(p, ConsultantTagKind.Expertise)))
            await MaterialEdit(p, ct);
        SetTags(p, ConsultantTagKind.Language, languages); SetTags(p, ConsultantTagKind.Expertise, expertise);
        return await Finish(p, actor, "onboarding_expertise_saved", ct);
    }

    public async Task<OnboardingResponse> SaveEducationAsync(Guid actor, OnboardingEducationRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        var items = ValidateEducation(request.Items);
        if (!Education(p).SequenceEqual(items)) { await MaterialEdit(p, ct); ReplaceEducation(p, items); }
        return await Finish(p, actor, "onboarding_education_saved", ct);
    }

    public async Task<OnboardingResponse> SaveExperienceAsync(Guid actor, OnboardingExperienceRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        var items = ValidateExperience(request.Items);
        if (!Experience(p).SequenceEqual(items)) { await MaterialEdit(p, ct); ReplaceExperience(p, items); }
        return await Finish(p, actor, "onboarding_experience_saved", ct);
    }

    public async Task<OnboardingImportOptions> ImportOptionsAsync(Guid actor, CancellationToken ct)
    { await Own(actor, ct); return await profiles.ImportOptionsAsync(actor, ct); }

    public async Task<OnboardingResponse> ImportAsync(Guid actor, OnboardingImportRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        if (request.EducationIds?.Length > 50 || request.ExperienceIds?.Length > 50) throw new BadRequestException("Import at most 50 entries per collection.");
        var source = await profiles.ImportOptionsAsync(actor, ct);
        var education = request.EducationIds is null ? null : ValidateEducation(request.EducationIds.Distinct().Select(id =>
            source.Education.SingleOrDefault(x => x.Id == id)?.Education ?? throw new NotFoundException("Education source not found.")).ToArray());
        var experience = request.ExperienceIds is null ? null : ValidateExperience(request.ExperienceIds.Distinct().Select(id =>
            source.WorkExperience.SingleOrDefault(x => x.Id == id)?.Experience ?? throw new NotFoundException("Experience source not found.")).ToArray());
        var photo = request.ImportPhoto ? Text(source.ProfileImageUrl, 2048) : p.ProfileImageUrl;
        var location = request.ImportLocation ? Text(source.Location, 200) : p.Location;
        if (request.ImportPhoto) ValidatePhoto(photo);
        if (photo != p.ProfileImageUrl || location != p.Location || education is not null && !Education(p).SequenceEqual(education) ||
            experience is not null && !Experience(p).SequenceEqual(experience)) await MaterialEdit(p, ct);
        p.ProfileImageUrl = photo; p.Location = location;
        if (education is not null) ReplaceEducation(p, education);
        if (experience is not null) ReplaceExperience(p, experience);
        return await Finish(p, actor, "onboarding_imported", ct);
    }

    public async Task<OnboardingResponse> SubmitOnboardingAsync(Guid actor, OnboardingSubmitRequest request, CancellationToken ct)
    {
        var p = await Own(actor, ct); CheckMutable(p, request.Revision);
        if (p.VerificationStatus != ConsultantVerificationStatus.Draft) throw new ConflictException("Only a draft can be submitted.");
        if (request.PolicyVersion != PolicyVersion || !request.AcceptIndependentGuidancePolicy || !request.AcceptPublicProfileConsent)
            throw new BadRequestException("Accept the current policy and explicitly consent to the public profile.");
        var missing = Missing(p, await profiles.OnboardingWindowsAsync(p.Id, ct), acceptingConsent: true);
        if (missing.Count > 0) throw new BadRequestException("Complete these requirements: " + string.Join(", ", missing));
        var now = clock.GetUtcNow().UtcDateTime;
        p.TermsAcceptedAtUtc = now; p.PolicyVersion = PolicyVersion; p.PublicProfileConsentAtUtc = now;
        p.SubmittedAtUtc ??= now; // First actual submission; resubmission does not create another application.
        p.VerificationStatus = ConsultantVerificationStatus.Pending; p.IsAcceptingBookings = false;
        return await Finish(p, actor, "onboarding_submitted", ct);
    }

    private async Task GuardMaterialEdit(CareerConsultant p, CancellationToken ct)
    {
        if (p.VerificationStatus == ConsultantVerificationStatus.Verified &&
            await profiles.HasProtectedBookingsAsync(p.Id, clock.GetUtcNow().UtcDateTime, ct))
            throw new ConflictException("Material profile changes are blocked while paid confirmed sessions remain.");
    }

    private async Task MaterialEdit(CareerConsultant p, CancellationToken ct)
    {
        await GuardMaterialEdit(p, ct);
        p.VerificationStatus = ConsultantVerificationStatus.Draft; p.IsAcceptingBookings = false;
        p.VerifiedAtUtc = null; p.VerificationMethod = null; p.ReviewedAtUtc = null; p.ReviewedByUserId = null;
        // Retain applicant-facing rejection feedback until the next review.
        p.PublicProfileConsentAtUtc = null;
    }

    private async Task<OnboardingResponse> Finish(CareerConsultant p, Guid actor, string result, CancellationToken ct)
    { await Save(p, AuditAction.Update, result, ct); return await OnboardingDto(p, actor, ct); }

    private async Task<OnboardingResponse> OnboardingDto(CareerConsultant p, Guid actor, CancellationToken ct)
    {
        var user = await users.GetByIdWithRoleAsync(actor, ct) ?? throw new UnauthorizedException();
        var windows = await profiles.OnboardingWindowsAsync(p.Id, ct);
        return new(p.Id, p.Revision, p.VerificationStatus, p.DisplayName, p.ProfessionalHeadline, p.Bio, p.ProfileImageUrl,
            p.Location, p.ProfessionalEmail, user.Email, p.CompanyId, p.CompanyName, p.CurrentRole, p.YearsOfExperience,
            p.ProfessionalType, p.LinkedInUrl, p.Industry, p.FunctionalArea, TagValues(p, ConsultantTagKind.Language),
            TagValues(p, ConsultantTagKind.Expertise), Education(p), Experience(p),
            p.Services.Where(s => !s.IsDeleted).OrderBy(s => s.Id).Select(Service).ToArray(),
            new(p.TimeZoneId, p.VerificationStatus == ConsultantVerificationStatus.Verified && p.IsAcceptingBookings,
                windows.Select(w => new WeeklyWindow(w.DayOfWeek, w.StartTime, w.EndTime, w.IsActive)).ToArray(), p.Revision),
            p.SubmittedAtUtc, p.PublicProfileConsentAtUtc, p.TermsAcceptedAtUtc, p.PolicyVersion,
            p.VerificationStatus is ConsultantVerificationStatus.Rejected or ConsultantVerificationStatus.Draft ? p.VerificationReason : null,
            Missing(p, windows));
    }

    private static List<string> Missing(CareerConsultant p, IReadOnlyList<CareerConsultantAvailability> windows, bool acceptingConsent = false)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(p.DisplayName)) missing.Add("DisplayName");
        if (string.IsNullOrWhiteSpace(p.ProfessionalHeadline)) missing.Add("ProfessionalHeadline");
        if (string.IsNullOrWhiteSpace(p.Bio)) missing.Add("Bio");
        if (string.IsNullOrWhiteSpace(p.CurrentRole)) missing.Add("CurrentRole");
        if (!p.ProfessionalType.HasValue) missing.Add("ProfessionalType");
        if (p.ProfessionalType != CareerProfessionalType.CareerCoach && string.IsNullOrWhiteSpace(p.CompanyName)) missing.Add("CompanyName");
        if (!p.YearsOfExperience.HasValue) missing.Add("YearsOfExperience");
        if (!ValidLinkedIn(p.LinkedInUrl)) missing.Add("LinkedInUrl");
        if (TagValues(p, ConsultantTagKind.Language).Length == 0) missing.Add("Languages");
        if (TagValues(p, ConsultantTagKind.Expertise).Length == 0) missing.Add("Expertise");
        var durations = p.Services.Where(s => !s.IsDeleted && s.IsActive && s.Currency == "INR" && s.DurationMinutes is 15 or 30 or 60)
            .Select(s => s.DurationMinutes).ToArray();
        if (durations.Length == 0) missing.Add("LaunchService");
        try { _ = CareerSlotGenerator.TimeZone(p.TimeZoneId); }
        catch (BadRequestException) { missing.Add("TimeZoneId"); }
        if (!windows.Any(w => !w.IsDeleted && w.IsActive && durations.Any(d => (w.EndTime - w.StartTime).TotalMinutes >= d)))
            missing.Add("UsableAvailability");
        if (!acceptingConsent)
        {
            if (!p.TermsAcceptedAtUtc.HasValue || p.PolicyVersion != PolicyVersion) missing.Add("CurrentPolicyAcceptance");
            if (!p.PublicProfileConsentAtUtc.HasValue) missing.Add("PublicProfileConsent");
        }
        return missing;
    }

    private static string? Patch(OnboardingField<string?> field, string? previous, int max) => field.IsSpecified ? Text(field.Value, max) : previous;
    private static string? Text(string? value, int max)
    {
        if (value?.Length > max) throw new BadRequestException($"Value exceeds maximum length {max}.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    private static string Required(string? value, int max) => Text(value, max) ?? throw new BadRequestException("History entries require non-empty names.");
    private static bool ValidLinkedIn(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host is "linkedin.com" or "www.linkedin.com" && uri.AbsolutePath.StartsWith("/in/", StringComparison.Ordinal) &&
        uri.AbsolutePath.Length > 4 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    private static void ValidatePhoto(string? value)
    {
        if (value is not null && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0))
            throw new BadRequestException("Use an HTTPS photo URL without credentials.");
    }
    private static string[] TagValues(CareerConsultant p, ConsultantTagKind kind) =>
        p.Tags.Where(t => !t.IsDeleted && t.Kind == kind).Select(t => t.Value).Order(StringComparer.Ordinal).ToArray();
    private static string[] Tags(OnboardingField<string[]?> field, CareerConsultant p, ConsultantTagKind kind, int max)
    {
        if (!field.IsSpecified) return TagValues(p, kind);
        var values = field.Value ?? [];
        if (values.Length > max) throw new BadRequestException($"At most {max} tags are allowed.");
        return values.Select(v => Required(v, 60).ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private static ConsultantEducationItem[] Education(CareerConsultant p) => p.Education.Where(e => !e.IsDeleted)
        .OrderBy(e => e.DisplayOrder).ThenBy(e => e.Qualification, StringComparer.Ordinal).ThenBy(e => e.Institution, StringComparer.Ordinal)
        .Select(e => new ConsultantEducationItem(e.Qualification, e.Institution, e.FieldOfStudy, e.StartYear, e.EndYear, e.IsCurrentlyStudying, e.DisplayOrder)).ToArray();
    private static ConsultantExperienceItem[] Experience(CareerConsultant p) => p.WorkExperience.Where(e => !e.IsDeleted)
        .OrderBy(e => e.DisplayOrder).ThenBy(e => e.JobTitle, StringComparer.Ordinal).ThenBy(e => e.CompanyName, StringComparer.Ordinal)
        .Select(e => new ConsultantExperienceItem(e.JobTitle, e.CompanyName, e.StartDate, e.EndDate, e.IsCurrent, e.Description, e.DisplayOrder)).ToArray();
    private static ConsultantEducationItem[] ValidateEducation(ConsultantEducationItem[]? items)
    {
        if (items is null || items.Length > 50 || items.Any(e => e is null)) throw new BadRequestException("Supply up to 50 education entries.");
        return items.Select(e =>
        {
            if (e.StartYear is < 1900 or > 2100 || e.EndYear is < 1900 or > 2100 || e.EndYear < e.StartYear ||
                e.IsCurrentlyStudying && e.EndYear.HasValue || e.DisplayOrder < 0) throw new BadRequestException("Invalid education dates/order.");
            return e with { Qualification = Required(e.Qualification, 200), Institution = Required(e.Institution, 200), FieldOfStudy = Text(e.FieldOfStudy, 200) };
        }).OrderBy(e => e.DisplayOrder).ThenBy(e => e.Qualification, StringComparer.Ordinal).ThenBy(e => e.Institution, StringComparer.Ordinal).ToArray();
    }
    private static ConsultantExperienceItem[] ValidateExperience(ConsultantExperienceItem[]? items)
    {
        if (items is null || items.Length > 50 || items.Any(e => e is null)) throw new BadRequestException("Supply up to 50 experience entries.");
        return items.Select(e =>
        {
            if (e.StartDate.Year is < 1900 or > 2100 || e.EndDate?.Year is < 1900 or > 2100 || e.EndDate < e.StartDate ||
                e.IsCurrent && e.EndDate.HasValue || e.DisplayOrder < 0) throw new BadRequestException("Invalid experience dates/order.");
            return e with { JobTitle = Required(e.JobTitle, 160), CompanyName = Required(e.CompanyName, 200), Description = Text(e.Description, 4000) };
        }).OrderBy(e => e.DisplayOrder).ThenBy(e => e.JobTitle, StringComparer.Ordinal).ThenBy(e => e.CompanyName, StringComparer.Ordinal).ToArray();
    }
    private void ReplaceEducation(CareerConsultant p, ConsultantEducationItem[] items)
    {
        foreach (var e in p.Education.Where(e => !e.IsDeleted)) { e.IsDeleted = true; e.DeletedAtUtc = clock.GetUtcNow().UtcDateTime; }
        foreach (var e in items) p.Education.Add(new() { ConsultantId = p.Id, Qualification = e.Qualification, Institution = e.Institution,
            FieldOfStudy = e.FieldOfStudy, StartYear = e.StartYear, EndYear = e.EndYear, IsCurrentlyStudying = e.IsCurrentlyStudying, DisplayOrder = e.DisplayOrder });
    }
    private void ReplaceExperience(CareerConsultant p, ConsultantExperienceItem[] items)
    {
        foreach (var e in p.WorkExperience.Where(e => !e.IsDeleted)) { e.IsDeleted = true; e.DeletedAtUtc = clock.GetUtcNow().UtcDateTime; }
        foreach (var e in items) p.WorkExperience.Add(new() { ConsultantId = p.Id, JobTitle = e.JobTitle, CompanyName = e.CompanyName,
            StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent, Description = e.Description, DisplayOrder = e.DisplayOrder });
    }
}
