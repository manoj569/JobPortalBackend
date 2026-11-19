namespace JobPortal.Application.Features.CandidateRecruiterVisibility;

public sealed class UpdateCandidateRecruiterVisibilityRequest
{
    public bool IsRecruiterVisible { get; set; }
}

public sealed record CandidateRecruiterVisibilityResponse(
    bool IsRecruiterVisible);
