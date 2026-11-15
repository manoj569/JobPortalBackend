using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPortal.Persistence.Repositories;

public sealed partial class AcceleratorTalentService
{
    public async Task<TalentSubmissionView> SubmitAsync(Guid actor, Guid jobId, Guid goalId,
        CancellationToken ct)
    {
        await Candidate(actor, ct);
        if (!Outreach.RegisteredEmployerTalentInboxEnabled)
            throw new AppException("Employer talent submissions are disabled.", 503, "talent_inbox_disabled");
        if (!await db.AcceleratorCareerGoals.AnyAsync(x => x.Id == goalId && x.UserId == actor &&
            !x.IsDeleted, ct)) throw new NotFoundException("Career goal not found.");
        var job = await PublicJobRepository.AvailableJobs(db.Jobs, Now)
            .Where(x => x.Id == jobId && x.Company.IsVerified &&
                x.Company.OwnerUser.Role.Name == "Employer")
            .Select(x => new { x.Id, x.Title, x.CompanyId, Company = x.Company.Name })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Registered employer job not found.");
        var existing = await db.TalentSubmissions.SingleOrDefaultAsync(x => x.CandidateId == actor &&
            x.JobId == jobId && !x.IsDeleted, ct);
        if (existing is not null)
        {
            if (existing.GoalId != goalId) throw new ConflictException("Submission already exists for this job.");
            return View(existing, job.Title, job.Company);
        }
        var submission = new TalentSubmission { CandidateId = actor, JobId = jobId,
            CompanyId = job.CompanyId, GoalId = goalId, SubmittedAtUtc = Now };
        db.TalentSubmissions.Add(submission);
        var firstEvent = new TalentSubmissionEvent { SubmissionId = submission.Id,
            ActorId = actor, Status = TalentSubmissionStatus.Submitted, OccurredAtUtc = Now };
        db.TalentSubmissionEvents.Add(firstEvent);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(submission).State = EntityState.Detached;
            db.Entry(firstEvent).State = EntityState.Detached;
            var winner = await db.TalentSubmissions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.CandidateId == actor && x.JobId == jobId && !x.IsDeleted, ct);
            if (winner is null) throw;
            if (winner.GoalId != goalId)
                throw new ConflictException("Submission already exists for this job.");
            return View(winner, job.Title, job.Company);
        }
        await Audit(actor, AuditAction.Create, "TalentSubmission", submission.Id, "Candidate", ct);
        return View(submission, job.Title, job.Company);
    }

    public async Task<IReadOnlyList<TalentSubmissionView>> CandidateSubmissionsAsync(Guid actor,
        int page, int pageSize, CancellationToken ct)
    {
        await Candidate(actor, ct); CheckPage(page, pageSize);
        var rows = await db.TalentSubmissions.AsNoTracking().Where(x => x.CandidateId == actor &&
            !x.IsDeleted).OrderByDescending(x => x.SubmittedAtUtc).Skip((page - 1) * pageSize)
            .Take(pageSize).Join(db.Jobs, x => x.JobId, j => j.Id,
                (x, j) => new { Submission = x, j.Title, Company = j.Company.Name }).ToArrayAsync(ct);
        return rows.Select(x => View(x.Submission, x.Title, x.Company)).ToArray();
    }

    public async Task<IReadOnlyList<TalentSubmissionView>> EmployerInboxAsync(Guid actor,
        Guid companyId, int page, int pageSize, CancellationToken ct)
    {
        await Employer(actor, companyId, ct); CheckPage(page, pageSize);
        var rows = await db.TalentSubmissions.AsNoTracking().Where(x => x.CompanyId == companyId &&
            !x.IsDeleted && x.Status != TalentSubmissionStatus.Withdrawn)
            .OrderByDescending(x => x.SubmittedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.Jobs, x => x.JobId, j => j.Id,
                (x, j) => new { Submission = x, j.Title, Company = j.Company.Name }).ToArrayAsync(ct);
        return rows.Select(x => View(x.Submission, x.Title, x.Company)).ToArray();
    }

    public async Task<(TalentSubmissionView Submission, TalentPublicPackage Package)> EmployerDetailAsync(
        Guid actor, Guid id, CancellationToken ct)
    {
        var row = await db.TalentSubmissions.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted,
            ct) ?? throw new NotFoundException("Submission not found.");
        await Employer(actor, row.CompanyId, ct);
        if (row.Status == TalentSubmissionStatus.Withdrawn)
            throw new NotFoundException("Submission not found.");
        if (row.Status == TalentSubmissionStatus.Submitted)
            await Transition(row, actor, TalentSubmissionStatus.Viewed, null, "Employer", ct);
        var candidatePassport = await passport.PassportAsync(row.CandidateId, row.GoalId, ct);
        var job = await db.Jobs.AsNoTracking().Where(x => x.Id == row.JobId)
            .Select(x => new { x.Title, Company = x.Company.Name }).SingleAsync(ct);
        var package = new TalentPublicPackage(candidatePassport.TargetRole,
            candidatePassport.Readiness.Total, candidatePassport.Skills
                .Where(x => x.TrustLevel is "AssessmentVerified" or "ProjectDemonstrated" or
                    "InterviewDemonstrated").Select(x => x.SkillName).ToArray(), Now);
        return (View(row, job.Title, job.Company), package);
    }

    public async Task<TalentSubmissionView> EmployerDecisionAsync(Guid actor, Guid id,
        TalentSubmissionStatus decision, string? message, CancellationToken ct)
    {
        if (decision is not (TalentSubmissionStatus.Shortlisted or TalentSubmissionStatus.InterviewRequested
            or TalentSubmissionStatus.Rejected)) throw new BadRequestException("Invalid employer decision.");
        if (!Outreach.RegisteredEmployerTalentInboxEnabled)
            throw new AppException("Employer talent decisions are disabled.", 503, "talent_inbox_disabled");
        if (message is { Length: > 2000 }) throw new BadRequestException("Interview message is too long.");
        var row = await db.TalentSubmissions.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted,
            ct) ?? throw new NotFoundException("Submission not found.");
        await Employer(actor, row.CompanyId, ct);
        if (row.Status == TalentSubmissionStatus.Withdrawn)
            throw new ConflictException("Candidate withdrew this submission.");
        if (row.Status is TalentSubmissionStatus.Rejected or TalentSubmissionStatus.InterviewRequested &&
            row.Status != decision)
            throw new ConflictException("This submission has a final employer decision.");
        await Transition(row, actor, decision, message, "Employer", ct);
        var job = await db.Jobs.AsNoTracking().Where(x => x.Id == row.JobId)
            .Select(x => new { x.Title, Company = x.Company.Name }).SingleAsync(ct);
        return View(row, job.Title, job.Company);
    }

    public async Task WithdrawAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await Candidate(actor, ct);
        var row = await db.TalentSubmissions.SingleOrDefaultAsync(x => x.Id == id &&
            x.CandidateId == actor && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Submission not found.");
        if (row.Status == TalentSubmissionStatus.Withdrawn) return;
        await Transition(row, actor, TalentSubmissionStatus.Withdrawn, null, "Candidate", ct);
        row.WithdrawnAtUtc = Now;
        await db.SaveChangesAsync(ct);
    }

    private async Task Transition(TalentSubmission row, Guid actor, TalentSubmissionStatus status,
        string? message, string role, CancellationToken ct)
    {
        if (row.Status == status) return;
        row.Status = status; row.Revision = Guid.NewGuid();
        if (status == TalentSubmissionStatus.Viewed) row.ViewedAtUtc = Now;
        if (status == TalentSubmissionStatus.InterviewRequested) row.InterviewMessage = message?.Trim();
        db.TalentSubmissionEvents.Add(new TalentSubmissionEvent { SubmissionId = row.Id,
            ActorId = actor, Status = status, OccurredAtUtc = Now });
        await db.SaveChangesAsync(ct);
        await Audit(actor, AuditAction.Update, "TalentSubmission", row.Id, role, ct);
    }

    private static TalentSubmissionView View(TalentSubmission row, string title, string company) =>
        new(row.Id, row.JobId, title, company, row.Status, row.SubmittedAtUtc, row.InterviewMessage);

    private static void CheckPage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new BadRequestException("Invalid page size.");
    }
}
