using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Moq;
using Xunit;

namespace JobPortal.Application.Tests;

/// <summary>
/// Unit tests for JobDeduplicationService.
/// </summary>
public class JobDeduplicationServiceTests
{
    private readonly Mock<IJobRepository> _jobRepositoryMock;
    private readonly Mock<IUrlCanonicalizer> _urlCanonicalizerMock;
    private readonly IJobFingerprintService _fingerprintService;
    private readonly JobDeduplicationService _service;

    public JobDeduplicationServiceTests()
    {
        _jobRepositoryMock = new Mock<IJobRepository>();
        _urlCanonicalizerMock = new Mock<IUrlCanonicalizer>();
        _fingerprintService = new JobFingerprintService();
        _service = new JobDeduplicationService(_jobRepositoryMock.Object, _fingerprintService, _urlCanonicalizerMock.Object);
    }

    [Fact]
    public async Task FindDuplicateAsync_ExternalUrlMatch_ReturnsSourceUrlMatch()
    {
        // Arrange
        var existingJob = CreateTestJob("Existing Title", "Acme Corp", "SF");
        _urlCanonicalizerMock.Setup(c => c.Canonicalize("https://example.com/job/123")).Returns("https://example.com/job/123");
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync("https://example.com/job/123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingJob);

        // Act
        var result = await _service.FindDuplicateAsync(
            "New Title", "Acme Corp", "SF", "https://example.com/job/123", Guid.NewGuid());

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.SourceUrl, result.MatchTypeEnum);
        Assert.Same(existingJob, result.MatchedJob);
        Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_FingerprintMatch_ReturnsFingerprintMatch()
    {
        // Arrange
        var title = "Software Engineer";
        var company = "Acme Corp";
        var location = "San Francisco";
        var fingerprint = _fingerprintService.GenerateFingerprint(title, company, location);
        
        var existingJob = CreateTestJob(title, company, location);
        existingJob.FingerprintHash = fingerprint;
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingJob);

        // Act
        var result = await _service.FindDuplicateAsync(title, company, location, null, Guid.NewGuid());

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.Fingerprint, result.MatchTypeEnum);
        Assert.Same(existingJob, result.MatchedJob);
        Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_FuzzyMatchAboveThreshold_ReturnsFuzzyMatch()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var candidateJob = CreateTestJob("Software Engineer", "Acme Corp", "San Francisco");
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindCandidatesForFuzzyMatchAsync(companyId, It.IsAny<string>(), It.IsAny<string>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateJob });

        // Act - Very similar title and same location
        var result = await _service.FindDuplicateAsync(
            "Senior Software Engineer", "Acme Corp", "San Francisco", null, companyId);

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.Fuzzy, result.MatchTypeEnum);
        Assert.Same(candidateJob, result.MatchedJob);
        Assert.NotNull(result.SimilarityScore);
        Assert.InRange(result.SimilarityScore.Value, 0.85, 1.0);
    }

    [Fact]
    public async Task FindDuplicateAsync_FuzzyMatchBelowThreshold_ReturnsNoMatch()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var candidateJob = CreateTestJob("Data Scientist", "Acme Corp", "New York");
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindCandidatesForFuzzyMatchAsync(companyId, It.IsAny<string>(), It.IsAny<string>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateJob });

        // Act - Very different title and location
        var result = await _service.FindDuplicateAsync(
            "Marketing Manager", "Acme Corp", "Los Angeles", null, companyId);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Equal(MatchType.None, result.MatchTypeEnum);
        Assert.Null(result.MatchedJob);
        Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_NoMatch_ReturnsNoMatch()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindCandidatesForFuzzyMatchAsync(companyId, It.IsAny<string>(), It.IsAny<string>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Job>());

        // Act
        var result = await _service.FindDuplicateAsync(
            "Software Engineer", "Acme Corp", "San Francisco", null, companyId);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Equal(MatchType.None, result.MatchTypeEnum);
        Assert.Null(result.MatchedJob);
        Assert.Null(result.SimilarityScore);
    }

    [Fact]
    public async Task FindDuplicateAsync_UrlMatchTakesPrecedenceOverFingerprint()
    {
        // Arrange
        var urlMatchJob = CreateTestJob("URL Match", "Company A", "Location A");
        var fingerprintMatchJob = CreateTestJob("Fingerprint Match", "Company B", "Location B");
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize("https://example.com/job")).Returns("https://example.com/job");
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync("https://example.com/job", It.IsAny<CancellationToken>()))
            .ReturnsAsync(urlMatchJob);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fingerprintMatchJob);

        // Act
        var result = await _service.FindDuplicateAsync(
            "Any Title", "Any Company", "Any Location", "https://example.com/job", Guid.NewGuid());

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.SourceUrl, result.MatchTypeEnum);
        Assert.Same(urlMatchJob, result.MatchedJob);
    }

    [Fact]
    public async Task FindDuplicateAsync_FingerprintTakesPrecedenceOverFuzzy()
    {
        // Arrange
        var title = "Software Engineer";
        var company = "Acme Corp";
        var location = "San Francisco";
        var fingerprint = _fingerprintService.GenerateFingerprint(title, company, location);
        
        var fingerprintMatchJob = CreateTestJob(title, company, location);
        fingerprintMatchJob.FingerprintHash = fingerprint;
        
        var fuzzyMatchJob = CreateTestJob("Similar Software Engineer", "Acme Corp", "San Francisco");
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(fingerprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fingerprintMatchJob);
        _jobRepositoryMock.Setup(r => r.FindCandidatesForFuzzyMatchAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { fuzzyMatchJob });

        // Act
        var result = await _service.FindDuplicateAsync(title, company, location, null, Guid.NewGuid());

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.Fingerprint, result.MatchTypeEnum);
        Assert.Same(fingerprintMatchJob, result.MatchedJob);
    }

    [Fact]
    public async Task FindDuplicateAsync_NullCompanyId_SkipsFuzzyMatch()
    {
        // Arrange
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        // Act - companyId is null, so fuzzy match should be skipped
        var result = await _service.FindDuplicateAsync(
            "Software Engineer", "Acme Corp", "San Francisco", null, null);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Equal(MatchType.None, result.MatchTypeEnum);
        
        // Verify fuzzy match was never called
        _jobRepositoryMock.Verify(r => r.FindCandidatesForFuzzyMatchAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindDuplicateAsync_EmptyExternalUrl_SkipsUrlMatch()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        
        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindCandidatesForFuzzyMatchAsync(companyId, It.IsAny<string>(), It.IsAny<string>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Job>());

        // Act
        var result = await _service.FindDuplicateAsync(
            "Software Engineer", "Acme Corp", "San Francisco", "", companyId);

        // Assert
        Assert.False(result.IsDuplicate);
        
        // Verify URL lookup was never called
        _jobRepositoryMock.Verify(r => r.FindByExternalUrlAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindDuplicateAsync_CanonicalizedUrlMatch_ReturnsSourceUrlMatch()
    {
        // Arrange - URL with tracking params should canonicalize to same base URL
        var existingJob = CreateTestJob("Existing Title", "Acme Corp", "SF");
        _urlCanonicalizerMock.Setup(c => c.Canonicalize("https://example.com/job/123?utm_source=test")).Returns("https://example.com/job/123");
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync("https://example.com/job/123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingJob);

        // Act
        var result = await _service.FindDuplicateAsync(
            "New Title", "Acme Corp", "SF", "https://example.com/job/123?utm_source=test", Guid.NewGuid());

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.SourceUrl, result.MatchTypeEnum);
        Assert.Same(existingJob, result.MatchedJob);
    }

    [Fact]
    public async Task FindDuplicateAsync_MultipleFuzzyCandidates_ReturnsHighestSimilarity()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var lowScoreJob = CreateTestJob("Data Analyst", "Acme Corp", "Boston");
        var highScoreJob = CreateTestJob("Senior Software Engineer", "Acme Corp", "San Francisco");
        var mediumScoreJob = CreateTestJob("Software Developer", "Acme Corp", "San Jose");

        _urlCanonicalizerMock.Setup(c => c.Canonicalize(It.IsAny<string>())).Returns((string? s) => s);
        _jobRepositoryMock.Setup(r => r.FindByExternalUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindByFingerprintHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);
        _jobRepositoryMock.Setup(r => r.FindCandidatesForFuzzyMatchAsync(companyId, It.IsAny<string>(), It.IsAny<string>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lowScoreJob, highScoreJob, mediumScoreJob });

        // Act - Looking for "Senior Software Engineer" in SF
        var result = await _service.FindDuplicateAsync(
            "Senior Software Engineer", "Acme Corp", "San Francisco", null, companyId);

        // Assert - Should return the highest similarity match
        Assert.True(result.IsDuplicate);
        Assert.Equal(MatchType.Fuzzy, result.MatchTypeEnum);
        Assert.Same(highScoreJob, result.MatchedJob);
    }

    private static Job CreateTestJob(string title, string company, string location)
    {
        return new Job
        {
            Id = Guid.NewGuid(),
            Title = title,
            Location = location,
            Status = JobStatus.Published,
            Company = new Company { Id = Guid.NewGuid(), Name = company },
            CompanyId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ApplicationUrl = "https://example.com/job"
        };
    }
}
