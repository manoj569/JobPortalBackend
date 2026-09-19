using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;
using MatchType = JobPortal.Application.Abstractions.Jobs.MatchType;

namespace JobPortal.Application.Tests;

public class JobIngestionServiceTests
{
    private readonly TestJobRepository _jobs = new();
    private readonly TestCompanyRepository _companies = new();
    private readonly TestCategoryRepository _categories = new();
    private readonly TestDeduplicationService _deduplication = new();
    private readonly TestUnitOfWork _unitOfWork = new();
    private readonly IJobFingerprintService _fingerprints =
        new JobFingerprintService();

    private readonly JobIngestionService _service;

    private readonly Company _company = new()
    {
        Id = Guid.NewGuid(),
        Name = "Acme Corp",
        NormalizedName = "Acme Corp",
        Slug = "acme-corp"
    };

    private readonly Guid _categoryId = Guid.NewGuid();

    public JobIngestionServiceTests()
    {
        _companies.Company = _company;
        _categories.ExistingIds.Add(_categoryId);

        _service = new JobIngestionService(
            _jobs,
            _companies,
            _categories,
            _deduplication,
            _fingerprints,
            _unitOfWork,
            TimeProvider.System);
    }

    [Fact]
    public async Task IngestAsync_NewValidJob_CreatesDraftJob()
    {
        var raw = CreateRawJob();

        var result = await _service.IngestAsync(raw);

        Assert.Equal(JobIngestionOutcome.Created, result.Outcome);
        Assert.True(result.Created);
        Assert.NotNull(result.JobId);

        var created = Assert.Single(_jobs.AddedJobs);

        Assert.Equal(result.JobId, created.Id);
        Assert.Equal("Software Engineer", created.Title);
        Assert.Equal(_company.Id, created.CompanyId);
        Assert.Equal(_categoryId, created.CategoryId);
        Assert.Equal(JobStatus.Draft, created.Status);

        Assert.Equal(
            "https://example.com/jobs/123",
            created.ApplicationUrl);

        Assert.NotNull(created.FingerprintHash);
        Assert.Equal(64, created.FingerprintHash!.Length);

        Assert.NotNull(created.FirstSeenAtUtc);
        Assert.NotNull(created.LastSeenAtUtc);

        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_UrlDuplicate_ReturnsExistingJob()
    {
        var existing = CreateExistingJob();

        _deduplication.Result =
            DeduplicationResult.SourceUrlMatch(existing);

        var result = await _service.IngestAsync(CreateRawJob());

        Assert.Equal(
            JobIngestionOutcome.MatchedByUrl,
            result.Outcome);

        Assert.True(result.MatchedExisting);
        Assert.Equal(existing.Id, result.JobId);

        Assert.Empty(_jobs.AddedJobs);
        Assert.Empty(_jobs.UpdatedJobs);
        Assert.NotNull(existing.LastSeenAtUtc);
        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_FingerprintDuplicate_ReturnsExistingJob()
    {
        var existing = CreateExistingJob();

        _deduplication.Result =
            DeduplicationResult.FingerprintMatch(existing);

        var result = await _service.IngestAsync(CreateRawJob());

        Assert.Equal(
            JobIngestionOutcome.MatchedByFingerprint,
            result.Outcome);

        Assert.True(result.MatchedExisting);
        Assert.Equal(existing.Id, result.JobId);

        Assert.Empty(_jobs.AddedJobs);
        Assert.Empty(_jobs.UpdatedJobs);
    }

    [Fact]
    public async Task IngestAsync_FuzzyDuplicate_ReturnsExistingJob()
    {
        var existing = CreateExistingJob();

        _deduplication.Result =
            DeduplicationResult.FuzzyMatch(existing, 0.91);

        var result = await _service.IngestAsync(CreateRawJob());

        Assert.Equal(
            JobIngestionOutcome.MatchedByFuzzy,
            result.Outcome);

        Assert.True(result.MatchedExisting);
        Assert.Equal(existing.Id, result.JobId);

        Assert.Empty(_jobs.AddedJobs);
        Assert.Empty(_jobs.UpdatedJobs);

        Assert.Contains("0.910", result.Message);
    }

    [Fact]
    public async Task IngestAsync_Duplicate_DoesNotOverwriteCuratedFields()
    {
        var existing = CreateExistingJob();

        existing.Title = "Curated Senior Engineer";
        existing.Description = "Curated description";
        existing.Requirements = "Curated requirements";
        existing.Responsibilities = "Curated responsibilities";
        existing.Benefits = "Curated benefits";
        existing.MinimumSalary = 50000;
        existing.MaximumSalary = 90000;
        existing.Status = JobStatus.Published;
        var referral = new JobReferral { JobId = existing.Id, ApprovalStatus = JobReferralApprovalStatus.Approved };
        var recruiter = new JobRecruiterContact { JobId = existing.Id, ContactName = "Curated recruiter", IsSharingApproved = true };
        existing.Referral = referral;
        existing.RecruiterContact = recruiter;
        existing.PublishedAtUtc = DateTime.UtcNow.AddDays(-2);
        var publishedAt = existing.PublishedAtUtc;

        _deduplication.Result =
            DeduplicationResult.SourceUrlMatch(existing);

        var raw = CreateRawJob() with
        {
            Title = "External Changed Title",
            Description = "External description",
            Requirements = "External requirements",
            Responsibilities = "External responsibilities",
            Benefits = "External benefits",
            SalaryMin = 1,
            SalaryMax = 2
        };

        var result = await _service.IngestAsync(raw);

        Assert.Equal(
            JobIngestionOutcome.MatchedByUrl,
            result.Outcome);

        Assert.Equal(
            "Curated Senior Engineer",
            existing.Title);

        Assert.Equal(
            "Curated description",
            existing.Description);

        Assert.Equal(
            "Curated requirements",
            existing.Requirements);

        Assert.Equal(
            "Curated responsibilities",
            existing.Responsibilities);

        Assert.Equal(
            "Curated benefits",
            existing.Benefits);

        Assert.Equal(50000m, existing.MinimumSalary);
        Assert.Equal(90000m, existing.MaximumSalary);
        Assert.Equal(JobStatus.Published, existing.Status);
        Assert.Same(_company, existing.Company);
        Assert.Equal(_categoryId, existing.CategoryId);
        Assert.Same(referral, existing.Referral);
        Assert.Equal(JobReferralApprovalStatus.Approved, existing.Referral.ApprovalStatus);
        Assert.Same(recruiter, existing.RecruiterContact);
        Assert.True(existing.RecruiterContact.IsSharingApproved);
        Assert.Equal(publishedAt, existing.PublishedAtUtc);

        Assert.NotNull(existing.LastSeenAtUtc);
    }

    [Fact]
    public async Task IngestAsync_CompanyNotFound_DoesNotCreateJob()
    {
        _companies.Company = null;

        var result = await _service.IngestAsync(CreateRawJob());

        Assert.Equal(
            JobIngestionOutcome.CompanyNotFound,
            result.Outcome);

        Assert.Null(result.JobId);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Equal(0, _unitOfWork.SaveCalls);

        Assert.Equal(
            0,
            _deduplication.FindCalls);
    }

    [Fact]
    public async Task IngestAsync_MissingCategory_DoesNotCreateNewJob()
    {
        var raw = CreateRawJob() with
        {
            CategoryId = null
        };

        var result = await _service.IngestAsync(raw);

        Assert.Equal(
            JobIngestionOutcome.Invalid,
            result.Outcome);

        Assert.Null(result.JobId);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_MalformedApplicationUrl_ReturnsInvalid()
    {
        var raw = CreateRawJob() with
        {
            ApplicationUrl = "not-a-valid-url"
        };

        var result = await _service.IngestAsync(raw);

        Assert.Equal(JobIngestionOutcome.Invalid, result.Outcome);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Equal(0, _deduplication.FindCalls);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_InvalidSalaryRange_ReturnsInvalid()
    {
        var raw = CreateRawJob() with
        {
            SalaryMin = 100000,
            SalaryMax = 50000
        };

        var result = await _service.IngestAsync(raw);

        Assert.Equal(JobIngestionOutcome.Invalid, result.Outcome);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Equal(0, _deduplication.FindCalls);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_DuplicateWithoutAggregationMetadata_FillsMissingMetadata()
    {
        var existing = CreateExistingJob();

        existing.FingerprintHash = null;
        existing.FirstSeenAtUtc = null;
        existing.LastSeenAtUtc = null;

        _deduplication.Result =
            DeduplicationResult.SourceUrlMatch(existing);

        var result = await _service.IngestAsync(CreateRawJob());

        Assert.Equal(JobIngestionOutcome.MatchedByUrl, result.Outcome);
        Assert.Equal(existing.Id, result.JobId);

        Assert.NotNull(existing.FingerprintHash);
        Assert.Equal(64, existing.FingerprintHash!.Length);
        Assert.NotNull(existing.FirstSeenAtUtc);
        Assert.NotNull(existing.LastSeenAtUtc);

        Assert.Empty(_jobs.UpdatedJobs);
        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_DuplicateWithExistingAggregationMetadata_PreservesFirstSeenAndFingerprint()
    {
        var existing = CreateExistingJob();

        var originalFirstSeen = DateTime.UtcNow.AddDays(-10);
        const string originalFingerprint =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        existing.FirstSeenAtUtc = originalFirstSeen;
        existing.LastSeenAtUtc = DateTime.UtcNow.AddDays(-1);
        existing.FingerprintHash = originalFingerprint;

        _deduplication.Result =
            DeduplicationResult.FingerprintMatch(existing);

        var result = await _service.IngestAsync(CreateRawJob());

        Assert.Equal(
            JobIngestionOutcome.MatchedByFingerprint,
            result.Outcome);

        Assert.Equal(originalFirstSeen, existing.FirstSeenAtUtc);
        Assert.Equal(originalFingerprint, existing.FingerprintHash);

        Assert.True(existing.LastSeenAtUtc > originalFirstSeen);

        Assert.Empty(_jobs.UpdatedJobs);
        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_InvalidCategory_DoesNotCreateNewJob()
    {
        var raw = CreateRawJob() with
        {
            CategoryId = Guid.NewGuid()
        };

        var result = await _service.IngestAsync(raw);

        Assert.Equal(
            JobIngestionOutcome.Invalid,
            result.Outcome);

        Assert.Null(result.JobId);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_EmptyCategory_DoesNotCreateNewJob()
    {
        var result = await _service.IngestAsync(CreateRawJob() with { CategoryId = Guid.Empty });

        Assert.Equal(JobIngestionOutcome.Invalid, result.Outcome);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task IngestAsync_MissingCategory_StillRefreshesCanonicalJob()
    {
        var existing = CreateExistingJob();
        _deduplication.Result = DeduplicationResult.SourceUrlMatch(existing);

        var result = await _service.IngestAsync(CreateRawJob() with { CategoryId = null });

        Assert.Equal(JobIngestionOutcome.MatchedByUrl, result.Outcome);
        Assert.Equal(existing.Id, result.JobId);
        Assert.Equal(_categoryId, existing.CategoryId);
        Assert.NotNull(existing.LastSeenAtUtc);
        Assert.Empty(_jobs.AddedJobs);
        Assert.Empty(_jobs.UpdatedJobs);
        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    private RawExternalJob CreateRawJob() => new()
    {
        Title = "Software Engineer",
        CompanyName = "Acme Corp",
        Location = "Pune",
        Description = "Build software.",
        Requirements = ".NET",
        Responsibilities = "Develop APIs",
        Benefits = "Health insurance",
        ApplicationUrl = "https://example.com/jobs/123",
        SalaryMin = 50000,
        SalaryMax = 100000,
        CategoryId = _categoryId
    };

    private Job CreateExistingJob() => _jobs.ExistingJob = new Job
    {
        Id = Guid.NewGuid(),
        ReferenceNumber = "JOB-EXISTING",
        Title = "Software Engineer",
        Slug = "software-engineer-existing",
        Description = "Existing description",
        ApplicationUrl = "https://example.com/jobs/123",
        Location = "Pune",
        CompanyId = _company.Id,
        Company = _company,
        CategoryId = _categoryId,
        Status = JobStatus.Published
    };

    private sealed class TestDeduplicationService :
        IJobDeduplicationService
    {
        public DeduplicationResult Result { get; set; } =
            DeduplicationResult.NoMatch();

        public int FindCalls { get; private set; }

        public Task<DeduplicationResult> FindDuplicateAsync(
            string title,
            string companyName,
            string? location,
            string? externalUrl,
            Guid? companyId,
            CancellationToken cancellationToken = default)
        {
            FindCalls++;

            return Task.FromResult(Result);
        }
    }

    private sealed class TestJobRepository : IJobRepository
    {
        public Job? ExistingJob { get; set; }
        public List<Job> AddedJobs { get; } = [];
        public List<Job> UpdatedJobs { get; } = [];

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken = default)
        {
            AddedJobs.Add(job);
            return Task.CompletedTask;
        }

        public void Update(Job job)
        {
            UpdatedJobs.Add(job);
        }

        public Task<Job?> GetByIdAsync(
            Guid id,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingJob?.Id == id ? ExistingJob : null);

        public Task<(IReadOnlyCollection<Job> Items, int TotalCount)>
            SearchAsync(
                JobSearchQuery query,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ((IReadOnlyCollection<Job>)Array.Empty<Job>(), 0));

        public Task<bool> CompanyExistsAsync(
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CategoryExistsAsync(
            Guid categoryId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> ExpireOverduePublishedAsync(
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public void Remove(Job job)
        {
        }

        public Task DeletePermanentlyAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Job?> FindByExternalUrlAsync(
            string externalUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Job?>(null);

        public Task<Job?> FindByFingerprintHashAsync(
            string fingerprintHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Job?>(null);

        public Task<IReadOnlyList<Job>>
            FindCandidatesForFuzzyMatchAsync(
                Guid companyId,
                string title,
                string location,
                int maxResults,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Job>>(
                Array.Empty<Job>());
    }

    private sealed class TestCompanyRepository :
        ICompanyManagementRepository
    {
        public Company? Company { get; set; }

        public Task<Company?> FindByNameOrSlugAsync(
            string normalizedName,
            string slug,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Company);

        public Task<Company?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Company?.Id == id ? Company : null);

        public Task<(
            IReadOnlyCollection<CompanyResponse> Items,
            int TotalCount)> SearchAsync(
                CompanySearchQuery query,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ((IReadOnlyCollection<CompanyResponse>)
                    Array.Empty<CompanyResponse>(), 0));

        public Task<CompanyResponse?> GetResponseAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CompanyResponse?>(null);

        public Task<bool> SlugExistsAsync(
            string slug,
            Guid? excludingId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasJobsAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddAsync(
            Company company,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Remove(Company company)
        {
        }

        public Task<IReadOnlyCollection<AdminOptionResponse>>
            GetOptionsAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AdminOptionResponse>>(
                Array.Empty<AdminOptionResponse>());
    }

    private sealed class TestCategoryRepository :
        ICategoryManagementRepository
    {
        public HashSet<Guid> ExistingIds { get; } = [];

        public Task<bool> ExistsAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingIds.Contains(id));

        public Task<Category?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Category?>(null);

        public Task<Category?> FindByNameOrSlugAsync(
            string name,
            string slug,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Category?>(null);

        public Task<(
            IReadOnlyCollection<CategoryResponse> Items,
            int TotalCount)> SearchAsync(
                CategorySearchQuery query,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ((IReadOnlyCollection<CategoryResponse>)
                    Array.Empty<CategoryResponse>(), 0));

        public Task<CategoryResponse?> GetResponseAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CategoryResponse?>(null);

        public Task<bool> SlugExistsAsync(
            string slug,
            Guid? excludingId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> IsDescendantAsync(
            Guid categoryId,
            Guid possibleDescendantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasChildrenOrJobsAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task AddAsync(
            Category category,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Remove(Category category)
        {
        }

        public Task<IReadOnlyCollection<AdminOptionResponse>>
            GetOptionsAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AdminOptionResponse>>(
                Array.Empty<AdminOptionResponse>());
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }
}
