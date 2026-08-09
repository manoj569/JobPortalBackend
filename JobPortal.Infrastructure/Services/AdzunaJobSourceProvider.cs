using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Features.JobDiscovery;
using Microsoft.Extensions.Configuration;

#pragma warning disable CA1725

namespace JobPortal.Infrastructure.Services;

public sealed class AdzunaJobSourceProvider(IHttpClientFactory clients, IConfiguration configuration) : IExternalJobSourceProvider
{
    public const string HttpClientName = "AdzunaJobDiscovery";
    public string Name => "Adzuna";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["JobDiscovery:Adzuna:AppId"]) &&
        !string.IsNullOrWhiteSpace(configuration["JobDiscovery:Adzuna:ApiKey"]);

    public async Task<IReadOnlyCollection<ExternalJobCandidate>> SearchAsync(JobDiscoveryCriteria criteria, CancellationToken ct)
    {
        if (!IsConfigured) return [];
        var country = string.IsNullOrWhiteSpace(criteria.Country) ? "in" : criteria.Country.Trim().ToLowerInvariant();
        var uri = $"v1/api/jobs/{Uri.EscapeDataString(country)}/search/1?app_id={Uri.EscapeDataString(configuration["JobDiscovery:Adzuna:AppId"]!)}&app_key={Uri.EscapeDataString(configuration["JobDiscovery:Adzuna:ApiKey"]!)}&results_per_page=50&what={Uri.EscapeDataString(criteria.Query?.Trim() ?? "")}&where={Uri.EscapeDataString(criteria.Location?.Trim() ?? "")}";
        using var response = await clients.CreateClient(HttpClientName).GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<Response>(cancellationToken: ct);
        return payload?.Results.Select(x => new ExternalJobCandidate(x.Id ?? "", x.Title ?? "", x.Company?.DisplayName ?? "",
            x.Category?.Label ?? "Other", x.RedirectUrl ?? "", x.Location?.DisplayName, x.Description, x.ContractType, x.Created)).ToArray() ?? [];
    }

    private sealed record Response([property: JsonPropertyName("results")] Result[] Results);
    private sealed record Result([property: JsonPropertyName("id")] string? Id, [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description, [property: JsonPropertyName("redirect_url")] string? RedirectUrl,
        [property: JsonPropertyName("created")] DateTime? Created, [property: JsonPropertyName("contract_type")] string? ContractType,
        [property: JsonPropertyName("company")] Label? Company, [property: JsonPropertyName("category")] Category? Category,
        [property: JsonPropertyName("location")] Label? Location);
    private sealed record Label([property: JsonPropertyName("display_name")] string? DisplayName);
    private sealed record Category([property: JsonPropertyName("label")] string? Label);
}
