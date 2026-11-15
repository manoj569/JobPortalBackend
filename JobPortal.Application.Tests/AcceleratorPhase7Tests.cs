using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AcceleratorPhase7Tests
{
    [Fact]
    public void AcceleratorPhase7MatchExplainsVerifiedSkillsAndGaps()
    {
        var result = TalentMatchPolicy.Evaluate(true, ["SQL", "Python", "Airflow"],
            ["SQL", "Python"]);
        Assert.Equal(67, result.MarketMatchScore);
        Assert.Equal(["Python", "SQL"], result.SkillMatches);
        Assert.Equal(["Airflow"], result.SkillGaps);
        Assert.Contains("Matches active career goal role", result.Reasons);
        Assert.Contains("No verified evidence for: Airflow", result.Reasons);
    }

    [Fact]
    public void AcceleratorPhase7MatchDoesNotTreatMissingEvidenceAsReadiness()
    {
        var result = TalentMatchPolicy.Evaluate(false, ["SQL"], []);
        Assert.Equal(0, result.MarketMatchScore);
        Assert.Empty(result.SkillMatches);
        Assert.Equal(["SQL"], result.SkillGaps);
    }

    [Fact]
    public void AcceleratorPhase7MatchIsDeterministicAcrossInputOrdering()
    {
        var first = TalentMatchPolicy.Evaluate(false, ["Snowflake", "SQL", "ETL"], ["ETL", "SQL"]);
        var second = TalentMatchPolicy.Evaluate(false, ["SQL", "ETL", "Snowflake"], ["SQL", "ETL"]);
        Assert.Equal(first.MarketMatchScore, second.MarketMatchScore);
        Assert.Equal(first.Reasons, second.Reasons);
    }

    [Fact]
    public void AcceleratorPhase7ContactRankerPrefersRelevantTechnicalRecruiter()
    {
        var technical = TalentContactRanker.Score("Software Engineer", "Engineering", "Technical Recruiter");
        var finance = TalentContactRanker.Score("Software Engineer", "Finance", "Finance Recruiter");
        Assert.True(technical > finance);
    }

    [Fact]
    public void AcceleratorPhase7ContactVerificationIsDistinctFromRelevance()
    {
        var contact = new EmployerContact { Title = "Technical Recruiter",
            Verification = EmployerContactVerification.Unknown };
        Assert.True(TalentContactRanker.Score("Software Engineer", "Engineering", contact.Title) > 0);
        Assert.NotEqual(EmployerContactVerification.Verified, contact.Verification);
    }

    [Fact]
    public void AcceleratorPhase7OutreachDefaultsFailClosed()
    {
        var options = new TalentOutreachOptions();
        Assert.False(options.EmployerOutreachEnabled);
        Assert.False(options.ContactDiscoveryEnabled);
        Assert.False(options.GoogleMailboxEnabled);
        Assert.False(options.MicrosoftMailboxEnabled);
        Assert.False(options.OutreachAiDraftingEnabled);
    }

    [Fact]
    public async Task AcceleratorPhase7SubmissionEventsAreAppendOnly()
    {
        await using var db = Database();
        var item = new TalentSubmissionEvent { SubmissionId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(), Status = TalentSubmissionStatus.Submitted,
            OccurredAtUtc = DateTime.UtcNow };
        db.TalentSubmissionEvents.Add(item);
        await db.SaveChangesAsync();
        item.Status = TalentSubmissionStatus.Rejected;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AcceleratorPhase7ShareTokenHashCannotBeRebound()
    {
        await using var db = Database();
        var link = new TalentShareLink { CandidateId = Guid.NewGuid(), GoalId = Guid.NewGuid(),
            TokenHash = new string('A', 64), ExpiresAtUtc = DateTime.UtcNow.AddDays(1) };
        db.TalentShareLinks.Add(link);
        await db.SaveChangesAsync();
        link.TokenHash = new string('B', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static JobPortalDbContext Database() => new(new DbContextOptionsBuilder<JobPortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task AcceleratorPhase7CandidateCannotCreateAdminContact()
    {
        await using var db = Database();
        var candidate = User(db, "Candidate");
        var company = new Company { Name = "Example", OwnerUserId = candidate.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var service = AdminService(db, new RecordingAudit());
        var request = new AdminEmployerContactRequest(company.Id, "Recruiter One",
            "Technical Recruiter", "Engineering", "recruiter@example.com",
            EmployerContactRole.TechnicalRecruiter, EmployerContactVerification.Unknown,
            "Administrator", "Reviewed", null);
        var error = await Assert.ThrowsAsync<AppException>(() => service.AddContactAsync(candidate.Id,
            request, default));
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(db.EmployerContacts);
    }

    [Fact]
    public async Task AcceleratorPhase7VerifiedContactRequiresEvidenceAndSuppressionIsAudited()
    {
        await using var db = Database();
        var admin = User(db, "Administrator");
        var company = new Company { Name = "Example", OwnerUserId = admin.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var audit = new RecordingAudit();
        var service = AdminService(db, audit);
        var request = new AdminEmployerContactRequest(company.Id, "Recruiter One",
            "Technical Recruiter", "Engineering", "recruiter@example.com",
            EmployerContactRole.TechnicalRecruiter, EmployerContactVerification.Verified,
            "Administrator", "Reviewed", null);
        await Assert.ThrowsAsync<BadRequestException>(() => service.AddContactAsync(admin.Id,
            request, default));
        var contact = await service.AddContactAsync(admin.Id,
            request with { VerificationEvidence = "Confirmed by company-domain reply" }, default);
        Assert.Equal(EmployerContactVerification.Verified, contact.Verification);
        await service.SuppressContactAsync(admin.Id, contact.Id, "Requested no contact", default);
        Assert.True(contact.IsSuppressed);
        Assert.Contains(audit.Events, x => x.EntityType == "EmployerContactSuppression");
    }

    [Fact]
    public async Task AcceleratorPhase7MailboxListingIsCandidateOwnedAndHidesTokenReference()
    {
        await using var db = Database();
        var first = User(db, "Candidate");
        var second = User(db, "Candidate");
        db.CandidateMailboxConnections.AddRange(
            new CandidateMailboxConnection { CandidateId = first.Id, Provider = "google",
                EmailAddress = "first@example.com", TokenReference = "private-first",
                ConnectedAtUtc = DateTime.UtcNow },
            new CandidateMailboxConnection { CandidateId = second.Id, Provider = "microsoft",
                EmailAddress = "second@example.com", TokenReference = "private-second",
                ConnectedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var row = Assert.Single(await AdminService(db, new RecordingAudit())
            .MailboxesAsync(first.Id, default));
        Assert.Equal("first@example.com", row.EmailAddress);
        Assert.DoesNotContain("Token", string.Join(',', row.GetType().GetProperties()
            .Select(x => x.Name)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceleratorPhase7DisabledMailboxConnectionFailsClosed()
    {
        using var db = Database();
        var service = AdminService(db, new RecordingAudit());
        var error = Assert.Throws<AppException>(() => service.BeginMailboxConnect(Guid.NewGuid(),
            "google"));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task AcceleratorPhase7SendFinalizesExistingQuotaAndReplayDoesNotDispatchTwice()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var provider = new FakeMailboxProvider();
        var (service, draft, mailbox) = SeedOutreach(fixture, provider);
        var sent = await service.SendAsync(fixture.Candidate.Id, draft.Id, mailbox.Id,
            "candidate-click-1", default);
        Assert.Equal(TalentOutreachStatus.Sent, sent.Status);
        Assert.Equal(1, provider.Calls);
        Assert.False(string.IsNullOrWhiteSpace(draft.ProviderMessageId));
        var replay = await service.SendAsync(fixture.Candidate.Id, draft.Id, mailbox.Id,
            "candidate-click-1", default);
        Assert.Equal(TalentOutreachStatus.Sent, replay.Status);
        Assert.Equal(1, provider.Calls);
        var usage = Assert.Single(fixture.Db.Set<AcceleratorUsageReservation>().Where(x =>
            x.Feature == AcceleratorFeature.EmployerOutreach));
        Assert.Equal(AcceleratorUsageState.Completed, usage.State);
    }

    [Fact]
    public async Task AcceleratorPhase7UnknownSendKeepsQuotaAndRefusesReplay()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var provider = new FakeMailboxProvider { Outcome = MailboxDispatchOutcome.Unknown };
        var (service, draft, mailbox) = SeedOutreach(fixture, provider);
        await Assert.ThrowsAsync<ConflictException>(() => service.SendAsync(fixture.Candidate.Id,
            draft.Id, mailbox.Id, "candidate-click-1", default));
        Assert.Equal(TalentOutreachStatus.Unknown, draft.Status);
        Assert.Equal(AcceleratorUsageState.Uncertain, Assert.Single(fixture.Db
            .Set<AcceleratorUsageReservation>().Where(x =>
                x.Feature == AcceleratorFeature.EmployerOutreach)).State);
        await Assert.ThrowsAsync<ConflictException>(() => service.SendAsync(fixture.Candidate.Id,
            draft.Id, mailbox.Id, "candidate-click-1", default));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task AcceleratorPhase7ConfirmedNonChargeableFailureReleasesQuota()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var provider = new FakeMailboxProvider
        { Outcome = MailboxDispatchOutcome.ConfirmedNonChargeableFailure };
        var (service, draft, mailbox) = SeedOutreach(fixture, provider);
        var result = await service.SendAsync(fixture.Candidate.Id, draft.Id, mailbox.Id,
            "candidate-click-1", default);
        Assert.Equal(TalentOutreachStatus.Failed, result.Status);
        Assert.Equal(AcceleratorUsageState.Released, Assert.Single(fixture.Db
            .Set<AcceleratorUsageReservation>().Where(x =>
                x.Feature == AcceleratorFeature.EmployerOutreach)).State);
        Assert.Equal("provider-confirmed-no-charge", draft.FailureEvidenceReference);
    }

    [Fact]
    public async Task AcceleratorPhase7OutreachUsesDedicatedSubscriptionAllowance()
    {
        using var fixture = new AcceleratorFixture();
        fixture.Settings.Allowances[AcceleratorFeature.EmployerOutreach] = 1;
        await fixture.Activate();
        var first = await fixture.Quota.ReserveOutreachAsync(fixture.Candidate.Id, Guid.NewGuid(),
            new string('A', 64), default);
        Assert.Equal(AcceleratorFeature.EmployerOutreach, first.Feature);
        var error = await Assert.ThrowsAsync<AppException>(() => fixture.Quota.ReserveOutreachAsync(
            fixture.Candidate.Id, Guid.NewGuid(), new string('B', 64), default));
        Assert.Equal(429, error.StatusCode);
    }

    [Fact]
    public async Task AcceleratorPhase7RestartRecoveryFinalizesSentOutreachExactlyOnce()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var (service, draft, _) = SeedOutreach(fixture, new FakeMailboxProvider());
        var reservation = await fixture.Quota.ReserveOutreachAsync(fixture.Candidate.Id,
            Guid.NewGuid(), new string('A', 64), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, reservation.Id, default);
        draft.Status = TalentOutreachStatus.Sent;
        draft.UsageReservationId = reservation.Id;
        draft.ProviderMessageId = "provider-message-1";
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(1, await service.ReconcileOutreachAsync(10, default));
        Assert.Equal(0, await service.ReconcileOutreachAsync(10, default));
        Assert.Equal(AcceleratorUsageState.Completed, fixture.Db.Set<AcceleratorUsageReservation>()
            .Single(x => x.Id == reservation.Id).State);
    }

    [Fact]
    public async Task AcceleratorPhase7StaleDispatchBecomesUnknownWithoutProviderRetry()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var provider = new FakeMailboxProvider();
        var (service, draft, _) = SeedOutreach(fixture, provider);
        var reservation = await fixture.Quota.ReserveOutreachAsync(fixture.Candidate.Id,
            Guid.NewGuid(), new string('A', 64), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, reservation.Id, default);
        draft.Status = TalentOutreachStatus.Dispatching;
        draft.UsageReservationId = reservation.Id;
        draft.DispatchStartedAtUtc = fixture.Clock.Utc.AddMinutes(-20);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(1, await service.ReconcileOutreachAsync(10, default));
        Assert.Equal(TalentOutreachStatus.Unknown, draft.Status);
        Assert.Equal(AcceleratorUsageState.Uncertain, fixture.Db.Set<AcceleratorUsageReservation>()
            .Single(x => x.Id == reservation.Id).State);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task AcceleratorPhase7VerifiedFailureRecoveryReleasesOnlyWithEvidence()
    {
        using var fixture = new AcceleratorFixture();
        await fixture.Activate();
        var (service, draft, _) = SeedOutreach(fixture, new FakeMailboxProvider());
        var reservation = await fixture.Quota.ReserveOutreachAsync(fixture.Candidate.Id,
            Guid.NewGuid(), new string('A', 64), default);
        await fixture.Quota.MarkDispatchedAsync(fixture.Candidate.Id, reservation.Id, default);
        draft.Status = TalentOutreachStatus.Failed;
        draft.UsageReservationId = reservation.Id;
        draft.SendIdempotencyKey = "provider-request-1";
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(0, await service.ReconcileOutreachAsync(10, default));
        Assert.Equal(AcceleratorUsageState.Dispatched, fixture.Db.Set<AcceleratorUsageReservation>()
            .Single(x => x.Id == reservation.Id).State);
        draft.FailureEvidenceReference = "provider-confirmed-no-charge";
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(1, await service.ReconcileOutreachAsync(10, default));
        Assert.Equal(AcceleratorUsageState.Released, fixture.Db.Set<AcceleratorUsageReservation>()
            .Single(x => x.Id == reservation.Id).State);
    }

    private static (AcceleratorTalentService Service, TalentOutreachDraft Draft,
        CandidateMailboxConnection Mailbox) SeedOutreach(AcceleratorFixture fixture,
        FakeMailboxProvider provider)
    {
        var company = new Company { Name = "Example", OwnerUserId = fixture.Admin.Id,
            OwnerUser = fixture.Admin };
        var job = new Job { Company = company, CompanyId = company.Id, Title = "Backend Engineer",
            Status = JobStatus.Published, PublishedAtUtc = fixture.Clock.Utc.AddDays(-1),
            ExpiresAtUtc = fixture.Clock.Utc.AddDays(5) };
        var contact = new EmployerContact { CompanyId = company.Id, FullName = "Recruiter",
            Title = "Technical Recruiter", ProfessionalEmail = "recruiter@example.com",
            NormalizedEmail = "RECRUITER@EXAMPLE.COM",
            Verification = EmployerContactVerification.Verified };
        var mailbox = new CandidateMailboxConnection { CandidateId = fixture.Candidate.Id,
            Provider = provider.Name, EmailAddress = "candidate@example.com",
            TokenReference = "test-only-token-ref", ConnectedAtUtc = fixture.Clock.Utc };
        var draft = new TalentOutreachDraft { CandidateId = fixture.Candidate.Id,
            JobId = job.Id, CompanyId = company.Id, Subject = "Application",
            Body = "I am interested in this opportunity." };
        var recipient = new TalentOutreachRecipient { DraftId = draft.Id, ContactId = contact.Id,
            EmailSnapshot = contact.ProfessionalEmail };
        fixture.Db.AddRange(company, job, contact, mailbox, draft, recipient);
        fixture.Db.SaveChanges();
        var options = new TalentOutreachOptions { EmployerOutreachEnabled = true,
            MaxSendsPerDay = 5, MaxSendsPerMonth = 30 };
        var service = new AcceleratorTalentService(fixture.Db, null!, fixture.Audit,
            fixture.Clock, Options.Create(options), [provider], fixture.Quota);
        return (service, draft, mailbox);
    }

    private sealed class FakeMailboxProvider : ICandidateMailboxProvider
    {
        public string Name => "Fake";
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public MailboxDispatchOutcome Outcome { get; set; } = MailboxDispatchOutcome.Sent;
        public Task<MailboxDispatchResult> SendAsync(string tokenReference, string idempotencyKey,
            IReadOnlyList<string> recipients, string subject, string body,
            CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("test-only-token-ref", tokenReference);
            Assert.Single(recipients);
            return Task.FromResult(new MailboxDispatchResult(Outcome,
                Outcome == MailboxDispatchOutcome.Sent ? "fake-message-1" : null,
                Outcome == MailboxDispatchOutcome.ConfirmedNonChargeableFailure ?
                    "provider-confirmed-no-charge" : null));
        }
    }

    private static User User(JobPortalDbContext db, string roleName)
    {
        var role = new Role { Name = roleName, NormalizedName = roleName.ToUpperInvariant() };
        var user = new User { Role = role, RoleId = role.Id, Email = Guid.NewGuid() + "@example.com",
            Status = UserStatus.Active };
        db.AddRange(role, user);
        return user;
    }

    private static AcceleratorTalentService AdminService(JobPortalDbContext db, RecordingAudit audit) =>
        new(db, null!, audit, TimeProvider.System, Options.Create(new TalentOutreachOptions()), [], null!);

    private sealed class RecordingAudit : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        { Events.Add(auditEvent); return Task.CompletedTask; }
    }
}
