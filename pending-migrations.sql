START TRANSACTION;
ALTER TABLE "CareerGuidanceSessions" DROP CONSTRAINT "CK_CGSession_Status";

ALTER TABLE "CareerGuidanceRefunds" DROP CONSTRAINT "CK_CGRefund_Status";

ALTER TABLE "CareerGuidanceBookings" DROP CONSTRAINT "CK_CareerBooking_Status";

ALTER TABLE "CareerGuidanceSessions" ADD "CandidateConfirmedAtUtc" timestamp with time zone;

ALTER TABLE "CareerGuidanceSessions" ADD "CompletionRequestedAtUtc" timestamp with time zone;

ALTER TABLE "CareerGuidanceRefunds" ADD "PolicyDecisionSnapshotJson" jsonb;

ALTER TABLE "CareerGuidanceBookings" ADD "AcceptanceDueAtUtc" timestamp with time zone;

ALTER TABLE "CareerGuidanceBookings" ADD "ConsultantDecision" integer;

ALTER TABLE "CareerGuidanceBookings" ADD "ConsultantDecisionAtUtc" timestamp with time zone;

ALTER TABLE "CareerGuidanceBookings" ADD "ConsultantDecisionByUserId" uuid;

ALTER TABLE "CareerGuidanceBookings" ADD "LifecycleVersion" integer NOT NULL DEFAULT 1;

ALTER TABLE "CareerGuidanceBookings" ADD "PolicySnapshotJson" jsonb;

ALTER TABLE "CareerGuidanceBookings" ADD "RequestId" uuid;

ALTER TABLE "CareerGuidanceBookings" ADD "RequestPayloadHash" character varying(64);

CREATE TABLE "CareerConsultantPayouts" (
    "Id" uuid NOT NULL,
    "EarningId" uuid NOT NULL,
    "ConsultantId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "Status" integer NOT NULL,
    "ProviderPayoutId" character varying(150),
    "ProcessingAtUtc" timestamp with time zone,
    "PaidAtUtc" timestamp with time zone,
    "FailedAtUtc" timestamp with time zone,
    "FailureCode" character varying(100),
    "FailureReason" character varying(1000),
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_CareerConsultantPayouts" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_CGPayout_Amount" CHECK ("Amount" > 0),
    CONSTRAINT "CK_CGPayout_Status" CHECK ("Status" BETWEEN 1 AND 4),
    CONSTRAINT "FK_CareerConsultantPayouts_CareerConsultants_ConsultantId" FOREIGN KEY ("ConsultantId") REFERENCES "CareerConsultants" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CareerConsultantPayouts_CareerGuidanceEarnings_EarningId" FOREIGN KEY ("EarningId") REFERENCES "CareerGuidanceEarnings" ("Id") ON DELETE RESTRICT
);

ALTER TABLE "CareerGuidanceSessions" ADD CONSTRAINT "CK_CGSession_Status" CHECK ("Status" BETWEEN 1 AND 8);

ALTER TABLE "CareerGuidanceRefunds" ADD CONSTRAINT "CK_CGRefund_Status" CHECK ("Status" BETWEEN 1 AND 4 AND "ReasonCode" BETWEEN 1 AND 7);

CREATE UNIQUE INDEX "IX_CareerGuidanceBookings_CandidateUserId_RequestId" ON "CareerGuidanceBookings" ("CandidateUserId", "RequestId") WHERE "RequestId" IS NOT NULL;

CREATE INDEX "IX_CareerGuidanceBookings_ConsultantDecisionByUserId" ON "CareerGuidanceBookings" ("ConsultantDecisionByUserId");

CREATE INDEX "IX_CareerGuidanceBookings_Status_AcceptanceDueAtUtc" ON "CareerGuidanceBookings" ("Status", "AcceptanceDueAtUtc");

ALTER TABLE "CareerGuidanceBookings" ADD CONSTRAINT "CK_CareerBooking_ConsultantDecision" CHECK ("ConsultantDecision" IS NULL OR "ConsultantDecision" BETWEEN 1 AND 2);

