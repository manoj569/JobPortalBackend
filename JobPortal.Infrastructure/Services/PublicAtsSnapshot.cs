using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

internal static class PublicAtsSnapshot
{
    internal const int MaximumRecords = 20_000;

    internal static ExternalJobSourceSnapshot Validate(JobSource source,
        IReadOnlyCollection<RawExternalJob> records, bool complete)
    {
        if (records.Count > MaximumRecords) throw new InvalidDataException("public_ats_record_limit");
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var jobs = new List<RawExternalJob>();
        var skipped = 0;
        foreach (var job in records)
        {
            if (string.IsNullOrWhiteSpace(job.Title) || string.IsNullOrWhiteSpace(job.Description) ||
                string.IsNullOrWhiteSpace(job.ExternalId) || !ValidApplicationUrl(source, job.ApplicationUrl, job.ExternalId) ||
                !identifiers.Add(job.ExternalId))
            {
                skipped++;
                continue;
            }
            jobs.Add(job);
        }
        return new(jobs, skipped, complete && skipped == 0);
    }

    internal static bool ValidApplicationUrl(JobSource source, string? value, string id)
    {
        if (!SafeHttps(value, out var uri)) return false;
        var parts = uri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var board = source.AtsIdentifier?.Trim();
        var valid = source.AtsType switch
        {
            AtsType.Greenhouse => uri.Host is "boards.greenhouse.io" or "job-boards.greenhouse.io" &&
                parts.Length == 3 && parts[0] == board && parts[1] == "jobs" && parts[2] == id,
            AtsType.Lever => uri.Host is "jobs.lever.co" or "jobs.eu.lever.co" &&
                parts.Length is 2 or 3 && parts[0] == board && parts[1] == id &&
                (parts.Length == 2 || parts[2] == "apply"),
            AtsType.Ashby => uri.Host == "jobs.ashbyhq.com" && parts.Length is 2 or 3 &&
                parts[0] == board && parts[1] == id && (parts.Length == 2 || parts[2] == "application" || parts[2] == "apply"),
            _ => false
        };
        if (valid) return true;
        // Greenhouse can publish official company-hosted pages with its documented gh_jid key.
        return source.AtsType == AtsType.Greenhouse && SafeHttps(source.CareerPageUrl, out var career) &&
            career!.Host == uri.Host && uri.Query.TrimStart('?').Split('&')
                .Count(pair => pair == "gh_jid=" + id) == 1;
    }

    internal static bool SafeHttps(string? value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IsLoopback;

    internal static string? AshbyId(string? url, string board)
    {
        if (!SafeHttps(url, out var uri) || uri!.Host != "jobs.ashbyhq.com") return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == board && Guid.TryParseExact(parts[1], "D", out var id) && id != Guid.Empty
            ? id.ToString("D") : null;
    }

    internal static string[] Strings(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
}
