using System.Net.Mail;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed record AdminEmployerContactRequest(Guid CompanyId, string FullName, string Title,
    string Department, string ProfessionalEmail, EmployerContactRole Role,
    EmployerContactVerification Verification, string Source, string Reason,
    string? VerificationEvidence);
public sealed record TalentOperationsSummary(int Submitted, int Shortlisted, int InterviewsRequested,
    int Rejected, int Drafts, int UnknownSends, int SuppressedContacts);

public sealed partial class AcceleratorTalentService
{
    private async Task Administrator(Guid actor, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actor && !x.IsDeleted &&
            x.Status == UserStatus.Active && x.Role.Name == "Administrator", ct))
            throw new AppException("Administrator access required.", 403, "forbidden");
    }

    public async Task<EmployerContact> AddContactAsync(Guid actor,
        AdminEmployerContactRequest request, CancellationToken ct)
    {
        await Administrator(actor, ct);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500 ||
            string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 160 ||
            request.Title.Length > 160 || request.Department.Length > 100 ||
            string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 60 ||
            request.Verification == EmployerContactVerification.Verified &&
                string.IsNullOrWhiteSpace(request.VerificationEvidence) ||
            request.VerificationEvidence is { Length: > 500 } ||
            !MailAddress.TryCreate(request.ProfessionalEmail, out var address) ||
            address.Address != request.ProfessionalEmail.Trim() ||
            request.ProfessionalEmail.Length > 254)
            throw new BadRequestException("Invalid professional contact data.");
        if (!await db.Companies.AnyAsync(x => x.Id == request.CompanyId && !x.IsDeleted, ct))
            throw new NotFoundException("Company not found.");
        var normalized = request.ProfessionalEmail.Trim().ToUpperInvariant();
        if (await db.EmployerContacts.AnyAsync(x => x.CompanyId == request.CompanyId &&
            x.NormalizedEmail == normalized && !x.IsDeleted, ct))
            throw new ConflictException("Contact already exists for this company.");
        var contact = new EmployerContact { CompanyId = request.CompanyId,
            FullName = request.FullName.Trim(), Title = request.Title.Trim(),
            Department = request.Department.Trim(), ProfessionalEmail = address.Address,
            NormalizedEmail = normalized, Role = request.Role, Verification = request.Verification,
            VerificationEvidence = request.VerificationEvidence?.Trim(),
            Source = request.Source.Trim(), LastVerifiedAtUtc =
                request.Verification == EmployerContactVerification.Verified ? Now : null };
        db.EmployerContacts.Add(contact);
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Create, "EmployerContact", contact.Id, "Administrator", ct);
        return contact;
    }

    public async Task SuppressContactAsync(Guid actor, Guid id, string reason,
        CancellationToken ct)
    {
        await Administrator(actor, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BadRequestException("Suppression reason is required.");
        var contact = await db.EmployerContacts.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted,
            ct) ?? throw new NotFoundException("Contact not found.");
        if (contact.IsSuppressed) return;
        contact.IsSuppressed = true;
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Update, "EmployerContactSuppression", id, "Administrator", ct);
    }

    public async Task<TalentOperationsSummary> OperationsAsync(Guid actor, CancellationToken ct)
    {
        await Administrator(actor, ct);
        var submitted = await db.TalentSubmissions.CountAsync(x => !x.IsDeleted &&
            x.Status == TalentSubmissionStatus.Submitted, ct);
        var shortlisted = await db.TalentSubmissions.CountAsync(x => !x.IsDeleted &&
            x.Status == TalentSubmissionStatus.Shortlisted, ct);
        var interviews = await db.TalentSubmissions.CountAsync(x => !x.IsDeleted &&
            x.Status == TalentSubmissionStatus.InterviewRequested, ct);
        var rejected = await db.TalentSubmissions.CountAsync(x => !x.IsDeleted &&
            x.Status == TalentSubmissionStatus.Rejected, ct);
        var drafts = await db.TalentOutreachDrafts.CountAsync(x => !x.IsDeleted &&
            x.Status == TalentOutreachStatus.Draft, ct);
        var unknown = await db.TalentOutreachDrafts.CountAsync(x => !x.IsDeleted &&
            x.Status == TalentOutreachStatus.Unknown, ct);
        var suppressed = await db.EmployerContacts.CountAsync(x => !x.IsDeleted && x.IsSuppressed,
            ct);
        return new(submitted, shortlisted, interviews, rejected, drafts, unknown, suppressed);
    }
}
