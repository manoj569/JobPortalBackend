using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Infrastructure.Services;

/// <summary>
/// Registered configuration-only provider. No HTTP client or complete-snapshot
/// capability is exposed until partner/employer access and the feed are validated.
/// A blocked source must never look like a successful empty job inventory.
/// </summary>
public sealed class OracleRecruitingCloudJobSourceProvider : IExternalJobProvider
{
    public AtsType AtsType => AtsType.OracleRecruitingCloud;

    public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.AtsType != AtsType || OracleRecruitingCloudConfiguration.Parse(source.AtsIdentifier) is not { } config ||
            !config.MatchesCareerPage(source.CareerPageUrl))
            throw new BadRequestException("Invalid Oracle Recruiting Cloud source configuration.", "invalid_oracle_source");
        throw new BadRequestException(OracleRecruitingCloudConfiguration.BlockedMessage, OracleRecruitingCloudConfiguration.ErrorCode);
    }
}
