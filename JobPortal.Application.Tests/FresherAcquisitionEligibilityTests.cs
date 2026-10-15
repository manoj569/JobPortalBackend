using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Application.Abstractions.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class FresherAcquisitionEligibilityTests
{
    [Theory]
    [InlineData("0 years", 0, null)]
    [InlineData("0+ years", 0, null)]
    [InlineData("0-1 years", null, null)]
    [InlineData("0-2 years", null, null)]
    [InlineData("0-3 years", null, null)]
    [InlineData("0-5 years", null, null)]
    [InlineData("0 to 2 years", null, null)]
    public void ZeroMinimumRangesAreEligibleRegardlessOfMaximum(string text, int? min, int? max)
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateExperience(new FresherCandidate
        { Title = "Software Engineer", ExperienceText = text, MinimumExperienceYears = min, MaximumExperienceYears = max });
        Assert.Equal(FresherExperienceDecision.Eligible, result.Decision);
    }

    [Theory]
    [InlineData("1-2 years")]
    [InlineData("1+ years")]
    [InlineData("2+ years")]
    [InlineData("2-4 years")]
    [InlineData("3-5 years")]
    public void PositiveMinimumOrPositiveRangeIsRejected(string text)
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateExperience(new FresherCandidate
        { Title = "Software Engineer", ExperienceText = text });
        Assert.Equal(FresherExperienceDecision.Ineligible, result.Decision);
    }

    [Theory]
    [InlineData("Intern")]
    [InlineData("Fresher")]
    [InlineData("Graduate Trainee")]
    [InlineData("New Graduate")]
    public void ExplicitFresherTitleIsEligible(string title)
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateExperience(new FresherCandidate { Title = title });
        Assert.Equal(FresherExperienceDecision.Eligible, result.Decision);
    }

    [Theory]
    [InlineData("Junior Software Engineer", "Minimum 3 years professional experience required")]
    [InlineData("Senior Software Engineer", "5+ years required")]
    [InlineData("Entry Level Engineer", "Must have 2 years experience")]
    public void PositiveMandatoryExperienceOverridesFresherTitle(string title, string experience)
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateExperience(new FresherCandidate
        { Title = title, ExperienceText = experience });
        Assert.Equal(FresherExperienceDecision.Ineligible, result.Decision);
    }

    [Theory]
    [InlineData("Pune, Maharashtra, India", "India", FresherGeographyDecision.IndiaEligible)]
    [InlineData("Bengaluru, India", "", FresherGeographyDecision.IndiaEligible)]
    [InlineData("Remote - India", "", FresherGeographyDecision.IndiaEligible)]
    [InlineData("Worldwide Remote", "", FresherGeographyDecision.GlobalRemoteEligible)]
    [InlineData("Global Remote", "", FresherGeographyDecision.GlobalRemoteEligible)]
    [InlineData("Remote - United States Only", "", FresherGeographyDecision.RemoteRestricted)]
    [InlineData("Remote - UK Only", "", FresherGeographyDecision.RemoteRestricted)]
    [InlineData("Remote - Europe Only", "", FresherGeographyDecision.RemoteRestricted)]
    [InlineData("Remote", "", FresherGeographyDecision.EligibilityUnknown)]
    public void GeographyRequiresPositiveIndiaOrGlobalRemoteEvidence(string location, string country,
        FresherGeographyDecision expected)
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateGeography(new FresherCandidate
        { LocationText = location, Country = country });
        Assert.Equal(expected, result.Decision);
    }

    [Fact]
    public void ApplicationUrlRejectsUnsafeSchemesAndLocalDestinations()
    {
        Assert.False(FresherAcquisitionEligibilityPolicy.IsSafePublicUrl("javascript:alert(1)"));
        Assert.False(FresherAcquisitionEligibilityPolicy.IsSafePublicUrl("http://localhost/apply"));
        Assert.False(FresherAcquisitionEligibilityPolicy.IsSafePublicUrl("http://192.168.1.2/apply"));
        Assert.True(FresherAcquisitionEligibilityPolicy.IsSafePublicUrl("https://jobs.example.com/apply?id=42"));
    }

    [Fact]
    public void ZeroMinimumRangeRemainsEligibleWhenRequirementUsesRequiredLanguage()
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateExperience(new FresherCandidate
        { Title = "Software Engineer", ExperienceText = "0-5 years experience required" });
        Assert.Equal(FresherExperienceDecision.Eligible, result.Decision);
    }

    [Fact]
    public void PositiveExperienceOnInternRoleIsStillRejected()
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateExperience(new FresherCandidate
        { Title = "Software Engineer Intern", ExperienceText = "2 years experience required" });
        Assert.Equal(FresherExperienceDecision.Ineligible, result.Decision);
    }

    [Fact]
    public void IndiaMentionCannotOverrideExplicitCountryExclusion()
    {
        var result = FresherAcquisitionEligibilityPolicy.EvaluateGeography(new FresherCandidate
        { LocationText = "Remote", Description = "Remote, India is not eligible for this role" });
        Assert.Equal(FresherGeographyDecision.RemoteRestricted, result.Decision);
    }

    [Fact]
    public async Task ImportNeverCallsPersistenceForIneligibleCandidates()
    {
        var ingestion = new RecordingIngestionService();
        var importer = new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance);
        var response = await importer.ImportAsync(new FresherBulkImportRequest
        {
            ImportId = "test-1", SourceType = "feed", SourceName = "test",
            Jobs = new[]
            {
                Candidate("Remote", "Software Engineer", "0-2 years"),
                Candidate("Remote - US Only", "Intern", null),
                Candidate("Pune, India", "Junior Developer", "Minimum 3 years required")
            }
        });
        Assert.Equal(0, ingestion.Calls);
        Assert.Equal(2, response.RejectedGeography);
        Assert.Equal(1, response.RejectedExperience);
        Assert.Equal(0, response.Failed);
    }

    [Fact]
    public async Task EligibleCandidateUsesExistingIngestionAndApplyUrl()
    {
        var ingestion = new RecordingIngestionService();
        var importer = new FresherBulkImportService(ingestion, NullLogger<FresherBulkImportService>.Instance);
        var candidate = Candidate("Pune, India", "Graduate Trainee", "0-5 years",
            "https://careers.example.com/job/123?gh_jid=123");
        candidate = new FresherImportCandidate
        {
            CompanyName = candidate.CompanyName, Title = candidate.Title, Description = candidate.Description,
            LocationText = candidate.LocationText, Country = candidate.Country, ExperienceText = candidate.ExperienceText,
            ApplyUrl = candidate.ApplyUrl, CanonicalJobUrl = "https://careers.example.com/job/123"
        };
        var response = await importer.ImportAsync(new FresherBulkImportRequest
        { ImportId = "test-2", SourceType = "feed", SourceName = "test", Jobs = [candidate] });
        Assert.Equal(1, ingestion.Calls);
        Assert.Equal("https://careers.example.com/job/123?gh_jid=123", ingestion.Last!.ApplicationUrl);
        Assert.Equal(1, response.Created);
    }

    private static FresherImportCandidate Candidate(string location, string title, string? experience,
        string applyUrl = "https://careers.example.com/apply") => new()
    {
        CompanyName = "Example Employer", Title = title, Description = "A valid entry-level role",
        LocationText = location, Country = location.Contains("India", StringComparison.OrdinalIgnoreCase) ? "India" : null,
        ExperienceText = experience, ApplyUrl = applyUrl
    };

    private sealed class RecordingIngestionService : IJobIngestionService
    {
        public int Calls { get; private set; }
        public RawExternalJob? Last { get; private set; }
        public Task<JobIngestionResult> IngestAsync(RawExternalJob rawJob, CancellationToken cancellationToken = default)
        {
            Calls++; Last = rawJob;
            return Task.FromResult(new JobIngestionResult { Outcome = JobIngestionOutcome.Created, JobId = Guid.NewGuid() });
        }
    }
}
