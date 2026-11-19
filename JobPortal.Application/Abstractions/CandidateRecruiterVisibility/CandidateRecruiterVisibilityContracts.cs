using JobPortal.Application.Features.CandidateRecruiterVisibility;

namespace JobPortal.Application.Abstractions.CandidateRecruiterVisibility;

public interface ICandidateRecruiterVisibilityService
{
    Task<CandidateRecruiterVisibilityResponse> GetAsync(
        Guid candidateUserId,
        CancellationToken cancellationToken = default);

    Task<CandidateRecruiterVisibilityResponse> UpdateAsync(
        Guid candidateUserId,
        UpdateCandidateRecruiterVisibilityRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICandidateRecruiterVisibilityRepository
{
    Task<bool?> GetVisibilityAsync(
        Guid candidateUserId,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateVisibilityAsync(
        Guid candidateUserId,
        bool isRecruiterVisible,
        CancellationToken cancellationToken = default);
}
