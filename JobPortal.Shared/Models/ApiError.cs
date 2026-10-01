namespace JobPortal.Shared.Models;

public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Errors = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? LoginMethod = null)
{
    public static ApiError InternalServerError() => new("internal_error", "An unexpected error occurred.");
}
