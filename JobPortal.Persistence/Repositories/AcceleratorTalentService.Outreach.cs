using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace JobPortal.Persistence.Repositories;

public sealed record HiringContactView(Guid Id, string FullName, string Title, string Department,
    string ProfessionalEmail, EmployerContactVerification Verification, int Relevance);
public sealed record TalentDraftView(Guid Id, Guid JobId, string Subject, string Body,
    TalentOutreachStatus Status, IReadOnlyList<string> Recipients, DateTime? SentAtUtc);
public sealed record CreateTalentDraftRequest(Guid GoalId, Guid[] ContactIds);
public sealed record EditTalentDraftRequest(string Subject, string Body);

public sealed partial class AcceleratorTalentService
{
    public async Task<IReadOnlyList<HiringContactView>> HiringContactsAsync(Guid actor, Guid jobId,
        CancellationToken ct)
    {
        await Candidate(actor, ct);
        var job = await PublicJobRepository.AvailableJobs(db.Jobs.AsNoTracking(), Now)
            .Where(x => x.Id == jobId).Select(x => new { x.Title, x.CompanyId })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Job not found.");
        var limit = Math.Clamp(Outreach.MaxRecommendedContacts, 1, 5);
        var contacts = await db.EmployerContacts.AsNoTracking().Where(x => x.CompanyId == job.CompanyId &&
            !x.IsDeleted && !x.IsSuppressed && x.Verification != EmployerContactVerification.Invalid &&
            (x.DiscoveryExpiresAtUtc == null || x.DiscoveryExpiresAtUtc > Now))
            .OrderBy(x => x.Id).Take(100).ToArrayAsync(ct);
        return contacts.Select(x => new HiringContactView(x.Id, x.FullName, x.Title, x.Department,
            x.ProfessionalEmail, x.Verification,
            TalentContactRanker.Score(job.Title, x.Department, x.Title)))
            .OrderByDescending(x => x.Relevance).ThenBy(x => x.Id).Take(limit).ToArray();
    }

