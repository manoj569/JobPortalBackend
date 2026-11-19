using JobPortal.Application.Abstractions.CandidateRecruiterVisibility;

namespace JobPortal.Application.Features.CandidateRecruiterVisibility;

public sealed class CandidateRecruiterVisibilityService(
    ICandidateRecruiterVisibilityRepository repository)
    : ICandidateRecruiterVisibilityService
{
    public async Task<CandidateRecruiterVisibilityResponse> GetAsync(
        Guid candidateUserId,
        CancellationToken cancellationToken = default)
    {
        var visibility = await repository.GetVisibilityAsync(
            candidateUserId,
            cancellationToken);

        if (!visibility.HasValue)
        {
            throw new KeyNotFoundException(
                "Candidate profile was not found.");
        }

        return new CandidateRecruiterVisibilityResponse(
            visibility.Value);
    }

    public async Task<CandidateRecruiterVisibilityResponse> UpdateAsync(
        Guid candidateUserId,
        UpdateCandidateRecruiterVisibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await repository.UpdateVisibilityAsync(
            candidateUserId,
            request.IsRecruiterVisible,
            cancellationToken);

        if (!updated)
        {
            throw new KeyNotFoundException(
                "Candidate profile was not found.");
        }

        return new CandidateRecruiterVisibilityResponse(
            request.IsRecruiterVisible);
    }
}
