using JobPortal.Application.Abstractions.Jobs;

namespace JobPortal.Application.Services;

// Validate bounded source fields before any Job/Skill mutation. Long-form employer
// content is stored intact in text columns; identifiers and URLs must never be cut.
public static class ExternalJobStorageContract
{
    public static string? Validate(RawExternalJob raw)
    {
        (string Name, string? Value, int Limit)[] fields =
        [
            (nameof(raw.Title), raw.Title, 250),
            (nameof(raw.Location), raw.Location, 250),
            (nameof(raw.ApplicationUrl), raw.ApplicationUrl, 2048),
            (nameof(raw.DiscoveredUrl), raw.DiscoveredUrl, 2048),
            (nameof(raw.CanonicalJobUrl), raw.CanonicalJobUrl, 2048),
            (nameof(raw.ExternalId), raw.ExternalId, 255),
            (nameof(raw.AcquisitionExternalId), raw.AcquisitionExternalId, 255),
            (nameof(raw.EducationRequirement), raw.EducationRequirement, 200),
            (nameof(raw.CurrencyCode), raw.CurrencyCode, 3)
        ];
        foreach (var (name, value, limit) in fields)
            if (value?.Length > limit)
                return $"Validation:{name} exceeds the storage limit of {limit} characters.";
        foreach (var skill in raw.Skills.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var normalized = string.Join(' ', skill.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (normalized.Length > 150 || normalized.ToUpperInvariant().Length > 150)
                return "Validation:Skill exceeds the storage limit of 150 characters.";
        }
        return null;
    }
}
