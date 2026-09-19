using FluentValidation;
using JobPortal.Application.Abstractions.Authentication;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Authentication;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;
using static JobPortal.Application.Features.Jobs.JobSearchQueryValidator;

namespace JobPortal.Application.Tests;

public sealed class AdminManagementTests
{
    [Fact]
    public async Task DeletedOrInvalidCompanyAndCategoryCannotBeAssignedToJob()
    {
        var repository = new JobRepositoryFake();
        var service = new JobService(repository, new UnitOfWorkFake(), new AuditWriterTestDouble(), new CreateJobRequestValidator(),
new UpdateJobRequestValidator(),
new UpdateRecruiterContactRequestValidator(),
new JobSearchQueryValidator(), TimeProvider.System);
        var request = ValidJobRequest();

        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(request));
        repository.CompanyExists = true;
        await Assert.ThrowsAsync<BadRequestException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CompanyCrudUsesAdministratorOwnershipAndSafeDeletion()
    {
        var repository = new CompanyRepositoryFake();
        var service = CompanyService(repository);
        var administratorId = Guid.NewGuid();
        var created = await service.CreateAsync(administratorId,
            new("Acme Ltd", null, null, "https://acme.test", null, "Tech", "Pune", 10, true));

        Assert.Equal("acme-ltd", created.Slug);
        Assert.Equal(administratorId, repository.Entity!.OwnerUserId);
        var updated = await service.UpdateAsync(created.Id,
            new("Acme Global", null, "Updated", null, null, null, null, 20, false));
        Assert.Equal("acme-global", updated.Slug);
        Assert.Single(await service.GetOptionsAsync());

        repository.HasJobs = true;
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task CompanyDuplicateSlugAndInvalidValuesAreRejected()
    {
        var repository = new CompanyRepositoryFake { DuplicateSlug = true };
        var service = CompanyService(repository);
        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Guid.NewGuid(),
            new("Acme", "acme", null, null, null, null, null, null, false)));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Guid.NewGuid(),
            new("Bad", null, null, "javascript:alert(1)", null, null, null, -1, false)));
    }

    [Fact]
    public async Task CategoryParentCycleAndReferencedDeletionAreRejected()
    {
        var repository = new CategoryRepositoryFake();
        var service = CategoryService(repository);
        var created = await service.CreateAsync(new("Engineering", null, null, 1, null));
        repository.ParentExists = true;
        repository.IsDescendant = true;

        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(created.Id,
            new("Engineering", null, null, 1, Guid.NewGuid())));
        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateAsync(created.Id,
            new("Engineering", null, null, 1, created.Id)));
        repository.HasReferences = true;
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(created.Id));
        Assert.Single(await service.GetOptionsAsync());
    }

    private static CompanyManagementService CompanyService(CompanyRepositoryFake repository) =>
        new(repository, new UnitOfWorkFake(), new AuditWriterTestDouble(), new CreateCompanyRequestValidator(),
            new UpdateCompanyRequestValidator(), new CompanySearchQueryValidator());

    private static CategoryManagementService CategoryService(CategoryRepositoryFake repository) =>
        new(repository, new UnitOfWorkFake(), new AuditWriterTestDouble(), new CreateCategoryRequestValidator(),
            new UpdateCategoryRequestValidator(), new CategorySearchQueryValidator());

    private static CreateJobRequest ValidJobRequest() => new(
        "Developer", "Description", Guid.NewGuid(), Guid.NewGuid(), "https://apply.test",
        null, null, null, null, null, null, "INR", EmploymentType.FullTime,
        WorkplaceType.Remote, ExperienceLevel.Mid, DateTime.UtcNow.AddDays(10));

    private sealed class CompanyRepositoryFake : ICompanyManagementRepository
    {
        public Company? Entity { get; private set; }
        public bool DuplicateSlug { get; init; }
        public bool HasJobs { get; set; }
        public Task<(IReadOnlyCollection<CompanyResponse> Items, int TotalCount)> SearchAsync(CompanySearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<CompanyResponse>)(Entity is null ? [] : [Response(Entity)]), Entity is null ? 0 : 1));
        public Task<Company?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Entity?.Id == id ? Entity : null);
        public Task<CompanyResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entity?.Id == id ? Response(Entity) : null);
        public Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(DuplicateSlug);
        public Task<bool> HasJobsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(HasJobs);
        public Task AddAsync(Company company, CancellationToken cancellationToken = default) { Entity = company; return Task.CompletedTask; }
        public void Remove(Company company) => company.IsDeleted = true;
        public Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AdminOptionResponse>>(Entity is null ? [] : [new(Entity.Id, Entity.Name, Entity.Slug)]);
        private static CompanyResponse Response(Company x) => new(x.Id, x.Name, x.Slug, x.Description,
            x.WebsiteUrl, x.LogoUrl, x.Industry, x.Location, x.EmployeeCount, x.IsVerified,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.IsDeleted);
    }

    private sealed class CategoryRepositoryFake : ICategoryManagementRepository
    {
        private Category? _entity;
        public bool ParentExists { get; set; }
        public bool IsDescendant { get; set; }
        public bool HasReferences { get; set; }
        public Task<(IReadOnlyCollection<CategoryResponse> Items, int TotalCount)> SearchAsync(CategorySearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<CategoryResponse>)(_entity is null ? [] : [Response(_entity)]), _entity is null ? 0 : 1));
        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_entity?.Id == id ? _entity : null);
        public Task<CategoryResponse?> GetResponseAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entity?.Id == id ? Response(_entity) : null);
        public Task<bool> SlugExistsAsync(string slug, Guid? excludingId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(ParentExists);
        public Task<bool> IsDescendantAsync(Guid categoryId, Guid possibleDescendantId, CancellationToken cancellationToken = default) => Task.FromResult(IsDescendant);
        public Task<bool> HasChildrenOrJobsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(HasReferences);
        public Task AddAsync(Category category, CancellationToken cancellationToken = default) { _entity = category; return Task.CompletedTask; }
        public void Remove(Category category) => category.IsDeleted = true;
        public Task<IReadOnlyCollection<AdminOptionResponse>> GetOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AdminOptionResponse>>(_entity is null ? [] : [new(_entity.Id, _entity.Name, _entity.Slug)]);
        private static CategoryResponse Response(Category x) => new(x.Id, x.Name, x.Slug, x.Description,
            x.DisplayOrder, x.ParentCategoryId, null, x.CreatedAtUtc, x.UpdatedAtUtc, x.IsDeleted);
    }

    private sealed class JobRepositoryFake : IJobRepository
    {
        public bool CompanyExists { get; set; }
        public Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);
        public Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<Job>)[], 0));
        public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(CompanyExists);
        public Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> ExpireOverduePublishedAsync(
            DateTime utcNow, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
        public Task AddAsync(Job job, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Job job) { }
        public void Remove(Job job) { }
        public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);
        public Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);
        public Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(Guid companyId, string title, string location, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Job>>(Array.Empty<Job>());
    }
}

