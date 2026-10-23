using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace JobPortal.Application.Abstractions.Jobs;

public sealed record ExternalJobInventoryEvidence(int Version, Guid Generation, int Count,
    string Digest, string CensusADigest, string CensusBDigest);

public static class ExternalJobInventoryCensus
{
    public const string CompletionReason = "AUTHORITATIVE_IDENTITY_CENSUS";
    public const int MaximumIdentities = 10_000;

    private static readonly JsonSerializerOptions CanonicalJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Digest(IEnumerable<string> ids) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ids.Order(StringComparer.Ordinal).ToArray(), CanonicalJson))));

    public static void Validate(IReadOnlyCollection<string> ids, ExternalJobInventoryEvidence? evidence,
        IEnumerable<RawExternalJob> jobs)
    {
        if (evidence is null || evidence.Version != 1 || evidence.Generation == Guid.Empty ||
            ids.Count > MaximumIdentities || ids.Count != evidence.Count ||
            ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256 || id != id.Trim() || id.Any(c => c < 33 || c > 126)))
            throw new InvalidOperationException("Invalid complete presence census.");
        var unique = ids.ToHashSet(StringComparer.Ordinal);
        if (unique.Count != ids.Count || jobs.Any(job => job.ExternalId is null || !unique.Contains(job.ExternalId)))
            throw new InvalidOperationException("Incomplete or duplicate presence census.");
        var digest = Digest(ids);
        if (evidence.Digest != digest || evidence.CensusADigest != digest || evidence.CensusBDigest != digest)
            throw new InvalidOperationException("Inconsistent independent census evidence.");
    }
}