ALTER TABLE "CareerGuidanceBookings" ADD CONSTRAINT "CK_CareerBooking_LifecycleVersion" CHECK ("LifecycleVersion" >= 1);

ALTER TABLE "CareerGuidanceBookings" ADD CONSTRAINT "CK_CareerBooking_Status" CHECK ("Status" BETWEEN 1 AND 9);

ALTER TABLE "CareerGuidanceBookings"
DROP CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap";

ALTER TABLE "CareerGuidanceBookings"
ADD CONSTRAINT "EX_CareerGuidanceBookings_NoOverlap"
EXCLUDE USING gist (
    "ConsultantId" WITH =,
    tstzrange("StartUtc", "EndUtc", '[)') WITH &&
)
WHERE ("IsDeleted" = FALSE AND "Status" IN (1, 2, 9));

CREATE INDEX "IX_CareerConsultantPayouts_ConsultantId_Status_CreatedAtUtc" ON "CareerConsultantPayouts" ("ConsultantId", "Status", "CreatedAtUtc");

CREATE UNIQUE INDEX "IX_CareerConsultantPayouts_EarningId" ON "CareerConsultantPayouts" ("EarningId");

CREATE INDEX "IX_CareerConsultantPayouts_IsDeleted" ON "CareerConsultantPayouts" ("IsDeleted");

CREATE UNIQUE INDEX "IX_CareerConsultantPayouts_ProviderPayoutId" ON "CareerConsultantPayouts" ("ProviderPayoutId") WHERE "ProviderPayoutId" IS NOT NULL;

CREATE INDEX "IX_CareerConsultantPayouts_Status_CreatedAtUtc" ON "CareerConsultantPayouts" ("Status", "CreatedAtUtc");

ALTER TABLE "CareerGuidanceBookings" ADD CONSTRAINT "FK_CareerGuidanceBookings_Users_ConsultantDecisionByUserId" FOREIGN KEY ("ConsultantDecisionByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261003120351_AddCareerGuidancePaymentLifecycleFoundation', '9.0.8');

CREATE TABLE "SupportTickets" (
    "Id" uuid NOT NULL,
    "TicketNumber" character varying(40) NOT NULL,
    "UserId" uuid,
    "Name" character varying(201) NOT NULL,
    "Email" character varying(256) NOT NULL,
    "Category" integer NOT NULL,
    "Subject" character varying(200) NOT NULL,
    "Description" character varying(5000) NOT NULL,
    "ScreenshotPath" character varying(40),
    "Status" integer NOT NULL,
    "AdminNotes" character varying(5000),
    "ResolvedAtUtc" timestamp with time zone,
    "Revision" bigint NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_SupportTickets" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_SupportTickets_Category" CHECK ("Category" BETWEEN 1 AND 10),
    CONSTRAINT "CK_SupportTickets_ResolvedAtUtc" CHECK (("Status" = 3 AND "ResolvedAtUtc" IS NOT NULL) OR ("Status" <> 3 AND "ResolvedAtUtc" IS NULL)),
    CONSTRAINT "CK_SupportTickets_Status" CHECK ("Status" BETWEEN 1 AND 4),
    CONSTRAINT "FK_SupportTickets_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_SupportTickets_Category_CreatedAtUtc" ON "SupportTickets" ("Category", "CreatedAtUtc");

CREATE INDEX "IX_SupportTickets_CreatedAtUtc" ON "SupportTickets" ("CreatedAtUtc");

CREATE INDEX "IX_SupportTickets_Email" ON "SupportTickets" ("Email");

CREATE INDEX "IX_SupportTickets_IsDeleted" ON "SupportTickets" ("IsDeleted");

CREATE INDEX "IX_SupportTickets_Status_CreatedAtUtc" ON "SupportTickets" ("Status", "CreatedAtUtc");

CREATE UNIQUE INDEX "IX_SupportTickets_TicketNumber" ON "SupportTickets" ("TicketNumber");

CREATE INDEX "IX_SupportTickets_UserId_CreatedAtUtc" ON "SupportTickets" ("UserId", "CreatedAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261004064755_AddSupportTickets', '9.0.8');

COMMIT;

