START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE TABLE "CareerConsultants" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CompanyId" uuid,
        "DisplayName" character varying(120) NOT NULL,
        "ProfessionalHeadline" character varying(200) NOT NULL,
        "Bio" character varying(4000) NOT NULL,
        "CompanyName" character varying(200) NOT NULL,
        "CurrentRole" character varying(160) NOT NULL,
        "YearsOfExperience" numeric(4,1) NOT NULL,
        "ProfessionalType" integer NOT NULL,
        "LinkedInUrl" character varying(500) NOT NULL,
        "VerificationStatus" integer NOT NULL,
        "VerificationMethod" character varying(50),
        "VerificationReason" character varying(1000),
        "ReviewedByUserId" uuid,
        "ReviewedAtUtc" timestamp with time zone,
        "VerifiedAtUtc" timestamp with time zone,
        "TermsAcceptedAtUtc" timestamp with time zone NOT NULL,
        "PolicyVersion" character varying(50) NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerConsultants" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CareerConsultants_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerConsultants_Users_ReviewedByUserId" FOREIGN KEY ("ReviewedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerConsultants_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE TABLE "CareerConsultantServices" (
        "Id" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "ServiceType" character varying(60) NOT NULL,
        "Title" character varying(160) NOT NULL,
        "Description" character varying(3000) NOT NULL,
        "DurationMinutes" integer NOT NULL,
        "Price" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerConsultantServices" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CareerConsultantServices_Duration" CHECK ("DurationMinutes" >= 15 AND "DurationMinutes" <= 180),
        CONSTRAINT "CK_CareerConsultantServices_Price" CHECK ("Price" > 0 AND "Price" <= 1000000),
        CONSTRAINT "FK_CareerConsultantServices_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE TABLE "CareerConsultantTags" (
        "Id" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "Kind" integer NOT NULL,
        "Value" character varying(60) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerConsultantTags" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CareerConsultantTags_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultants_CompanyId" ON "CareerConsultants" ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultants_IsDeleted" ON "CareerConsultants" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultants_ProfessionalType_YearsOfExperience" ON "CareerConsultants" ("ProfessionalType", "YearsOfExperience");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultants_ReviewedByUserId" ON "CareerConsultants" ("ReviewedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE UNIQUE INDEX "IX_CareerConsultants_UserId" ON "CareerConsultants" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultants_VerificationStatus_CreatedAtUtc_Id" ON "CareerConsultants" ("VerificationStatus", "CreatedAtUtc", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultantServices_ConsultantId_IsActive" ON "CareerConsultantServices" ("ConsultantId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultantServices_IsDeleted" ON "CareerConsultantServices" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultantServices_ServiceType_Currency_Price" ON "CareerConsultantServices" ("ServiceType", "Currency", "Price");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE UNIQUE INDEX "IX_CareerConsultantTags_ConsultantId_Kind_Value" ON "CareerConsultantTags" ("ConsultantId", "Kind", "Value");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultantTags_IsDeleted" ON "CareerConsultantTags" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    CREATE INDEX "IX_CareerConsultantTags_Kind_Value_ConsultantId" ON "CareerConsultantTags" ("Kind", "Value", "ConsultantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920171246_AddCareerGuidanceFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260920171246_AddCareerGuidanceFoundation', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE EXTENSION IF NOT EXISTS btree_gist;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    ALTER TABLE "CareerConsultants" ADD "IsAcceptingBookings" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    ALTER TABLE "CareerConsultants" ADD "TimeZoneId" character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE TABLE "CareerConsultantAvailability" (
        "Id" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "DayOfWeek" integer NOT NULL,
        "StartTime" time without time zone NOT NULL,
        "EndTime" time without time zone NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerConsultantAvailability" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CareerAvailability_Day" CHECK ("DayOfWeek" BETWEEN 0 AND 6),
        CONSTRAINT "CK_CareerAvailability_Time" CHECK ("StartTime" < "EndTime"),
        CONSTRAINT "FK_CareerConsultantAvailability_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE TABLE "CareerConsultantAvailabilityExceptions" (
        "Id" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "LocalDate" date NOT NULL,
        "StartTime" time without time zone,
        "EndTime" time without time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerConsultantAvailabilityExceptions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CareerAvailabilityException_Time" CHECK (("StartTime" IS NULL AND "EndTime" IS NULL) OR ("StartTime" IS NOT NULL AND "EndTime" IS NOT NULL AND "StartTime" < "EndTime")),
        CONSTRAINT "FK_CareerConsultantAvailabilityExceptions_CareerConsultants_Co~" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE TABLE "CareerGuidanceBookings" (
        "Id" uuid NOT NULL,
        "CandidateUserId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "ConsultantServiceId" uuid NOT NULL,
        "StartUtc" timestamp with time zone NOT NULL,
        "EndUtc" timestamp with time zone NOT NULL,
        "ConsultantTimeZoneSnapshot" character varying(100) NOT NULL,
        "ServiceTitleSnapshot" character varying(160) NOT NULL,
        "ServiceTypeSnapshot" character varying(60) NOT NULL,
        "DurationMinutesSnapshot" integer NOT NULL,
        "PriceSnapshot" numeric(18,2) NOT NULL,
        "CurrencySnapshot" character varying(3) NOT NULL,
        "Status" integer NOT NULL,
        "TargetCompany" character varying(200),
        "TargetRole" character varying(200),
        "YearsOfExperience" numeric(4,1),
        "CurrentRoleOrStatus" character varying(200),
        "SessionGoal" character varying(2000) NOT NULL,
        "Questions" character varying(4000),
        "Notes" character varying(2000),
        "CancellationReason" character varying(1000),
        "CancelledByUserId" uuid,
        "CancelledAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceBookings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CareerBooking_Duration" CHECK ("DurationMinutesSnapshot" BETWEEN 15 AND 180),
        CONSTRAINT "CK_CareerBooking_Price" CHECK ("PriceSnapshot" > 0),
        CONSTRAINT "CK_CareerBooking_Status" CHECK ("Status" BETWEEN 1 AND 7),
        CONSTRAINT "CK_CareerBooking_Time" CHECK ("StartUtc" < "EndUtc"),
        CONSTRAINT "FK_CareerGuidanceBookings_CareerConsultantServices_ConsultantS~" FOREIGN KEY ("ConsultantServiceId") REFERENCES "CareerConsultantServices" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceBookings_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceBookings_Users_CancelledByUserId" FOREIGN KEY ("CancelledByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceBookings_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerConsultantAvailability_ConsultantId_DayOfWeek_IsActive" ON "CareerConsultantAvailability" ("ConsultantId", "DayOfWeek", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerConsultantAvailability_IsDeleted" ON "CareerConsultantAvailability" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerConsultantAvailabilityExceptions_ConsultantId_LocalDa~" ON "CareerConsultantAvailabilityExceptions" ("ConsultantId", "LocalDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerConsultantAvailabilityExceptions_IsDeleted" ON "CareerConsultantAvailabilityExceptions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerGuidanceBookings_CancelledByUserId" ON "CareerGuidanceBookings" ("CancelledByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerGuidanceBookings_CandidateUserId_Status_StartUtc" ON "CareerGuidanceBookings" ("CandidateUserId", "Status", "StartUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerGuidanceBookings_ConsultantId_Status_StartUtc_EndUtc" ON "CareerGuidanceBookings" ("ConsultantId", "Status", "StartUtc", "EndUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerGuidanceBookings_ConsultantServiceId" ON "CareerGuidanceBookings" ("ConsultantServiceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    CREATE INDEX "IX_CareerGuidanceBookings_IsDeleted" ON "CareerGuidanceBookings" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    ALTER TABLE "CareerGuidanceBookings"
    ADD CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap"
    EXCLUDE USING gist (
        "ConsultantId" WITH =,
        tstzrange("StartUtc", "EndUtc", '[)') WITH &&
    ) WHERE ("IsDeleted" = FALSE AND "Status" IN (1, 2));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260920180130_AddCareerGuidanceSchedulingAndBookings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260920180130_AddCareerGuidanceSchedulingAndBookings', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    ALTER TABLE "CareerGuidanceBookings" ADD "RequiresPayment" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE TABLE "CareerGuidancePayments" (
        "Id" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "CandidateUserId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "Provider" character varying(30) NOT NULL,
        "ProviderOrderId" character varying(100),
        "ProviderPaymentId" character varying(100),
        "AmountGross" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "PlatformCommissionPercentSnapshot" numeric(7,4) NOT NULL,
        "PlatformCommissionAmount" numeric(18,2) NOT NULL,
        "ConsultantNetAmount" numeric(18,2) NOT NULL,
        "Status" integer NOT NULL,
        "FailureCode" character varying(60),
        "PaidAtUtc" timestamp with time zone,
        "RequiresRefundReview" boolean NOT NULL,
        "RefundPolicyVersion" character varying(40) NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidancePayments" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGPayment_Amounts" CHECK ("AmountGross" > 0 AND "PlatformCommissionAmount" >= 0 AND "ConsultantNetAmount" >= 0 AND "AmountGross" = "PlatformCommissionAmount" + "ConsultantNetAmount"),
        CONSTRAINT "CK_CGPayment_Commission" CHECK ("PlatformCommissionPercentSnapshot" BETWEEN 0 AND 100),
        CONSTRAINT "CK_CGPayment_Status" CHECK ("Status" BETWEEN 1 AND 6),
        CONSTRAINT "FK_CareerGuidancePayments_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidancePayments_CareerGuidanceBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "CareerGuidanceBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidancePayments_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE TABLE "CareerGuidanceEarnings" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "GrossAmount" numeric(18,2) NOT NULL,
        "PlatformCommissionAmount" numeric(18,2) NOT NULL,
        "NetAmount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "Status" integer NOT NULL,
        "AvailableAtUtc" timestamp with time zone,
        "SettledAtUtc" timestamp with time zone,
        "ReversedAtUtc" timestamp with time zone,
        "SettlementReference" character varying(100),
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceEarnings" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGEarning_Amounts" CHECK ("GrossAmount" > 0 AND "PlatformCommissionAmount" >= 0 AND "NetAmount" >= 0 AND "GrossAmount" = "PlatformCommissionAmount" + "NetAmount"),
        CONSTRAINT "CK_CGEarning_Status" CHECK ("Status" BETWEEN 1 AND 4),
        CONSTRAINT "FK_CareerGuidanceEarnings_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceEarnings_CareerGuidanceBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "CareerGuidanceBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceEarnings_CareerGuidancePayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CareerGuidancePayments" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE TABLE "CareerGuidancePaymentEvents" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "EventKey" character varying(64) NOT NULL,
        "EventType" character varying(60) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidancePaymentEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CareerGuidancePaymentEvents_CareerGuidancePayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CareerGuidancePayments" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE TABLE "CareerGuidanceRefunds" (
        "Id" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "CandidateUserId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "Amount" numeric(18,2) NOT NULL,
        "Currency" character varying(3) NOT NULL,
        "ReasonCode" integer NOT NULL,
        "RequestedByUserId" uuid NOT NULL,
        "RequestedAtUtc" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "ProviderRefundId" character varying(100),
        "ProcessedAtUtc" timestamp with time zone,
        "FailedAtUtc" timestamp with time zone,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceRefunds" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGRefund_Amount" CHECK ("Amount" > 0),
        CONSTRAINT "CK_CGRefund_Status" CHECK ("Status" BETWEEN 1 AND 4 AND "ReasonCode" BETWEEN 1 AND 4),
        CONSTRAINT "FK_CareerGuidanceRefunds_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceRefunds_CareerGuidanceBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "CareerGuidanceBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceRefunds_CareerGuidancePayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CareerGuidancePayments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceRefunds_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceRefunds_Users_RequestedByUserId" FOREIGN KEY ("RequestedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceEarnings_BookingId" ON "CareerGuidanceEarnings" ("BookingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceEarnings_ConsultantId_Status_CreatedAtUtc" ON "CareerGuidanceEarnings" ("ConsultantId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceEarnings_IsDeleted" ON "CareerGuidanceEarnings" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceEarnings_PaymentId" ON "CareerGuidanceEarnings" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceEarnings_Status_CreatedAtUtc" ON "CareerGuidanceEarnings" ("Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidancePaymentEvents_EventKey" ON "CareerGuidancePaymentEvents" ("EventKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidancePaymentEvents_IsDeleted" ON "CareerGuidancePaymentEvents" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidancePaymentEvents_PaymentId" ON "CareerGuidancePaymentEvents" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidancePayments_BookingId" ON "CareerGuidancePayments" ("BookingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidancePayments_CandidateUserId_Status_CreatedAtUtc" ON "CareerGuidancePayments" ("CandidateUserId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidancePayments_ConsultantId" ON "CareerGuidancePayments" ("ConsultantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidancePayments_IsDeleted" ON "CareerGuidancePayments" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidancePayments_ProviderOrderId" ON "CareerGuidancePayments" ("ProviderOrderId") WHERE "ProviderOrderId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidancePayments_ProviderPaymentId" ON "CareerGuidancePayments" ("ProviderPaymentId") WHERE "ProviderPaymentId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidancePayments_Status_RequiresRefundReview_CreatedA~" ON "CareerGuidancePayments" ("Status", "RequiresRefundReview", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceRefunds_BookingId" ON "CareerGuidanceRefunds" ("BookingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceRefunds_CandidateUserId_Status_CreatedAtUtc" ON "CareerGuidanceRefunds" ("CandidateUserId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceRefunds_ConsultantId" ON "CareerGuidanceRefunds" ("ConsultantId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceRefunds_IsDeleted" ON "CareerGuidanceRefunds" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceRefunds_PaymentId" ON "CareerGuidanceRefunds" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceRefunds_ProviderRefundId" ON "CareerGuidanceRefunds" ("ProviderRefundId") WHERE "ProviderRefundId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceRefunds_RequestedByUserId" ON "CareerGuidanceRefunds" ("RequestedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    CREATE INDEX "IX_CareerGuidanceRefunds_Status_CreatedAtUtc" ON "CareerGuidanceRefunds" ("Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921045557_AddCareerGuidancePaymentsAndEarnings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921045557_AddCareerGuidancePaymentsAndEarnings', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE TABLE "CareerGuidanceSessions" (
        "Id" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "CandidateUserId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "MeetingProvider" character varying(30) NOT NULL,
        "ProviderMeetingId" character varying(200),
        "ProtectedParticipantUrl" bytea,
        "ProtectedHostUrl" bytea,
        "ScheduledStartUtc" timestamp with time zone NOT NULL,
        "ScheduledEndUtc" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "StartedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "CandidateJoinedAtUtc" timestamp with time zone,
        "ConsultantJoinedAtUtc" timestamp with time zone,
        "CandidateNoShowMarkedAtUtc" timestamp with time zone,
        "ConsultantNoShowMarkedAtUtc" timestamp with time zone,
        "ConsultantNoShowReportedAtUtc" timestamp with time zone,
        "MeetingProvisioningAttemptedAtUtc" timestamp with time zone NOT NULL,
        "MeetingCreatedAtUtc" timestamp with time zone,
        "EarningReleaseDelayHours" integer NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGSession_Interval" CHECK ("ScheduledStartUtc" < "ScheduledEndUtc"),
        CONSTRAINT "CK_CGSession_ReleaseDelay" CHECK ("EarningReleaseDelayHours" BETWEEN 24 AND 2160),
        CONSTRAINT "CK_CGSession_Status" CHECK ("Status" BETWEEN 1 AND 7),
        CONSTRAINT "FK_CareerGuidanceSessions_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceSessions_CareerGuidanceBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "CareerGuidanceBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceSessions_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE TABLE "CareerGuidanceSessionReminders" (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "RecipientUserId" uuid NOT NULL,
        "OffsetMinutes" integer NOT NULL,
        "ScheduledForUtc" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "SentAtUtc" timestamp with time zone,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceSessionReminders" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGReminder_Offset" CHECK ("OffsetMinutes" BETWEEN 1 AND 10080),
        CONSTRAINT "CK_CGReminder_Status" CHECK ("Status" BETWEEN 1 AND 3),
        CONSTRAINT "FK_CareerGuidanceSessionReminders_CareerGuidanceSessions_Sessi~" FOREIGN KEY ("SessionId") REFERENCES "CareerGuidanceSessions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceSessionReminders_Users_RecipientUserId" FOREIGN KEY ("RecipientUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE INDEX "IX_CareerGuidanceSessionReminders_IsDeleted" ON "CareerGuidanceSessionReminders" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE INDEX "IX_CareerGuidanceSessionReminders_RecipientUserId" ON "CareerGuidanceSessionReminders" ("RecipientUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceSessionReminders_SessionId_RecipientUserId_Of~" ON "CareerGuidanceSessionReminders" ("SessionId", "RecipientUserId", "OffsetMinutes");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE INDEX "IX_CareerGuidanceSessionReminders_Status_ScheduledForUtc" ON "CareerGuidanceSessionReminders" ("Status", "ScheduledForUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceSessions_BookingId" ON "CareerGuidanceSessions" ("BookingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE INDEX "IX_CareerGuidanceSessions_CandidateUserId_Status_ScheduledStar~" ON "CareerGuidanceSessions" ("CandidateUserId", "Status", "ScheduledStartUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE INDEX "IX_CareerGuidanceSessions_ConsultantId_Status_ScheduledStartUtc" ON "CareerGuidanceSessions" ("ConsultantId", "Status", "ScheduledStartUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE INDEX "IX_CareerGuidanceSessions_IsDeleted" ON "CareerGuidanceSessions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceSessions_MeetingProvider_ProviderMeetingId" ON "CareerGuidanceSessions" ("MeetingProvider", "ProviderMeetingId") WHERE "ProviderMeetingId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921061646_AddCareerGuidanceSessionsAndReminders') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921061646_AddCareerGuidanceSessionsAndReminders', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE TABLE "CareerGuidanceDisputes" (
        "Id" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "SessionId" uuid,
        "PaymentId" uuid NOT NULL,
        "CandidateUserId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "Category" integer NOT NULL,
        "Description" character varying(4000) NOT NULL,
        "RequestedRefund" boolean NOT NULL,
        "Status" integer NOT NULL,
        "Resolution" integer NOT NULL,
        "AdminNotes" character varying(2000),
        "SubmittedAtUtc" timestamp with time zone NOT NULL,
        "ResolvedAtUtc" timestamp with time zone,
        "ResolvedByUserId" uuid,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceDisputes" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGDispute_Description" CHECK (length(btrim("Description")) > 0),
        CONSTRAINT "CK_CGDispute_Enums" CHECK ("Status" BETWEEN 1 AND 7 AND "Category" BETWEEN 1 AND 8 AND "Resolution" BETWEEN 1 AND 8),
        CONSTRAINT "CK_CGDispute_Resolution" CHECK (("Status" <= 4 AND "Resolution" = 1 AND "ResolvedAtUtc" IS NULL AND "ResolvedByUserId" IS NULL) OR ("Status" >= 5 AND "Resolution" > 1 AND "ResolvedAtUtc" IS NOT NULL AND "ResolvedByUserId" IS NOT NULL)),
        CONSTRAINT "FK_CareerGuidanceDisputes_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceDisputes_CareerGuidanceBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "CareerGuidanceBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceDisputes_CareerGuidancePayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CareerGuidancePayments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceDisputes_CareerGuidanceSessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "CareerGuidanceSessions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceDisputes_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceDisputes_Users_ResolvedByUserId" FOREIGN KEY ("ResolvedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE TABLE "CareerGuidanceReviews" (
        "Id" uuid NOT NULL,
        "BookingId" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "ConsultantId" uuid NOT NULL,
        "CandidateUserId" uuid NOT NULL,
        "Rating" integer NOT NULL,
        "Title" character varying(120),
        "Comment" character varying(2000),
        "IsPublished" boolean NOT NULL,
        "ModerationStatus" integer NOT NULL,
        "ModerationReason" character varying(1000),
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceReviews" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGReview_Moderation" CHECK ("ModerationStatus" BETWEEN 1 AND 4 AND (NOT "IsPublished" OR ("ModerationStatus" = 2 AND NOT "IsDeleted"))),
        CONSTRAINT "CK_CGReview_Rating" CHECK ("Rating" BETWEEN 1 AND 5),
        CONSTRAINT "FK_CareerGuidanceReviews_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceReviews_CareerGuidanceBookings_BookingId" FOREIGN KEY ("BookingId") REFERENCES "CareerGuidanceBookings" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceReviews_CareerGuidancePayments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "CareerGuidancePayments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceReviews_CareerGuidanceSessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "CareerGuidanceSessions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceReviews_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE TABLE "CareerGuidanceDisputeEvidence" (
        "Id" uuid NOT NULL,
        "DisputeId" uuid NOT NULL,
        "SubmittedByUserId" uuid NOT NULL,
        "RequestId" uuid NOT NULL,
        "EvidenceType" integer NOT NULL,
        "Description" character varying(4000) NOT NULL,
        "IsPrivateToAdmin" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CareerGuidanceDisputeEvidence" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CGEvidence_Description" CHECK (length(btrim("Description")) > 0),
        CONSTRAINT "CK_CGEvidence_Type" CHECK ("EvidenceType" BETWEEN 1 AND 3 AND ("EvidenceType" <> 3 OR "IsPrivateToAdmin")),
        CONSTRAINT "FK_CareerGuidanceDisputeEvidence_CareerGuidanceDisputes_Disput~" FOREIGN KEY ("DisputeId") REFERENCES "CareerGuidanceDisputes" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_CareerGuidanceDisputeEvidence_Users_SubmittedByUserId" FOREIGN KEY ("SubmittedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceDisputeEvidence_DisputeId_RequestId" ON "CareerGuidanceDisputeEvidence" ("DisputeId", "RequestId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputeEvidence_IsDeleted" ON "CareerGuidanceDisputeEvidence" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputeEvidence_SubmittedByUserId" ON "CareerGuidanceDisputeEvidence" ("SubmittedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceDisputes_BookingId" ON "CareerGuidanceDisputes" ("BookingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_CandidateUserId_Status_CreatedAtUtc" ON "CareerGuidanceDisputes" ("CandidateUserId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_ConsultantId_Status_CreatedAtUtc" ON "CareerGuidanceDisputes" ("ConsultantId", "Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_IsDeleted" ON "CareerGuidanceDisputes" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_PaymentId_Status" ON "CareerGuidanceDisputes" ("PaymentId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_ResolvedByUserId" ON "CareerGuidanceDisputes" ("ResolvedByUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_SessionId" ON "CareerGuidanceDisputes" ("SessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceDisputes_Status_CreatedAtUtc" ON "CareerGuidanceDisputes" ("Status", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE UNIQUE INDEX "IX_CareerGuidanceReviews_BookingId" ON "CareerGuidanceReviews" ("BookingId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceReviews_CandidateUserId" ON "CareerGuidanceReviews" ("CandidateUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceReviews_ConsultantId_ModerationStatus_Created~" ON "CareerGuidanceReviews" ("ConsultantId", "ModerationStatus", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceReviews_IsDeleted" ON "CareerGuidanceReviews" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceReviews_PaymentId" ON "CareerGuidanceReviews" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    CREATE INDEX "IX_CareerGuidanceReviews_SessionId" ON "CareerGuidanceReviews" ("SessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921074724_AddCareerGuidanceReviewsDisputesAndTrust') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921074724_AddCareerGuidanceReviewsDisputesAndTrust', '9.0.8');
    END IF;
END $EF$;
COMMIT;

