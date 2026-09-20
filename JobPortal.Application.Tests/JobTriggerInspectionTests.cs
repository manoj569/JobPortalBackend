using JobPortal.Maintenance;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobTriggerInspectionTests
{
    [Fact]
    public void CheckTriggersHasSeparateExclusiveDispatch()
    {
        Assert.True(TriggerInspection.IsCommand(["job-url-hash-backfill", "--check-triggers"]));
        Assert.Throws<ArgumentException>(() => BackfillOptions.Parse(["job-url-hash-backfill", "--check-triggers"]));
    }

    [Theory]
    [InlineData("--apply")]
    [InlineData("--batch-size")]
    [InlineData("--check-triggers")]
    [InlineData("--unknown")]
    public void MixedOptionsCannotReachInspectionOrBackfill(string option)
    {
        string[] args = ["job-url-hash-backfill", "--check-triggers", option];
        Assert.False(TriggerInspection.IsCommand(args));
        Assert.Throws<ArgumentException>(() => BackfillOptions.Parse(args));
    }

    [Fact]
    public void EmptyResultIsExplicit()
    {
        using var output = new StringWriter();
        TriggerInspection.WriteEmpty(output);
        Assert.Equal("No user-defined triggers found on Jobs" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void ReportsMetadataButRedactsTriggerArguments()
    {
        var output = TriggerInspection.Format("audit_jobs", "BEFORE", "UPDATE", "Disabled",
            "CREATE TRIGGER audit_jobs BEFORE UPDATE ON \"Jobs\" FOR EACH ROW EXECUTE FUNCTION audit('password', 'it''s secret')");
        Assert.Contains("audit_jobs", output);
        Assert.Contains("BEFORE UPDATE", output);
        Assert.Contains("Disabled", output);
        Assert.Contains("EXECUTE FUNCTION audit('[REDACTED]', '[REDACTED]')", output);
        Assert.DoesNotContain("password", output);
        Assert.DoesNotContain("secret", output);
    }

    [Fact]
    public void CatalogQueryIsSelectOnlyAndExcludesInternalTriggers()
    {
        Assert.StartsWith("SELECT", TriggerInspection.QuerySql.Trim());
        Assert.Contains("NOT t.tgisinternal", TriggerInspection.QuerySql);
        Assert.Contains("t.tgrelid = @table", TriggerInspection.QuerySql);
        Assert.Contains("pg_catalog.pg_get_triggerdef", TriggerInspection.QuerySql);
        Assert.DoesNotContain(";", TriggerInspection.QuerySql);
        Assert.DoesNotContain("UPDATE \"Jobs\"", TriggerInspection.QuerySql);
    }
}
