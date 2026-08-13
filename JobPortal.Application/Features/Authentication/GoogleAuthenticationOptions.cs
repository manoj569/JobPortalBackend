namespace JobPortal.Application.Features.Authentication;

public sealed class GoogleAuthenticationOptions
{
    public const string SectionName = "Authentication:Google";
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
}
