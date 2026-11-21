-- READ ONLY. Operator supplies the affected account UUID; no credentials belong in this file.
-- psql <approved connection> -v ON_ERROR_STOP=1 -v candidate_id=<uuid> -f scripts/verify-accelerator-access-and-full-stack.sql
-- Do not run automatically. No mutations, migrations, provider calls or payment reconciliation.
BEGIN TRANSACTION READ ONLY;

SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 5;

-- Feature 6 is EmployerOutreach. Older constraints rejecting 6 prevent cycle creation entirely.
SELECT conname,pg_get_constraintdef(oid) AS definition FROM pg_constraint
WHERE conname IN ('CK_AcceleratorAllowance_Bounds','CK_AcceleratorUsage_State');

SELECT "Id", "Name", "Status", "IsDeleted", "AcceleratorRoleId", "Revision"
FROM "MarketRoles" WHERE "Id"='523641a2-0532-40ad-96dd-a8f8f64870e2'::uuid;

-- Explicit canonical-role candidates for operator review, never an automatic title-based mapping.
SELECT r."Id",r."Code",r."Name",r."TaxonomyId",r."IsDeleted",t."Version",t."PublishedAtUtc"
FROM "AcceleratorCareerRoles" r JOIN "AcceleratorTaxonomies" t ON t."Id"=r."TaxonomyId"
WHERE r."Code"='full-stack-developer' OR r."Name"='Full Stack Developer';

-- Include the account's stored plan names/codes so a mislabeled or noncanonical code is visible.
SELECT m."Id",m."PlanCode",m."PlanName",m."Status",m."StartsAtUtc",m."EndsAtUtc",m."IsDeleted",
       CURRENT_TIMESTAMP AS "DatabaseNowUtc"
FROM "Memberships" m WHERE m."UserId"=:'candidate_id'::uuid;

-- IDs/status only: deliberately omit provider identifiers and secrets.
SELECT p."Id",p."MembershipId",p."PlanCode",p."Status",p."PaidAtUtc",p."DurationDays",p."IsDeleted",
       (NULLIF(p."ProviderPaymentId",'') IS NOT NULL AND NULLIF(p."ProviderOrderId",'') IS NOT NULL) AS "HasProviderIdentifiers",
       c."Id" AS "CycleId",c."StartsAtUtc",c."EndsAtUtc"
FROM "Payments" p LEFT JOIN "AcceleratorBillingCycles" c ON c."PaymentId"=p."Id"
WHERE p."UserId"=:'candidate_id'::uuid
ORDER BY p."PaidAtUtc" NULLS LAST,p."Id";

-- Configuration chain and baseline compatibility. Counts do not expose answer keys/rubrics.
SELECT m."Name",m."AcceleratorRoleId",r."TaxonomyId",p."Id" AS "LearningPathId",p."Status" AS "PathStatus",
       a."AssessmentTemplateId",t."IsActive" AS "TemplateActive",t."RoleId" AS "TemplateRoleId",t."TaxonomyId" AS "TemplateTaxonomyId",
       (SELECT COUNT(*) FROM "AcceleratorLearningModules" x WHERE x."LearningPathId"=p."Id" AND NOT x."IsDeleted" AND x."Status"=2) AS "PublishedModules",
       (SELECT COUNT(*) FROM "AcceleratorLessons" l JOIN "AcceleratorLearningModules" x ON x."Id"=l."ModuleId"
         WHERE x."LearningPathId"=p."Id" AND NOT x."IsDeleted" AND NOT l."IsDeleted" AND x."Status"=2 AND l."Status"=2) AS "PublishedLessons",
       (SELECT COUNT(*) FROM "AcceleratorRoleRequirements" x WHERE x."RoleId"=r."Id" AND NOT x."IsDeleted") AS "RequiredCompetencies",
       (SELECT COUNT(*) FROM "AcceleratorRoleRequirements" x WHERE x."RoleId"=r."Id" AND NOT x."IsDeleted" AND NOT EXISTS
         (SELECT 1 FROM "AcceleratorQuestions" q WHERE q."TaxonomyId"=r."TaxonomyId" AND q."CompetencyId"=x."CompetencyId" AND q."IsActive" AND NOT q."IsDeleted" AND q."Type" IN (1,2,3))) AS "CompetenciesMissingObjectiveQuestions"
FROM "MarketRoles" m
LEFT JOIN "AcceleratorCareerRoles" r ON r."Id"=m."AcceleratorRoleId" AND NOT r."IsDeleted"
LEFT JOIN "AcceleratorLearningPaths" p ON p."CanonicalCareerRoleId"=r."Id" AND p."Type"=1 AND NOT p."IsDeleted"
LEFT JOIN "AcceleratorLearningPathAssessments" a ON a."LearningPathId"=p."Id" AND a."Purpose"=1 AND NOT a."IsDeleted"
LEFT JOIN "AcceleratorAssessmentTemplates" t ON t."Id"=a."AssessmentTemplateId" AND NOT t."IsDeleted"
WHERE m."Id"='523641a2-0532-40ad-96dd-a8f8f64870e2'::uuid;

SELECT v."Id",v."TargetRoleId",v."CatalogStatus",v."IsActive" FROM "AcceleratorProjectVersions" v
WHERE v."TargetRoleId" IN (SELECT "AcceleratorRoleId" FROM "MarketRoles" WHERE "Id"='523641a2-0532-40ad-96dd-a8f8f64870e2'::uuid);
SELECT v."Id",d."TargetRoleId",v."Status" FROM "AcceleratorInterviewVersions" v
JOIN "AcceleratorInterviewDefinitions" d ON d."Id"=v."DefinitionId"
WHERE d."TargetRoleId" IN (SELECT "AcceleratorRoleId" FROM "MarketRoles" WHERE "Id"='523641a2-0532-40ad-96dd-a8f8f64870e2'::uuid);

ROLLBACK;
