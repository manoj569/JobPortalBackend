using System.Globalization;
using System.Text.Json;

namespace JobPortal.Infrastructure.Services;

// Optional malformed fields are absent, not grounds to discard otherwise valid jobs.
internal static class ExternalJobJson
{
    public static JsonElement Field(this JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) return default;
        if (value.TryGetProperty(name, out var field)) return field;
        // Preserve the previous providers' case-insensitive JSON binding.
        foreach (var property in value.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    public static string? Text(this JsonElement value, string name) =>
        value.Field(name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    public static string? NumericId(this JsonElement value, string name)
    {
        var number = value.Field(name);
        if (number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out var id))
            return id.ToString(CultureInfo.InvariantCulture);
        // JsonSerializerDefaults.Web previously allowed numeric strings as well.
        return number.ValueKind == JsonValueKind.String &&
            long.TryParse(number.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
            ? id.ToString(CultureInfo.InvariantCulture) : null;
    }

    public static string? SingleDepartment(this JsonElement value)
    {
        var departments = value.Field("departments");
        // Multiple departments have no unambiguous category; use source fallback.
        return departments.ValueKind == JsonValueKind.Array && departments.GetArrayLength() == 1
            ? departments[0].Text("name") : null;
    }
}
