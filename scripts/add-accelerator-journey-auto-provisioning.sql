START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    ALTER TABLE "AcceleratorLearningPaths" ADD "ConfigurationSource" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    ALTER TABLE "AcceleratorCareerRoles" ADD "ConfigurationSource" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE TABLE "AcceleratorJourneyProvisioningReceipts" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "MarketRoleId" uuid,
        "MarketSkillId" uuid,
        "LearningPathId" uuid NOT NULL,
        "EnrollmentId" uuid NOT NULL,
        "JourneyContextId" uuid NOT NULL,
        "PolicyVersion" character varying(80) NOT NULL,
        "RequiredCompetencies" integer NOT NULL,
        "AssessedCompetencies" integer NOT NULL,
        "UnassessedCompetencyIdsJson" jsonb NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorJourneyProvisioningReceipts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_JourneyProvisioning_Coverage" CHECK ("RequiredCompetencies" > 0 AND "AssessedCompetencies" > 0 AND "AssessedCompetencies" <= "RequiredCompetencies"),
        CONSTRAINT "CK_JourneyProvisioning_Source" CHECK (("MarketRoleId" IS NOT NULL AND "MarketSkillId" IS NULL) OR ("MarketRoleId" IS NULL AND "MarketSkillId" IS NOT NULL)),
        CONSTRAINT "FK_AcceleratorJourneyProvisioningReceipts_AcceleratorJourneyCo~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorJourneyProvisioningReceipts_AcceleratorLearningP~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorJourneyProvisioningReceipts_AcceleratorPathEnrol~" FOREIGN KEY ("EnrollmentId") REFERENCES "AcceleratorPathEnrollments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorJourneyProvisioningReceipts_MarketRoles_MarketRo~" FOREIGN KEY ("MarketRoleId") REFERENCES "MarketRoles" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorJourneyProvisioningReceipts_MarketSkills_MarketS~" FOREIGN KEY ("MarketSkillId") REFERENCES "MarketSkills" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorJourneyProvisioningReceipts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE INDEX "IX_AcceleratorJourneyProvisioningReceipts_EnrollmentId" ON "AcceleratorJourneyProvisioningReceipts" ("EnrollmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE INDEX "IX_AcceleratorJourneyProvisioningReceipts_IsDeleted" ON "AcceleratorJourneyProvisioningReceipts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE INDEX "IX_AcceleratorJourneyProvisioningReceipts_JourneyContextId" ON "AcceleratorJourneyProvisioningReceipts" ("JourneyContextId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE INDEX "IX_AcceleratorJourneyProvisioningReceipts_LearningPathId" ON "AcceleratorJourneyProvisioningReceipts" ("LearningPathId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE INDEX "IX_AcceleratorJourneyProvisioningReceipts_MarketRoleId" ON "AcceleratorJourneyProvisioningReceipts" ("MarketRoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE INDEX "IX_AcceleratorJourneyProvisioningReceipts_MarketSkillId" ON "AcceleratorJourneyProvisioningReceipts" ("MarketSkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorJourneyProvisioningReceipts_UserId_RequestKey" ON "AcceleratorJourneyProvisioningReceipts" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121180817_AddAcceleratorJourneyAutoProvisioning') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261121180817_AddAcceleratorJourneyAutoProvisioning', '9.0.8');
    END IF;
END $EF$;
COMMIT;

