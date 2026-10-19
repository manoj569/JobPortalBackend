using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobPortal.Infrastructure.Services;

public sealed class EmployerAcquisitionOptions
{
    public string? Endpoint { get; set; }
    public string? Token { get; set; }
}

// Transport to the fixed, operator-configured acquisition worker, not an arbitrary
// admin URL. Public employer requests are vetted/pinned by the worker's SafeWeb.
public sealed class GenericEmployerScanner(IHttpClientFactory clients, IOptions<EmployerAcquisitionOptions> options,
    IJobSourceRunCheckpoint? checkpoint = null, EmployerDirectInventoryScanner? inventory = null,
    IServiceProvider? services = null) : IEmployerDirectInventoryScanner
{
    public const string Identifier = "employer-direct:auto-v1";
    public const string HttpClientName = "EmployerAcquisitionWorker";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExternalJobSourceSnapshot> ScanAsync(JobSource source, CancellationToken cancellationToken)
    {
        if (source.AtsIdentifier == EmployerDirectInventoryContract.Identifier)
            return await (inventory ?? throw new InvalidOperationException("Inventory scanner is not registered.")).ScanAsync(source, cancellationToken);
        if (source.AtsIdentifier != Identifier || checkpoint?.RunId is not { } runId)
            throw new InvalidOperationException("Generic acquisition requires a configured source and durable run.");
        var settings = options.Value;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback) ||
            endpoint.UserInfo.Length != 0 || settings.Token?.Length < 32 || string.IsNullOrEmpty(settings.Token))
            throw new InvalidOperationException("EMPLOYER_ACQUISITION_WORKER_UNCONFIGURED");
        using var client = clients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/employer/scan"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        JsonElement? strategy = string.IsNullOrEmpty(source.EmployerStrategyJson) ? null : JsonSerializer.Deserialize<JsonElement>(source.EmployerStrategyJson);
        request.Content = JsonContent.Create(new { runId, source = new { source.Id, source.CompanyId,
            CompanyName = source.Company?.Name, source.CareerPageUrl, source.AutoPublishEnabled, source.ScanIntervalMinutes, strategy } });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 12_000_000)
            throw new HttpRequestException("Acquisition response too large.");
        await response.Content.LoadIntoBufferAsync(12_000_000, cancellationToken);
        var observed = await response.Content.ReadFromJsonAsync<WorkerObservation>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Acquisition response is empty.");
        if (observed.RunId != runId || observed.SourceId != source.Id || observed.CompanyId != source.CompanyId ||
            observed.CareerPageUrl != source.CareerPageUrl || observed.ObservedAtUtc.Offset != TimeSpan.Zero ||
            observed.Jobs is null || observed.Jobs.Count > 5000)
            throw new InvalidOperationException("Acquisition response source binding failed.");
        var metadata = JsonSerializer.Serialize(new { observed.Strategy, observed.Metrics, observed.Errors,
            observed.CompletionReason, observed.IsComplete, observed.Status }, JsonOptions);
        if (metadata.Length > 128_000) throw new InvalidOperationException("Acquisition strategy evidence too large.");
        // Strategy memory is source configuration evidence, saved by the existing
        // runner/UnitOfWork. No alternate Job or import record is created.
        source.EmployerStrategyJson = JsonSerializer.Serialize(observed.Strategy, JsonOptions);
        source.EmployerDiagnosticsJson = metadata;
        if (observed.ProviderRoutes.Count > 0)
        {
            var jobs = observed.Jobs.ToList();
            var authoritative = observed.Jobs.Count == 0 && observed.ProviderRoutes.Count == 1 &&
                new Uri(source.CareerPageUrl).Host == new Uri(observed.ProviderRoutes[0].ProviderCareerUrl).Host;
            foreach (var route in observed.ProviderRoutes)
            {
                if (!route.ProviderRoutable || !Enum.TryParse<AtsType>(route.Provider, out var type) || type == AtsType.Custom)
                    throw new InvalidOperationException("Unsupported discovered ATS route.");
                var provider = (services ?? throw new InvalidOperationException("Provider resolver is not registered.")).GetServices<IExternalJobProvider>().SingleOrDefault(p => p.AtsType == type)
                    ?? throw new InvalidOperationException("Detected ATS provider is not executable.");
                var routed = new JobSource { Id = source.Id, CompanyId = source.CompanyId, Company = source.Company ?? throw new InvalidOperationException("Detected ATS route requires its source company."),
                    AtsType = type, AtsIdentifier = route.AtsIdentifier, CareerPageUrl = route.ProviderCareerUrl,
                    IsActive = source.IsActive, AutoPublishEnabled = source.AutoPublishEnabled,
                    ScanIntervalMinutes = source.ScanIntervalMinutes };
                var snapshot = provider is ICompleteExternalJobProvider complete
                    ? await complete.FetchSnapshotAsync(routed, cancellationToken)
                    : new ExternalJobSourceSnapshot(await provider.FetchJobsAsync(routed, cancellationToken), 0, false);
                jobs.AddRange(snapshot.Jobs);
                authoritative &= snapshot.IsComplete && snapshot.Skipped == 0 && type != AtsType.Workday;
            }
            return new(jobs, 0, authoritative) { ObservedAtUtc = observed.ObservedAtUtc.UtcDateTime,
                ProviderReasonCounts = new Dictionary<string, int> { ["GenericAtsHandoff"] = 1 } };
        }
        if (observed.IsComplete && observed.CompletionReason != "AUTHORITATIVE_API_CENSUS")
            throw new InvalidOperationException("Unrecognized generic enumeration contract.");
        return new(observed.Jobs, 0, observed.IsComplete) { ObservedAtUtc = observed.ObservedAtUtc.UtcDateTime,
            ProviderReasonCounts = new Dictionary<string, int> { [observed.CompletionReason] = 1 } };
    }

    private sealed record WorkerObservation(Guid RunId, Guid SourceId, Guid CompanyId, string CareerPageUrl,
        DateTimeOffset ObservedAtUtc, bool IsComplete, string CompletionReason, string Status,
        IReadOnlyCollection<RawExternalJob> Jobs, IReadOnlyList<WorkerRoute> ProviderRoutes,
        JsonElement Strategy, JsonElement Metrics, JsonElement Errors);

    private sealed record WorkerRoute(string Provider, [property: JsonPropertyName("ats_identifier")] string AtsIdentifier,
        [property: JsonPropertyName("provider_career_url")] string ProviderCareerUrl,
        [property: JsonPropertyName("provider_routable")] bool ProviderRoutable);
}
