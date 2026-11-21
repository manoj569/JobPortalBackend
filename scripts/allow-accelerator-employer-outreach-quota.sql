START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121171033_AllowAcceleratorEmployerOutreachQuota') THEN
    ALTER TABLE "AcceleratorUsageReservations" DROP CONSTRAINT "CK_AcceleratorUsage_State";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121171033_AllowAcceleratorEmployerOutreachQuota') THEN
    ALTER TABLE "AcceleratorAllowances" DROP CONSTRAINT "CK_AcceleratorAllowance_Bounds";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121171033_AllowAcceleratorEmployerOutreachQuota') THEN
    ALTER TABLE "AcceleratorUsageReservations" ADD CONSTRAINT "CK_AcceleratorUsage_State" CHECK ("State" BETWEEN 1 AND 5 AND "Feature" BETWEEN 1 AND 6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121171033_AllowAcceleratorEmployerOutreachQuota') THEN
    ALTER TABLE "AcceleratorAllowances" ADD CONSTRAINT "CK_AcceleratorAllowance_Bounds" CHECK ("Allocated" BETWEEN 1 AND 10000 AND "Feature" BETWEEN 1 AND 6);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261121171033_AllowAcceleratorEmployerOutreachQuota') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261121171033_AllowAcceleratorEmployerOutreachQuota', '9.0.8');
    END IF;
END $EF$;
COMMIT;

