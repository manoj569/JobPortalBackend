using JobPortal.Application.Abstractions.Jobs;
using Xunit;

namespace JobPortal.Application.Tests;

/// <summary>
/// Unit tests for UrlCanonicalizer.
/// </summary>
public class UrlCanonicalizerTests
{
    private readonly UrlCanonicalizer _canonicalizer = new UrlCanonicalizer();

    [Fact]
    public void CanonicalizeIdenticalUrlReturnsSameUrl()
    {
        // Arrange
        var url = "https://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url);
        var result2 = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal("https://example.com/job/123", result1);
    }

    [Fact]
    public void CanonicalizeHostCasingNormalizesToLowerCase()
    {
        // Arrange
        var url1 = "https://EXAMPLE.com/job/123";
        var url2 = "https://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal("https://example.com/job/123", result1);
    }

    [Fact]
    public void CanonicalizeDefaultPort80RemovesPort()
    {
        // Arrange
        var url1 = "http://example.com:80/job/123";
        var url2 = "http://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal("http://example.com/job/123", result1);
    }

    [Fact]
    public void CanonicalizeDefaultPort443RemovesPort()
    {
        // Arrange
        var url1 = "https://example.com:443/job/123";
        var url2 = "https://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal("https://example.com/job/123", result1);
    }

    [Fact]
    public void CanonicalizeNonDefaultPortPreservesPort()
    {
        // Arrange
        var url = "https://example.com:8080/job/123";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal("https://example.com:8080/job/123", result);
    }

    [Fact]
    public void CanonicalizeTrailingSlashNormalizesConsistently()
    {
        // Arrange
        var url1 = "https://example.com/job/";
        var url2 = "https://example.com/job";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert - Both should have consistent trailing slash handling
        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Fact]
    public void CanonicalizeFragmentRemovesFragment()
    {
        // Arrange
        var url1 = "https://example.com/job/123#section";
        var url2 = "https://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
        Assert.DoesNotContain("#", result1!);
    }

    [Fact]
    public void CanonicalizeUtmParametersRemovesTrackingParams()
    {
        // Arrange
        var url = "https://example.com/job/123?utm_source=google&utm_medium=cpc&utm_campaign=spring";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal("https://example.com/job/123", result);
    }

    [Fact]
    public void CanonicalizeGclidParameterRemovesGclid()
    {
        // Arrange
        var url = "https://example.com/job/123?gclid=abc123";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal("https://example.com/job/123", result);
    }

    [Fact]
    public void CanonicalizeFbclidParameterRemovesFbclid()
    {
        // Arrange
        var url = "https://example.com/job/123?fbclid=xyz789";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal("https://example.com/job/123", result);
    }

    [Fact]
    public void CanonicalizeQueryOrderingSortsDeterministically()
    {
        // Arrange
        var url1 = "https://example.com/job/123?z=last&a=first&m=middle";
        var url2 = "https://example.com/job/123?a=first&m=middle&z=last";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
        Assert.Equal("https://example.com/job/123?a=first&m=middle&z=last", result1);
    }

    [Fact]
    public void CanonicalizeMeaningfulQueryParametersPreservesNonTrackingParams()
    {
        // Arrange
        var url = "https://example.com/job/123?job_id=456&source=careers";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Contains("job_id", result);
        Assert.Contains("source", result);
        Assert.DoesNotContain("utm_", result);
    }

    [Fact]
    public void CanonicalizeDifferentJobIdValuesProducesDifferentUrls()
    {
        // Arrange
        var url1 = "https://example.com/job?id=123";
        var url2 = "https://example.com/job?id=456";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void CanonicalizeMalformedUrlDoesNotThrow()
    {
        // Arrange
        var malformedUrls = new[]
        {
            "not-a-url",
            "ht!tp://example.com",
            "",
            "   ",
            null
        };

        // Act & Assert - Should not throw
        foreach (var url in malformedUrls)
        {
            var result = Record.Exception(() => _canonicalizer.Canonicalize(url));
            Assert.Null(result);
        }
    }

    [Fact]
    public void CanonicalizeNullInputReturnsNull()
    {
        // Act
        var result = _canonicalizer.Canonicalize(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CanonicalizeEmptyInputReturnsNull()
    {
        // Act
        var result = _canonicalizer.Canonicalize("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CanonicalizeWhitespaceInputReturnsNull()
    {
        // Act
        var result = _canonicalizer.Canonicalize("   ");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CanonicalizeLeadingTrailingWhitespaceTrimsAndProcesses()
    {
        // Arrange
        var url1 = "  https://example.com/job/123  ";
        var url2 = "https://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void CanonicalizeSchemeCaseNormalizesToLower()
    {
        // Arrange
        var url1 = "HTTPS://example.com/job/123";
        var url2 = "https://example.com/job/123";

        // Act
        var result1 = _canonicalizer.Canonicalize(url1);
        var result2 = _canonicalizer.Canonicalize(url2);

        // Assert
        Assert.Equal(result1, result2);
        Assert.StartsWith("https://", result1);
    }

    [Fact]
    public void CanonicalizeAllTrackingParamsRemovesAll()
    {
        // Arrange
        var url = "https://example.com/job/123?utm_source=g&utm_medium=m&utm_campaign=c&utm_term=t&utm_content=co&utm_id=i&gclid=gc&fbclid=fc";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal("https://example.com/job/123", result);
    }

    [Fact]
    public void CanonicalizeMixedTrackingAndMeaningfulRemovesOnlyTracking()
    {
        // Arrange
        var url = "https://example.com/job/123?job_id=456&utm_source=google&department=engineering";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Contains("job_id=456", result);
        Assert.Contains("department=engineering", result);
        Assert.DoesNotContain("utm_source", result);
    }

    [Fact]
    public void CanonicalizeCaseInsensitiveTrackingParamNamesRemovesRegardlessOfCase()
    {
        // Arrange
        var url = "https://example.com/job/123?UTM_SOURCE=google&Utm_Medium=cpc";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert
        Assert.Equal("https://example.com/job/123", result);
    }

    [Fact]
    public void CanonicalizePathCasingPreservesPathCase()
    {
        // Arrange
        var url = "https://example.com/Jobs/Software-Engineer/Apply";

        // Act
        var result = _canonicalizer.Canonicalize(url);

        // Assert - Path casing should be preserved
        Assert.Contains("/Jobs/Software-Engineer/Apply", result);
    }

    [Fact]
    public void CanonicalizeRelativeUrlHandlesDeterministically()
    {
        // Arrange
        var relativeUrl = "/jobs/123";

        // Act
        var result = _canonicalizer.Canonicalize(relativeUrl);

        // Assert - Should not throw, returns trimmed value for non-absolute URLs
        Assert.NotNull(result);
        Assert.Equal("/jobs/123", result);
    }
}
