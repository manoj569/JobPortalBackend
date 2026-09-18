namespace JobPortal.Shared.Options;

public sealed class AiExtractionOptions
{
    public const string SectionName = "AiExtraction";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.6-luna";
    public int TimeoutSeconds { get; set; } = 20;
}
