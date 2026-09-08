CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    INSERT INTO "Roles" ("Id", "CreatedAtUtc", "DeletedAtUtc", "Description", "IsDeleted", "Name", "NormalizedName", "UpdatedAtUtc")
    VALUES ('2bdd5ba8-2fb0-476c-b9db-6696c1c94290', TIMESTAMPTZ '2025-01-01T00:00:00Z', NULL, 'Company employer', FALSE, 'Employer', 'EMPLOYER', NULL);
    INSERT INTO "Roles" ("Id", "CreatedAtUtc", "DeletedAtUtc", "Description", "IsDeleted", "Name", "NormalizedName", "UpdatedAtUtc")
    VALUES ('3ec6976c-8752-48f5-a14f-1c81b6522c5d', TIMESTAMPTZ '2025-01-01T00:00:00Z', NULL, 'Job candidate', FALSE, 'Candidate', 'CANDIDATE', NULL);
    INSERT INTO "Roles" ("Id", "CreatedAtUtc", "DeletedAtUtc", "Description", "IsDeleted", "Name", "NormalizedName", "UpdatedAtUtc")
    VALUES ('a2216ece-d9a7-4c61-9bda-530e64d50c01', TIMESTAMPTZ '2025-01-01T00:00:00Z', NULL, 'System administrator', FALSE, 'Administrator', 'ADMINISTRATOR', NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_ApplicationQuotaUsages_IsDeleted" ON "ApplicationQuotaUsages" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_ApplicationQuotaUsages_UserId_Period_PeriodStartsAtUtc" ON "ApplicationQuotaUsages" ("UserId", "Period", "PeriodStartsAtUtc") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_AuditLogs_Action_CreatedAtUtc" ON "AuditLogs" ("Action", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_AuditLogs_CorrelationId_CreatedAtUtc" ON "AuditLogs" ("CorrelationId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_AuditLogs_CreatedAtUtc" ON "AuditLogs" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_AuditLogs_EntityName_EntityId_CreatedAtUtc" ON "AuditLogs" ("EntityName", "EntityId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_AuditLogs_IsDeleted" ON "AuditLogs" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_AuditLogs_UserId_CreatedAtUtc" ON "AuditLogs" ("UserId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_CandidateResumeProfiles_ExtractionStatus_ExtractedAtUtc" ON "CandidateResumeProfiles" ("ExtractionStatus", "ExtractedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_CandidateResumeProfiles_IsDeleted" ON "CandidateResumeProfiles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_CandidateResumeProfiles_UserId" ON "CandidateResumeProfiles" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Categories_IsDeleted" ON "Categories" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Categories_ParentCategoryId_DisplayOrder" ON "Categories" ("ParentCategoryId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Categories_Slug" ON "Categories" ("Slug") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Companies_CompanyType_Industry" ON "Companies" ("CompanyType", "Industry");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Companies_IsDeleted" ON "Companies" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Companies_OwnerUserId" ON "Companies" ("OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Companies_Slug" ON "Companies" ("Slug") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobApplications_IsDeleted" ON "JobApplications" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobApplications_JobId" ON "JobApplications" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_JobApplications_UserId_JobId" ON "JobApplications" ("UserId", "JobId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobApplications_UserId_Status_SubmittedAtUtc" ON "JobApplications" ("UserId", "Status", "SubmittedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobApplicationStatusHistory_ActorUserId_ChangedAtUtc" ON "JobApplicationStatusHistory" ("ActorUserId", "ChangedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobApplicationStatusHistory_ApplicationId_ChangedAtUtc" ON "JobApplicationStatusHistory" ("ApplicationId", "ChangedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobApplicationStatusHistory_IsDeleted" ON "JobApplicationStatusHistory" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobDiscoveryItems_IsDeleted" ON "JobDiscoveryItems" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_JobDiscoveryItems_Provider_SourceJobId" ON "JobDiscoveryItems" ("Provider", "SourceJobId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobDiscoveryItems_RunId" ON "JobDiscoveryItems" ("RunId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobDiscoveryRuns_IsDeleted" ON "JobDiscoveryRuns" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobDiscoveryRuns_StartedAtUtc" ON "JobDiscoveryRuns" ("StartedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobRecruiterContacts_IsDeleted" ON "JobRecruiterContacts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_JobRecruiterContacts_JobId" ON "JobRecruiterContacts" ("JobId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_CategoryId_Status" ON "Jobs" ("CategoryId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_CompanyId_Status_PublishedAtUtc" ON "Jobs" ("CompanyId", "Status", "PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_CreatedAtUtc" ON "Jobs" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_Department" ON "Jobs" ("Department");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_ExpiresAtUtc" ON "Jobs" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_IsDeleted" ON "Jobs" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Jobs_ReferenceNumber" ON "Jobs" ("ReferenceNumber") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_RoleCategory" ON "Jobs" ("RoleCategory");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Jobs_Slug" ON "Jobs" ("Slug") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_Status_ExpiresAtUtc" ON "Jobs" ("Status", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_Status_IsFeatured_IsHidden_PublishedAtUtc" ON "Jobs" ("Status", "IsFeatured", "IsHidden", "PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_Status_PostedByType" ON "Jobs" ("Status", "PostedByType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Jobs_Status_WorkplaceType_EmploymentType" ON "Jobs" ("Status", "WorkplaceType", "EmploymentType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobSkills_IsDeleted" ON "JobSkills" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_JobSkills_JobId_SkillId" ON "JobSkills" ("JobId", "SkillId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_JobSkills_SkillId" ON "JobSkills" ("SkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_MembershipHistory_IsDeleted" ON "MembershipHistory" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_MembershipHistory_MembershipId_OccurredAtUtc" ON "MembershipHistory" ("MembershipId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_MembershipHistory_UserId_OccurredAtUtc" ON "MembershipHistory" ("UserId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Memberships_IsDeleted" ON "Memberships" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Memberships_Status_EndsAtUtc" ON "Memberships" ("Status", "EndsAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Memberships_UserId" ON "Memberships" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Notifications_IsDeleted" ON "Notifications" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Notifications_UserId_IsRead" ON "Notifications" ("UserId", "IsRead");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Notifications_UserId_IsRead_CreatedAtUtc" ON "Notifications" ("UserId", "IsRead", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Notifications_UserId1" ON "Notifications" ("UserId1");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_OtpChallenges_IsDeleted" ON "OtpChallenges" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_OtpChallenges_NormalizedPhoneNumber_Purpose_ConsumedAtUtc_E~" ON "OtpChallenges" ("NormalizedPhoneNumber", "Purpose", "ConsumedAtUtc", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_OtpChallenges_PendingRegistrationId" ON "OtpChallenges" ("PendingRegistrationId") WHERE "PendingRegistrationId" IS NOT NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_OtpChallenges_Purpose_LastSentAtUtc" ON "OtpChallenges" ("Purpose", "LastSentAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_OtpChallenges_UserId" ON "OtpChallenges" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_PaymentHistory_IsDeleted" ON "PaymentHistory" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_PaymentHistory_PaymentId_OccurredAtUtc" ON "PaymentHistory" ("PaymentId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_PaymentHistory_ProviderEventId" ON "PaymentHistory" ("ProviderEventId") WHERE "ProviderEventId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_PaymentHistory_UserId_OccurredAtUtc" ON "PaymentHistory" ("UserId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_CreatedAtUtc" ON "Payments" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_IsDeleted" ON "Payments" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_MembershipId" ON "Payments" ("MembershipId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Payments_ProviderOrderId" ON "Payments" ("ProviderOrderId") WHERE "ProviderOrderId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Payments_ProviderPaymentId" ON "Payments" ("ProviderPaymentId") WHERE "ProviderPaymentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_Status_PaidAtUtc_CurrencyCode" ON "Payments" ("Status", "PaidAtUtc", "CurrencyCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_Status_ProviderOrderCreatedAtUtc" ON "Payments" ("Status", "ProviderOrderCreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_Status_UserId" ON "Payments" ("Status", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Payments_UserId_Status_CreatedAtUtc" ON "Payments" ("UserId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_PendingRegistrations_CompletedUserId" ON "PendingRegistrations" ("CompletedUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_PendingRegistrations_ExpiresAtUtc_ClosedAtUtc" ON "PendingRegistrations" ("ExpiresAtUtc", "ClosedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_PendingRegistrations_IsDeleted" ON "PendingRegistrations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_PendingRegistrations_NormalizedEmail" ON "PendingRegistrations" ("NormalizedEmail") WHERE "ClosedAtUtc" IS NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_PendingRegistrations_NormalizedPhoneNumber" ON "PendingRegistrations" ("NormalizedPhoneNumber") WHERE "ClosedAtUtc" IS NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_RefreshTokens_IsDeleted" ON "RefreshTokens" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_RefreshTokens_UserId_ExpiresAtUtc" ON "RefreshTokens" ("UserId", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Roles_IsDeleted" ON "Roles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Roles_NormalizedName" ON "Roles" ("NormalizedName") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_SavedJobs_IsDeleted" ON "SavedJobs" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_SavedJobs_JobId" ON "SavedJobs" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_SavedJobs_UserId_CreatedAtUtc" ON "SavedJobs" ("UserId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_SavedJobs_UserId_JobId" ON "SavedJobs" ("UserId", "JobId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Settings_IsDeleted" ON "Settings" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Settings_Scope_Key" ON "Settings" ("Scope", "Key") WHERE "UserId" IS NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Settings_Scope_UserId_Key" ON "Settings" ("Scope", "UserId", "Key") WHERE "UserId" IS NOT NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Settings_UserId" ON "Settings" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Skills_IsDeleted" ON "Skills" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Skills_NormalizedName" ON "Skills" ("NormalizedName") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_UserJobHistories_IsDeleted" ON "UserJobHistories" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_UserJobHistories_JobId_Action_OccurredAtUtc" ON "UserJobHistories" ("JobId", "Action", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_UserJobHistories_UserId_JobId_Action_OccurredAtUtc" ON "UserJobHistories" ("UserId", "JobId", "Action", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Users_CreatedAtUtc" ON "Users" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Users_IsDeleted" ON "Users" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Users_NormalizedEmail" ON "Users" ("NormalizedEmail") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Users_NormalizedPhoneNumber" ON "Users" ("NormalizedPhoneNumber") WHERE "NormalizedPhoneNumber" IS NOT NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE UNIQUE INDEX "IX_Users_PasswordResetTokenHash" ON "Users" ("PasswordResetTokenHash") WHERE "PasswordResetTokenHash" IS NOT NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Users_RoleId" ON "Users" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    CREATE INDEX "IX_Users_Status_IsDeleted" ON "Users" ("Status", "IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260810222602_InitialPostgresSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260810222602_InitialPostgresSchema', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811193633_AddCandidateProfileCompletionPhase1') THEN
    CREATE TABLE "CandidateSkills" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "NormalizedName" character varying(100) NOT NULL,
        "Proficiency" integer,
        "YearsOfExperience" numeric(4,1),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateSkills" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateSkills_Proficiency" CHECK ("Proficiency" IS NULL OR "Proficiency" BETWEEN 1 AND 4),
        CONSTRAINT "CK_CandidateSkills_YearsOfExperience" CHECK ("YearsOfExperience" IS NULL OR ("YearsOfExperience" >= 0 AND "YearsOfExperience" <= 50)),
        CONSTRAINT "FK_CandidateSkills_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811193633_AddCandidateProfileCompletionPhase1') THEN
    CREATE INDEX "IX_CandidateSkills_IsDeleted" ON "CandidateSkills" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811193633_AddCandidateProfileCompletionPhase1') THEN
    CREATE INDEX "IX_CandidateSkills_UserId" ON "CandidateSkills" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811193633_AddCandidateProfileCompletionPhase1') THEN
    CREATE UNIQUE INDEX "IX_CandidateSkills_UserId_NormalizedName" ON "CandidateSkills" ("UserId", "NormalizedName") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260811193633_AddCandidateProfileCompletionPhase1') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260811193633_AddCandidateProfileCompletionPhase1', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "CandidateCertifications" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Issuer" character varying(200),
        "IssuedDate" date,
        "ExpiryDate" date,
        "DoesNotExpire" boolean NOT NULL,
        "CredentialId" character varying(200),
        "CredentialUrl" character varying(2048),
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateCertifications" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateCertifications_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_CandidateCertifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "CandidateEducation" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Qualification" character varying(200) NOT NULL,
        "Institution" character varying(250) NOT NULL,
        "FieldOfStudy" character varying(200),
        "StartYear" integer,
        "EndYear" integer,
        "Grade" character varying(100),
        "Description" character varying(4000),
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateEducation" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateEducation_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_CandidateEducation_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "CandidateExperiences" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "JobTitle" character varying(200) NOT NULL,
        "CompanyName" character varying(200) NOT NULL,
        "Location" character varying(200),
        "EmploymentType" integer,
        "StartDate" date NOT NULL,
        "EndDate" date,
        "IsCurrent" boolean NOT NULL,
        "Description" character varying(4000),
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateExperiences" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateExperiences_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_CandidateExperiences_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "CandidatePortfolios" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Slug" character varying(80) NOT NULL,
        "NormalizedSlug" character varying(80) NOT NULL,
        "Status" integer NOT NULL,
        "Template" integer NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidatePortfolios" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidatePortfolios_Status" CHECK ("Status" BETWEEN 1 AND 2),
        CONSTRAINT "CK_CandidatePortfolios_Template" CHECK ("Template" BETWEEN 1 AND 2),
        CONSTRAINT "FK_CandidatePortfolios_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "CandidateProfessionalLinks" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Type" integer NOT NULL,
        "Label" character varying(100),
        "Url" character varying(2048) NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateProfessionalLinks" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateProfessionalLinks_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_CandidateProfessionalLinks_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "CandidateProjects" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Role" character varying(150),
        "Description" character varying(4000) NOT NULL,
        "TechnologiesJson" text NOT NULL,
        "SourceUrl" character varying(2048),
        "LiveUrl" character varying(2048),
        "StartDate" date,
        "EndDate" date,
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateProjects" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateProjects_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_CandidateProjects_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "PortfolioCustomSections" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Title" character varying(120) NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_PortfolioCustomSections" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PortfolioCustomSections_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_PortfolioCustomSections_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "PortfolioSectionSettings" (
        "Id" uuid NOT NULL,
        "PortfolioId" uuid NOT NULL,
        "SectionType" integer NOT NULL,
        "IsVisible" boolean NOT NULL,
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_PortfolioSectionSettings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PortfolioSectionSettings_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_PortfolioSectionSettings_CandidatePortfolios_PortfolioId" FOREIGN KEY ("PortfolioId") REFERENCES "CandidatePortfolios" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE TABLE "PortfolioCustomItems" (
        "Id" uuid NOT NULL,
        "SectionId" uuid NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Description" character varying(4000),
        "Date" date,
        "Url" character varying(2048),
        "DisplayOrder" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_PortfolioCustomItems" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_PortfolioCustomItems_DisplayOrder" CHECK ("DisplayOrder" BETWEEN 0 AND 1000),
        CONSTRAINT "FK_PortfolioCustomItems_PortfolioCustomSections_SectionId" FOREIGN KEY ("SectionId") REFERENCES "PortfolioCustomSections" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateCertifications_IsDeleted" ON "CandidateCertifications" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateCertifications_UserId_DisplayOrder" ON "CandidateCertifications" ("UserId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateEducation_IsDeleted" ON "CandidateEducation" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateEducation_UserId_DisplayOrder" ON "CandidateEducation" ("UserId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateExperiences_IsDeleted" ON "CandidateExperiences" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateExperiences_UserId_DisplayOrder" ON "CandidateExperiences" ("UserId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidatePortfolios_IsDeleted" ON "CandidatePortfolios" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE UNIQUE INDEX "IX_CandidatePortfolios_NormalizedSlug" ON "CandidatePortfolios" ("NormalizedSlug") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidatePortfolios_Status_NormalizedSlug" ON "CandidatePortfolios" ("Status", "NormalizedSlug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE UNIQUE INDEX "IX_CandidatePortfolios_UserId" ON "CandidatePortfolios" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateProfessionalLinks_IsDeleted" ON "CandidateProfessionalLinks" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateProfessionalLinks_UserId_DisplayOrder" ON "CandidateProfessionalLinks" ("UserId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateProjects_IsDeleted" ON "CandidateProjects" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_CandidateProjects_UserId_DisplayOrder" ON "CandidateProjects" ("UserId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_PortfolioCustomItems_IsDeleted" ON "PortfolioCustomItems" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_PortfolioCustomItems_SectionId_DisplayOrder" ON "PortfolioCustomItems" ("SectionId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_PortfolioCustomSections_IsDeleted" ON "PortfolioCustomSections" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_PortfolioCustomSections_UserId_DisplayOrder" ON "PortfolioCustomSections" ("UserId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_PortfolioSectionSettings_IsDeleted" ON "PortfolioSectionSettings" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE INDEX "IX_PortfolioSectionSettings_PortfolioId_DisplayOrder" ON "PortfolioSectionSettings" ("PortfolioId", "DisplayOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    CREATE UNIQUE INDEX "IX_PortfolioSectionSettings_PortfolioId_SectionType" ON "PortfolioSectionSettings" ("PortfolioId", "SectionType") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812100432_AddCandidatePortfolio') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260812100432_AddCandidatePortfolio', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    ALTER TABLE "Users" ALTER COLUMN "PasswordHash" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    CREATE TABLE "UserExternalLogins" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Provider" integer NOT NULL,
        "ProviderSubject" character varying(255) NOT NULL,
        "ProviderEmail" character varying(256) NOT NULL,
        "LastLoginAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_UserExternalLogins" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_UserExternalLogins_Provider" CHECK ("Provider" = 1),
        CONSTRAINT "FK_UserExternalLogins_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    CREATE INDEX "IX_UserExternalLogins_IsDeleted" ON "UserExternalLogins" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    CREATE INDEX "IX_UserExternalLogins_Provider_ProviderEmail" ON "UserExternalLogins" ("Provider", "ProviderEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    CREATE UNIQUE INDEX "IX_UserExternalLogins_Provider_ProviderSubject" ON "UserExternalLogins" ("Provider", "ProviderSubject") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    CREATE UNIQUE INDEX "IX_UserExternalLogins_UserId_Provider" ON "UserExternalLogins" ("UserId", "Provider") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260813091640_AddGoogleExternalAuthentication') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260813091640_AddGoogleExternalAuthentication', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "AvailabilityToJoin" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CandidateEmploymentTypesJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CandidateJobTypesJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CurrentAnnualSalary" numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CurrentArea" character varying(150);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CurrentCity" character varying(150);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CurrentCountry" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CurrentFixedAnnualSalary" numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "CurrentVariableAnnualSalary" numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "ExpectedAnnualSalary" numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "IsOutsideIndia" boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "PreferredCitiesJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "PreferredJobRolesJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "PreferredShiftsJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "Users" ADD "WorkStatus" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "CandidateExperiences" ADD "AnnualSalary" numeric(14,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "CandidateExperiences" ADD "NoticePeriod" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "CandidateExperiences" ADD "SkillsUsedJson" text NOT NULL DEFAULT '[]';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "CandidateEducation" ADD "CourseType" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "CandidateEducation" ADD "GradingSystem" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    ALTER TABLE "CandidateEducation" ADD "IsCurrentlyStudying" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    CREATE TABLE "CandidateProfilePhotos" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Content" bytea NOT NULL,
        "ContentType" character varying(32) NOT NULL,
        "SizeBytes" integer NOT NULL,
        "Version" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateProfilePhotos" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CandidateProfilePhotos_SizeBytes" CHECK ("SizeBytes" BETWEEN 1 AND 1048576),
        CONSTRAINT "FK_CandidateProfilePhotos_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    CREATE INDEX "IX_CandidateProfilePhotos_IsDeleted" ON "CandidateProfilePhotos" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    CREATE UNIQUE INDEX "IX_CandidateProfilePhotos_UserId" ON "CandidateProfilePhotos" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260820135621_AddCandidateProfileExpansion') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260820135621_AddCandidateProfileExpansion', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260821181143_AddRegistrationEmailOutbox') THEN
    CREATE TABLE "RegistrationEmailRequests" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "VerificationToken" character varying(512) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "AttemptCount" integer NOT NULL,
        "NextAttemptAtUtc" timestamp with time zone NOT NULL,
        "LockedUntilUtc" timestamp with time zone,
        "SentAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_RegistrationEmailRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_RegistrationEmailRequests_AttemptCount" CHECK ("AttemptCount" >= 0),
        CONSTRAINT "FK_RegistrationEmailRequests_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260821181143_AddRegistrationEmailOutbox') THEN
    CREATE UNIQUE INDEX "IX_Users_EmailVerificationTokenHash" ON "Users" ("EmailVerificationTokenHash") WHERE "EmailVerificationTokenHash" IS NOT NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260821181143_AddRegistrationEmailOutbox') THEN
    CREATE INDEX "IX_RegistrationEmailRequests_IsDeleted" ON "RegistrationEmailRequests" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260821181143_AddRegistrationEmailOutbox') THEN
    CREATE INDEX "IX_RegistrationEmailRequests_SentAtUtc_NextAttemptAtUtc_Locked~" ON "RegistrationEmailRequests" ("SentAtUtc", "NextAttemptAtUtc", "LockedUntilUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260821181143_AddRegistrationEmailOutbox') THEN
    CREATE INDEX "IX_RegistrationEmailRequests_UserId" ON "RegistrationEmailRequests" ("UserId") WHERE "SentAtUtc" IS NULL AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260821181143_AddRegistrationEmailOutbox') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260821181143_AddRegistrationEmailOutbox', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE TABLE "CandidateInterviewSchedules" (
        "Id" uuid NOT NULL,
        "CandidateId" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "JobId" uuid,
        "RoleTitle" character varying(160),
        "InterviewAtUtc" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "ConfirmFeedbackAvailableAtUtc" timestamp with time zone NOT NULL,
        "FeedbackNotificationSentAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateInterviewSchedules" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CandidateInterviewSchedules_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CandidateInterviewSchedules_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_CandidateInterviewSchedules_Users_CandidateId" FOREIGN KEY ("CandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE TABLE "InterviewInsights" (
        "Id" uuid NOT NULL,
        "AuthorCandidateId" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "JobId" uuid,
        "RoleTitle" character varying(160) NOT NULL,
        "ExperienceLevel" character varying(80),
        "InterviewDateMonth" date NOT NULL,
        "OverallDifficulty" integer NOT NULL,
        "ProcessSummary" character varying(3000) NOT NULL,
        "PreparationTips" character varying(3000) NOT NULL,
        "Outcome" integer,
        "IsAnonymous" boolean NOT NULL,
        "Status" integer NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "ModerationReason" character varying(500),
        "HelpfulConfirmedCount" integer NOT NULL,
        "QualityScore" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_InterviewInsights" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewInsights_HelpfulCount" CHECK ("HelpfulConfirmedCount" >= 0),
        CONSTRAINT "CK_InterviewInsights_QualityScore" CHECK ("QualityScore" >= 0),
        CONSTRAINT "FK_InterviewInsights_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_InterviewInsights_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_InterviewInsights_Users_AuthorCandidateId" FOREIGN KEY ("AuthorCandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE TABLE "InsightHelpfulnessFeedback" (
        "Id" uuid NOT NULL,
        "InsightId" uuid NOT NULL,
        "CandidateId" uuid NOT NULL,
        "CandidateInterviewScheduleId" uuid NOT NULL,
        "Helpfulness" integer NOT NULL,
        "InterviewMatch" integer NOT NULL,
        "Feedback" character varying(500),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_InsightHelpfulnessFeedback" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InsightHelpfulnessFeedback_CandidateInterviewSchedules_Cand~" FOREIGN KEY ("CandidateInterviewScheduleId") REFERENCES "CandidateInterviewSchedules" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_InsightHelpfulnessFeedback_InterviewInsights_InsightId" FOREIGN KEY ("InsightId") REFERENCES "InterviewInsights" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_InsightHelpfulnessFeedback_Users_CandidateId" FOREIGN KEY ("CandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE TABLE "InsightReports" (
        "Id" uuid NOT NULL,
        "InsightId" uuid NOT NULL,
        "ReporterCandidateId" uuid NOT NULL,
        "Reason" integer NOT NULL,
        "Details" character varying(500),
        "Status" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_InsightReports" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_InsightReports_InterviewInsights_InsightId" FOREIGN KEY ("InsightId") REFERENCES "InterviewInsights" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_InsightReports_Users_ReporterCandidateId" FOREIGN KEY ("ReporterCandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE TABLE "InterviewRounds" (
        "Id" uuid NOT NULL,
        "InterviewInsightId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "RoundType" integer NOT NULL,
        "RoundTitle" character varying(160),
        "DurationMinutes" integer,
        "QuestionsOrTopics" character varying(3000) NOT NULL,
        "CandidateAdvice" character varying(2000),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_InterviewRounds" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewRounds_Duration" CHECK ("DurationMinutes" IS NULL OR ("DurationMinutes" BETWEEN 1 AND 1440)),
        CONSTRAINT "FK_InterviewRounds_InterviewInsights_InterviewInsightId" FOREIGN KEY ("InterviewInsightId") REFERENCES "InterviewInsights" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_CandidateInterviewSchedules_CandidateId_CompanyId_Interview~" ON "CandidateInterviewSchedules" ("CandidateId", "CompanyId", "InterviewAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_CandidateInterviewSchedules_CompanyId" ON "CandidateInterviewSchedules" ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_CandidateInterviewSchedules_FeedbackNotificationSentAtUtc_C~" ON "CandidateInterviewSchedules" ("FeedbackNotificationSentAtUtc", "ConfirmFeedbackAvailableAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_CandidateInterviewSchedules_JobId" ON "CandidateInterviewSchedules" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InsightHelpfulnessFeedback_CandidateId_CreatedAtUtc" ON "InsightHelpfulnessFeedback" ("CandidateId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE UNIQUE INDEX "IX_InsightHelpfulnessFeedback_CandidateId_InsightId" ON "InsightHelpfulnessFeedback" ("CandidateId", "InsightId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InsightHelpfulnessFeedback_CandidateInterviewScheduleId" ON "InsightHelpfulnessFeedback" ("CandidateInterviewScheduleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InsightHelpfulnessFeedback_InsightId" ON "InsightHelpfulnessFeedback" ("InsightId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InsightReports_InsightId" ON "InsightReports" ("InsightId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE UNIQUE INDEX "IX_InsightReports_ReporterCandidateId_InsightId" ON "InsightReports" ("ReporterCandidateId", "InsightId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InsightReports_Status_CreatedAtUtc" ON "InsightReports" ("Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InterviewInsights_AuthorCandidateId_CreatedAtUtc" ON "InterviewInsights" ("AuthorCandidateId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InterviewInsights_CompanyId_Status_PublishedAtUtc" ON "InterviewInsights" ("CompanyId", "Status", "PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE INDEX "IX_InterviewInsights_JobId" ON "InterviewInsights" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    CREATE UNIQUE INDEX "IX_InterviewRounds_InterviewInsightId_Sequence" ON "InterviewRounds" ("InterviewInsightId", "Sequence") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260822085739_AddInterviewInsights') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260822085739_AddInterviewInsights', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    ALTER TABLE "InterviewInsights" ADD "InterviewFormat" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    ALTER TABLE "CandidateInterviewSchedules" ADD "ApproximateTimeOfDay" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    ALTER TABLE "CandidateInterviewSchedules" ADD "ExpectedRoundTypes" character varying(160);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    ALTER TABLE "CandidateInterviewSchedules" ADD "InterviewFormat" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    ALTER TABLE "CandidateInterviewSchedules" ADD "PreparationStatus" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    ALTER TABLE "CandidateInterviewSchedules" ADD "ReminderRequested" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260823154856_ExtendInterviewInsightsUiContracts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260823154856_ExtendInterviewInsightsUiContracts', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    ALTER TABLE "Companies" ADD "NormalizedName" character varying(160) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    ALTER TABLE "Companies" ADD "SubmissionSource" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    ALTER TABLE "Companies" ADD "SubmittedByCandidateId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    UPDATE "Companies"
    SET "NormalizedName" = lower(regexp_replace(trim("Name"), '\s+', ' ', 'g'));
    ALTER TABLE "Companies" ALTER COLUMN "NormalizedName" DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    CREATE UNIQUE INDEX "IX_Companies_NormalizedName" ON "Companies" ("NormalizedName") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    CREATE INDEX "IX_Companies_SubmittedByCandidateId_CreatedAtUtc" ON "Companies" ("SubmittedByCandidateId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    ALTER TABLE "Companies" ADD CONSTRAINT "FK_Companies_Users_SubmittedByCandidateId" FOREIGN KEY ("SubmittedByCandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824084904_AddCandidateCompanySubmissions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824084904_AddCandidateCompanySubmissions', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyApplications" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "ExternalApplicationUrl" character varying(2048) NOT NULL,
        "NormalizedApplicationUrl" character varying(2048) NOT NULL,
        "Status" integer NOT NULL,
        "Priority" integer NOT NULL,
        "ScheduledAtUtc" timestamp with time zone NOT NULL,
        "StartedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "RetryCount" integer NOT NULL,
        "FailureKind" integer NOT NULL,
        "LastErrorCode" character varying(100),
        "RequiresUserInput" boolean NOT NULL,
        "MatchScore" numeric(5,2),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyApplications" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyApplications_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AIApplyApplications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyPreferences" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "JobTitlesJson" text NOT NULL,
        "SkillsJson" text NOT NULL,
        "PreferredLocationsJson" text NOT NULL,
        "WorkplaceTypesJson" text NOT NULL,
        "EmploymentTypesJson" text NOT NULL,
        "MinimumExperience" numeric(4,1),
        "MaximumExperience" numeric(4,1),
        "MinimumSalary" numeric(14,2),
        "MaximumSalary" numeric(14,2),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyPreferences" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyPreferences_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyProfiles" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GitHubUrl" character varying(2048),
        "WillingToRelocate" boolean,
        "WorkAuthorization" character varying(200),
        "VisaSponsorshipPreference" character varying(200),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyProfiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyProfiles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyRules" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "RuleType" integer NOT NULL,
        "Operator" integer NOT NULL,
        "Value" character varying(1000) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyRules" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyRules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplySettings" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Enabled" boolean NOT NULL,
        "Paused" boolean NOT NULL,
        "PreferredStartTime" time without time zone,
        "Timezone" character varying(100) NOT NULL,
        "PreferredDaysJson" text NOT NULL,
        "AutoResumeApplications" boolean NOT NULL,
        "AllowAIGeneratedAnswers" boolean NOT NULL,
        "RequireConfirmationBeforeSubmit" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplySettings" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplySettings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "UserApplicationAnswers" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Question" character varying(2000) NOT NULL,
        "NormalizedQuestion" character varying(1000) NOT NULL,
        "Answer" character varying(4000) NOT NULL,
        "Category" character varying(100),
        "Source" integer NOT NULL,
        "Confidence" numeric(5,4) NOT NULL,
        "IsVerified" boolean NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_UserApplicationAnswers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_UserApplicationAnswers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyCosts" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "AIRequestCount" integer NOT NULL,
        "AIInputTokens" bigint NOT NULL,
        "AIOutputTokens" bigint NOT NULL,
        "BrowserExecutionSeconds" numeric(12,3) NOT NULL,
        "ProxyCost" numeric(18,6) NOT NULL,
        "RetryCount" integer NOT NULL,
        "EstimatedCost" numeric(18,6) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyCosts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyCosts_AIApplyApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "AIApplyApplications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AIApplyCosts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyExecutionLogs" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "EventType" integer NOT NULL,
        "Status" integer NOT NULL,
        "MessageCode" character varying(100) NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "CompletedAtUtc" timestamp with time zone,
        "MetadataJson" text,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyExecutionLogs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyExecutionLogs_AIApplyApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "AIApplyApplications" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE TABLE "AIApplyQuestions" (
        "Id" uuid NOT NULL,
        "ApplicationId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Question" character varying(2000) NOT NULL,
        "NormalizedQuestion" character varying(1000) NOT NULL,
        "QuestionType" character varying(50) NOT NULL,
        "SuggestedAnswer" character varying(4000),
        "FinalAnswer" character varying(4000),
        "Status" integer NOT NULL,
        "AnsweredAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyQuestions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AIApplyQuestions_AIApplyApplications_ApplicationId" FOREIGN KEY ("ApplicationId") REFERENCES "AIApplyApplications" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AIApplyQuestions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyApplications_IsDeleted" ON "AIApplyApplications" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyApplications_JobId" ON "AIApplyApplications" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyApplications_Status_ScheduledAtUtc_Priority" ON "AIApplyApplications" ("Status", "ScheduledAtUtc", "Priority");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_AIApplyApplications_UserId_JobId" ON "AIApplyApplications" ("UserId", "JobId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_AIApplyApplications_UserId_NormalizedApplicationUrl" ON "AIApplyApplications" ("UserId", "NormalizedApplicationUrl") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyApplications_UserId_Status_CreatedAtUtc" ON "AIApplyApplications" ("UserId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_AIApplyCosts_ApplicationId" ON "AIApplyCosts" ("ApplicationId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyCosts_IsDeleted" ON "AIApplyCosts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyCosts_UserId_CreatedAtUtc" ON "AIApplyCosts" ("UserId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyExecutionLogs_ApplicationId_CreatedAtUtc" ON "AIApplyExecutionLogs" ("ApplicationId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyExecutionLogs_IsDeleted" ON "AIApplyExecutionLogs" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyPreferences_IsDeleted" ON "AIApplyPreferences" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_AIApplyPreferences_UserId" ON "AIApplyPreferences" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyProfiles_IsDeleted" ON "AIApplyProfiles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_AIApplyProfiles_UserId" ON "AIApplyProfiles" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyQuestions_ApplicationId" ON "AIApplyQuestions" ("ApplicationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyQuestions_IsDeleted" ON "AIApplyQuestions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyQuestions_UserId_Status_CreatedAtUtc" ON "AIApplyQuestions" ("UserId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyRules_IsDeleted" ON "AIApplyRules" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplyRules_UserId_IsEnabled" ON "AIApplyRules" ("UserId", "IsEnabled");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplySettings_Enabled_Paused" ON "AIApplySettings" ("Enabled", "Paused");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_AIApplySettings_IsDeleted" ON "AIApplySettings" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_AIApplySettings_UserId" ON "AIApplySettings" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_UserApplicationAnswers_IsDeleted" ON "UserApplicationAnswers" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE INDEX "IX_UserApplicationAnswers_UserId_IsActive" ON "UserApplicationAnswers" ("UserId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    CREATE UNIQUE INDEX "IX_UserApplicationAnswers_UserId_NormalizedQuestion" ON "UserApplicationAnswers" ("UserId", "NormalizedQuestion") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902210125_AddAIApplyFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260902210125_AddAIApplyFoundation', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "ClaimedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "DeadLetteredAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "FailureClassification" character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "FirstFailureAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "LastFailureAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "LeaseExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "LeaseOwner" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "SubmissionAttemptedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    ALTER TABLE "AIApplyApplications" ADD "SubmissionConfirmedAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    CREATE INDEX "IX_AIApplyApplications_Status_LeaseExpiresAtUtc" ON "AIApplyApplications" ("Status", "LeaseExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902222139_AddAIApplyReliability') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260902222139_AddAIApplyReliability', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904081623_AddExternalJobSiteSessions') THEN
    CREATE TABLE "ExternalJobSiteSessions" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Site" integer NOT NULL,
        "Status" integer NOT NULL,
        "EncryptedStorageState" bytea NOT NULL,
        "EncryptionPurposeVersion" character varying(16) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "LastValidatedAtUtc" timestamp with time zone,
        "RevokedAtUtc" timestamp with time zone,
        "RequiresReauthentication" boolean NOT NULL,
        "Version" bigint NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_ExternalJobSiteSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ExternalJobSiteSessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904081623_AddExternalJobSiteSessions') THEN
    CREATE INDEX "IX_ExternalJobSiteSessions_ExpiresAtUtc" ON "ExternalJobSiteSessions" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904081623_AddExternalJobSiteSessions') THEN
    CREATE INDEX "IX_ExternalJobSiteSessions_IsDeleted" ON "ExternalJobSiteSessions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904081623_AddExternalJobSiteSessions') THEN
    CREATE INDEX "IX_ExternalJobSiteSessions_Status" ON "ExternalJobSiteSessions" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904081623_AddExternalJobSiteSessions') THEN
    CREATE UNIQUE INDEX "IX_ExternalJobSiteSessions_UserId_Site" ON "ExternalJobSiteSessions" ("UserId", "Site") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904081623_AddExternalJobSiteSessions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260904081623_AddExternalJobSiteSessions', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE TABLE "AIApplySiteOperationalStates" (
        "Id" uuid NOT NULL,
        "Site" integer NOT NULL,
        "CircuitState" integer NOT NULL,
        "ObservationWindowStartedAtUtc" timestamp with time zone NOT NULL,
        "SampleCount" integer NOT NULL,
        "FailureCount" integer NOT NULL,
        "OpenedAtUtc" timestamp with time zone,
        "CooldownUntilUtc" timestamp with time zone,
        "HalfOpenProbeOwner" character varying(100),
        "HalfOpenProbeLeaseExpiresAtUtc" timestamp with time zone,
        "LastSuccessAtUtc" timestamp with time zone,
        "LastFailureAtUtc" timestamp with time zone,
        "ManualOverrideOpen" boolean,
        "Version" bigint NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplySiteOperationalStates" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE TABLE "AIApplyWorkerInstances" (
        "Id" uuid NOT NULL,
        "WorkerInstanceId" character varying(100) NOT NULL,
        "Status" integer NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "LastHeartbeatAtUtc" timestamp with time zone NOT NULL,
        "LastSuccessfulPollAtUtc" timestamp with time zone,
        "LastFailureAtUtc" timestamp with time zone,
        "StoppedAtUtc" timestamp with time zone,
        "ProcessingCount" integer NOT NULL,
        "MaximumConcurrency" integer NOT NULL,
        "HostVersion" character varying(64) NOT NULL,
        "Version" bigint NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AIApplyWorkerInstances" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE INDEX "IX_AIApplySiteOperationalStates_CircuitState_CooldownUntilUtc" ON "AIApplySiteOperationalStates" ("CircuitState", "CooldownUntilUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE INDEX "IX_AIApplySiteOperationalStates_IsDeleted" ON "AIApplySiteOperationalStates" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE UNIQUE INDEX "IX_AIApplySiteOperationalStates_Site" ON "AIApplySiteOperationalStates" ("Site") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE INDEX "IX_AIApplyWorkerInstances_IsDeleted" ON "AIApplyWorkerInstances" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE INDEX "IX_AIApplyWorkerInstances_Status_LastHeartbeatAtUtc" ON "AIApplyWorkerInstances" ("Status", "LastHeartbeatAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    CREATE UNIQUE INDEX "IX_AIApplyWorkerInstances_WorkerInstanceId" ON "AIApplyWorkerInstances" ("WorkerInstanceId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000000', 0, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000001', 1, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000002', 2, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000003', 3, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000004', 4, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000005', 5, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000006', 6, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000007', 7, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000008', 8, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000009', 9, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    INSERT INTO "AIApplySiteOperationalStates" ("Id", "Site", "CircuitState", "ObservationWindowStartedAtUtc", "SampleCount", "FailureCount", "Version", "CreatedAtUtc", "IsDeleted")
    VALUES ('39000000-0000-0000-0000-000000000010', 10, 1, TIMESTAMPTZ '1970-01-01T00:00:00Z', 0, 0, 0, TIMESTAMPTZ '1970-01-01T00:00:00Z', FALSE);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260904195443_AddAIApplyOperationalState') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260904195443_AddAIApplyOperationalState', '9.0.8');
    END IF;
END $EF$;
COMMIT;

