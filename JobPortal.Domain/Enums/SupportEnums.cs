namespace JobPortal.Domain.Enums;

public enum SupportCategory
{
    Login = 1,
    Registration = 2,
    Account = 3,
    Payment = 4,
    Membership = 5,
    JobApplication = 6,
    Referral = 7,
    InterviewInsights = 8,
    TechnicalIssue = 9,
    Other = 10
}

public enum SupportTicketStatus
{
    Open = 1,
    InProgress = 2,
    Resolved = 3,
    Closed = 4
}
