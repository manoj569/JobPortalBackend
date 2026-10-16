using System.Text.Json;
using JobPortal.Application.Common.Text;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.FresherAcquisition;

// Read the approved worker's exact evidence keys; never infer missing facts.
internal static class TalentdFields
{
    private static JsonElement? Field(FresherImportCandidate candidate, string key) =>
        candidate.SourceMetadata is { ValueKind: JsonValueKind.Object } root &&
        root.TryGetProperty("talentd", out var facts) && facts.ValueKind == JsonValueKind.Object &&
        facts.TryGetProperty(key, out var value) ? value : null;

    public static string? Text(FresherImportCandidate candidate, string key)
    {
        var value = Field(candidate, key);
        var text = value is { ValueKind: JsonValueKind.String } scalar ? scalar.GetString()
            : value is { ValueKind: JsonValueKind.Array } list ? string.Join("\n", list.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())) : null;
        return string.IsNullOrWhiteSpace(text) ? null : new JobPortal.Application.Services.ExternalJobNormalizer().Normalize(
            new JobPortal.Application.Abstractions.Jobs.RawExternalJob { Description = text, DescriptionIsHtml = true }).Description;
    }
    public static string? Education(FresherImportCandidate candidate)
    {
        var value = Text(candidate, "education") ?? Text(candidate, "degree");
        return value?.Length <= 200 ? value : null; // Full evidence remains in SourceMetadataJson.
    }
    private static JsonElement? SalaryValue(FresherImportCandidate candidate)
    {
        var salary = Field(candidate, "salary");
        return salary is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty("value", out var value) ? value : null;
    }
    public static decimal? Salary(FresherImportCandidate candidate, string key)
    {
        var value = SalaryValue(candidate);
        if (value is not { ValueKind: JsonValueKind.Object } range) return null;
        if (!range.TryGetProperty(key, out var amount) && !range.TryGetProperty("value", out amount)) return null;
        return amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var number) && number >= 0 && number <= 9999999999999999.99m ? number : null;
    }
    public static string? Currency(FresherImportCandidate candidate) =>
        Field(candidate, "salary") is { ValueKind: JsonValueKind.Object } salary &&
        salary.TryGetProperty("currency", out var code) && code.ValueKind == JsonValueKind.String &&
        code.GetString() is { Length: 3 } currency && currency.All(char.IsAsciiLetter) ? currency.ToUpperInvariant() : null;
    public static SalaryPeriod? Period(FresherImportCandidate candidate) =>
        SalaryValue(candidate) is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("unitText", out var unit) && unit.ValueKind == JsonValueKind.String
            ? unit.GetString()?.ToUpperInvariant() switch { "YEAR" => SalaryPeriod.Yearly, "MONTH" => SalaryPeriod.Monthly, "HOUR" => SalaryPeriod.Hourly, _ => null } : null;
}
