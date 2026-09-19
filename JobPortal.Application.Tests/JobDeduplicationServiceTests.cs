using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;
using MatchType = JobPortal.Application.Abstractions.Jobs.MatchType;

namespace JobPortal.Application.Tests;

public class JobDeduplicationServiceTests
{
    private readonly TestJobRepository _jobs = new();
    private readonly TestUrlCanonicalizer _urls = new();
    private readonly IJobFingerprintService _fingerprints = new JobFingerprintService();
    private readonly JobDeduplicationService _service;

    public JobDeduplicationServiceTests() => _service = new(_jobs, _fingerprints, _urls);

    [Fact]
    public async Task FindDuplicateAsync_ExternalUrlMatch_ReturnsSourceUrlMatch()
    {
        var job = CreateJob("Existing Title", "Acme Corp", "SF");
        _jobs.ExternalUrlMatch = job;
        var result = await Find("New Title", "Acme Corp", "SF", "https://example.com/job/123");
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.SourceUrl, result.MatchTypeEnum); Assert.Same(job, result.MatchedJob); Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_FingerprintMatch_ReturnsFingerprintMatch()
    {
        const string title = "Software Engineer", company = "Acme Corp", location = "San Francisco";
        var job = CreateJob(title, company, location);
        job.FingerprintHash = _fingerprints.GenerateFingerprint(title, company, location);
        _jobs.FingerprintMatch = job;
        var result = await Find(title, company, location);
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.Fingerprint, result.MatchTypeEnum); Assert.Same(job, result.MatchedJob); Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_FuzzyMatchAboveThreshold_ReturnsFuzzyMatch()
    {
        var job = CreateJob("Software Engineer", "Acme Corp", "San Francisco"); _jobs.FuzzyCandidates = [job];
        var result = await Find("Senior Software Engineer", "Acme Corp", "San Francisco");
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.Fuzzy, result.MatchTypeEnum); Assert.Same(job, result.MatchedJob); Assert.NotNull(result.SimilarityScore); Assert.InRange(result.SimilarityScore.Value, 0.85, 1.0);
    }

    [Fact]
    public async Task FindDuplicateAsync_FuzzyMatchBelowThreshold_ReturnsNoMatch()
    {
        _jobs.FuzzyCandidates = [CreateJob("Data Scientist", "Acme Corp", "New York")];
        var result = await Find("Marketing Manager", "Acme Corp", "Los Angeles");
        Assert.False(result.IsDuplicate); Assert.Equal(MatchType.None, result.MatchTypeEnum); Assert.Null(result.MatchedJob); Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_NoMatch_ReturnsNoMatch()
    {
        var result = await Find("Software Engineer", "Acme Corp", "San Francisco");
        Assert.False(result.IsDuplicate); Assert.Equal(MatchType.None, result.MatchTypeEnum); Assert.Null(result.MatchedJob); Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_UrlMatchTakesPrecedenceOverFingerprint()
    {
        var urlJob = CreateJob("URL Match", "Company A", "Location A"); _jobs.ExternalUrlMatch = urlJob; _jobs.FingerprintMatch = CreateJob("Fingerprint Match", "Company B", "Location B");
        var result = await Find("Any Title", "Any Company", "Any Location", "https://example.com/job");
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.SourceUrl, result.MatchTypeEnum); Assert.Same(urlJob, result.MatchedJob);
    }

    [Fact]
    public async Task FindDuplicateAsync_FingerprintTakesPrecedenceOverFuzzy()
    {
        const string title = "Software Engineer", company = "Acme Corp", location = "San Francisco";
        var job = CreateJob(title, company, location); job.FingerprintHash = _fingerprints.GenerateFingerprint(title, company, location);
        _jobs.FingerprintMatch = job; _jobs.FuzzyCandidates = [CreateJob("Similar Software Engineer", company, location)];
        var result = await Find(title, company, location);
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.Fingerprint, result.MatchTypeEnum); Assert.Same(job, result.MatchedJob);
    }

    [Fact]
    public async Task FindDuplicateAsync_NullCompanyId_SkipsFuzzyMatch()
    {
        var result = await _service.FindDuplicateAsync("Software Engineer", "Acme Corp", "San Francisco", null, null);
        Assert.False(result.IsDuplicate); Assert.Equal(MatchType.None, result.MatchTypeEnum); Assert.Equal(0, _jobs.FuzzyMatchCalls);
    }

    [Fact]
    public async Task FindDuplicateAsync_EmptyExternalUrl_SkipsUrlMatch()
    {
        var result = await Find("Software Engineer", "Acme Corp", "San Francisco", "");
        Assert.False(result.IsDuplicate); Assert.Equal(0, _jobs.ExternalUrlLookupCalls);
    }

    [Fact]
    public async Task FindDuplicateAsync_CanonicalizedUrlMatch_ReturnsSourceUrlMatch()
    {
        var job = CreateJob("Existing Title", "Acme Corp", "SF");
        _urls.Overrides["https://example.com/job/123?utm_source=test"] = "https://example.com/job/123"; _jobs.ExternalUrlMatch = job;
        var result = await Find("New Title", "Acme Corp", "SF", "https://example.com/job/123?utm_source=test");
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.SourceUrl, result.MatchTypeEnum); Assert.Same(job, result.MatchedJob);
    }

    [Fact]
    public async Task FindDuplicateAsync_MultipleFuzzyCandidates_ReturnsHighestSimilarity()
    {
        var low = CreateJob("Data Analyst", "Acme Corp", "Boston"); var high = CreateJob("Senior Software Engineer", "Acme Corp", "San Francisco"); var medium = CreateJob("Software Developer", "Acme Corp", "San Jose"); _jobs.FuzzyCandidates = [low, high, medium];
        var result = await Find("Senior Software Engineer", "Acme Corp", "San Francisco");
        Assert.True(result.IsDuplicate); Assert.Equal(MatchType.Fuzzy, result.MatchTypeEnum); Assert.Same(high, result.MatchedJob);
    }

    private Task<DeduplicationResult> Find(string title, string company, string location, string? externalUrl = null) => _service.FindDuplicateAsync(title, company, location, externalUrl, Guid.NewGuid());
    private static Job CreateJob(string title, string company, string location) => new() { Id = Guid.NewGuid(), Title = title, Location = location, Status = JobStatus.Published, Company = new Company { Id = Guid.NewGuid(), Name = company }, CompanyId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), ApplicationUrl = "https://example.com/job" };

    private sealed class TestUrlCanonicalizer : IUrlCanonicalizer
    {
        public Dictionary<string, string?> Overrides { get; } = new();
        public string? Canonicalize(string? url) => url is not null && Overrides.TryGetValue(url, out var value) ? value : url;
    }

    private sealed class TestJobRepository : IJobRepository
    {
        public Job? ExternalUrlMatch { get; set; }
        public Job? FingerprintMatch { get; set; }
        public IReadOnlyList<Job> FuzzyCandidates { get; set; } = Array.Empty<Job>();
        public int ExternalUrlLookupCalls { get; private set; }
        public int FuzzyMatchCalls { get; private set; }
        public Task<Job?> FindByExternalUrlAsync(string externalUrl, CancellationToken cancellationToken = default) { ExternalUrlLookupCalls++; return Task.FromResult(ExternalUrlMatch); }
        public Task<Job?> FindByFingerprintHashAsync(string fingerprintHash, CancellationToken cancellationToken = default) => Task.FromResult(FingerprintMatch);
        public Task<IReadOnlyList<Job>> FindCandidatesForFuzzyMatchAsync(Guid companyId, string title, string location, int maxResults, CancellationToken cancellationToken = default) { FuzzyMatchCalls++; return Task.FromResult(FuzzyCandidates); }
        public Task<Job?> GetByIdAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default) => Task.FromResult<Job?>(null);
        public Task<(IReadOnlyCollection<Job> Items, int TotalCount)> SearchAsync(JobSearchQuery query, CancellationToken cancellationToken = default) => Task.FromResult(((IReadOnlyCollection<Job>)Array.Empty<Job>(), 0));
        public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CategoryExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> ExpireOverduePublishedAsync(DateTime utcNow, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task AddAsync(Job job, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Job job) { }
        public void Remove(Job job) { }
        public Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
