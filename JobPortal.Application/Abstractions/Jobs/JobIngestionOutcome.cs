namespace JobPortal.Application.Abstractions.Jobs;

public enum JobIngestionOutcome
{
    Created = 1,
    MatchedByUrl = 2,
    MatchedByFingerprint = 3,
    MatchedByFuzzy = 4,
    CompanyNotFound = 5,
    Invalid = 6,
    Failed = 7
}