    public async Task<TalentDraftView> CreateDraftAsync(Guid actor, Guid jobId,
        CreateTalentDraftRequest request, CancellationToken ct)
    {
        await Candidate(actor, ct);
        if (!Outreach.EmployerOutreachEnabled)
            throw new AppException("Employer outreach is disabled.", 503, "outreach_disabled");
        if (request.ContactIds.Length is < 1 or > 2 ||
            request.ContactIds.Length > Outreach.MaxRecipientsPerDraft ||
            request.ContactIds.Distinct().Count() != request.ContactIds.Length)
            throw new BadRequestException("Invalid recipient selection.");
        var job = await PublicJobRepository.AvailableJobs(db.Jobs.AsNoTracking(), Now)
            .Where(x => x.Id == jobId).Select(x => new { x.Title, x.CompanyId,
                Company = x.Company.Name }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Job not found.");
        var candidate = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor, ct);
        if (!await db.AcceleratorCareerGoals.AnyAsync(x => x.Id == request.GoalId &&
            x.UserId == actor && !x.IsDeleted, ct)) throw new NotFoundException("Career goal not found.");
        var contacts = await db.EmployerContacts.AsNoTracking()
            .Where(x => request.ContactIds.Contains(x.Id) && x.CompanyId == job.CompanyId &&
                !x.IsDeleted && !x.IsSuppressed && x.Verification == EmployerContactVerification.Verified)
            .ToArrayAsync(ct);
        if (contacts.Length != request.ContactIds.Length)
            throw new BadRequestException("Recipients must be verified, current company contacts.");
        var evidence = await passport.PassportAsync(actor, request.GoalId, ct);
        var verified = evidence.Skills.Where(x => x.TrustLevel is "AssessmentVerified" or
            "ProjectDemonstrated" or "InterviewDemonstrated").Select(x => x.SkillName)
            .OrderBy(x => x).Take(4).ToArray();
        var name = string.Join(' ', new[] { candidate.FirstName, candidate.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var subject = $"Interest in {job.Title} — {name}";
        if (subject.Length > 180) subject = subject[..180];
        var evidenceLine = verified.Length == 0 ? "I'd welcome the opportunity to discuss my fit." :
            "My verified CareerHarbor evidence includes " + string.Join(", ", verified) + ".";
        var draft = new TalentOutreachDraft { CandidateId = actor, JobId = jobId,
            CompanyId = job.CompanyId, Subject = subject,
            Body = $"Hello,\n\nI'm interested in the {job.Title} opportunity at {job.Company}. " +
                $"{evidenceLine}\n\nRegards,\n{name}" };
        db.TalentOutreachDrafts.Add(draft);
        foreach (var contact in contacts)
            db.TalentOutreachRecipients.Add(new TalentOutreachRecipient { DraftId = draft.Id,
                ContactId = contact.Id, EmailSnapshot = contact.ProfessionalEmail });
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Create, "TalentOutreachDraft", draft.Id, "Candidate", ct);
        return new(draft.Id, jobId, draft.Subject, draft.Body, draft.Status,
            contacts.Select(x => x.ProfessionalEmail).ToArray(), null);
    }

    public async Task<TalentDraftView> EditDraftAsync(Guid actor, Guid id,
        EditTalentDraftRequest request, CancellationToken ct)
    {
        await Candidate(actor, ct);
        if (!Outreach.EmployerOutreachEnabled)
            throw new AppException("Employer outreach is disabled.", 503, "outreach_disabled");
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 180 ||
            string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 10000)
            throw new BadRequestException("Invalid outreach content.");
        var draft = await db.TalentOutreachDrafts.SingleOrDefaultAsync(x => x.Id == id &&
            x.CandidateId == actor && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Outreach draft not found.");
        if (draft.Status is not (TalentOutreachStatus.Draft or TalentOutreachStatus.Ready))
            throw new ConflictException("Outreach can no longer be edited.");
        draft.Subject = request.Subject.Trim(); draft.Body = request.Body.Trim();
        draft.Revision = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Update, "TalentOutreachDraft", id, "Candidate", ct);
        return await DraftAsync(actor, id, ct);
    }

