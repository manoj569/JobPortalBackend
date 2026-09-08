namespace JobPortal.Domain.Enums;

public enum AIApplyPlanTier { None = 0, Standard = 1, Pro = 2 }
public enum AIApplyRunStatus { Queued = 1, Processing, WaitingForUser, Submitted, Failed, Skipped, Expired, Cancelled, DeadLettered, NeedsReview }
public enum AIApplyFailureClassification { Transient = 1, Permanent, UserActionRequired, SiteUnavailable, UnsupportedFlow, HumanVerificationRequired, AuthenticationRequired, SubscriptionUnavailable, Cancelled, SubmissionUnconfirmed, InternalFailure, OperationalLimitExceeded }
public enum AIApplyQuestionStatus { Pending = 1, Answered, Skipped, Expired }
public enum AIApplyAnswerSource { UserVerified = 1, ProfileDerived, AIGenerated, Unknown }
public enum ApplicationQuestionCategory { Unknown = 0, PersonalInformation, ContactInformation, Location, TechnicalExperience, GeneralExperience, Education, EmploymentHistory, Salary, NoticePeriod, WorkPreference, Relocation, WorkAuthorization, VisaSponsorship, Availability, Resume, Portfolio, LinkedIn, GitHub, RoleMotivation, CompanyMotivation, CareerGoals, Behavioral, LegalDeclaration, ConflictOfInterest, NonCompete, CriminalDeclaration, SecurityClearance, ProtectedDemographic, Disability, Gender, RaceEthnicity, VeteranStatus, Consent }
public enum ApplicationQuestionRisk { LowRisk = 1, ModerateRisk, HighRisk, Sensitive, Legal }
public enum ApplicationAnswerScope { Global = 1, Skill, Role, Company, Job }
public enum ApplicationAnswerType { ShortText = 1, LongText, Numeric, Duration, Boolean, SingleSelect, MultiSelect, File, Url }
public enum ApplicationAnswerResolutionSource { None = 0, UserVerifiedCanonical, UserVerifiedExact, UserVerifiedSemantic, ProfileDerived, ReusableApproved, AIGenerated }
public enum JobSiteIdentifier { Generic = 0, Greenhouse, Lever, Ashby, SmartRecruiters, Workday, LinkedIn, Naukri, Indeed, Foundit, Wellfound }
public enum JobSiteSupportLevel { FullySupported = 1, PartiallySupported, GenericFallback, LoginRequired, HumanVerificationRequired, Unsupported, TemporarilyUnavailable }
public enum JobSiteAdapterHealth { Healthy = 1, Degraded, Failing, Disabled, Unknown }
public enum AIApplyCircuitState { Closed = 1, Open, HalfOpen }
public enum AIApplyWorkerStatus { Running = 1, Inactive }
public enum ExternalJobSiteSessionStatus { Active = 1, Expired, Invalid, Revoked, RequiresReauthentication }
public enum ExternalSessionCaptureStatus { Starting = 1, AwaitingCandidate, Validating, Completed, Failed, Expired, Cancelled }
public enum ExternalSessionValidationStatus { Valid = 1, RequiresLogin, RequiresHumanVerification, Expired, Unsupported, TemporarilyUnavailable, ValidationFailed }
public enum AIApplyRuleType { MinimumSalary = 1, ExcludeNightShift, ExcludeRelocation, ExcludePreviouslyAppliedCompany, RequiredSkill, RequiredQualification, Custom }
public enum AIApplyRuleOperator { Equals = 1, NotEquals, GreaterThanOrEqual, LessThanOrEqual, Contains, NotContains }
public enum AIApplyFailureKind { None = 0, JobExpired, ApplicationUnavailable, LoginRequired, HumanVerificationRequired, WebsiteError, FieldMappingError, MissingUserInformation, Timeout, Duplicate, Unknown, RequiredUserDeclaration, SubmissionUnconfirmed, UnsupportedApplicationFlow, OperationalLimitExceeded }
public enum AIApplyExecutionEvent { ApplicationStarted = 1, BrowserOpened, FormDetected, FieldMapped, FieldFilled, ResumeUploaded, QuestionDetected, WaitingForUser, UserAnswered, ApplicationResumed, SubmissionStarted, SubmissionDetected, ApplicationCompleted, ApplicationFailed, BrowserSessionStarted, NavigationStarted, NavigationCompleted, UnknownQuestionDetected, SubmissionClicked, SubmissionConfirmed, SubmissionUnconfirmed, StepCompleted, BrowserSessionCompleted, QuestionClassified, CanonicalAnswerMatched, SemanticAnswerMatched, ProfileAnswerDerived, AIAnswerGenerated, AIAnswerRejected, UserConfirmationRequired, AnswerMemoryReused, AdapterSelected, ExternalSessionLoaded, ExternalSessionValid, ExternalSessionExpired, ExternalSessionInvalid, ExternalSessionRevoked, LeaseClaimed, LeaseRenewed, LeaseRecovered, DeadLettered, DeadLetterRequeued, CircuitOpenedAutomatically, CircuitOpenedManually, CircuitClosedManually, OperationalLimitExceeded, CandidateContinueRequested }
