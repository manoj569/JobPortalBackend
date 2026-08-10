CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "Categories" (
    "Id" uuid NOT NULL,
    "Name" character varying(150) NOT NULL,
    "Slug" character varying(170) NOT NULL,
    "Description" character varying(1000),
    "DisplayOrder" integer NOT NULL,
    "ParentCategoryId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Categories" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Categories_Categories_ParentCategoryId" FOREIGN KEY ("ParentCategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JobDiscoveryRuns" (
    "Id" uuid NOT NULL,
    "Trigger" character varying(32) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "StartedAtUtc" timestamp with time zone NOT NULL,
    "CompletedAtUtc" timestamp with time zone,
    "CandidateCount" integer NOT NULL,
    "DuplicateCount" integer NOT NULL,
    "ImportedCount" integer NOT NULL,
    "ErrorSummary" character varying(2000),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_JobDiscoveryRuns" PRIMARY KEY ("Id")
);

CREATE TABLE "Roles" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NormalizedName" character varying(100) NOT NULL,
    "Description" character varying(1000),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Roles" PRIMARY KEY ("Id")
);

CREATE TABLE "Skills" (
    "Id" uuid NOT NULL,
    "Name" character varying(150) NOT NULL,
    "NormalizedName" character varying(150) NOT NULL,
    "Description" character varying(1000),
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Skills" PRIMARY KEY ("Id")
);

