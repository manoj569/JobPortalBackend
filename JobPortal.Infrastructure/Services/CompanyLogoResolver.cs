using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobPortal.Application.Abstractions.AdminManagement;

namespace JobPortal.Infrastructure.Services;

public sealed class CompanyLogoResolver(IHttpClientFactory clients) : ICompanyLogoResolver
{
    public const string HttpClientName = "CompanyLogoDiscovery";
    private static readonly string[] AtsDomains = ["myworkdayjobs.com", "greenhouse.io", "lever.co", "ashbyhq.com"];
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IconSignature = [0, 0, 1, 0];
    private static readonly Regex Tags = new("<(script)\\b([^>]*)>(.*?)</script>|<link\\b([^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
    private static readonly Regex Attributes = new("([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",
        RegexOptions.None, TimeSpan.FromMilliseconds(200));

    public async Task<CompanyLogoResolutionResult> ResolveAsync(string companyName, string? companyWebsiteUrl,
        string? careerPageUrl, CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            // A supplied official website is authoritative; never derive a company domain from an ATS tenant.
            var supplied = string.IsNullOrWhiteSpace(companyWebsiteUrl) ? careerPageUrl : companyWebsiteUrl;
            if (!Uri.TryCreate(supplied, UriKind.Absolute, out var page) || !CompanyLogoHttpTransport.IsSafeUrl(page) || IsAts(page))
                return new(null, "None");
            var html = await FetchAsync(page, false, visited, budget.Token);
            if (html is null) return new(null, "None");
            var candidates = Discover(Encoding.UTF8.GetString(html.Value.Bytes), html.Value.Uri).Take(8);
            foreach (var candidate in candidates)
            {
                // Same-origin only: an official page is not authority for arbitrary third-party images.
                if (candidate.Scheme != html.Value.Uri.Scheme || candidate.Host != html.Value.Uri.Host || !CompanyLogoHttpTransport.IsSafeUrl(candidate)) continue;
                (Uri Uri, byte[] Bytes)? image;
                try { image = await FetchAsync(candidate, true, visited, budget.Token); }
                catch (HttpRequestException) { continue; }
                catch (IOException) { continue; }
                if (image is not null)
                    return new(image.Value.Uri.AbsoluteUri, string.IsNullOrWhiteSpace(companyWebsiteUrl)
                        ? "CareerPageMetadata" : "OfficialWebsiteMetadata");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (IOException) { }
        catch (RegexMatchTimeoutException) { }
        return new(null, "None");
    }

    private static bool IsAts(Uri uri) => AtsDomains.Any(domain => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase));

    private async Task<(Uri Uri, byte[] Bytes)?> FetchAsync(Uri uri, bool image, HashSet<string> visited, CancellationToken token)
    {
        using var client = clients.CreateClient(HttpClientName);
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            var requestKey = new UriBuilder(uri) { Fragment = string.Empty }.Uri.AbsoluteUri;
            if (!CompanyLogoHttpTransport.IsSafeUrl(uri) || IsAts(uri) || !visited.Add(requestKey)) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                if (location is null || !Uri.TryCreate(uri, location, out var next) || next.Host != uri.Host || next.Scheme != uri.Scheme) return null;
                uri = next;
                continue;
            }
            if (!response.IsSuccessStatusCode) return null;
            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (image ? type is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/x-icon" or "image/vnd.microsoft.icon" or "image/svg+xml")
                : type is not ("text/html" or "application/xhtml+xml")) return null;
            var limit = image ? 2 * 1024 * 1024 : 512 * 1024;
            if (response.Content.Headers.ContentLength > limit) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (output.Length + read > limit) return null;
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            if (image && !IsImage(bytes, type!)) return null;
            return (uri, bytes);
        }
        return null;
    }

    internal static bool IsImage(byte[] bytes, string type)
    {
        var span = bytes.AsSpan();
        return type switch
        {
            "image/png" => bytes.Length >= 33 && span.StartsWith(PngSignature) &&
                span.Slice(12, 4).SequenceEqual("IHDR"u8) &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16, 4)) > 0 &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(20, 4)) > 0,
            "image/jpeg" => bytes.Length > 4 && span[0] == 255 && span[1] == 216 && span[2] == 255,
            "image/gif" => bytes.Length >= 13 && (span.StartsWith("GIF87a"u8) || span.StartsWith("GIF89a"u8)),
            "image/webp" => bytes.Length > 12 && span[..4].SequenceEqual("RIFF"u8) && span.Slice(8, 4).SequenceEqual("WEBP"u8),
            "image/x-icon" or "image/vnd.microsoft.icon" => bytes.Length > 6 && span[..4].SequenceEqual(IconSignature),
            // Inspect the root only; never parse XML, resolve entities, or execute SVG.
            "image/svg+xml" => Regex.IsMatch(Encoding.UTF8.GetString(bytes),
                "^\\s*(?:\\uFEFF)?\\s*(?:<\\?xml[^>]*>\\s*)?(?:<!--.*?-->\\s*)*<svg\\b[^<>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200)),
            _ => false
        };
    }

    internal static IEnumerable<Uri> Discover(string html, Uri page)
    {
        html = Regex.Replace(html, "<!--.*?-->", string.Empty, RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
        var logos = new List<string>();
        var icons = new List<string>();
        foreach (Match tag in Tags.Matches(html))
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attr in Attributes.Matches(tag.Groups[2].Value + tag.Groups[4].Value))
                attrs[attr.Groups[1].Value] = WebUtility.HtmlDecode(attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Value);
            if (attrs.TryGetValue("type", out var type) && type.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var json = JsonDocument.Parse(tag.Groups[3].Value, new JsonDocumentOptions { MaxDepth = 16 });
                    ReadOrganizations(json.RootElement, logos);
                }
                catch (JsonException) { }
            }
            if (attrs.TryGetValue("rel", out var rel) && attrs.TryGetValue("href", out var href) &&
                rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(value => value.Equals("icon", StringComparison.OrdinalIgnoreCase) || value.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)))
                icons.Add(href);
        }
        foreach (var value in logos.Concat(icons).Distinct(StringComparer.Ordinal))
            if (Uri.TryCreate(page, value, out var uri)) yield return uri;
    }

    private static void ReadOrganizations(JsonElement element, List<string> logos)
    {
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ReadOrganizations(child, logos);
        if (element.ValueKind != JsonValueKind.Object) return;
        if (element.TryGetProperty("@graph", out var graph)) ReadOrganizations(graph, logos);
        if (!element.TryGetProperty("@type", out var type) || type.ValueKind != JsonValueKind.String ||
            type.GetString() is not ("Organization" or "Corporation") || !element.TryGetProperty("logo", out var logo)) return;
        if (logo.ValueKind == JsonValueKind.Object && !logo.TryGetProperty("url", out logo)) return;
        if (logo.ValueKind == JsonValueKind.String && logo.GetString() is { } url) logos.Add(url);
    }
}
