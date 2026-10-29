using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
namespace JobPortal.Application.Abstractions.Jobs;

public sealed record JobSourceConnectionResult(AtsType Provider, bool ConfigurationValid, bool ProviderReachable,
    bool ListingReachable, string ReasonCode, string Message, int? StatusCode = null)
{
    public RippleHireDiscoveryResult? Discovery { get; init; }
}

public sealed record JobSourceConnectionTestRequest(AtsType AtsType, string CareerPageUrl, string? AtsIdentifier = null);

public interface IJobSourceConnectionTester
{
    Task<JobSourceConnectionResult> TestAsync(JobSource source, CancellationToken cancellationToken = default);
}