public sealed class AdminBootstrapTests
{
    private const string StrongPassword = "Correct-Horse-9-Battery!";

    [Fact]
    public async Task DisabledBootstrapDoesNothing()
    {
        var fixture = CreateFixture();
        Assert.Equal(AdminBootstrapResult.Disabled, await fixture.Service.InitializeAsync(
            new(false, null, null, null, null)));
        Assert.Null(fixture.Users.Added);
    }

    [Fact]
    public async Task ValidBootstrapCreatesOneAdminAndRepeatedRunIsIdempotent()
    {
        var fixture = CreateFixture();
        var settings = new AdminBootstrapSettings(true, "admin@careerportal.test", StrongPassword, "Portal", "Admin");
        await fixture.Service.InitializeAsync(settings);
        fixture.Users.Existing = fixture.Users.Added;
        await fixture.Service.InitializeAsync(settings);

        Assert.Equal(SystemRoleIds.Administrator, fixture.Users.Added!.RoleId);
        Assert.Equal(UserStatus.Active, fixture.Users.Added.Status);
        Assert.True(fixture.Users.Added.EmailConfirmed);
        Assert.Equal(1, fixture.Users.AddCount);
    }

    [Fact]
    public async Task ExistingCandidateIsNeverElevated()
    {
        var fixture = CreateFixture();
        fixture.Users.Existing = new User { RoleId = SystemRoleIds.Candidate };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InitializeAsync(
            new(true, "admin@careerportal.test", StrongPassword, "Portal", "Admin")));
        Assert.Equal(SystemRoleIds.Candidate, fixture.Users.Existing.RoleId);
    }

    [Fact]
    public async Task MissingOrWeakPasswordIsRejected()
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.InitializeAsync(
            new(true, "admin@careerportal.test", "weak", "Portal", "Admin")));
        Assert.Null(fixture.Users.Added);
    }

    private static BootstrapFixture CreateFixture()
    {
        var users = new UserRepositoryFake();
        var service = new AdminBootstrapService(
            users, new UnitOfWorkFake(), new PasswordHasherFake(), new RegisterRequestValidator());
        return new(service, users);
    }

    private sealed record BootstrapFixture(
        AdminBootstrapService Service, UserRepositoryFake Users);

    private sealed class UserRepositoryFake : IUserRepository
    {
        public User? Existing { get; set; }
        public User? Added { get; private set; }
        public int AddCount { get; private set; }
        public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => Task.FromResult(Existing);
        public Task<User?> GetByNormalizedPhoneAsync(
            string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);
        public Task<bool> RegistrationIdentityExistsAsync(
            string normalizedEmail, string normalizedPhoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task AddAsync(User user, CancellationToken cancellationToken = default) { Added = user; AddCount++; return Task.CompletedTask; }
        public void Update(User user) { }
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) => "hashed";
        public bool Verify(string password, string hash) => false;
    }

}

internal sealed class UnitOfWorkFake : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}
