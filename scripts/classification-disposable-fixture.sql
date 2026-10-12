-- Test-only: execute through the disposable Docker container, before the classification migration.
DO $$ BEGIN
    IF current_database() <> 'careerharbor_classification_cli_test' THEN RAISE EXCEPTION 'Wrong disposable database'; END IF;
    IF EXISTS (SELECT 1 FROM "Jobs") THEN RAISE EXCEPTION 'Fixture requires empty Jobs table'; END IF;
END $$;
INSERT INTO "Users" ("Id","Email","NormalizedEmail","FirstName","LastName","PhoneConfirmed","Status","EmailConfirmed","RoleId","CreatedAtUtc","IsDeleted")
SELECT '11111111-1111-1111-1111-111111111111','classification-fixture@example.test','CLASSIFICATION-FIXTURE@EXAMPLE.TEST','Fixture','Admin',false,2,true,"Id",'2025-01-01',false FROM "Roles" ORDER BY "Id" LIMIT 1;
INSERT INTO "Companies" ("Id","Name","NormalizedName","Slug","IsVerified","OwnerUserId","CreatedAtUtc","IsDeleted")
VALUES ('22222222-2222-2222-2222-222222222222','Disposable Classification','DISPOSABLE CLASSIFICATION','disposable-classification',false,'11111111-1111-1111-1111-111111111111','2025-01-01',false);
INSERT INTO "Jobs" ("Id","ReferenceNumber","Title","Slug","Description","ApplicationUrl","Location","CurrencyCode","EmploymentType","WorkplaceType","ExperienceLevel","IsFlexibleDuration","Status","IsFeatured","IsHidden","PublishedAtUtc","CompanyId","CreatedAtUtc","UpdatedAtUtc","IsDeleted","ExternalJobId","SourceMetadataJson")
SELECT ('00000000-0000-0000-0000-' || lpad(i::text,12,'0'))::uuid,'CLASS-'||i,'Engineer '||i,'classification-'||i,
    CASE WHEN i=3 THEN 'Full-time opportunity. Fully remote. Minimum 8 years experience. INR 8-12 LPA' ELSE 'Full-time opportunity. Fully remote. Minimum 3 years experience. INR 8-12 LPA' END,
    CASE WHEN i IN (2,3) THEN 'https://example.test/duplicate' ELSE 'https://example.test/jobs/'||i END,'Pune','INR',
    CASE WHEN i=1 THEN 1 ELSE 0 END,CASE WHEN i=1 THEN 1 ELSE 0 END,1,false,CASE WHEN i=4 THEN 1 ELSE 2 END,false,false,'2025-01-01',
    '22222222-2222-2222-2222-222222222222',CASE WHEN i=8 THEN '2099-01-01'::timestamptz ELSE '2025-01-01'::timestamptz END,'2025-02-01',i=6,'fixture-'||i,'{"fixture":true}'::jsonb
FROM generate_series(1,8) i;
UPDATE "Jobs" SET "MinimumSalary"=800000,"MaximumSalary"=1200000,"MinimumExperienceYears"=1 WHERE "ReferenceNumber"='CLASS-1';
CREATE TABLE classification_migration_baseline AS SELECT "Id",to_jsonb(j) AS original FROM "Jobs" j;
