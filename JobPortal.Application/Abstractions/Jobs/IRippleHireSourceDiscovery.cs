using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Abstractions.Jobs;

public sealed record RippleHireDiscoveryResult(string Status, string? ResolvedCareerPageUrl,
    bool ManualConfigurationRequired, string ReasonCode, IReadOnlyCollection<string> ValidationErrors)
{
    public bool Succeeded => Status is "Resolved" or "ManualUrlVerified";
}

public interface IRippleHireSourceDiscovery
{
    Task<RippleHireDiscoveryResult> DiscoverAsync(string careerPageUrl, string? identifier, CancellationToken cancellationToken = default);
}

public sealed class RippleHireDiscoveryException(RippleHireDiscoveryResult discovery)
    : AppException(string.Join(" ", discovery.ValidationErrors), 400, discovery.ReasonCode)
{
    public RippleHireDiscoveryResult Discovery { get; } = discovery;
}
