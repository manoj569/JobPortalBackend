START TRANSACTION;
ALTER TABLE "Jobs" ALTER COLUMN "Responsibilities" TYPE text;

ALTER TABLE "Jobs" ALTER COLUMN "Requirements" TYPE text;

ALTER TABLE "Jobs" ALTER COLUMN "Benefits" TYPE text;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261020191010_ExpandEmployerJobContentToText', '9.0.8');

COMMIT;

