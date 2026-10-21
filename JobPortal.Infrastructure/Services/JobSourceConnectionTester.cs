using System.Net.Http.Json;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

// Listing-only health check. Does not invoke ingestion, publication or reconciliation.
public sealed class JobSourceConnectionTester(IHttpClientFactory clients) : IJobSourceConnectionTester
{
    public async Task<JobSourceConnectionResult> TestAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = JobSourceConfiguration.Resolve(source.AtsType, source.CareerPageUrl, source.AtsIdentifier);
        if (!configuration.IsValid) return new(source.AtsType, false, false, false, configuration.ReasonCode, configuration.Message);
        var name = source.AtsType switch
        {
            AtsType.Greenhouse => GreenhouseExternalJobProvider.HttpClientName,
            AtsType.Lever => LeverExternalJobProvider.HttpClientName,
            AtsType.Ashby => AshbyExternalJobProvider.HttpClientName,
            AtsType.SmartRecruiters => SmartRecruitersJobSourceProvider.HttpClientName,
            AtsType.Workday => WorkdayJobSourceProvider.HttpClientName,
            _ => SuccessFactorsJobSourceProvider.HttpClientName
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var reached = false;
        int? status = null;
        try
        {
            using var request = new HttpRequestMessage(source.AtsType == AtsType.Workday ? HttpMethod.Post : HttpMethod.Get, configuration.ListingEndpoint);
            if (source.AtsType == AtsType.Workday)
                request.Content = JsonContent.Create(new { appliedFacets = new Dictionary<string,string[]>(), limit = 1, offset = 0, searchText = "" });
            using var response = await clients.CreateClient(name).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            reached = true; status = (int)response.StatusCode;
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(16 * 1024 * 1024, linked.Token);
            var body = await response.Content.ReadAsStringAsync(linked.Token);
            if (source.AtsType == AtsType.SuccessFactors)
                SuccessFactorsJobSourceProvider.ParseListingPage(body, 0, new Uri(source.CareerPageUrl));
            else
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var valid = source.AtsType switch
                {
                    AtsType.SmartRecruiters => root.Field("content").ValueKind == JsonValueKind.Array && root.Field("totalFound").ValueKind == JsonValueKind.Number,
                    AtsType.Lever => root.ValueKind == JsonValueKind.Array,
                    AtsType.Workday => root.Field("jobPostings").ValueKind == JsonValueKind.Array && root.Field("total").ValueKind == JsonValueKind.Number,
                    _ => root.Field("jobs").ValueKind == JsonValueKind.Array
                };
                if (!valid) throw new JsonException("Provider listing shape is invalid.");
            }
            return new(source.AtsType, true, true, true, "CONNECTION_VALID", "Listing contract verified; no jobs imported.", status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidDataException or OperationCanceledException)
        { return new(source.AtsType, true, reached, false, JobSourceFailure.Code(error), "Provider listing could not be verified.", status); }
    }
}
