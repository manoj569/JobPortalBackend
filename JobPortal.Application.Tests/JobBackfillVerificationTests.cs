using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Maintenance;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobBackfillVerificationTests
{
    [Fact]
    public void VerifyIsExclusiveAndNeverParsesAsBackfill()
    {
        Assert.True(BackfillVerification.IsCommand(["job-url-hash-backfill", "--verify"]));
        Assert.Throws<ArgumentException>(() => BackfillOptions.Parse(["job-url-hash-backfill", "--verify"]));
    }

    [Theory]
    [InlineData("--apply")]
    [InlineData("--check-triggers")]
    [InlineData("--batch-size")]
    [InlineData("--verify")]
    public void RejectsMixedOptions(string option)
    {
        string[] args = ["job-url-hash-backfill", "--verify", option];
        Assert.False(BackfillVerification.IsCommand(args));
        Assert.False(TriggerInspection.IsCommand(args));
        Assert.Throws<ArgumentException>(() => BackfillOptions.Parse(args));
    }

    [Fact]
    public void CountsAllJobsAndClassifiesActiveNullHashesUsingExactHelper()
    {
        const string url = "https://example.com/Job?token=private&utm_source=test";
        var report = new VerificationReport();
        report.Observe(false, 1, url, ApplicationUrlIdentity.Hash(url));
        report.Observe(false, 2, url, null);
        report.Observe(false, 2, "invalid", null);
        report.Observe(true, 3, url, null);
        report.Observe(true, 3, url, ApplicationUrlIdentity.Hash(url));
        Assert.Equal(5, report.Total);
        Assert.Equal(3, report.Active);
        Assert.Equal(2, report.Populated);
        Assert.Equal(1, report.ActivePopulated);
        Assert.Equal(1, report.ActiveNullEligible);
        Assert.Equal(1, report.ActiveNullInvalid);
        Assert.Equal(0, report.Mismatches);
        Assert.Equal(2, report.StatusCounts[2]);
        Assert.False(report.Passed);
        using var output = new StringWriter();
        report.Write(output);
        Assert.DoesNotContain(url, output.ToString());
        Assert.DoesNotContain("private", output.ToString());
        Assert.Contains("Soft-deleted Jobs: 2", output.ToString());
    }

    [Theory]
    [InlineData(false, "https://example.com", "incorrect")]
    [InlineData(true, "https://example.com", "incorrect")]
    [InlineData(false, "invalid", "populated")]
    [InlineData(false, null, "")]
    public void AllPopulatedMismatchesFailIncludingDeletedOrInvalidUrls(bool deleted, string? url, string hash)
    {
        var report = new VerificationReport();
        report.Observe(deleted, 1, url, hash);
        Assert.Equal(1, report.Mismatches);
        Assert.False(report.Passed);
    }

    [Fact]
    public void CleanStatePassesAndInvalidNullHashesAreAllowed()
    {
        var report = new VerificationReport();
        report.Observe(false, 1, "invalid", null);
        report.Observe(false, 2, "https://example.com", ApplicationUrlIdentity.Hash("https://example.com"));
        using var output = new StringWriter();
        report.Write(output);
        Assert.True(report.Passed);
        Assert.DoesNotContain("FAIL", output.ToString());
        Assert.Contains("Overall: PASS", output.ToString());
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void DuplicateOrAnyOrphanFails(long duplicate, long referral, long contact, long application)
    {
        var report = new VerificationReport { DuplicateValues = duplicate, OrphanReferrals = referral,
            OrphanContacts = contact, OrphanApplications = application };
        Assert.False(report.Passed);
        using var output = new StringWriter();
        report.Write(output);
        Assert.Contains("Overall: FAIL", output.ToString());
    }

    [Fact]
    public void QueriesAreSelectOnlyAndBackfillSetClauseIsHashOnly()
    {
        Assert.StartsWith("SELECT", BackfillVerification.JobsSql.Trim());
        Assert.StartsWith("SELECT", BackfillVerification.IntegritySql.Trim());
        Assert.Contains("HAVING count(*) > 1", BackfillVerification.IntegritySql);
        Assert.Contains("NOT EXISTS", BackfillVerification.IntegritySql);
        Assert.Equal("UPDATE \"Jobs\" SET \"CanonicalApplicationUrlHash\" = @hash",
            PostgresBackfillStore.UpdateSql.Split("WHERE", StringSplitOptions.None)[0].Trim());
        Assert.DoesNotContain("UpdatedAtUtc", PostgresBackfillStore.UpdateSql);
    }
}
