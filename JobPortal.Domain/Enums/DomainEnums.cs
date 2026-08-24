namespace JobPortal.Domain.Enums;

public enum UserStatus { Pending = 1, Active, Suspended, Inactive }
public enum MembershipStatus { Pending = 1, Active, Suspended, Cancelled, Expired }
public enum PaymentStatus
{
    Pending = 1,
    Authorized,
    Paid,
    Failed,
    Refunded,
    Cancelled,
    Created,
    Expired
}
public enum PaymentProvider { Unknown = 0, Stripe, Razorpay, PayPal, BankTransfer, PhonePe }
public enum JobStatus { Draft = 1, Published, Paused, Closed, Expired, Archived }
public enum ResumeExtractionStatus { NotStarted = 0, Processing, Ready, Failed }
public enum EmploymentType { FullTime = 1, PartTime, Contract, Internship, Freelance, Temporary }
public enum WorkplaceType { OnSite = 1, Remote, Hybrid }
public enum ExperienceLevel { Entry = 1, Junior, Mid, Senior, Lead, Executive }
public enum CompanyType { Startup = 1, Corporate, IndianMnc, ForeignMnc, Consultant }
public enum CompanySubmissionSource { Administrative = 0, CandidateSubmitted = 1 }
public enum PostedByType { Company = 1, Consultant }
public enum PublicJobSort
{
    Recommended = 1,
    LatestPublished,
    PublishedAt = LatestPublished,
    NewestAdded,
    CreatedAt = NewestAdded,
    ClosingSoon,
    ExpiresAt = ClosingSoon,
    SalaryHighToLow,
    Title,
    MinimumSalary,
    MaximumSalary
}

public enum JobHistoryAction { Viewed = 1, Saved, Applied, Withdrawn, Rejected, Shortlisted, Hired }
public enum NotificationType
{
    Application,
    Profile,
    Payment,
    Membership,
    Security,
    System
}
public enum AuditAction
{
    Create = 1,
    Update,
    Delete,
    Restore,
    Login,
    Logout,
    Publish,
    Unpublish,
    Close,
    Archive,
    Feature,
    Unfeature,
    Upload,
    Submit,
    Withdraw,
    Confirm,
    WebhookSuccess,
    WebhookFailure,
    Activate,
    View
}
public enum ApplicationQuotaPeriod
{
    FreeMonthly = 1,
    PremiumDaily = 2
}
public enum SettingScope { Global = 1, User, Company }
public enum JobApplicationStatus { Submitted = 1, Reviewed, Shortlisted, Rejected, Withdrawn, ExternalApplicationStarted }
public enum ApplicationMethod { Portal = 1, External }
public enum CareerStage { Student = 1, Fresher, Experienced }
public enum DesiredOpportunity { Internship = 1, FresherJob, ExperiencedJob }
public enum WorkPreference { Remote = 1, Hybrid, OnSite }
public enum OtpPurpose { Registration = 1, Login }
