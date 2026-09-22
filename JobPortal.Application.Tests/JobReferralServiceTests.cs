using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Referrals;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobReferralServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SubmitComposesJobAndCreatesPendingReferral()
    {
        var fixture = CreateFixture();

        var response = await fixture.Service.SubmitAsync(fixture.ReferrerUserId, new SubmitJobReferralRequest(
            new ComposeJobRequest(new ComposeJobDraftRequest("Frontend Developer")),
            "https://example.test/jobs/frontend-developer",
            ShowLinkedIn: true,
            ShowEmail: false,
            ShowPhone: false));

        Assert.Equal(JobReferralApprovalStatus.Pending, response.ApprovalStatus);
        Assert.True(response.ShowLinkedIn);
        Assert.Equal(fixture.ComposedJobId, response.JobId);
        Assert.Equal(JobStatus.Draft, fixture.Jobs.Job!.Status);
        Assert.Contains(fixture.Audit.Events, e => e.Action == AuditAction.Submit && e.EntityType == "JobReferral");
    }

    [Fact]
    public async Task SubmitRejectsWhenNoContactFieldChosen()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.SubmitAsync(
            fixture.ReferrerUserId,
            new SubmitJobReferralRequest(
                new ComposeJobRequest(new ComposeJobDraftRequest("Backend Developer")),
                null, ShowLinkedIn: false, ShowEmail: false, ShowPhone: false)));
    }

    [Fact]
    public async Task ReviewApprovingPublishesJobAndRejectingRequiresReason()
    {
        var fixture = CreateFixture();
        var submitted = await fixture.Service.SubmitAsync(fixture.ReferrerUserId, new SubmitJobReferralRequest(
            new ComposeJobRequest(new ComposeJobDraftRequest("QA Engineer")),
            null, ShowLinkedIn: true, ShowEmail: false, ShowPhone: false));

        // Rejecting without a reason is invalid.
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.ReviewAsync(
            submitted.Id, fixture.AdminUserId,
            new ReviewJobReferralRequest(JobReferralApprovalStatus.Rejected, null)));

        var approved = await fixture.Service.ReviewAsync(
            submitted.Id, fixture.AdminUserId,
            new ReviewJobReferralRequest(JobReferralApprovalStatus.Approved, null));

        Assert.Equal(JobReferralApprovalStatus.Approved, approved.ApprovalStatus);
        Assert.Equal(JobStatus.Published, fixture.Jobs.Job!.Status);
        Assert.Equal(Now, fixture.Jobs.Job!.PublishedAtUtc);

        // Already-reviewed referrals can't be reviewed again.
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.ReviewAsync(
            submitted.Id, fixture.AdminUserId,
            new ReviewJobReferralRequest(JobReferralApprovalStatus.Approved, null)));
    }

    [Fact]
    public async Task UnlockRequiresLoginThenActiveMembershipBeforeGrantingOnlyPermittedFields()
    {
        var fixture = CreateFixture();
        var submitted = await fixture.Service.SubmitAsync(fixture.ReferrerUserId, new SubmitJobReferralRequest(
            new ComposeJobRequest(new ComposeJobDraftRequest("Data Analyst")),
            null, ShowLinkedIn: true, ShowEmail: false, ShowPhone: true));
        await fixture.Service.ReviewAsync(
            submitted.Id, fixture.AdminUserId, new ReviewJobReferralRequest(JobReferralApprovalStatus.Approved, null));

        var anonymous = await fixture.Service.UnlockContactAsync(null, fixture.ComposedJobId);
        Assert.Equal(ReferralUnlockStatus.LoginRequired, anonymous.Status);

        var seekerId = Guid.NewGuid();
        fixture.Memberships.ActiveMembershipUserId = null;
        var noMembership = await fixture.Service.UnlockContactAsync(seekerId, fixture.ComposedJobId);
        Assert.Equal(ReferralUnlockStatus.MembershipRequired, noMembership.Status);

        fixture.Memberships.ActiveMembershipUserId = seekerId;
        var granted = await fixture.Service.UnlockContactAsync(seekerId, fixture.ComposedJobId);
        Assert.Equal(ReferralUnlockStatus.Granted, granted.Status);
        Assert.NotNull(granted.Contact);
        Assert.Equal(fixture.Referrer.LinkedInUrl, granted.Contact!.LinkedInUrl);
        Assert.Null(granted.Contact.Email); // ShowEmail was false
        Assert.Equal(fixture.Referrer.PhoneNumber, granted.Contact.PhoneNumber);
    }

    [Fact]
    public async Task UnlockThrowsWhenJobHasNoApprovedReferral()
    {
        var fixture = CreateFixture();
        await fixture.Service.SubmitAsync(fixture.ReferrerUserId, new SubmitJobReferralRequest(
            new ComposeJobRequest(new ComposeJobDraftRequest("Unreviewed Role")),
            null, ShowLinkedIn: true, ShowEmail: false, ShowPhone: false));

        // Still pending, not approved.
        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.UnlockContactAsync(Guid.NewGuid(), fixture.ComposedJobId));
    }

    private static Fixture CreateFixture()
    {
        var referrerUserId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var composedJobId = Guid.NewGuid();

        var referrer = new User
        {
            Id = referrerUserId,
            Email = "referrer@example.test",
            NormalizedEmail = "REFERRER@EXAMPLE.TEST",
            PasswordHash = "not-used",
            FirstName = "Riya",
            LastName = "Referrer",
            RoleId = SystemRoleIds.Candidate,
            Status = UserStatus.Active,
            EmailConfirmed = true,
            LinkedInUrl = "https://linkedin.com/in/riya",
            PhoneNumber = "+91-9999900000",
        };

        var company = new Company { Id = Guid.NewGuid(), Name = "Acme", Slug = "acme", OwnerUserId = adminUserId };
        var category = new Category { Id = Guid.NewGuid(), Name = "Engineering", Slug = "engineering" };
        var job = new Job
        {
            Id = composedJobId,
            ReferenceNumber = "JOB-REF-1",
            Title = "Placeholder",
            Slug = "placeholder",
            Description = "Placeholder",
            ApplicationUrl = "https://example.test/apply",
            CompanyId = company.Id,
            Company = company,
            CategoryId = category.Id,
            Category = category,
            CurrencyCode = "INR",
            EmploymentType = EmploymentType.FullTime,
            WorkplaceType = WorkplaceType.Remote,
            ExperienceLevel = ExperienceLevel.Mid,
            Status = JobStatus.Draft,
        };

        var jobRepository = new JobRepositoryFake { Job = job };
        var jobService = new JobServiceFake(composedJobId);
        var referralRepository = new JobReferralRepositoryFake(referrer);
        var memberships = new MembershipRepositoryFake();
        var audit = new AuditWriterTestDouble();
        var unitOfWork = new UnitOfWorkFake();

        var service = new JobReferralService(
            referralRepository, jobRepository, jobService, memberships, audit, unitOfWork,
            new FixedTimeProvider(Now));

        return new Fixture(
            service, jobRepository, referralRepository, memberships, audit,
            referrerUserId, adminUserId, composedJobId, referrer);
    }

    private sealed record Fixture(
        JobReferralService Service,
        JobRepositoryFake Jobs,
        JobReferralRepositoryFake Referrals,
        MembershipRepositoryFake Memberships,
        AuditWriterTestDouble Audit,
        Guid ReferrerUserId,
        Guid AdminUserId,
        Guid ComposedJobId,
        User Referrer);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    /// <summary>Only ComposeAsync is exercised by JobReferralService; every other member of the
    /// (large) IJobService interface is intentionally unimplemented for this fake.</summary>
    private sealed class JobServiceFake(Guid composedJobId) : IJobService
    {
        public Task<ComposeJobResponse> ComposeAsync(
            Guid administratorUserId, ComposeJobRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ComposeJobResponse(composedJobId, "placeholder", JobStatus.Draft,
                new ComposedRelationResponse(Guid.NewGuid(), "Acme", true),
                new ComposedRelationResponse(Guid.NewGuid(), "Engineering", true)));

        public Task<JobResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> PublishAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> UnpublishAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> CloseAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> SetFeaturedAsync(Guid id, bool isFeatured, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobResponse> SetHiddenAsync(Guid id, bool isHidden, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<JobPortal.Shared.Models.PagedResponse<JobResponse>> SearchAsync(
            JobSearchQuery query, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<AdminRecruiterContactResponse> GetRecruiterContactAsync(
            Guid jobId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<AdminRecruiterContactResponse> UpdateRecruiterContactAsync(
            Guid jobId, UpdateRecruiterContactRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class JobRepositoryFake : IJobRepository
    {
        public Job? Job { get; init; }

        public Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Job?.Id == id ? Job : null);
        public Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(
            JobSearchQuery query, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<int> ExpireOverduePublishedAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
        public Task AddAsync(Job job, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Job job) { }
        public void Remove(Job job) => job.IsDeleted = true;
        public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);
        public Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);
        public Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(Guid companyId, string title, string location, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Job>>(Array.Empty<Job>());
    }

    private sealed class MembershipRepositoryFake : IMembershipRepository
    {
        private const string ReferralContactPlanCode = "ReferralContactAccess";

        public Guid? ActiveMembershipUserId { get; set; }

        public Task<Membership?> GetActiveForUserAsync(
            Guid userId,
            string planCode,
            CancellationToken cancellationToken = default)
        {
            var hasReferralMembership =
                userId == ActiveMembershipUserId &&
                string.Equals(
                    planCode,
                    ReferralContactPlanCode,
                    StringComparison.OrdinalIgnoreCase);

            return Task.FromResult(
                hasReferralMembership
                    ? new Membership
                    {
                        UserId = userId,
                        PlanCode = ReferralContactPlanCode,
                        PlanName = "Referral Contact Access",
                        Status = MembershipStatus.Active,
                        StartsAtUtc = Now
                    }
                    : null);
        }

        public Task<Membership?> GetMembershipForUserAndPlanAsync(
            Guid userId,
            string planCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Membership?>(null);

        public Task<AvailableJobAccess?> GetAvailableJobAsync(
            string slug,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Membership?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task AddAsync(
            Membership membership,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(IReadOnlyCollection<MembershipHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
            Guid userId,
            HistoryQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task RecordApplicationAsync(
            Guid userId,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
    /// <summary>Minimal in-memory store — one referral at a time is all these tests need,
    /// keyed loosely since each test creates its own fixture/job.</summary>
    private sealed class JobReferralRepositoryFake(User referrer) : IJobReferralRepository
    {
        private readonly List<JobReferral> _referrals = [];

        public Task AddAsync(JobReferral referral, CancellationToken cancellationToken = default)
        {
            referral.ReferrerUser = referrer;
            _referrals.Add(referral);
            return Task.CompletedTask;
        }

        public Task<JobReferral?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_referrals.SingleOrDefault(x => x.Id == id));

        public Task<JobReferral?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_referrals.SingleOrDefault(x => x.JobId == jobId));

        public Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetPendingAsync(
            int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(IReadOnlyCollection<JobReferral> Items, int TotalCount)> GetApprovedAsync(
            int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyCollection<JobReferral>> GetByReferrerAsync(
            Guid referrerUserId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task RecordUnlockAsync(Guid jobReferralId, Guid seekerUserId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
