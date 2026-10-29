using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;

namespace JobPortal.Infrastructure.Services;

// Static public career links only. No JavaScript execution, token guessing, crawling or login flows.
public sealed partial class RippleHireSourceDiscovery(IHttpClientFactory clients) : IRippleHireSourceDiscovery
{
    public const string HttpClientName = "RippleHireDiscovery";
    public async Task<RippleHireDiscoveryResult> DiscoverAsync(string careerPageUrl, string? identifier,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RippleHireSourceConfiguration.IsPublicDiscoveryUrl(careerPageUrl, out var input))
            return Failed("UnsafeInput", "RIPPLEHIRE_DISCOVERY_UNSAFE_URL", "Use a public HTTPS career page on port 443 without credentials or private network addresses.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var directUrl = input!.Fragment == "#list" || input.Fragment.StartsWith("#list/", StringComparison.Ordinal)
                ? RippleHireSourceConfiguration.Normalize(input) : careerPageUrl;
            var direct = RippleHireSourceConfiguration.Resolve(directUrl, identifier);
            if (direct.IsValid)
                return await VerifyAsync(RippleHireSourceConfiguration.Normalize(input!), "ManualUrlVerified", budget.Token);
            // A supplied token or candidate query must not silently fall back to a different board.
            if (!string.IsNullOrWhiteSpace(identifier) || RippleHireSourceConfiguration.IsTenant(input!) && input!.Query.Length != 0)
                return Failed("InvalidConfiguration", direct.ReasonCode, direct.Message);
            if (input!.Fragment.Length != 0)
                return Failed("Unsupported", "RIPPLEHIRE_DISCOVERY_UNSUPPORTED", "Provide the employer career page or tenant root without a job fragment, or a complete public career URL.");
            if (RippleHireSourceConfiguration.IsTenant(input))
            {
                if (input.AbsolutePath is not ("/" or "/candidate/"))
                    return Failed("Unsupported", "RIPPLEHIRE_DISCOVERY_UNSUPPORTED", "Provide the tenant root or /candidate/ entry page; login and legacy paths are unsupported.");
                // The public entry route is verified; tenant roots commonly redirect to employee SSO.
                input = new Uri(input, "/candidate/");
            }
            var (page, html) = await ReadPageAsync(input, budget.Token);
            var candidates = ExtractCandidates(page, html);
            if (candidates.Count > 1)
                return Failed("Ambiguous", "RIPPLEHIRE_DISCOVERY_AMBIGUOUS", "Multiple distinct RippleHire public sources were exposed. Supply the exact employer public career URL containing its token.");
            if (candidates.Count == 0)
                return Failed("NotFound", "RIPPLEHIRE_PUBLIC_TOKEN_NOT_FOUND", "No supported public RippleHire career URL was exposed. Supply the employer's complete https://{tenant}.ripplehire.com/candidate/?token=... URL. Internal shareToken fields are not public source evidence.");
            return await VerifyAsync(candidates.Single(), "Resolved", budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidDataException or JsonException or InvalidOperationException or RegexMatchTimeoutException)
        {
            // Never expose arbitrary HTTP exception messages or token-bearing URLs.
            return Failed("VerificationFailed", "RIPPLEHIRE_DISCOVERY_VERIFICATION_FAILED", "Public discovery or listing verification failed. Check the public career link and retry, or supply a complete working public RippleHire URL. Authentication and anti-bot pages are unsupported.");
        }
    }

    private async Task<RippleHireDiscoveryResult> VerifyAsync(string url, string status, CancellationToken token)
    {
        var configuration = RippleHireSourceConfiguration.Resolve(url, null);
        if (!configuration.IsValid) return Failed("Unsupported", configuration.ReasonCode, configuration.Message);
        using var listing = await RippleHireJobSourceProvider.SearchAsync(clients.CreateClient(RippleHireJobSourceProvider.HttpClientName),
            new Uri(url), configuration.Identifier!, 0, token);
        RippleHireJobSourceProvider.ValidateListing(listing.RootElement);
        return new(status, url, false, "RIPPLEHIRE_SOURCE_VERIFIED", []);
    }

    private async Task<(Uri Page, string Html)> ReadPageAsync(Uri current, CancellationToken token)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var client = clients.CreateClient(HttpClientName);
        for (var hop = 0; hop <= 4; hop++)
        {
            if (!RippleHireSourceConfiguration.IsPublicDiscoveryUrl(current.AbsoluteUri, out _) || !visited.Add(current.AbsoluteUri) ||
                current.AbsolutePath.Split('/').Any(part => part.Equals("auth", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("login", StringComparison.OrdinalIgnoreCase) || part.Equals("sso", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsafe or authentication redirect.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (hop == 4 || response.Headers.Location is not { } location) throw new InvalidDataException("Redirect limit.");
                current = new Uri(current, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                throw new InvalidDataException("Public career HTML required.");
            await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
            return (current, await response.Content.ReadAsStringAsync(timeout.Token));
        }
        throw new InvalidDataException("Redirect limit.");
    }

    internal static IReadOnlyCollection<string> ExtractCandidates(Uri page, string html)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        // A redirect may itself land directly on the supported public candidate URL.
        Add(page);
        foreach (Match tag in PublicTags().Matches(NonContent().Replace(html, "")))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in Attributes().Matches(tag.Groups[2].Value))
                if (!attributes.TryAdd(attribute.Groups[1].Value, WebUtility.HtmlDecode(attribute.Groups[2].Value)))
                    throw new InvalidDataException("Duplicate HTML attribute.");
            var name = tag.Groups[1].Value.ToLowerInvariant();
            string? value = null;
            if (name == "a") attributes.TryGetValue("href", out value);
            else if (name == "iframe") attributes.TryGetValue("src", out value);
            else if (name == "link" && attributes.GetValueOrDefault("rel")?.Equals("canonical", StringComparison.OrdinalIgnoreCase) == true)
                attributes.TryGetValue("href", out value);
            else if (name == "meta" && attributes.GetValueOrDefault("property")?.Equals("og:url", StringComparison.OrdinalIgnoreCase) == true)
                attributes.TryGetValue("content", out value);
            if (value is not null && Uri.TryCreate(page, value, out var candidate)) Add(candidate);
        }
        return found.Values.ToArray();

        void Add(Uri candidate)
        {
            if (!RippleHireSourceConfiguration.IsPublicDiscoveryUrl(candidate.AbsoluteUri, out _)) return;
            if (candidate.Fragment.Length != 0 && candidate.Fragment != "#list" && !candidate.Fragment.StartsWith("#list/", StringComparison.Ordinal)) return;
            var normalized = RippleHireSourceConfiguration.Normalize(candidate);
            var configuration = RippleHireSourceConfiguration.Resolve(normalized, null);
            if (!configuration.IsValid) return;
            // Repeated links to filtered views or differing attribution do not create different source identities.
            var identity = candidate.Host + "/" + configuration.Identifier;
            found.TryAdd(identity, normalized);
        }
    }

    private static RippleHireDiscoveryResult Failed(string status, string code, string message) => new(status, null, true, code, [message]);

    [GeneratedRegex("<(a|iframe|link|meta)\\b((?:[^>\"']|\"[^\"]*\"|'[^']*')*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PublicTags();
    [GeneratedRegex("(?:^|\\s)([a-zA-Z][a-zA-Z0-9:_-]*)\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Attributes();
    [GeneratedRegex("<!--.*?-->|<(script|style)\\b[^>]*>.*?</\\1\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex NonContent();
}
