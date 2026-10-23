namespace JobPortal.Application.Features.JobAggregation;

public static class GenericEmployerContract
{
    public const string Identifier = "employer-direct:auto-v1";
    public const string LegacyIdentifier = "employer-direct";

    public static bool IsGeneric(string? identifier) => identifier is Identifier or LegacyIdentifier;

    public static string Normalize(string? identifier) =>
        string.IsNullOrWhiteSpace(identifier) || IsGeneric(identifier.Trim()) ? Identifier : identifier.Trim();
}
