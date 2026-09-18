using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Referrals;
using JobPortal.Shared.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Services;

public sealed class ClaudeJobUrlExtractionService(
    IHttpClientFactory httpClientFactory,
    IOptions<AiExtractionOptions> options,
    ILogger<ClaudeJobUrlExtractionService> logger) : IJobUrlExtractionService
{
    public const string PageFetchClientName = "JobUrlExtraction.PageFetch";
    public const string AnthropicClientName = "JobUrlExtraction.OpenAI";

    private const int MaxPageCharsSentToModel = 15000;

    // Fix for CA1869: Cache and reuse JsonSerializerOptions
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Fix for CA1848: Use LoggerMessage.Define delegates instead of source generators
    private static readonly Action<ILogger, string, Exception?> LogCouldNotFetchJobUrl =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, nameof(LogCouldNotFetchJobUrl)),
            "Could not fetch job URL {Url} for extraction.");

    private static readonly Action<ILogger, HttpStatusCode, string, Exception?> LogAnthropicExtractionFailed =
        LoggerMessage.Define<HttpStatusCode, string>(LogLevel.Warning, new EventId(2, nameof(LogAnthropicExtractionFailed)),
            "OpenAI extraction call failed: {Status} {Body}");

    private static readonly Action<ILogger, string, Exception?> LogUnexpectedError =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(3, nameof(LogUnexpectedError)),
            "Unexpected error during AI job extraction for {Url}.");

    public async Task<ExtractedJobDetails> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return Failed("Job URL extraction is not configured on this server.");

        string pageText;
        try
        {
            var pageClient = httpClientFactory.CreateClient(PageFetchClientName);
            var html = await pageClient.GetStringAsync(url, cancellationToken);
            pageText = StripHtml(html);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Use the cached delegate
            LogCouldNotFetchJobUrl(logger, url, ex);
            return Failed("Couldn't fetch that page. It may block automated requests — please fill the form manually.");
        }

        if (string.IsNullOrWhiteSpace(pageText))
            return Failed("The page appears to have no readable content. Please fill the form manually.");

        if (pageText.Length > MaxPageCharsSentToModel)
            pageText = pageText[..MaxPageCharsSentToModel];

        try
        {
            var anthropicClient = httpClientFactory.CreateClient(AnthropicClientName);
            var requestBody = new
            {
                model = options.Value.Model,
                max_output_tokens = 1024,
                store = false,
                input = new object[] { new { role = "developer", content = "You extract job posting details from raw page text. " +
                         "Respond with ONLY a single JSON object — no markdown fences, no commentary — " +
                         "matching exactly this shape: " +
                         "{\"title\":string|null,\"companyName\":string|null,\"location\":string|null," +
                         "\"description\":string|null,\"minimumSalary\":number|null,\"maximumSalary\":number|null," +
                         "\"minimumExperienceYears\":number|null,\"maximumExperienceYears\":number|null}. " +
                         "Use null for anything not confidently present in the text. Keep description under 800 characters." },
                    new { role = "user", content = pageText } }
            };

            using var response = await anthropicClient.PostAsJsonAsync("responses", requestBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                // Use the cached delegate
                LogAnthropicExtractionFailed(logger, response.StatusCode, body, null);
                return Failed("AI extraction failed. Please fill the form manually.");
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: cancellationToken);
            var text = payload?.Output?.SelectMany(x => x.Content ?? []).FirstOrDefault(c => c.Type == "output_text")?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return Failed("AI extraction returned no content. Please fill the form manually.");

            // Fix for CA1869: Use the cached JsonOptions
            var extracted = JsonSerializer.Deserialize<ExtractedJobFields>(text, JsonOptions);
            if (extracted is null)
                return Failed("Couldn't parse the extracted job details. Please fill the form manually.");

            return new ExtractedJobDetails(
                extracted.Title, extracted.CompanyName, extracted.Location, extracted.Description,
                extracted.MinimumSalary, extracted.MaximumSalary,
                extracted.MinimumExperienceYears, extracted.MaximumExperienceYears,
                Succeeded: true);
        }
        catch (Exception ex)
        {
            // Use the cached delegate
            LogUnexpectedError(logger, url, ex);
            return Failed("Something went wrong extracting job details. Please fill the form manually.");
        }
    }

    private static ExtractedJobDetails Failed(string reason) =>
        new(null, null, null, null, null, null, null, null, Succeeded: false, FailureReason: reason);

    /// <summary>Very small, dependency-free HTML-to-text step — good enough to hand the model
    /// readable content without pulling in a full HTML parser for an MVP feature.</summary>
    private static string StripHtml(string html)
    {
        var noScripts = System.Text.RegularExpressions.Regex.Replace(
            html, "<script.*?</script>|<style.*?</style>", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var noTags = System.Text.RegularExpressions.Regex.Replace(noScripts, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return System.Text.RegularExpressions.Regex.Replace(decoded, "\\s+", " ").Trim();
    }

    private sealed record ExtractedJobFields(
        string? Title, string? CompanyName, string? Location, string? Description,
        decimal? MinimumSalary, decimal? MaximumSalary,
        int? MinimumExperienceYears, int? MaximumExperienceYears);

    private sealed class OpenAiResponse
    {
        [JsonPropertyName("output")]
        public List<OpenAiOutput>? Output { get; set; }
    }

    private sealed class OpenAiOutput
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("content")]
        public List<OpenAiContent>? Content { get; set; }
    }

    private sealed class OpenAiContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
