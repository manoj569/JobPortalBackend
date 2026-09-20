using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.CareerGuidance;

public sealed class CareerGuidanceService(ICareerGuidanceRepository profiles, IUserRepository users,
    ICompanyManagementRepository companies, IUnitOfWork unitOfWork, IAuditWriter audit, TimeProvider clock,
    IValidator<ConsultantProfileRequest> profileValidator, IValidator<ConsultantServiceRequest> serviceValidator,
    IValidator<ConsultantSearchQuery> searchValidator, IValidator<ConsultantReviewRequest> reviewValidator,
    IValidator<ConsultantAdminQuery> adminValidator) : ICareerGuidanceService
{
    public const string PolicyVersion = "career-guidance-v1";
    public const string Disclaimer = "Independent career guidance, not employer representation. No guaranteed job, interview or referral. Do not disclose employer-confidential information; comply with your employer's outside-work policies.";

    public async Task<PagedResponse<ConsultantPublicResponse>> SearchAsync(ConsultantSearchQuery query, CancellationToken ct)
    {
        await searchValidator.ValidateAndThrowAsync(query, ct);
        var (items, total) = await profiles.SearchAsync(query, ct);
        return new(items.Select(Public).ToArray(), query.PageNumber, query.PageSize, total);
    }

    public async Task<ConsultantPublicResponse> GetAsync(Guid id, CancellationToken ct) =>
        Public(await profiles.FindAsync(id, true, ct) ?? throw new NotFoundException("Consultant not found."));

    public async Task<ConsultantPrivateResponse> MineAsync(Guid actor, CancellationToken ct) => Private(await Own(actor, ct));

    public async Task<ConsultantPrivateResponse> ApplyAsync(Guid actor, ConsultantProfileRequest request, CancellationToken ct)
    {
        await RequireActor(actor, false, ct);
        await profileValidator.ValidateAndThrowAsync(request, ct);
        if (await profiles.FindByUserAsync(actor, ct) is not null) throw new ConflictException("A consultant profile already exists.");
        var profile = new CareerConsultant { UserId = actor };
        await ApplyProfile(profile, request, ct);
        await profiles.AddAsync(profile, ct);
        await Save(profile, AuditAction.Submit, "application_submitted", ct);
        return Private(profile);
    }

    public async Task<ConsultantPrivateResponse> UpdateAsync(Guid actor, ConsultantProfileRequest request, CancellationToken ct)
    {
        var profile = await Own(actor, ct);
        await profileValidator.ValidateAndThrowAsync(request, ct);
        CheckMutable(profile, request.Revision);
        await ApplyProfile(profile, request, ct);
        // Approval of an older claim must not carry across identity/employment/profile changes.
        profile.VerificationStatus = ConsultantVerificationStatus.Pending;
        profile.VerificationMethod = null;
        profile.VerifiedAtUtc = null;
        profile.VerificationReason = null;
        profile.ReviewedAtUtc = null;
        profile.ReviewedByUserId = null;
        await Save(profile, AuditAction.Update, "profile_resubmitted", ct);
        return Private(profile);
    }

    public async Task<ConsultantPrivateResponse> SaveServiceAsync(Guid actor, Guid? id, ConsultantServiceRequest request, CancellationToken ct)
    {
        var profile = await Own(actor, ct);
        await serviceValidator.ValidateAndThrowAsync(request, ct);
        CheckMutable(profile, request.Revision);
        var service = id.HasValue ? profile.Services.SingleOrDefault(x => x.Id == id && !x.IsDeleted)
            ?? throw new NotFoundException("Service not found.") : new CareerConsultantService { ConsultantId = profile.Id };
        if (!id.HasValue)
        {
            if (profile.Services.Count(x => !x.IsDeleted) >= 20) throw new ConflictException("At most 20 services are allowed.");
            profile.Services.Add(service);
        }
        service.ServiceType = request.ServiceType.Trim().ToUpperInvariant();
        service.Title = request.Title.Trim();
        service.Description = request.Description.Trim();
        service.DurationMinutes = request.DurationMinutes;
        service.Price = request.Price;
        service.Currency = request.Currency;
        service.IsActive = request.IsActive;
        await Save(profile, AuditAction.Update, "service_saved", ct);
        return Private(profile);
    }

    public async Task<ConsultantPrivateResponse> DeleteServiceAsync(Guid actor, Guid id, Guid revision, CancellationToken ct)
    {
        var profile = await Own(actor, ct);
        CheckMutable(profile, revision);
        var service = profile.Services.SingleOrDefault(x => x.Id == id && !x.IsDeleted) ?? throw new NotFoundException("Service not found.");
        service.IsActive = false;
        service.IsDeleted = true;
        service.DeletedAtUtc = clock.GetUtcNow().UtcDateTime;
        await Save(profile, AuditAction.Delete, "service_deleted", ct);
        return Private(profile);
    }

    public async Task<PagedResponse<ConsultantPrivateResponse>> AdminSearchAsync(Guid actor, ConsultantAdminQuery query, CancellationToken ct)
    {
        await RequireActor(actor, true, ct);
        await adminValidator.ValidateAndThrowAsync(query, ct);
        var (items, total) = await profiles.AdminSearchAsync(query, ct);
        return new(items.Select(Private).ToArray(), query.PageNumber, query.PageSize, total);
    }

    public async Task<ConsultantPrivateResponse> AdminGetAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await RequireActor(actor, true, ct);
        return Private(await profiles.FindAsync(id, false, ct) ?? throw new NotFoundException("Consultant not found."));
    }

    public async Task<ConsultantPrivateResponse> ReviewAsync(Guid actor, Guid id, ConsultantReviewRequest request, CancellationToken ct)
    {
        await RequireActor(actor, true, ct);
        await reviewValidator.ValidateAndThrowAsync(request, ct);
        var profile = await profiles.FindAsync(id, false, ct) ?? throw new NotFoundException("Consultant not found.");
        if (profile.UserId == actor) throw new AppException("Self-verification is not allowed.", 403, "self_verification");
        CheckRevision(profile, request.Revision);
        var previous = profile.VerificationStatus;
        profile.VerificationStatus = (request.Action, previous) switch
        {
            (ConsultantReviewAction.Approve, ConsultantVerificationStatus.Pending) => ConsultantVerificationStatus.Verified,
            (ConsultantReviewAction.Reject, ConsultantVerificationStatus.Pending) => ConsultantVerificationStatus.Rejected,
            (ConsultantReviewAction.Suspend, ConsultantVerificationStatus.Verified) => ConsultantVerificationStatus.Suspended,
            (ConsultantReviewAction.Reactivate, ConsultantVerificationStatus.Suspended) when profile.VerifiedAtUtc.HasValue => ConsultantVerificationStatus.Verified,
            _ => throw new ConflictException("Invalid verification transition.")
        };
        if (profile.VerificationStatus == ConsultantVerificationStatus.Verified)
        {
            await RequireActor(profile.UserId, false, ct);
            profile.VerifiedAtUtc = clock.GetUtcNow().UtcDateTime;
            profile.VerificationMethod = "ManualLinkedInReview";
        }
        profile.VerificationReason = request.Reason.Trim();
        profile.ReviewedByUserId = actor;
        profile.ReviewedAtUtc = clock.GetUtcNow().UtcDateTime;
        await Save(profile, AuditAction.Update, request.Action.ToString(), ct);
        return Private(profile);
    }

    private async Task ApplyProfile(CareerConsultant profile, ConsultantProfileRequest request, CancellationToken ct)
    {
        var company = request.CompanyId.HasValue ? await companies.GetByIdAsync(request.CompanyId.Value, ct)
            ?? throw new BadRequestException("Company must exist.") : null;
        profile.CompanyId = company?.Id;
        profile.CompanyName = company?.Name ?? request.CompanyName.Trim();
        profile.DisplayName = request.DisplayName.Trim();
        profile.ProfessionalHeadline = request.ProfessionalHeadline.Trim();
        profile.Bio = request.Bio.Trim();
        profile.CurrentRole = request.CurrentRole.Trim();
        profile.YearsOfExperience = request.YearsOfExperience;
        profile.ProfessionalType = request.ProfessionalType;
        profile.LinkedInUrl = request.LinkedInUrl.Trim();
        profile.TermsAcceptedAtUtc = clock.GetUtcNow().UtcDateTime;
        profile.PolicyVersion = PolicyVersion;
        SetTags(profile, ConsultantTagKind.Language, request.Languages);
        SetTags(profile, ConsultantTagKind.Expertise, request.Expertise);
    }

    private void SetTags(CareerConsultant profile, ConsultantTagKind kind, string[] values)
    {
        var desired = values.Select(x => x.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        foreach (var tag in profile.Tags.Where(x => x.Kind == kind))
        {
            tag.IsDeleted = !desired.Remove(tag.Value);
            tag.DeletedAtUtc = tag.IsDeleted ? clock.GetUtcNow().UtcDateTime : null;
        }
        foreach (var value in desired) profile.Tags.Add(new() { ConsultantId = profile.Id, Kind = kind, Value = value });
    }

    private async Task<CareerConsultant> Own(Guid actor, CancellationToken ct)
    {
        await RequireActor(actor, false, ct);
        var profile = await profiles.FindByUserAsync(actor, ct);
        return profile is { IsDeleted: false } ? profile : throw new NotFoundException("Consultant profile not found.");
    }

    private async Task RequireActor(Guid actor, bool admin, CancellationToken ct)
    {
        var user = await users.GetByIdWithRoleAsync(actor, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active) throw new UnauthorizedException();
        if (admin && user.Role.Name != "Administrator") throw new AppException("Administrator access required.", 403, "forbidden");
    }

    private static void CheckRevision(CareerConsultant profile, Guid? revision)
    {
        if (!revision.HasValue || revision == Guid.Empty || revision != profile.Revision)
            throw new ConflictException("Profile changed. Reload before retrying.", "concurrency_conflict");
    }

    private static void CheckMutable(CareerConsultant profile, Guid? revision)
    {
        CheckRevision(profile, revision);
        if (profile.VerificationStatus == ConsultantVerificationStatus.Suspended)
            throw new AppException("Suspended consultant profiles cannot be changed.", 403, "consultant_suspended");
    }

    private async Task Save(CareerConsultant profile, AuditAction action, string result, CancellationToken ct)
    {
        profile.Revision = Guid.NewGuid();
        await audit.AppendAsync(new(action, "CareerConsultant", profile.Id.ToString(), new Dictionary<string, string?>
            { ["result"] = result, ["status"] = profile.VerificationStatus.ToString() }), ct);
        try { await unitOfWork.SaveChangesAsync(ct); }
        catch (UniqueConstraintException) { unitOfWork.ResetAfterFailure(); throw new ConflictException("Consultant data already exists. Reload before retrying."); }
    }

    private static ConsultantServiceResponse Service(CareerConsultantService s) =>
        new(s.Id, s.ServiceType, s.Title, s.Description, s.DurationMinutes, s.Price, s.Currency, s.IsActive);
    private static ConsultantPublicResponse Public(CareerConsultant p) => new(p.Id, p.DisplayName, p.ProfessionalHeadline,
        p.Bio, p.CompanyId, p.CompanyName, p.CurrentRole, p.YearsOfExperience, p.ProfessionalType,
        p.Tags.Where(t => !t.IsDeleted && t.Kind == ConsultantTagKind.Language).Select(t => t.Value).Order().ToArray(),
        p.Tags.Where(t => !t.IsDeleted && t.Kind == ConsultantTagKind.Expertise).Select(t => t.Value).Order().ToArray(),
        false, Disclaimer, p.Services.Where(s => !s.IsDeleted && s.IsActive).OrderBy(s => s.Id).Select(Service).ToArray());
    private static ConsultantPrivateResponse Private(CareerConsultant p) => new(Public(p), p.UserId, p.LinkedInUrl,
        p.VerificationStatus, p.VerificationMethod, p.VerificationReason, p.ReviewedByUserId, p.ReviewedAtUtc, p.VerifiedAtUtc,
        p.TermsAcceptedAtUtc, p.PolicyVersion, p.Revision, p.Services.Where(s => !s.IsDeleted).OrderBy(s => s.Id).Select(Service).ToArray());
}