    public async Task<TalentDraftView> DraftAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var draft = await db.TalentOutreachDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.CandidateId == actor && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Outreach draft not found.");
        var recipients = await db.TalentOutreachRecipients.AsNoTracking().Where(x =>
            x.DraftId == id && !x.IsDeleted).OrderBy(x => x.Id).Select(x => x.EmailSnapshot)
            .ToArrayAsync(ct);
        return new(draft.Id, draft.JobId, draft.Subject, draft.Body, draft.Status,
            recipients, draft.SentAtUtc);
    }

    public async Task<IReadOnlyList<TalentDraftView>> OutreachHistoryAsync(Guid actor, int page,
        int pageSize, CancellationToken ct)
    {
        await Candidate(actor, ct); CheckPage(page, pageSize);
        var ids = await db.TalentOutreachDrafts.AsNoTracking().Where(x => x.CandidateId == actor &&
            !x.IsDeleted).OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize)
            .Take(pageSize).Select(x => x.Id).ToArrayAsync(ct);
        var result = new List<TalentDraftView>(ids.Length);
        foreach (var id in ids) result.Add(await DraftAsync(actor, id, ct));
        return result;
    }

    // The candidate's explicit command creates a durable, conditional send intent.
    // Dispatching and unknown requests are never resent by this method.
    public async Task<TalentDraftView> SendAsync(Guid actor, Guid id, Guid mailboxConnectionId,
        string idempotencyKey, CancellationToken ct)
    {
        await Candidate(actor, ct);
        if (!Outreach.EmployerOutreachEnabled)
            throw new AppException("Employer outreach is disabled.", 503, "outreach_disabled");
        if (mailboxConnectionId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 100)
            throw new BadRequestException("An idempotency key is required.");
        var draft = await db.TalentOutreachDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            x.CandidateId == actor && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Outreach draft not found.");
        if (draft.Status == TalentOutreachStatus.Sent && draft.SendIdempotencyKey == idempotencyKey)
        {
            if (draft.UsageReservationId.HasValue)
                await quota.CompleteAsync(actor, draft.UsageReservationId.Value, 0, 0, 0, ct);
            return await DraftAsync(actor, id, ct);
        }
        if (draft.Status is TalentOutreachStatus.Pending or TalentOutreachStatus.Dispatching or
            TalentOutreachStatus.Unknown)
            throw new ConflictException("Send outcome requires reconciliation; it will not be retried.");
        if (draft.Status == TalentOutreachStatus.Sent)
            throw new ConflictException("Outreach already sent.");
        if (draft.Status is not (TalentOutreachStatus.Draft or TalentOutreachStatus.Ready))
            throw new ConflictException("Outreach cannot be sent in its current state.");
        if (!await PublicJobRepository.AvailableJobs(db.Jobs, Now).AnyAsync(x =>
            x.Id == draft.JobId && x.CompanyId == draft.CompanyId, ct))
            throw new ConflictException("This job is no longer available.");
        if (draft.ShareLinkId.HasValue && !await db.TalentShareLinks.AnyAsync(x =>
            x.Id == draft.ShareLinkId && x.CandidateId == actor && !x.IsDeleted &&
            x.RevokedAtUtc == null && x.ExpiresAtUtc > Now, ct))
            throw new ConflictException("Talent share link is unavailable.");
        var connection = await db.CandidateMailboxConnections.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == mailboxConnectionId && x.CandidateId == actor && !x.IsDeleted &&
            x.RevokedAtUtc == null, ct);
        if (connection is null)
            throw new AppException("Connect an authorized mailbox before sending.", 503, "mailbox_unavailable");
        var provider = mailboxProviders.SingleOrDefault(x => x.Name == connection.Provider && x.IsAvailable);
        if (provider is null || connection.Provider == "google" && !Outreach.GoogleMailboxEnabled ||
            connection.Provider == "microsoft" && !Outreach.MicrosoftMailboxEnabled)
            throw new AppException("Mailbox provider is unavailable.", 503, "mailbox_unavailable");
        var recipients = await (from item in db.TalentOutreachRecipients.AsNoTracking()
            join contact in db.EmployerContacts.AsNoTracking() on item.ContactId equals contact.Id
            where item.DraftId == id && !item.IsDeleted && !contact.IsDeleted && !contact.IsSuppressed &&
                contact.Verification == EmployerContactVerification.Verified &&
                contact.CompanyId == draft.CompanyId && item.EmailSnapshot == contact.ProfessionalEmail
            orderby item.EmailSnapshot
            select item.EmailSnapshot).ToArrayAsync(ct);
        var totalRecipients = await db.TalentOutreachRecipients.CountAsync(x => x.DraftId == id &&
            !x.IsDeleted, ct);
        if (recipients.Length != totalRecipients || recipients.Length == 0 ||
            recipients.Length > Outreach.MaxRecipientsPerDraft)
            throw new ConflictException("Recipient verification or suppression changed.");
        var since = Now.AddDays(-Math.Max(1, Outreach.RecipientCooldownDays));
        if (await db.TalentOutreachRecipients.AnyAsync(x => recipients.Contains(x.EmailSnapshot) &&
            db.TalentOutreachDrafts.Any(d => d.Id == x.DraftId && d.Id != id &&
                d.CandidateId == actor && d.JobId == draft.JobId &&
                (d.Status == TalentOutreachStatus.Sent || d.Status == TalentOutreachStatus.Unknown ||
                 d.Status == TalentOutreachStatus.Dispatching) && d.CreatedAtUtc >= since), ct))
            throw new ConflictException("This job contact is within the outreach cooldown.");
        var day = Now.Date;
        var usedToday = await db.TalentOutreachDrafts.CountAsync(x => x.CandidateId == actor &&
            x.CreatedAtUtc >= day && x.Status != TalentOutreachStatus.Draft &&
            x.Status != TalentOutreachStatus.Ready && x.Status != TalentOutreachStatus.Failed &&
            x.Status != TalentOutreachStatus.Cancelled, ct);
        var month = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usedMonth = await db.TalentOutreachDrafts.CountAsync(x => x.CandidateId == actor &&
            x.CreatedAtUtc >= month && x.Status != TalentOutreachStatus.Draft &&
            x.Status != TalentOutreachStatus.Ready && x.Status != TalentOutreachStatus.Failed &&
            x.Status != TalentOutreachStatus.Cancelled, ct);
        if (usedToday >= Outreach.MaxSendsPerDay || usedMonth >= Outreach.MaxSendsPerMonth)
            throw new AppException("Outreach send limit reached.", 429, "outreach_rate_limit");
        var requestBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{actor:N}:{id:N}:{idempotencyKey}"));
        var requestKey = new Guid(requestBytes.AsSpan(0, 16));
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{draft.Subject}\n{draft.Body}\n{string.Join(';', recipients)}")));
        var reservation = await quota.ReserveOutreachAsync(actor, requestKey, payloadHash, ct);
        var claimed = await ClaimSendAsync(draft, mailboxConnectionId, idempotencyKey,
            reservation.Id, ct);
        if (!claimed)
        {
            var winner = await db.TalentOutreachDrafts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            if (winner.SendIdempotencyKey != idempotencyKey)
                await quota.ReleaseUnusedAsync(actor, reservation.Id, ct);
            throw new ConflictException("Outreach send is already in progress.");
        }
        await quota.MarkDispatchedAsync(actor, reservation.Id, ct);
        if (!await StartDispatchAsync(actor, id, idempotencyKey, ct))
            throw new ConflictException("Send intent changed before provider dispatch; reconciliation is required.");
        MailboxDispatchResult outcome;
        try
        {
            outcome = await provider.SendAsync(connection.TokenReference, idempotencyKey,
                recipients, draft.Subject, draft.Body, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            outcome = new(MailboxDispatchOutcome.Unknown, null);
        }
        catch (OperationCanceledException)
        {
            await RecordUnknownAsync(actor, id, reservation.Id);
            throw;
        }
        if (outcome.Outcome == MailboxDispatchOutcome.Sent &&
            !string.IsNullOrWhiteSpace(outcome.ProviderMessageId))
        {
            if (!await SaveOutcomeAsync(actor, id, TalentOutreachStatus.Sent,
                outcome.ProviderMessageId, ct))
                throw new ConflictException("Send result changed; reconciliation is required.");
            await quota.CompleteAsync(actor, reservation.Id, 0, 0, 0, ct);
            await Audit(actor, AuditAction.Publish, "TalentOutreachSend", id, "Candidate", ct);
            return await DraftAsync(actor, id, ct);
        }
        if (outcome.Outcome == MailboxDispatchOutcome.ConfirmedNonChargeableFailure &&
            !string.IsNullOrWhiteSpace(outcome.FailureEvidenceReference) &&
            outcome.FailureEvidenceReference.Length <= 500)
        {
            if (!await SaveOutcomeAsync(actor, id, TalentOutreachStatus.Failed, null, ct,
                outcome.FailureEvidenceReference))
                throw new ConflictException("Send result changed; reconciliation is required.");
            await quota.ResolveVerifiedNonChargeableAsync(actor, reservation.Id,
                idempotencyKey, outcome.FailureEvidenceReference, ct);
            return await DraftAsync(actor, id, ct);
        }
        await RecordUnknownAsync(actor, id, reservation.Id);
        throw new ConflictException("Mailbox outcome is unknown and requires reconciliation.");
    }

    private async Task<bool> ClaimSendAsync(TalentOutreachDraft draft, Guid mailboxConnectionId,
        string idempotencyKey, Guid reservationId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var tracked = await db.TalentOutreachDrafts.SingleAsync(x => x.Id == draft.Id, ct);
            if (tracked.Status is not (TalentOutreachStatus.Draft or TalentOutreachStatus.Ready)) return false;
            tracked.Status = TalentOutreachStatus.Pending;
            tracked.MailboxConnectionId = mailboxConnectionId;
            tracked.SendIdempotencyKey = idempotencyKey;
            tracked.UsageReservationId = reservationId;
            tracked.Revision = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            return true;
        }
        return await db.TalentOutreachDrafts.Where(x => x.Id == draft.Id &&
            x.CandidateId == draft.CandidateId &&
            (x.Status == TalentOutreachStatus.Draft || x.Status == TalentOutreachStatus.Ready) &&
            x.SendIdempotencyKey == "")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, TalentOutreachStatus.Pending)
                .SetProperty(x => x.MailboxConnectionId, mailboxConnectionId)
                .SetProperty(x => x.SendIdempotencyKey, idempotencyKey)
                .SetProperty(x => x.UsageReservationId, reservationId)
                .SetProperty(x => x.Revision, Guid.NewGuid()), ct) == 1;
    }

    private async Task RecordUnknownAsync(Guid actor, Guid id, Guid reservationId)
    {
        await SaveOutcomeAsync(actor, id, TalentOutreachStatus.Unknown, null,
            CancellationToken.None);
        await quota.MarkOutreachUncertainAsync(actor, reservationId, CancellationToken.None);
        await Audit(actor, AuditAction.Update, "TalentOutreachUnknown", id, "Candidate",
            CancellationToken.None);
    }

    private async Task<bool> StartDispatchAsync(Guid actor, Guid id, string key, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var row = await db.TalentOutreachDrafts.SingleAsync(x => x.Id == id, ct);
            if (row.CandidateId != actor || row.Status != TalentOutreachStatus.Pending ||
                row.SendIdempotencyKey != key) return false;
            row.Status = TalentOutreachStatus.Dispatching;
            row.DispatchStartedAtUtc = Now;
            await db.SaveChangesAsync(ct);
            return true;
        }
        return await db.TalentOutreachDrafts.Where(x => x.Id == id && x.CandidateId == actor &&
            x.Status == TalentOutreachStatus.Pending && x.SendIdempotencyKey == key)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, TalentOutreachStatus.Dispatching)
                .SetProperty(x => x.DispatchStartedAtUtc, Now), ct) == 1;
    }

    private async Task<bool> SaveOutcomeAsync(Guid actor, Guid id, TalentOutreachStatus status,
        string? providerMessageId, CancellationToken ct, string? failureEvidenceReference = null)
    {
        if (!db.Database.IsRelational())
        {
            var row = await db.TalentOutreachDrafts.SingleAsync(x => x.Id == id, ct);
            if (row.CandidateId != actor || row.Status != TalentOutreachStatus.Dispatching)
                return false;
            row.Status = status;
            row.ProviderMessageId = providerMessageId;
            row.FailureEvidenceReference = failureEvidenceReference;
            if (status == TalentOutreachStatus.Sent) row.SentAtUtc = Now;
            await db.SaveChangesAsync(ct);
            return true;
        }
        return await db.TalentOutreachDrafts.Where(x => x.Id == id && x.CandidateId == actor &&
            x.Status == TalentOutreachStatus.Dispatching)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, status)
                .SetProperty(x => x.ProviderMessageId, providerMessageId)
                .SetProperty(x => x.FailureEvidenceReference, failureEvidenceReference)
                .SetProperty(x => x.SentAtUtc, status == TalentOutreachStatus.Sent ? Now : null), ct) == 1;
    }
}
