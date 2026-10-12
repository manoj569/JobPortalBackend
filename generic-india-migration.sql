START TRANSACTION;
ALTER TABLE "JobSourceRuns" ADD "DiagnosticsJson" jsonb;

ALTER TABLE "Jobs" ALTER COLUMN "CategoryId" DROP NOT NULL;

ALTER TABLE "Jobs" ADD "SourceMetadataJson" jsonb;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011085034_AllowOptionalJobCategoryAndIngestionDiagnostics', '9.0.8');

COMMIT;

