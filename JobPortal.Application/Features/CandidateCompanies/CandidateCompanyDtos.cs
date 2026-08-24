namespace JobPortal.Application.Features.CandidateCompanies;

public sealed record CompanyOption(Guid Id, string CompanyName);
public sealed record CreateCandidateCompanyRequest(string CompanyName);
public sealed record CreateCandidateCompanyResponse(Guid Id, string CompanyName, bool Created);
