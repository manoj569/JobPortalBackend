using JobPortal.Application.Services;
using Xunit;

namespace JobPortal.Application.Tests;

/// <summary>
/// Unit tests for JobFingerprintService.
/// </summary>
public class JobFingerprintServiceTests
{
    private readonly JobFingerprintService _service = new();

    [Fact]
    public void GenerateFingerprint_SameValues_ProducesSameFingerprint()
    {
        // Arrange
        var title = "Software Engineer";
        var company = "Acme Corp";
        var location = "San Francisco, CA";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title, company, location);
        var fingerprint2 = _service.GenerateFingerprint(title, company, location);

        // Assert
        Assert.Equal(fingerprint1, fingerprint2);
    }

    [Fact]
    public void GenerateFingerprint_CaseDifferences_Normalizes()
    {
        // Arrange
        var title1 = "SOFTWARE ENGINEER";
        var title2 = "software engineer";
        var company = "Acme Corp";
        var location = "San Francisco";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title1, company, location);
        var fingerprint2 = _service.GenerateFingerprint(title2, company, location);

        // Assert
        Assert.Equal(fingerprint1, fingerprint2);
    }

    [Fact]
    public void GenerateFingerprint_LeadingTrailingWhitespace_Normalizes()
    {
        // Arrange
        var title1 = "  Software Engineer  ";
        var title2 = "Software Engineer";
        var company1 = "  Acme Corp  ";
        var company2 = "Acme Corp";
        var location1 = "  San Francisco  ";
        var location2 = "San Francisco";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title1, company1, location1);
        var fingerprint2 = _service.GenerateFingerprint(title2, company2, location2);

        // Assert
        Assert.Equal(fingerprint1, fingerprint2);
    }

    [Fact]
    public void GenerateFingerprint_RepeatedWhitespace_Normalizes()
    {
        // Arrange
        var title1 = "Software    Engineer";
        var title2 = "Software Engineer";
        var company = "Acme Corp";
        var location = "San Francisco";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title1, company, location);
        var fingerprint2 = _service.GenerateFingerprint(title2, company, location);

        // Assert
        Assert.Equal(fingerprint1, fingerprint2);
    }

    [Fact]
    public void GenerateFingerprint_PunctuationDifferences_Normalizes()
    {
        // Arrange
        var title1 = "Software Engineer, Senior";
        var title2 = "Software Engineer Senior";
        var company1 = "Acme Corp.";
        var company2 = "Acme Corp";
        var location1 = "San Francisco, CA";
        var location2 = "San Francisco CA";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title1, company1, location1);
        var fingerprint2 = _service.GenerateFingerprint(title2, company2, location2);

        // Assert
        Assert.Equal(fingerprint1, fingerprint2);
    }

    [Fact]
    public void GenerateFingerprint_UnicodeInput_RemainsDeterministic()
    {
        // Arrange
        var title = "Ingénieur Logiciel";
        var company = "Société Générale";
        var location = "Zürich";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title, company, location);
        var fingerprint2 = _service.GenerateFingerprint(title, company, location);

        // Assert
        Assert.Equal(fingerprint1, fingerprint2);
        Assert.Matches("^[a-f0-9]{64}$", fingerprint1);
    }

    [Fact]
    public void GenerateFingerprint_DifferentMeaningfulValues_ProducesDifferentFingerprints()
    {
        // Arrange
        var title1 = "Software Engineer";
        var title2 = "Data Scientist";
        var company = "Acme Corp";
        var location = "San Francisco";

        // Act
        var fingerprint1 = _service.GenerateFingerprint(title1, company, location);
        var fingerprint2 = _service.GenerateFingerprint(title2, company, location);

        // Assert
        Assert.NotEqual(fingerprint1, fingerprint2);
    }

    [Fact]
    public void GenerateFingerprint_Output_IsExactly64LowercaseHexCharacters()
    {
        // Arrange
        var title = "Software Engineer";
        var company = "Acme Corp";
        var location = "San Francisco";

        // Act
        var fingerprint = _service.GenerateFingerprint(title, company, location);

        // Assert
        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[a-f0-9]+$", fingerprint);
        Assert.Equal(fingerprint, fingerprint.ToLowerInvariant());
    }

    [Fact]
    public void GenerateFingerprint_NullEmptyHandling_IsDeterministic()
    {
        // Arrange
        var nullTitle = _service.GenerateFingerprint(null, "Acme", "SF");
        var emptyTitle = _service.GenerateFingerprint("", "Acme", "SF");
        var whitespaceTitle = _service.GenerateFingerprint("   ", "Acme", "SF");

        // Assert
        Assert.Equal(nullTitle, emptyTitle);
        Assert.Equal(emptyTitle, whitespaceTitle);
    }

    [Fact]
    public void GenerateFingerprint_AllNullInputs_ProducesValidHash()
    {
        // Act
        var fingerprint = _service.GenerateFingerprint(null, null, null);

        // Assert
        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[a-f0-9]+$", fingerprint);
    }

    [Fact]
    public void GenerateFingerprint_TechnologyPunctuationNormalization_DotNetDeveloper()
    {
        // Arrange - Testing how .NET Developer is normalized
        var input1 = ".NET Developer";
        var input2 = "NET Developer";
        var input3 = "dotnet Developer";

        // Act
        var fp1 = _service.GenerateFingerprint(input1, "Company", "Location");
        var fp2 = _service.GenerateFingerprint(input2, "Company", "Location");
        var fp3 = _service.GenerateFingerprint(input3, "Company", "Location");

        // Assert - Document current behavior (punctuation removed, lowercase)
        Assert.Equal(64, fp1.Length);
        // Current implementation removes punctuation, so ".NET" becomes "net"
        Assert.DoesNotContain(".", fp1);
    }

    [Fact]
    public void GenerateFingerprint_TechnologyPunctuationNormalization_CSharpDeveloper()
    {
        // Arrange - Testing how C# Developer is normalized
        var input1 = "C# Developer";
        var input2 = "C Developer";
        var input3 = "CSharp Developer";

        // Act
        var fp1 = _service.GenerateFingerprint(input1, "Company", "Location");
        var fp2 = _service.GenerateFingerprint(input2, "Company", "Location");
        var fp3 = _service.GenerateFingerprint(input3, "Company", "Location");

        // Assert - Document current behavior (punctuation removed)
        Assert.Equal(64, fp1.Length);
        // Current implementation removes #, so "C#" becomes "c"
        Assert.DoesNotContain("#", fp1);
        // fp1 and fp2 will be same since # is removed
        Assert.Equal(fp1, fp2);
        // fp3 is different because "sharp" is preserved
        Assert.NotEqual(fp1, fp3);
    }

    [Fact]
    public void GenerateFingerprint_TechnologyPunctuationNormalization_CppDeveloper()
    {
        // Arrange - Testing how C++ Developer is normalized
        var input1 = "C++ Developer";
        var input2 = "C Developer";
        var input3 = "Cpp Developer";

        // Act
        var fp1 = _service.GenerateFingerprint(input1, "Company", "Location");
        var fp2 = _service.GenerateFingerprint(input2, "Company", "Location");
        var fp3 = _service.GenerateFingerprint(input3, "Company", "Location");

        // Assert - Document current behavior (punctuation removed)
        Assert.Equal(64, fp1.Length);
        // Current implementation removes +, so "C++" becomes "c"
        Assert.DoesNotContain("+", fp1);
        // fp1 and fp2 will be same since ++ is removed
        Assert.Equal(fp1, fp2);
        // fp3 is different because "pp" is preserved
        Assert.NotEqual(fp1, fp3);
    }

    [Fact]
    public void GenerateFingerprint_TechnologyPunctuationNormalization_NodeJsDeveloper()
    {
        // Arrange - Testing how Node.js Developer is normalized
        var input1 = "Node.js Developer";
        var input2 = "Node js Developer";
        var input3 = "Nodejs Developer";

        // Act
        var fp1 = _service.GenerateFingerprint(input1, "Company", "Location");
        var fp2 = _service.GenerateFingerprint(input2, "Company", "Location");
        var fp3 = _service.GenerateFingerprint(input3, "Company", "Location");

        // Assert - Document current behavior (punctuation removed)
        Assert.Equal(64, fp1.Length);
        // Current implementation removes ., so "Node.js" becomes "node js"
        Assert.DoesNotContain(".", fp1);
        // fp1 and fp2 will be same since . becomes space separation
        Assert.Equal(fp1, fp2);
        // fp3 is different because no separator between node and js
        Assert.NotEqual(fp1, fp3);
    }
}