CREATE TABLE "JobDiscoveryItems" (
    "Id" uuid NOT NULL,
    "RunId" uuid NOT NULL,
    "Provider" character varying(64) NOT NULL,
    "SourceJobId" character varying(256) NOT NULL,
    "Title" character varying(300) NOT NULL,
    "CompanyName" character varying(200) NOT NULL,
    "CategoryName" character varying(200) NOT NULL,
    "ApplicationUrl" character varying(2048) NOT NULL,
    "Location" character varying(300),
    "Description" text,
    "EmploymentType" character varying(50),
    "PublishedAtUtc" timestamp with time zone,
    "Status" character varying(32) NOT NULL,
    "DuplicateReason" character varying(64),
    "ExistingJobId" uuid,
    "ImportedJobId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_JobDiscoveryItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_JobDiscoveryItems_JobDiscoveryRuns_RunId" FOREIGN KEY ("RunId") REFERENCES "JobDiscoveryRuns" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "Email" character varying(256) NOT NULL,
    "NormalizedEmail" character varying(256) NOT NULL,
    "PasswordHash" character varying(512) NOT NULL,
    "FirstName" character varying(100) NOT NULL,
    "LastName" character varying(100) NOT NULL,
    "PhoneNumber" character varying(32),
    "NormalizedPhoneNumber" character varying(13),
    "TermsAndPrivacyAcceptedAtUtc" timestamp with time zone,
    "TermsAndPrivacyVersion" character varying(32),
    "PhoneConfirmed" boolean NOT NULL,
    "ProfileImageUrl" character varying(2048),
    "Headline" character varying(250),
    "Bio" character varying(4000),
    "Location" character varying(250),
    "LinkedInUrl" character varying(2048),
    "PortfolioUrl" character varying(2048),
    "SkillsJson" text NOT NULL DEFAULT '[]',
    "EducationJson" text NOT NULL DEFAULT '[]',
    "ExperienceJson" text NOT NULL DEFAULT '[]',
    "PreferredJobTypesJson" text NOT NULL DEFAULT '[]',
    "CareerStage" integer,
    "DesiredOpportunitiesJson" text NOT NULL DEFAULT '[]',
    "WorkPreferencesJson" text NOT NULL DEFAULT '[]',
    "College" character varying(200),
    "Degree" character varying(200),
    "GraduationYear" integer,
    "YearsOfExperience" numeric(4,1),
    "OnboardingCompletedAtUtc" timestamp with time zone,
    "ResumeStorageKey" character varying(255),
    "ResumeFileName" character varying(255),
    "ResumeContentType" character varying(100),
    "ResumeSizeBytes" bigint,
    "ResumeUploadedAtUtc" timestamp with time zone,
    "Status" integer NOT NULL,
    "EmailConfirmed" boolean NOT NULL,
    "LastLoginAtUtc" timestamp with time zone,
    "PasswordResetTokenHash" character varying(64),
    "PasswordResetTokenExpiresAtUtc" timestamp with time zone,
    "EmailVerificationTokenHash" character varying(64),
    "EmailVerificationTokenExpiresAtUtc" timestamp with time zone,
    "EmailVerificationSentAtUtc" timestamp with time zone,
    "RoleId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Users_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "ApplicationQuotaUsages" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Period" integer NOT NULL,
    "PeriodStartsAtUtc" timestamp with time zone NOT NULL,
    "PeriodEndsAtUtc" timestamp with time zone NOT NULL,
    "UsedApplications" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_ApplicationQuotaUsages" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ApplicationQuotaUsages_UsedApplications" CHECK ("UsedApplications" >= 0),
    CONSTRAINT "FK_ApplicationQuotaUsages_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AuditLogs" (
    "Id" uuid NOT NULL,
    "Action" integer NOT NULL,
    "EntityName" character varying(200) NOT NULL,
    "EntityId" character varying(64) NOT NULL,
    "ChangesJson" text,
    "ActorRole" character varying(50),
    "CorrelationId" character varying(64),
    "IpAddress" character varying(45),
    "UserAgent" character varying(1024),
    "UserId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AuditLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CandidateResumeProfiles" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "ExtractionStatus" integer NOT NULL,
    "SkillsJson" varchar(4000) NOT NULL,
    "RoleKeywordsJson" varchar(2000) NOT NULL,
    "EducationKeywordsJson" varchar(2000) NOT NULL,
    "LocationsJson" varchar(2000) NOT NULL,
    "YearsOfExperience" numeric(4,1),
    "ExtractionError" character varying(1000),
    "ExtractedAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_CandidateResumeProfiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CandidateResumeProfiles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Companies" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Slug" character varying(220) NOT NULL,
    "Description" character varying(4000),
    "WebsiteUrl" character varying(2048),
    "LogoUrl" character varying(2048),
    "Industry" character varying(150),
    "Location" character varying(250),
    "EmployeeCount" integer,
    "CompanyType" integer,
    "IsVerified" boolean NOT NULL,
    "OwnerUserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Companies" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Companies_Users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Memberships" (
    "Id" uuid NOT NULL,
    "PlanName" character varying(100) NOT NULL,
    "Status" integer NOT NULL,
    "StartsAtUtc" timestamp with time zone NOT NULL,
    "EndsAtUtc" timestamp with time zone,
    "AutoRenew" boolean NOT NULL,
    "UserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Memberships" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Memberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Notifications" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Message" character varying(4000) NOT NULL,
    "Type" integer NOT NULL,
    "ActionUrl" character varying(2048),
    "IsRead" boolean NOT NULL,
    "ReadAtUtc" timestamp with time zone,
    "UserId1" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Notifications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Notifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Notifications_Users_UserId1" FOREIGN KEY ("UserId1") REFERENCES "Users" ("Id")
);

CREATE TABLE "PendingRegistrations" (
    "Id" uuid NOT NULL,
    "Email" character varying(256) NOT NULL,
    "NormalizedEmail" character varying(256) NOT NULL,
    "PasswordHash" character varying(512),
    "FirstName" character varying(100) NOT NULL,
    "LastName" character varying(100) NOT NULL,
    "NormalizedPhoneNumber" character varying(13) NOT NULL,
    "TermsAndPrivacyAcceptedAtUtc" timestamp with time zone NOT NULL,
    "TermsAndPrivacyVersion" character varying(32) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "ClosedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "CompletedUserId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_PendingRegistrations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PendingRegistrations_Users_CompletedUserId" FOREIGN KEY ("CompletedUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "RefreshTokens" (
    "Id" uuid NOT NULL,
    "Token" character varying(512) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "RevokedAtUtc" timestamp with time zone,
    "ReplacedByToken" character varying(512),
    "CreatedByIp" character varying(45),
    "RevokedByIp" character varying(45),
    "UserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Settings" (
    "Id" uuid NOT NULL,
    "Key" character varying(200) NOT NULL,
    "Value" text NOT NULL,
    "Description" character varying(1000),
    "Scope" integer NOT NULL,
    "UserId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Settings" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Settings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Jobs" (
    "Id" uuid NOT NULL,
    "ReferenceNumber" character varying(50) NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Slug" character varying(270) NOT NULL,
    "Description" character varying(16000) NOT NULL,
    "Responsibilities" character varying(8000),
    "Requirements" character varying(8000),
    "Benefits" character varying(4000),
    "ApplicationUrl" character varying(2048) NOT NULL,
    "Location" character varying(250),
    "MinimumSalary" numeric(18,2),
    "MaximumSalary" numeric(18,2),
    "CurrencyCode" character(3) NOT NULL,
    "EmploymentType" integer NOT NULL,
    "WorkplaceType" integer NOT NULL,
    "ExperienceLevel" integer NOT NULL,
    "MinimumExperienceYears" integer,
    "MaximumExperienceYears" integer,
    "InternshipDurationMonths" integer,
    "IsFlexibleDuration" boolean NOT NULL,
    "Department" character varying(150),
    "RoleCategory" character varying(150),
    "EducationRequirement" character varying(200),
    "PostedByType" integer,
    "Status" integer NOT NULL,
    "IsFeatured" boolean NOT NULL,
    "IsHidden" boolean NOT NULL,
    "PublishedAtUtc" timestamp with time zone,
    "ExpiresAtUtc" timestamp with time zone,
    "CompanyId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Jobs" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Jobs_ExperienceRange" CHECK ("MinimumExperienceYears" IS NULL OR "MaximumExperienceYears" IS NULL OR "MinimumExperienceYears" <= "MaximumExperienceYears"),
    CONSTRAINT "CK_Jobs_InternshipDuration" CHECK ("InternshipDurationMonths" IS NULL OR "InternshipDurationMonths" IN (1, 2, 3, 6)),
    CONSTRAINT "CK_Jobs_SalaryRange" CHECK ("MinimumSalary" IS NULL OR "MaximumSalary" IS NULL OR "MinimumSalary" <= "MaximumSalary"),
    CONSTRAINT "FK_Jobs_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Jobs_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "MembershipHistory" (
    "Id" uuid NOT NULL,
    "PreviousStatus" integer,
    "CurrentStatus" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "Reason" character varying(1000),
    "MembershipId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_MembershipHistory" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MembershipHistory_Memberships_MembershipId" FOREIGN KEY ("MembershipId") REFERENCES "Memberships" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MembershipHistory_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Payments" (
    "Id" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "CurrencyCode" character varying(3) NOT NULL,
    "Status" integer NOT NULL,
    "Provider" integer NOT NULL,
    "TransactionReference" character varying(100),
    "ProviderPaymentId" character varying(200),
    "ProviderOrderId" character varying(200),
    "ProviderReceipt" character varying(100),
    "ProviderOrderCreatedAtUtc" timestamp with time zone,
    "LastReconciledAtUtc" timestamp with time zone,
    "PaidAtUtc" timestamp with time zone,
    "UserId" uuid NOT NULL,
    "MembershipId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_Payments" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Payments_Amount" CHECK ("Amount" >= 0),
    CONSTRAINT "FK_Payments_Memberships_MembershipId" FOREIGN KEY ("MembershipId") REFERENCES "Memberships" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Payments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "OtpChallenges" (
    "Id" uuid NOT NULL,
    "Purpose" integer NOT NULL,
    "NormalizedPhoneNumber" character varying(13) NOT NULL,
    "OtpHash" character varying(64) NOT NULL,
    "ExpiresAtUtc" timestamp with time zone NOT NULL,
    "FailedAttemptCount" integer NOT NULL,
    "SendCount" integer NOT NULL,
    "LastSentAtUtc" timestamp with time zone NOT NULL,
    "VerifiedAtUtc" timestamp with time zone,
    "ResetChallengeExpiresAtUtc" timestamp with time zone,
    "ConsumedAtUtc" timestamp with time zone,
    "UserId" uuid,
    "PendingRegistrationId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_OtpChallenges" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_OtpChallenges_FailedAttemptCount" CHECK ("FailedAttemptCount" BETWEEN 0 AND 5),
    CONSTRAINT "CK_OtpChallenges_SendCount" CHECK ("SendCount" >= 1),
    CONSTRAINT "FK_OtpChallenges_PendingRegistrations_PendingRegistrationId" FOREIGN KEY ("PendingRegistrationId") REFERENCES "PendingRegistrations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_OtpChallenges_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JobApplications" (
    "Id" uuid NOT NULL,
    "Status" integer NOT NULL,
    "ApplicationMethod" integer NOT NULL,
    "CoverLetter" character varying(5000),
    "ResumeStorageKey" character varying(255),
    "ResumeFileName" character varying(255),
    "ResumeContentType" character varying(100),
    "SubmittedAtUtc" timestamp with time zone NOT NULL,
    "WithdrawnAtUtc" timestamp with time zone,
    "UserId" uuid NOT NULL,
    "JobId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_JobApplications" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_JobApplications_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_JobApplications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JobRecruiterContacts" (
    "Id" uuid NOT NULL,
    "JobId" uuid NOT NULL,
    "ContactName" character varying(150) NOT NULL,
    "ContactRole" character varying(150) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "PhoneNumber" character varying(32),
    "IsSharingApproved" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_JobRecruiterContacts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_JobRecruiterContacts_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE CASCADE
);

CREATE TABLE "JobSkills" (
    "Id" uuid NOT NULL,
    "JobId" uuid NOT NULL,
    "SkillId" uuid NOT NULL,
    "IsRequired" boolean NOT NULL,
    "ProficiencyLevel" smallint NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_JobSkills" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_JobSkills_ProficiencyLevel" CHECK ("ProficiencyLevel" BETWEEN 1 AND 5),
    CONSTRAINT "FK_JobSkills_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_JobSkills_Skills_SkillId" FOREIGN KEY ("SkillId") REFERENCES "Skills" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "SavedJobs" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "JobId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_SavedJobs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SavedJobs_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SavedJobs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "UserJobHistories" (
    "Id" uuid NOT NULL,
    "Action" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "Notes" character varying(2000),
    "UserId" uuid NOT NULL,
    "JobId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_UserJobHistories" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_UserJobHistories_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_UserJobHistories_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PaymentHistory" (
    "Id" uuid NOT NULL,
    "PreviousStatus" integer,
    "CurrentStatus" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "ProviderEventId" character varying(200),
    "Reason" character varying(1000),
    "PaymentId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_PaymentHistory" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PaymentHistory_Payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PaymentHistory_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JobApplicationStatusHistory" (
    "Id" uuid NOT NULL,
    "PreviousStatus" integer,
    "NewStatus" integer NOT NULL,
    "ChangedAtUtc" timestamp with time zone NOT NULL,
    "InternalNote" character varying(2000),
    "ApplicationId" uuid NOT NULL,
    "ActorUserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_JobApplicationStatusHistory" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_JobApplicationStatusHistory_JobApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "JobApplications" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_JobApplicationStatusHistory_Users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

INSERT INTO "Roles" ("Id", "CreatedAtUtc", "DeletedAtUtc", "Description", "IsDeleted", "Name", "NormalizedName", "UpdatedAtUtc")
VALUES ('2bdd5ba8-2fb0-476c-b9db-6696c1c94290', TIMESTAMPTZ '2025-01-01T00:00:00Z', NULL, 'Company employer', FALSE, 'Employer', 'EMPLOYER', NULL);
INSERT INTO "Roles" ("Id", "CreatedAtUtc", "DeletedAtUtc", "Description", "IsDeleted", "Name", "NormalizedName", "UpdatedAtUtc")
VALUES ('3ec6976c-8752-48f5-a14f-1c81b6522c5d', TIMESTAMPTZ '2025-01-01T00:00:00Z', NULL, 'Job candidate', FALSE, 'Candidate', 'CANDIDATE', NULL);
INSERT INTO "Roles" ("Id", "CreatedAtUtc", "DeletedAtUtc", "Description", "IsDeleted", "Name", "NormalizedName", "UpdatedAtUtc")
VALUES ('a2216ece-d9a7-4c61-9bda-530e64d50c01', TIMESTAMPTZ '2025-01-01T00:00:00Z', NULL, 'System administrator', FALSE, 'Administrator', 'ADMINISTRATOR', NULL);

CREATE INDEX "IX_ApplicationQuotaUsages_IsDeleted" ON "ApplicationQuotaUsages" ("IsDeleted");

CREATE UNIQUE INDEX "IX_ApplicationQuotaUsages_UserId_Period_PeriodStartsAtUtc" ON "ApplicationQuotaUsages" ("UserId", "Period", "PeriodStartsAtUtc") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_AuditLogs_Action_CreatedAtUtc" ON "AuditLogs" ("Action", "CreatedAtUtc");

CREATE INDEX "IX_AuditLogs_CorrelationId_CreatedAtUtc" ON "AuditLogs" ("CorrelationId", "CreatedAtUtc");

CREATE INDEX "IX_AuditLogs_CreatedAtUtc" ON "AuditLogs" ("CreatedAtUtc");

CREATE INDEX "IX_AuditLogs_EntityName_EntityId_CreatedAtUtc" ON "AuditLogs" ("EntityName", "EntityId", "CreatedAtUtc");

CREATE INDEX "IX_AuditLogs_IsDeleted" ON "AuditLogs" ("IsDeleted");

CREATE INDEX "IX_AuditLogs_UserId_CreatedAtUtc" ON "AuditLogs" ("UserId", "CreatedAtUtc");

CREATE INDEX "IX_CandidateResumeProfiles_ExtractionStatus_ExtractedAtUtc" ON "CandidateResumeProfiles" ("ExtractionStatus", "ExtractedAtUtc");

CREATE INDEX "IX_CandidateResumeProfiles_IsDeleted" ON "CandidateResumeProfiles" ("IsDeleted");

CREATE UNIQUE INDEX "IX_CandidateResumeProfiles_UserId" ON "CandidateResumeProfiles" ("UserId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Categories_IsDeleted" ON "Categories" ("IsDeleted");

CREATE INDEX "IX_Categories_ParentCategoryId_DisplayOrder" ON "Categories" ("ParentCategoryId", "DisplayOrder");

CREATE UNIQUE INDEX "IX_Categories_Slug" ON "Categories" ("Slug") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Companies_CompanyType_Industry" ON "Companies" ("CompanyType", "Industry");

CREATE INDEX "IX_Companies_IsDeleted" ON "Companies" ("IsDeleted");

CREATE INDEX "IX_Companies_OwnerUserId" ON "Companies" ("OwnerUserId");

CREATE UNIQUE INDEX "IX_Companies_Slug" ON "Companies" ("Slug") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_JobApplications_IsDeleted" ON "JobApplications" ("IsDeleted");

CREATE INDEX "IX_JobApplications_JobId" ON "JobApplications" ("JobId");

CREATE UNIQUE INDEX "IX_JobApplications_UserId_JobId" ON "JobApplications" ("UserId", "JobId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_JobApplications_UserId_Status_SubmittedAtUtc" ON "JobApplications" ("UserId", "Status", "SubmittedAtUtc");

CREATE INDEX "IX_JobApplicationStatusHistory_ActorUserId_ChangedAtUtc" ON "JobApplicationStatusHistory" ("ActorUserId", "ChangedAtUtc");

CREATE INDEX "IX_JobApplicationStatusHistory_ApplicationId_ChangedAtUtc" ON "JobApplicationStatusHistory" ("ApplicationId", "ChangedAtUtc");

CREATE INDEX "IX_JobApplicationStatusHistory_IsDeleted" ON "JobApplicationStatusHistory" ("IsDeleted");

CREATE INDEX "IX_JobDiscoveryItems_IsDeleted" ON "JobDiscoveryItems" ("IsDeleted");

CREATE UNIQUE INDEX "IX_JobDiscoveryItems_Provider_SourceJobId" ON "JobDiscoveryItems" ("Provider", "SourceJobId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_JobDiscoveryItems_RunId" ON "JobDiscoveryItems" ("RunId");

CREATE INDEX "IX_JobDiscoveryRuns_IsDeleted" ON "JobDiscoveryRuns" ("IsDeleted");

CREATE INDEX "IX_JobDiscoveryRuns_StartedAtUtc" ON "JobDiscoveryRuns" ("StartedAtUtc");

CREATE INDEX "IX_JobRecruiterContacts_IsDeleted" ON "JobRecruiterContacts" ("IsDeleted");

CREATE UNIQUE INDEX "IX_JobRecruiterContacts_JobId" ON "JobRecruiterContacts" ("JobId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Jobs_CategoryId_Status" ON "Jobs" ("CategoryId", "Status");

CREATE INDEX "IX_Jobs_CompanyId_Status_PublishedAtUtc" ON "Jobs" ("CompanyId", "Status", "PublishedAtUtc");

CREATE INDEX "IX_Jobs_CreatedAtUtc" ON "Jobs" ("CreatedAtUtc");

CREATE INDEX "IX_Jobs_Department" ON "Jobs" ("Department");

CREATE INDEX "IX_Jobs_ExpiresAtUtc" ON "Jobs" ("ExpiresAtUtc");

CREATE INDEX "IX_Jobs_IsDeleted" ON "Jobs" ("IsDeleted");

CREATE UNIQUE INDEX "IX_Jobs_ReferenceNumber" ON "Jobs" ("ReferenceNumber") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Jobs_RoleCategory" ON "Jobs" ("RoleCategory");

CREATE UNIQUE INDEX "IX_Jobs_Slug" ON "Jobs" ("Slug") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Jobs_Status_ExpiresAtUtc" ON "Jobs" ("Status", "ExpiresAtUtc");

CREATE INDEX "IX_Jobs_Status_IsFeatured_IsHidden_PublishedAtUtc" ON "Jobs" ("Status", "IsFeatured", "IsHidden", "PublishedAtUtc");

CREATE INDEX "IX_Jobs_Status_PostedByType" ON "Jobs" ("Status", "PostedByType");

CREATE INDEX "IX_Jobs_Status_WorkplaceType_EmploymentType" ON "Jobs" ("Status", "WorkplaceType", "EmploymentType");

CREATE INDEX "IX_JobSkills_IsDeleted" ON "JobSkills" ("IsDeleted");

CREATE UNIQUE INDEX "IX_JobSkills_JobId_SkillId" ON "JobSkills" ("JobId", "SkillId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_JobSkills_SkillId" ON "JobSkills" ("SkillId");

CREATE INDEX "IX_MembershipHistory_IsDeleted" ON "MembershipHistory" ("IsDeleted");

CREATE INDEX "IX_MembershipHistory_MembershipId_OccurredAtUtc" ON "MembershipHistory" ("MembershipId", "OccurredAtUtc");

CREATE INDEX "IX_MembershipHistory_UserId_OccurredAtUtc" ON "MembershipHistory" ("UserId", "OccurredAtUtc");

CREATE INDEX "IX_Memberships_IsDeleted" ON "Memberships" ("IsDeleted");

CREATE INDEX "IX_Memberships_Status_EndsAtUtc" ON "Memberships" ("Status", "EndsAtUtc");

CREATE UNIQUE INDEX "IX_Memberships_UserId" ON "Memberships" ("UserId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Notifications_IsDeleted" ON "Notifications" ("IsDeleted");

CREATE INDEX "IX_Notifications_UserId_IsRead" ON "Notifications" ("UserId", "IsRead");

CREATE INDEX "IX_Notifications_UserId_IsRead_CreatedAtUtc" ON "Notifications" ("UserId", "IsRead", "CreatedAtUtc");

CREATE INDEX "IX_Notifications_UserId1" ON "Notifications" ("UserId1");

CREATE INDEX "IX_OtpChallenges_IsDeleted" ON "OtpChallenges" ("IsDeleted");

CREATE INDEX "IX_OtpChallenges_NormalizedPhoneNumber_Purpose_ConsumedAtUtc_E~" ON "OtpChallenges" ("NormalizedPhoneNumber", "Purpose", "ConsumedAtUtc", "ExpiresAtUtc");

CREATE UNIQUE INDEX "IX_OtpChallenges_PendingRegistrationId" ON "OtpChallenges" ("PendingRegistrationId") WHERE "PendingRegistrationId" IS NOT NULL AND "IsDeleted" = FALSE;

CREATE INDEX "IX_OtpChallenges_Purpose_LastSentAtUtc" ON "OtpChallenges" ("Purpose", "LastSentAtUtc");

CREATE INDEX "IX_OtpChallenges_UserId" ON "OtpChallenges" ("UserId");

CREATE INDEX "IX_PaymentHistory_IsDeleted" ON "PaymentHistory" ("IsDeleted");

CREATE INDEX "IX_PaymentHistory_PaymentId_OccurredAtUtc" ON "PaymentHistory" ("PaymentId", "OccurredAtUtc");

CREATE UNIQUE INDEX "IX_PaymentHistory_ProviderEventId" ON "PaymentHistory" ("ProviderEventId") WHERE "ProviderEventId" IS NOT NULL;

CREATE INDEX "IX_PaymentHistory_UserId_OccurredAtUtc" ON "PaymentHistory" ("UserId", "OccurredAtUtc");

CREATE INDEX "IX_Payments_CreatedAtUtc" ON "Payments" ("CreatedAtUtc");

CREATE INDEX "IX_Payments_IsDeleted" ON "Payments" ("IsDeleted");

CREATE INDEX "IX_Payments_MembershipId" ON "Payments" ("MembershipId");

CREATE UNIQUE INDEX "IX_Payments_ProviderOrderId" ON "Payments" ("ProviderOrderId") WHERE "ProviderOrderId" IS NOT NULL;

CREATE UNIQUE INDEX "IX_Payments_ProviderPaymentId" ON "Payments" ("ProviderPaymentId") WHERE "ProviderPaymentId" IS NOT NULL;

CREATE INDEX "IX_Payments_Status_PaidAtUtc_CurrencyCode" ON "Payments" ("Status", "PaidAtUtc", "CurrencyCode");

CREATE INDEX "IX_Payments_Status_ProviderOrderCreatedAtUtc" ON "Payments" ("Status", "ProviderOrderCreatedAtUtc");

CREATE INDEX "IX_Payments_Status_UserId" ON "Payments" ("Status", "UserId");

CREATE INDEX "IX_Payments_UserId_Status_CreatedAtUtc" ON "Payments" ("UserId", "Status", "CreatedAtUtc");

CREATE INDEX "IX_PendingRegistrations_CompletedUserId" ON "PendingRegistrations" ("CompletedUserId");

CREATE INDEX "IX_PendingRegistrations_ExpiresAtUtc_ClosedAtUtc" ON "PendingRegistrations" ("ExpiresAtUtc", "ClosedAtUtc");

CREATE INDEX "IX_PendingRegistrations_IsDeleted" ON "PendingRegistrations" ("IsDeleted");

CREATE UNIQUE INDEX "IX_PendingRegistrations_NormalizedEmail" ON "PendingRegistrations" ("NormalizedEmail") WHERE "ClosedAtUtc" IS NULL AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX "IX_PendingRegistrations_NormalizedPhoneNumber" ON "PendingRegistrations" ("NormalizedPhoneNumber") WHERE "ClosedAtUtc" IS NULL AND "IsDeleted" = FALSE;

CREATE INDEX "IX_RefreshTokens_IsDeleted" ON "RefreshTokens" ("IsDeleted");

CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_RefreshTokens_UserId_ExpiresAtUtc" ON "RefreshTokens" ("UserId", "ExpiresAtUtc");

CREATE INDEX "IX_Roles_IsDeleted" ON "Roles" ("IsDeleted");

CREATE UNIQUE INDEX "IX_Roles_NormalizedName" ON "Roles" ("NormalizedName") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_SavedJobs_IsDeleted" ON "SavedJobs" ("IsDeleted");

CREATE INDEX "IX_SavedJobs_JobId" ON "SavedJobs" ("JobId");

CREATE INDEX "IX_SavedJobs_UserId_CreatedAtUtc" ON "SavedJobs" ("UserId", "CreatedAtUtc");

CREATE UNIQUE INDEX "IX_SavedJobs_UserId_JobId" ON "SavedJobs" ("UserId", "JobId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_Settings_IsDeleted" ON "Settings" ("IsDeleted");

CREATE UNIQUE INDEX "IX_Settings_Scope_Key" ON "Settings" ("Scope", "Key") WHERE "UserId" IS NULL AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX "IX_Settings_Scope_UserId_Key" ON "Settings" ("Scope", "UserId", "Key") WHERE "UserId" IS NOT NULL AND "IsDeleted" = FALSE;

CREATE INDEX "IX_Settings_UserId" ON "Settings" ("UserId");

CREATE INDEX "IX_Skills_IsDeleted" ON "Skills" ("IsDeleted");

CREATE UNIQUE INDEX "IX_Skills_NormalizedName" ON "Skills" ("NormalizedName") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_UserJobHistories_IsDeleted" ON "UserJobHistories" ("IsDeleted");

CREATE INDEX "IX_UserJobHistories_JobId_Action_OccurredAtUtc" ON "UserJobHistories" ("JobId", "Action", "OccurredAtUtc");

CREATE INDEX "IX_UserJobHistories_UserId_JobId_Action_OccurredAtUtc" ON "UserJobHistories" ("UserId", "JobId", "Action", "OccurredAtUtc");

CREATE INDEX "IX_Users_CreatedAtUtc" ON "Users" ("CreatedAtUtc");

CREATE INDEX "IX_Users_IsDeleted" ON "Users" ("IsDeleted");

CREATE UNIQUE INDEX "IX_Users_NormalizedEmail" ON "Users" ("NormalizedEmail") WHERE "IsDeleted" = FALSE;

CREATE UNIQUE INDEX "IX_Users_NormalizedPhoneNumber" ON "Users" ("NormalizedPhoneNumber") WHERE "NormalizedPhoneNumber" IS NOT NULL AND "IsDeleted" = FALSE;

CREATE UNIQUE INDEX "IX_Users_PasswordResetTokenHash" ON "Users" ("PasswordResetTokenHash") WHERE "PasswordResetTokenHash" IS NOT NULL AND "IsDeleted" = FALSE;

CREATE INDEX "IX_Users_RoleId" ON "Users" ("RoleId");

CREATE INDEX "IX_Users_Status_IsDeleted" ON "Users" ("Status", "IsDeleted");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260810222602_InitialPostgresSchema', '9.0.8');

COMMIT;
