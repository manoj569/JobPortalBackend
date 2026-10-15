using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Services;

public static class ImportedJobIntegrity
{
    public static bool HasMeaningfulTitle(string? title) => !string.IsNullOrWhiteSpace(title) &&
        title.Any(char.IsLetterOrDigit) && title.Trim().ToUpperInvariant() is not ("N/A" or "UNKNOWN" or "NOT SPECIFIED");
    public static bool IsSafeApplicationUrl(string? value) => value is { Length: > 0 and <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) &&
        !IPAddress.TryParse(uri.Host, out _) && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    public static bool MetadataEqual(string? first, string? second)
    {
        if (first == second) return true;
        if (first is null || second is null) return false;
        try { return System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(first), System.Text.Json.Nodes.JsonNode.Parse(second)); }
        catch (JsonException) { return false; }
    }

    public static IndiaJobEligibilityResult Eligibility(Job job)
    {
        try
        {
            if (job.JobSourceId is null && job.AcquisitionSourceId.HasValue && job.SourceMetadataJson is not null)
            {
                // Acquisition imports persist a compact envelope, not RawExternalJob.
                // Location and the normalized workplace enum live on the job itself.
                var metadata = JsonSerializer.Deserialize<AcquisitionGeographyMetadata>(job.SourceMetadataJson);
                return metadata is null ? new(IndiaJobEligibilityDecision.Review, "InvalidSourceMetadata") :
                    IndiaJobEligibilityPolicy.Evaluate(new RawExternalJob
                    {
                        Location = job.Location,
                        WorkplaceType = job.WorkplaceType,
                        CountryCodes = metadata.CountryCodes ?? [],
                        RemoteEligibilityText = metadata.RemoteEligibility,
                        WorkplaceTypeText = metadata.WorkplaceType
                    });
            }
            var raw = job.SourceMetadataJson is null ? new RawExternalJob { Location = job.Location,
                WorkplaceType = job.WorkplaceType } : JsonSerializer.Deserialize<RawExternalJob>(job.SourceMetadataJson);
            return raw is null ? new(IndiaJobEligibilityDecision.Review, "InvalidSourceMetadata") : IndiaJobEligibilityPolicy.Evaluate(raw);
        }
        catch (JsonException) { return new(IndiaJobEligibilityDecision.Review, "InvalidSourceMetadata"); }
    }

    private sealed record AcquisitionGeographyMetadata
    {
        [JsonPropertyName("countryCodes")]
        public IReadOnlyCollection<string>? CountryCodes { get; init; }
        [JsonPropertyName("remoteEligibility")]
        public string? RemoteEligibility { get; init; }
        [JsonPropertyName("workplaceType")]
        public string? WorkplaceType { get; init; }
    }
}
