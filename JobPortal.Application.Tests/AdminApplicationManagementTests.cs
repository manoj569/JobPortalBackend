using System.Text.Json;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Application.Features.Candidates;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AdminApplicationManagementTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 10, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(JobApplicationStatus.Submitted, JobApplicationStatus.Reviewed)]
    [InlineData(JobApplicationStatus.Submitted, JobApplicationStatus.Shortlisted)]
    [InlineData(JobApplicationStatus.Submitted, JobApplicationStatus.Rejected)]
    [InlineData(JobApplicationStatus.Reviewed, JobApplicationStatus.Shortlisted)]
    [InlineData(JobApplicationStatus.Reviewed, JobApplicationStatus.Rejected)]
    public async Task AllowedTransitionsCreateAdministratorHistory(
        JobApplicationStatus current, JobApplicationStatus requested)
    {
        var fixture = CreateFixture(current);

        var response = await fixture.Service.UpdateStatusAsync(
            fixture.Administrator.Id,
            fixture.Application.Id,
            new(requested, "  Private hiring note  "));

        Assert.Equal(requested, fixture.Application.Status);
        Assert.Equal(requested, response.Status);
        var history = Assert.Single(fixture.Application.StatusHistory);
        Assert.Equal(current, history.PreviousStatus);
        Assert.Equal(requested, history.NewStatus);
        Assert.Equal(fixture.Administrator.Id, history.ActorUserId);
        Assert.Equal(Now, history.ChangedAtUtc);
        Assert.Equal("Private hiring note", history.InternalNote);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        var audit = Assert.Single(fixture.Audit.Events);
        Assert.Equal(AuditAction.Update, audit.Action);
        Assert.DoesNotContain(
            "Private hiring note",
            JsonSerializer.Serialize(audit),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobApplicationStatus.Shortlisted, JobApplicationStatus.Rejected)]
    [InlineData(JobApplicationStatus.Rejected, JobApplicationStatus.Shortlisted)]
    [InlineData(JobApplicationStatus.Withdrawn, JobApplicationStatus.Reviewed)]
    [InlineData(JobApplicationStatus.Reviewed, JobApplicationStatus.Reviewed)]
    public async Task FinalWithdrawnAndUnsupportedTransitionsAreRejected(
        JobApplicationStatus current, JobApplicationStatus requested)
    {
        var fixture = CreateFixture(current);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.UpdateStatusAsync(
                fixture.Administrator.Id,
                fixture.Application.Id,
                new(requested, null)));

        Assert.Equal(current, fixture.Application.Status);
        Assert.Empty(fixture.Application.StatusHistory);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task NonAdministratorCannotChangeStatus()
    {
        var fixture = CreateFixture(JobApplicationStatus.Submitted);
        fixture.Users.User = new User
        {
            Id = fixture.Administrator.Id,
            RoleId = SystemRoleIds.Candidate,
            Status = UserStatus.Active,
            EmailConfirmed = true
        };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.UpdateStatusAsync(
                fixture.Administrator.Id,
                fixture.Application.Id,
                new(JobApplicationStatus.Reviewed, null)));

        Assert.Equal(JobApplicationStatus.Submitted, fixture.Application.Status);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(JobApplicationStatus.Shortlisted)]
    [InlineData(JobApplicationStatus.Rejected)]
    public async Task NotificationFailureDoesNotUndoSavedStatus(
        JobApplicationStatus requested)
    {
        var fixture = CreateFixture(JobApplicationStatus.Submitted);
        fixture.Email.Result = EmailDeliveryResult.Failed;

        var response = await fixture.Service.UpdateStatusAsync(
            fixture.Administrator.Id,
            fixture.Application.Id,
            new(requested, "Must never appear in candidate email."));

        Assert.Equal(requested, response.Status);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(1, fixture.Email.CallCount);
        Assert.Equal(1, fixture.Email.SaveCountAtSend);
        Assert.Equal(requested, fixture.Email.Status);
        Assert.Equal("Platform Engineer", fixture.Email.JobTitle);
    }

    [Fact]
    public async Task ReviewedDoesNotSendCandidateNotification()
    {
        var fixture = CreateFixture(JobApplicationStatus.Submitted);

        await fixture.Service.UpdateStatusAsync(
            fixture.Administrator.Id,
            fixture.Application.Id,
            new(JobApplicationStatus.Reviewed, null));

        Assert.Equal(0, fixture.Email.CallCount);
    }

    [Fact]
    public async Task DetailContainsOnlyReviewDataAndKeepsStorageKeyPrivate()
    {
        var fixture = CreateFixture(JobApplicationStatus.Reviewed);
        fixture.Application.ResumeStorageKey = "private/opaque-storage-key.pdf";
        fixture.Application.ResumeFileName = "../../candidate-secret-name.pdf";
        fixture.Application.User.PhoneNumber = "+91-secret";
        fixture.Application.User.PasswordHash = "secret-password-hash";
        fixture.Application.StatusHistory.Add(new JobApplicationStatusHistory
        {
            PreviousStatus = JobApplicationStatus.Submitted,
            NewStatus = JobApplicationStatus.Reviewed,
            ChangedAtUtc = Now,
            ActorUserId = fixture.Administrator.Id,
            ActorUser = fixture.Administrator,
            InternalNote = "Admin only"
        });

        var detail = await fixture.Service.GetAsync(fixture.Application.Id);
        var json = JsonSerializer.Serialize(detail);

        Assert.Equal("resume.pdf", detail.ResumeFileName);
        Assert.Equal("Admin only", Assert.Single(detail.StatusHistory).InternalNote);
        Assert.DoesNotContain("opaque-storage-key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate-secret-name", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password-hash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+91-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeStorageKey", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateApplicationContractCannotExposeAdministratorNotesOrHistory()
    {
        var properties = typeof(JobApplicationResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("InternalNote", properties);
        Assert.DoesNotContain("StatusHistory", properties);
    }

    [Fact]
    public async Task ResumeDownloadUsesOpaqueSnapshotAndSafeDownloadName()
    {
        var fixture = CreateFixture(JobApplicationStatus.Submitted);
        fixture.Application.ResumeStorageKey = "opaque/2a49e";
        fixture.Application.ResumeFileName = "../../private-original.docx";
        fixture.Storage.Content = new MemoryStream([1, 2, 3]);

        var download = await fixture.Service.DownloadResumeAsync(fixture.Application.Id);

        Assert.Equal("opaque/2a49e", fixture.Storage.OpenedKey);
        Assert.Equal("resume.docx", download.FileName);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            download.ContentType);
        Assert.DoesNotContain("opaque", download.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain("private-original", download.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingOrUnsafeResumeIsNotDownloadable()
    {
        var fixture = CreateFixture(JobApplicationStatus.Submitted);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.DownloadResumeAsync(fixture.Application.Id));

        fixture.Application.ResumeStorageKey = "opaque";
        fixture.Application.ResumeFileName = "resume.exe";
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.DownloadResumeAsync(fixture.Application.Id));
        Assert.Null(fixture.Storage.OpenedKey);
    }

    [Fact]
    public async Task ListValidatesAndPreservesPaginationAndFilters()
    {
        var fixture = CreateFixture(JobApplicationStatus.Submitted);
        await Assert.ThrowsAnyAsync<FluentValidation.ValidationException>(() =>
            fixture.Service.SearchAsync(new(PageNumber: 0)));

        var query = new AdminApplicationQuery(
            2, 10, fixture.Application.JobId, fixture.Application.Job.CompanyId,
            fixture.Application.Job.CategoryId, JobApplicationStatus.Submitted,
            Now.AddDays(-1), Now.AddDays(1), "Casey");
        fixture.Repository.Items =
        [
            new(
                fixture.Application.Id, JobApplicationStatus.Submitted, Now,
                fixture.Application.UserId, "Casey Candidate",
                fixture.Application.User.Email, fixture.Application.JobId,
                fixture.Application.Job.Title, fixture.Application.Job.Slug,
                fixture.Application.Job.CompanyId, fixture.Application.Job.Company.Name,
                fixture.Application.Job.CategoryId, fixture.Application.Job.Category.Name,
                false)
        ];
        fixture.Repository.TotalCount = 14;

        var response = await fixture.Service.SearchAsync(query);

        Assert.Same(query, fixture.Repository.LastQuery);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(14, response.TotalCount);
        Assert.Single(response.Items);
    }

    private static Fixture CreateFixture(JobApplicationStatus status)
    {
        var administrator = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.test",
            FirstName = "Ada",
            LastName = "Admin",
            RoleId = SystemRoleIds.Administrator,
            Status = UserStatus.Active,
            EmailConfirmed = true
        };
        var candidate = new User
        {
            Id = Guid.NewGuid(),
            Email = "casey@example.test",
            FirstName = "Casey",
            LastName = "Candidate",
            RoleId = SystemRoleIds.Candidate,
            Status = UserStatus.Active,
            EmailConfirmed = true,
            SkillsJson = "[\"C#\"]",
            EducationJson = "[\"BSc\"]",
            ExperienceJson = "[\"Engineer\"]",
            PreferredJobTypesJson = "[1]"
        };
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Slug = "acme",
            OwnerUserId = administrator.Id,
            OwnerUser = administrator
        };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Engineering",
            Slug = "engineering"
        };
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Platform Engineer",
            Slug = "platform-engineer",
            ReferenceNumber = "JOB-42",
            CompanyId = company.Id,
            Company = company,
            CategoryId = category.Id,
            Category = category,
            EmploymentType = EmploymentType.FullTime,
            WorkplaceType = WorkplaceType.Remote
        };
        var application = new JobApplication
        {
            Id = Guid.NewGuid(),
            UserId = candidate.Id,
            User = candidate,
            JobId = job.Id,
            Job = job,
            Status = status,
            CoverLetter = "Please consider my application.",
            SubmittedAtUtc = Now.AddHours(-1)
        };
        var repository = new AdminApplicationRepositoryFake { Application = application };
        var users = new UserRepositoryFake { User = administrator };
        var storage = new ResumeStorageFake();
        var unitOfWork = new CountingUnitOfWork();
        var email = new StatusEmailFake(unitOfWork);
        var audit = new AuditWriterTestDouble();
        var service = new AdminApplicationService(
            repository,
            users,
            storage,
            email,
            unitOfWork,
            audit,
            new AdminApplicationQueryValidator(),
            new UpdateAdminApplicationStatusRequestValidator(),
            new FixedTimeProvider(Now));
        return new(
            service, repository, users, storage, unitOfWork,
            email, audit, administrator, application);
    }

    private sealed record Fixture(
        AdminApplicationService Service,
        AdminApplicationRepositoryFake Repository,
        UserRepositoryFake Users,
        ResumeStorageFake Storage,
        CountingUnitOfWork UnitOfWork,
        StatusEmailFake Email,
        AuditWriterTestDouble Audit,
        User Administrator,
        JobApplication Application);

    private sealed class AdminApplicationRepositoryFake : IAdminApplicationRepository
    {
        public JobApplication? Application { get; init; }
        public IReadOnlyCollection<AdminApplicationListItem> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public AdminApplicationQuery? LastQuery { get; private set; }

        public Task<(IReadOnlyCollection<AdminApplicationListItem> Items, int TotalCount)>
            SearchAsync(
                AdminApplicationQuery query,
                CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult((Items, TotalCount));
        }

        public Task<JobApplication?> GetByIdAsync(
            Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Application?.Id == applicationId ? Application : null);
    }

    private sealed class UserRepositoryFake : IUserRepository
    {
        public User? User { get; set; }

        public Task<User?> GetByNormalizedEmailAsync(
            string normalizedEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<User?> GetByNormalizedPhoneAsync(
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);

        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail, string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<User?> GetByIdWithRoleAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(User?.Id == userId ? User : null);

        public Task AddAsync(
            User user, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Update(User user)
        {
        }
    }

    private sealed class ResumeStorageFake : IResumeStorage
    {
        public Stream? Content { get; set; }
        public string? OpenedKey { get; private set; }

        public Task<string> StoreAsync(
            Stream content, string extension,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(
            string storageKey, CancellationToken cancellationToken = default)
        {
            OpenedKey = storageKey;
            return Task.FromResult(Content);
        }

        public Task DeleteAsync(
            string storageKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class StatusEmailFake(CountingUnitOfWork unitOfWork) : IEmailService
    {
        public EmailDeliveryResult Result { get; set; } = EmailDeliveryResult.Sent;
        public int CallCount { get; private set; }
        public int SaveCountAtSend { get; private set; }
        public string? JobTitle { get; private set; }
        public JobApplicationStatus? Status { get; private set; }

        public Task<EmailDeliveryResult> SendPasswordResetAsync(
            User user,
            string rawToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EmailDeliveryResult> SendApplicationStatusAsync(
            User user, string jobTitle, JobApplicationStatus status,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            SaveCountAtSend = unitOfWork.SaveCount;
            JobTitle = jobTitle;
            Status = status;
            return Task.FromResult(Result);
        }
        public Task<EmailDeliveryResult> SendRegistrationVerificationAsync(
            User user, string rawToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed class AdminApplicationRepositoryTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SearchCombinesAllFiltersWithoutLeakingPrivateApplicationData()
    {
        await using var context = CreateContext();
        var seed = Seed(context);
        await context.SaveChangesAsync();
        var repository = new AdminApplicationRepository(context);

        var (items, totalCount) = await repository.SearchAsync(new(
            1,
            20,
            seed.Match.JobId,
            seed.Match.Job.CompanyId,
            seed.Match.Job.CategoryId,
            JobApplicationStatus.Shortlisted,
            Now.AddDays(-2),
            Now,
            "Casey"));

        var item = Assert.Single(items);
        Assert.Equal(1, totalCount);
        Assert.Equal(seed.Match.Id, item.Id);
        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("ResumeStorageKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CoverLetter", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchReturnsDeterministicRequestedPage()
    {
        await using var context = CreateContext();
        var seed = Seed(context);
        await context.SaveChangesAsync();
        var repository = new AdminApplicationRepository(context);

        var (items, totalCount) = await repository.SearchAsync(
            new(2, 1));

        Assert.Equal(2, totalCount);
        Assert.Equal(seed.Older.Id, Assert.Single(items).Id);
    }

    private static (JobApplication Match, JobApplication Older) Seed(
        JobPortalDbContext context)
    {
        var ownerId = Guid.NewGuid();
        var company = new Company
        {
            Name = "Acme Systems",
            Slug = "acme-systems",
            OwnerUserId = ownerId
        };
        var otherCompany = new Company
        {
            Name = "Other Co",
            Slug = "other-co",
            OwnerUserId = ownerId
        };
        var category = new Category { Name = "Engineering", Slug = "engineering" };
        var otherCategory = new Category { Name = "Sales", Slug = "sales" };
        var matchingJob = NewJob(company, category, "Platform Engineer", "JOB-42");
        var olderJob = NewJob(otherCompany, otherCategory, "Account Manager", "JOB-7");
        var casey = NewCandidate("Casey", "Patel", "casey@example.test");
        var alex = NewCandidate("Alex", "Singh", "alex@example.test");
        var match = NewApplication(
            casey, matchingJob, JobApplicationStatus.Shortlisted, Now.AddHours(-1));
        match.ResumeStorageKey = "must-not-be-projected";
        match.CoverLetter = "private";
        var older = NewApplication(
            alex, olderJob, JobApplicationStatus.Rejected, Now.AddDays(-3));
        context.AddRange(
            company, otherCompany, category, otherCategory,
            matchingJob, olderJob, casey, alex, match, older);
        return (match, older);
    }

    private static Job NewJob(
        Company company, Category category, string title, string reference) => new()
        {
            Title = title,
            Slug = title.ToLowerInvariant().Replace(' ', '-'),
            ReferenceNumber = reference,
            Description = "Description",
            ApplicationUrl = "https://example.test/apply",
            CompanyId = company.Id,
            Company = company,
            CategoryId = category.Id,
            Category = category,
            Status = JobStatus.Published
        };

    private static User NewCandidate(
        string firstName, string lastName, string email) => new()
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used",
            RoleId = SystemRoleIds.Candidate,
            Status = UserStatus.Active,
            EmailConfirmed = true
        };

    private static JobApplication NewApplication(
        User candidate, Job job, JobApplicationStatus status, DateTime submittedAtUtc) =>
        new()
        {
            UserId = candidate.Id,
            User = candidate,
            JobId = job.Id,
            Job = job,
            Status = status,
            SubmittedAtUtc = submittedAtUtc
        };

    private static JobPortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<JobPortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

public sealed class AdminApplicationApiContractTests
{
    [Fact]
    public void EveryEndpointRequiresExactAdministratorRoleAndResumeIsNotPublic()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            root, "JobPortal.API", "Controllers", "AdminApplicationsController.cs"));

        Assert.Contains(
            "[Authorize(Roles = \"Administrator\")]",
            controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[AllowAnonymous]", controller, StringComparison.Ordinal);
        Assert.Contains(
            "[HttpGet(\"{applicationId:guid}/resume\")]",
            controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("storageKey", controller, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resumeUrl", controller, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "JobPortal.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
