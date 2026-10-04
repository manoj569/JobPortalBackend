START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    ALTER TABLE "Jobs" ADD "FingerprintHash" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    ALTER TABLE "Jobs" ADD "FirstSeenAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    ALTER TABLE "Jobs" ADD "LastSeenAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE TABLE "JobSources" (
        "Id" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "CareerPageUrl" character varying(2048) NOT NULL,
        "AtsType" integer NOT NULL,
        "AtsIdentifier" character varying(255),
        "IsActive" boolean NOT NULL,
        "ScanIntervalMinutes" integer NOT NULL,
        "LastRunAtUtc" timestamp with time zone,
        "LastSuccessfulRunAtUtc" timestamp with time zone,
        "LastError" character varying(2048),
        "ConsecutiveFailures" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_JobSources" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_JobSources_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_Jobs_FingerprintHash" ON "Jobs" ("FingerprintHash") WHERE "IsDeleted" = FALSE AND "FingerprintHash" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_Jobs_FirstSeenAtUtc" ON "Jobs" ("FirstSeenAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_Jobs_LastSeenAtUtc" ON "Jobs" ("LastSeenAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_JobSources_CompanyId" ON "JobSources" ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE UNIQUE INDEX "IX_JobSources_CompanyId_AtsType_AtsIdentifier" ON "JobSources" ("CompanyId", "AtsType", "AtsIdentifier") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_JobSources_IsActive" ON "JobSources" ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_JobSources_IsActive_LastRunAtUtc" ON "JobSources" ("IsActive", "LastRunAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    CREATE INDEX "IX_JobSources_IsDeleted" ON "JobSources" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260918171054_AddJobAggregationDedupFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260918171054_AddJobAggregationDedupFoundation', '9.0.8');
    END IF;
END $EF$;
COMMIT;

