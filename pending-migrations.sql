START TRANSACTION;
CREATE TABLE "AcceleratorLearningPaths" (
    "Id" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Slug" character varying(160) NOT NULL,
    "Title" character varying(250) NOT NULL,
    "ShortDescription" character varying(800) NOT NULL,
    "Description" character varying(8000) NOT NULL,
    "CanonicalCareerRoleId" uuid,
    "CanonicalSkillId" uuid,
    "Difficulty" integer NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "Status" integer NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLearningPaths" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorLearningPath_Bounds" CHECK ("Difficulty" BETWEEN 1 AND 5 AND "EstimatedMinutes" >= 0),
    CONSTRAINT "CK_AcceleratorLearningPath_CanonicalReference" CHECK (("Type" = 1 AND "CanonicalCareerRoleId" IS NOT NULL AND "CanonicalSkillId" IS NULL) OR ("Type" = 2 AND "CanonicalSkillId" IS NOT NULL AND "CanonicalCareerRoleId" IS NULL)),
    CONSTRAINT "FK_AcceleratorLearningPaths_AcceleratorCareerRoles_CanonicalCa~" FOREIGN KEY ("CanonicalCareerRoleId") REFERENCES "AcceleratorCareerRoles" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningPaths_Skills_CanonicalSkillId" FOREIGN KEY ("CanonicalSkillId") REFERENCES "Skills" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLearningModules" (
    "Id" uuid NOT NULL,
    "LearningPathId" uuid NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Description" character varying(8000) NOT NULL,
    "Sequence" integer NOT NULL,
    "Difficulty" integer NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "IsRequired" boolean NOT NULL,
    "Status" integer NOT NULL,
    "AssessmentTemplateId" uuid,
    "ProjectVersionId" uuid,
    "InterviewVersionId" uuid,
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLearningModules" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorLearningModule_Bounds" CHECK ("Sequence" >= 1 AND "Difficulty" BETWEEN 1 AND 5 AND "EstimatedMinutes" >= 0),
    CONSTRAINT "FK_AcceleratorLearningModules_AcceleratorAssessmentTemplates_A~" FOREIGN KEY ("AssessmentTemplateId") REFERENCES "AcceleratorAssessmentTemplates" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningModules_AcceleratorInterviewVersions_Int~" FOREIGN KEY ("InterviewVersionId") REFERENCES "AcceleratorInterviewVersions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningModules_AcceleratorLearningPaths_Learnin~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningModules_AcceleratorProjectVersions_Proje~" FOREIGN KEY ("ProjectVersionId") REFERENCES "AcceleratorProjectVersions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLearningModulePrerequisites" (
    "Id" uuid NOT NULL,
    "LearningPathId" uuid NOT NULL,
    "ModuleId" uuid NOT NULL,
    "RequiredModuleId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLearningModulePrerequisites" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorLearningModulePrerequisite_NotSelf" CHECK ("ModuleId" <> "RequiredModuleId"),
    CONSTRAINT "FK_AcceleratorLearningModulePrerequisites_AcceleratorLearningM~" FOREIGN KEY ("ModuleId") REFERENCES "AcceleratorLearningModules" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningModulePrerequisites_AcceleratorLearningP~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningModulePrerequisites_AcceleratorLearning~1" FOREIGN KEY ("RequiredModuleId") REFERENCES "AcceleratorLearningModules" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLessons" (
    "Id" uuid NOT NULL,
    "ModuleId" uuid NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Description" character varying(8000) NOT NULL,
    "Type" integer NOT NULL,
    "Content" character varying(64000) NOT NULL,
    "Sequence" integer NOT NULL,
    "EstimatedMinutes" integer NOT NULL,
    "IsRequired" boolean NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLessons" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorLesson_Bounds" CHECK ("Sequence" >= 1 AND "EstimatedMinutes" >= 0),
    CONSTRAINT "FK_AcceleratorLessons_AcceleratorLearningModules_ModuleId" FOREIGN KEY ("ModuleId") REFERENCES "AcceleratorLearningModules" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLessonResources" (
    "Id" uuid NOT NULL,
    "LessonId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Url" character varying(2048) NOT NULL,
    "Description" character varying(4000) NOT NULL,
    "Sequence" integer NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLessonResources" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AcceleratorLessonResources_AcceleratorLessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES "AcceleratorLessons" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorPathEnrollments" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "LearningPathId" uuid NOT NULL,
    "CurrentLevel" integer NOT NULL,
    "RequestKey" uuid NOT NULL,
    "RequestHash" character varying(64) NOT NULL,
    "TargetLevel" integer NOT NULL,
    "WeeklyCommitmentMinutes" integer NOT NULL,
    "TargetCompletionAtUtc" timestamp with time zone,
    "EnrolledAtUtc" timestamp with time zone NOT NULL,
    "StartedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "CurrentModuleId" uuid,
    "CurrentLessonId" uuid,
    "Status" integer NOT NULL,
    "OverallProgressPercent" integer NOT NULL,
    "LastActivityAtUtc" timestamp with time zone NOT NULL,
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorPathEnrollments" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorPathEnrollment_Bounds" CHECK ("CurrentLevel" BETWEEN 0 AND 5 AND "TargetLevel" BETWEEN 1 AND 5 AND "WeeklyCommitmentMinutes" BETWEEN 1 AND 10080 AND "OverallProgressPercent" BETWEEN 0 AND 100),
    CONSTRAINT "FK_AcceleratorPathEnrollments_AcceleratorLearningModules_Curre~" FOREIGN KEY ("CurrentModuleId") REFERENCES "AcceleratorLearningModules" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorPathEnrollments_AcceleratorLearningPaths_Learnin~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorPathEnrollments_AcceleratorLessons_CurrentLesson~" FOREIGN KEY ("CurrentLessonId") REFERENCES "AcceleratorLessons" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorEnrollmentProgressEvents" (
    "Id" uuid NOT NULL,
    "EnrollmentId" uuid NOT NULL,
    "LessonId" uuid NOT NULL,
    "RequestKey" uuid NOT NULL,
    "Kind" integer NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorEnrollmentProgressEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AcceleratorEnrollmentProgressEvents_AcceleratorLessons_Less~" FOREIGN KEY ("LessonId") REFERENCES "AcceleratorLessons" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorEnrollmentProgressEvents_AcceleratorPathEnrollme~" FOREIGN KEY ("EnrollmentId") REFERENCES "AcceleratorPathEnrollments" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLessonProgress" (
    "Id" uuid NOT NULL,
    "EnrollmentId" uuid NOT NULL,
    "LessonId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "StartedAtUtc" timestamp with time zone,
    "CompletedAtUtc" timestamp with time zone,
    "LastRequestKey" uuid,
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLessonProgress" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AcceleratorLessonProgress_AcceleratorLessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES "AcceleratorLessons" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLessonProgress_AcceleratorPathEnrollments_Enroll~" FOREIGN KEY ("EnrollmentId") REFERENCES "AcceleratorPathEnrollments" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AcceleratorEnrollmentProgressEvents_EnrollmentId_OccurredAt~" ON "AcceleratorEnrollmentProgressEvents" ("EnrollmentId", "OccurredAtUtc");

CREATE UNIQUE INDEX "IX_AcceleratorEnrollmentProgressEvents_EnrollmentId_RequestKey" ON "AcceleratorEnrollmentProgressEvents" ("EnrollmentId", "RequestKey");

CREATE INDEX "IX_AcceleratorEnrollmentProgressEvents_IsDeleted" ON "AcceleratorEnrollmentProgressEvents" ("IsDeleted");

CREATE INDEX "IX_AcceleratorEnrollmentProgressEvents_LessonId" ON "AcceleratorEnrollmentProgressEvents" ("LessonId");

CREATE INDEX "IX_AcceleratorLearningModulePrerequisites_IsDeleted" ON "AcceleratorLearningModulePrerequisites" ("IsDeleted");

CREATE INDEX "IX_AcceleratorLearningModulePrerequisites_LearningPathId_Requi~" ON "AcceleratorLearningModulePrerequisites" ("LearningPathId", "RequiredModuleId");

CREATE UNIQUE INDEX "IX_AcceleratorLearningModulePrerequisites_ModuleId_RequiredMod~" ON "AcceleratorLearningModulePrerequisites" ("ModuleId", "RequiredModuleId");

CREATE INDEX "IX_AcceleratorLearningModulePrerequisites_RequiredModuleId" ON "AcceleratorLearningModulePrerequisites" ("RequiredModuleId");

CREATE INDEX "IX_AcceleratorLearningModules_AssessmentTemplateId" ON "AcceleratorLearningModules" ("AssessmentTemplateId");

CREATE INDEX "IX_AcceleratorLearningModules_InterviewVersionId" ON "AcceleratorLearningModules" ("InterviewVersionId");

CREATE INDEX "IX_AcceleratorLearningModules_IsDeleted" ON "AcceleratorLearningModules" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorLearningModules_LearningPathId_Sequence" ON "AcceleratorLearningModules" ("LearningPathId", "Sequence");

CREATE INDEX "IX_AcceleratorLearningModules_LearningPathId_Status" ON "AcceleratorLearningModules" ("LearningPathId", "Status");

CREATE INDEX "IX_AcceleratorLearningModules_ProjectVersionId" ON "AcceleratorLearningModules" ("ProjectVersionId");

CREATE INDEX "IX_AcceleratorLearningPaths_CanonicalCareerRoleId" ON "AcceleratorLearningPaths" ("CanonicalCareerRoleId");

CREATE INDEX "IX_AcceleratorLearningPaths_CanonicalSkillId" ON "AcceleratorLearningPaths" ("CanonicalSkillId");

CREATE INDEX "IX_AcceleratorLearningPaths_IsDeleted" ON "AcceleratorLearningPaths" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorLearningPaths_Slug" ON "AcceleratorLearningPaths" ("Slug");

CREATE INDEX "IX_AcceleratorLearningPaths_Type_Status_DisplayOrder" ON "AcceleratorLearningPaths" ("Type", "Status", "DisplayOrder");

CREATE UNIQUE INDEX "IX_AcceleratorLessonProgress_EnrollmentId_LessonId" ON "AcceleratorLessonProgress" ("EnrollmentId", "LessonId");

CREATE INDEX "IX_AcceleratorLessonProgress_EnrollmentId_Status" ON "AcceleratorLessonProgress" ("EnrollmentId", "Status");

CREATE INDEX "IX_AcceleratorLessonProgress_IsDeleted" ON "AcceleratorLessonProgress" ("IsDeleted");

CREATE INDEX "IX_AcceleratorLessonProgress_LessonId" ON "AcceleratorLessonProgress" ("LessonId");

CREATE INDEX "IX_AcceleratorLessonResources_IsDeleted" ON "AcceleratorLessonResources" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorLessonResources_LessonId_Sequence" ON "AcceleratorLessonResources" ("LessonId", "Sequence");

CREATE INDEX "IX_AcceleratorLessons_IsDeleted" ON "AcceleratorLessons" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorLessons_ModuleId_Sequence" ON "AcceleratorLessons" ("ModuleId", "Sequence");

CREATE INDEX "IX_AcceleratorLessons_ModuleId_Status" ON "AcceleratorLessons" ("ModuleId", "Status");

CREATE INDEX "IX_AcceleratorPathEnrollments_CurrentLessonId" ON "AcceleratorPathEnrollments" ("CurrentLessonId");

CREATE INDEX "IX_AcceleratorPathEnrollments_CurrentModuleId" ON "AcceleratorPathEnrollments" ("CurrentModuleId");

CREATE INDEX "IX_AcceleratorPathEnrollments_IsDeleted" ON "AcceleratorPathEnrollments" ("IsDeleted");

CREATE INDEX "IX_AcceleratorPathEnrollments_LearningPathId" ON "AcceleratorPathEnrollments" ("LearningPathId");

CREATE UNIQUE INDEX "IX_AcceleratorPathEnrollments_UserId_LearningPathId" ON "AcceleratorPathEnrollments" ("UserId", "LearningPathId") WHERE "IsDeleted" = false AND "Status" IN (1, 2, 4);

CREATE UNIQUE INDEX "IX_AcceleratorPathEnrollments_UserId_RequestKey" ON "AcceleratorPathEnrollments" ("UserId", "RequestKey");

CREATE INDEX "IX_AcceleratorPathEnrollments_UserId_Status_LastActivityAtUtc" ON "AcceleratorPathEnrollments" ("UserId", "Status", "LastActivityAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261117145522_AddCareerAcceleratorLearningContentJourney', '9.0.8');

ALTER TABLE "AcceleratorSkillEvidence" ALTER COLUMN "GoalId" DROP NOT NULL;

ALTER TABLE "AcceleratorSkillEvidence" ADD "JourneyContextId" uuid;

ALTER TABLE "AcceleratorRoadmaps" ALTER COLUMN "GoalId" DROP NOT NULL;

ALTER TABLE "AcceleratorRoadmaps" ADD "JourneyContextId" uuid;

ALTER TABLE "AcceleratorRecommendations" ALTER COLUMN "GoalId" DROP NOT NULL;

ALTER TABLE "AcceleratorRecommendations" ADD "JourneyContextId" uuid;

ALTER TABLE "AcceleratorAssessmentTemplates" ALTER COLUMN "RoleId" DROP NOT NULL;

ALTER TABLE "AcceleratorAssessmentTemplates" ADD "TaxonomyId" uuid;

ALTER TABLE "AcceleratorAssessmentAttempts" ALTER COLUMN "GoalId" DROP NOT NULL;

ALTER TABLE "AcceleratorAssessmentAttempts" ADD "JourneyContextId" uuid;

CREATE TABLE "AcceleratorJourneyContexts" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "ScopeType" integer NOT NULL,
    "CareerGoalId" uuid,
    "LearningPathEnrollmentId" uuid,
    "TaxonomyId" uuid NOT NULL,
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorJourneyContexts" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorJourneyContext_Scope" CHECK (("ScopeType" = 1 AND "CareerGoalId" IS NOT NULL AND "LearningPathEnrollmentId" IS NULL) OR ("ScopeType" = 2 AND "CareerGoalId" IS NULL AND "LearningPathEnrollmentId" IS NOT NULL)),
    CONSTRAINT "FK_AcceleratorJourneyContexts_AcceleratorCareerGoals_CareerGoa~" FOREIGN KEY ("CareerGoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorJourneyContexts_AcceleratorPathEnrollments_Learn~" FOREIGN KEY ("LearningPathEnrollmentId") REFERENCES "AcceleratorPathEnrollments" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorJourneyContexts_AcceleratorTaxonomies_TaxonomyId" FOREIGN KEY ("TaxonomyId") REFERENCES "AcceleratorTaxonomies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLearningPathAssessments" (
    "Id" uuid NOT NULL,
    "LearningPathId" uuid NOT NULL,
    "AssessmentTemplateId" uuid NOT NULL,
    "Purpose" integer NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLearningPathAssessments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AcceleratorLearningPathAssessments_AcceleratorAssessmentTem~" FOREIGN KEY ("AssessmentTemplateId") REFERENCES "AcceleratorAssessmentTemplates" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningPathAssessments_AcceleratorLearningPaths~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorLearningPathCompetencyRequirements" (
    "Id" uuid NOT NULL,
    "LearningPathId" uuid NOT NULL,
    "CompetencyId" uuid NOT NULL,
    "TargetProficiency" integer NOT NULL,
    "Weight" numeric(9,4) NOT NULL,
    "IsRequired" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorLearningPathCompetencyRequirements" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AcceleratorLearningPathCompetencyRequirement_Bounds" CHECK ("TargetProficiency" BETWEEN 1 AND 5 AND "Weight" > 0),
    CONSTRAINT "FK_AcceleratorLearningPathCompetencyRequirements_AcceleratorCo~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorLearningPathCompetencyRequirements_AcceleratorLe~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AcceleratorSkillEvidence_JourneyContextId" ON "AcceleratorSkillEvidence" ("JourneyContextId");

CREATE INDEX "IX_AcceleratorSkillEvidence_UserId_JourneyContextId" ON "AcceleratorSkillEvidence" ("UserId", "JourneyContextId");

CREATE UNIQUE INDEX "IX_AcceleratorRoadmaps_JourneyContextId_Version" ON "AcceleratorRoadmaps" ("JourneyContextId", "Version") WHERE "JourneyContextId" IS NOT NULL;

CREATE INDEX "IX_AcceleratorRoadmaps_UserId_JourneyContextId" ON "AcceleratorRoadmaps" ("UserId", "JourneyContextId");

CREATE INDEX "IX_AcceleratorRecommendations_JourneyContextId" ON "AcceleratorRecommendations" ("JourneyContextId");

CREATE INDEX "IX_AcceleratorRecommendations_UserId_JourneyContextId" ON "AcceleratorRecommendations" ("UserId", "JourneyContextId");

CREATE INDEX "IX_AcceleratorAssessmentTemplates_TaxonomyId" ON "AcceleratorAssessmentTemplates" ("TaxonomyId");

ALTER TABLE "AcceleratorAssessmentTemplates" ADD CONSTRAINT "CK_AcceleratorAssessmentTemplate_Applicability" CHECK ("RoleId" IS NOT NULL OR "TaxonomyId" IS NOT NULL);

CREATE INDEX "IX_AcceleratorAssessmentAttempts_JourneyContextId" ON "AcceleratorAssessmentAttempts" ("JourneyContextId");

CREATE INDEX "IX_AcceleratorAssessmentAttempts_UserId_JourneyContextId" ON "AcceleratorAssessmentAttempts" ("UserId", "JourneyContextId");

CREATE UNIQUE INDEX "IX_AcceleratorJourneyContexts_CareerGoalId" ON "AcceleratorJourneyContexts" ("CareerGoalId") WHERE "CareerGoalId" IS NOT NULL AND "IsDeleted" = false;

CREATE INDEX "IX_AcceleratorJourneyContexts_IsDeleted" ON "AcceleratorJourneyContexts" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorJourneyContexts_LearningPathEnrollmentId" ON "AcceleratorJourneyContexts" ("LearningPathEnrollmentId") WHERE "LearningPathEnrollmentId" IS NOT NULL AND "IsDeleted" = false;

CREATE INDEX "IX_AcceleratorJourneyContexts_TaxonomyId" ON "AcceleratorJourneyContexts" ("TaxonomyId");

CREATE INDEX "IX_AcceleratorJourneyContexts_UserId_ScopeType" ON "AcceleratorJourneyContexts" ("UserId", "ScopeType");

CREATE INDEX "IX_AcceleratorLearningPathAssessments_AssessmentTemplateId" ON "AcceleratorLearningPathAssessments" ("AssessmentTemplateId");

CREATE INDEX "IX_AcceleratorLearningPathAssessments_IsDeleted" ON "AcceleratorLearningPathAssessments" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorLearningPathAssessments_LearningPathId_Purpose" ON "AcceleratorLearningPathAssessments" ("LearningPathId", "Purpose");

CREATE INDEX "IX_AcceleratorLearningPathCompetencyRequirements_CompetencyId" ON "AcceleratorLearningPathCompetencyRequirements" ("CompetencyId");

CREATE INDEX "IX_AcceleratorLearningPathCompetencyRequirements_IsDeleted" ON "AcceleratorLearningPathCompetencyRequirements" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorLearningPathCompetencyRequirements_LearningPathI~" ON "AcceleratorLearningPathCompetencyRequirements" ("LearningPathId", "CompetencyId");

INSERT INTO "AcceleratorJourneyContexts"
    ("Id", "UserId", "ScopeType", "CareerGoalId", "LearningPathEnrollmentId", "TaxonomyId", "Revision", "CreatedAtUtc", "IsDeleted")
SELECT g."Id", g."UserId", 1, g."Id", NULL, g."TaxonomyId", g."Id", g."CreatedAtUtc", false
FROM "AcceleratorCareerGoals" g
WHERE NOT EXISTS (SELECT 1 FROM "AcceleratorJourneyContexts" c WHERE c."CareerGoalId" = g."Id" AND NOT c."IsDeleted");

UPDATE "AcceleratorAssessmentAttempts" a SET "JourneyContextId" = c."Id"
FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
UPDATE "AcceleratorSkillEvidence" a SET "JourneyContextId" = c."Id"
FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
UPDATE "AcceleratorRoadmaps" a SET "JourneyContextId" = c."Id"
FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;
UPDATE "AcceleratorRecommendations" a SET "JourneyContextId" = c."Id"
FROM "AcceleratorJourneyContexts" c WHERE a."GoalId" = c."CareerGoalId" AND a."UserId" = c."UserId" AND a."JourneyContextId" IS NULL;

ALTER TABLE "AcceleratorAssessmentAttempts" ADD CONSTRAINT "FK_AcceleratorAssessmentAttempts_AcceleratorJourneyContexts_Jo~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorAssessmentTemplates" ADD CONSTRAINT "FK_AcceleratorAssessmentTemplates_AcceleratorTaxonomies_Taxono~" FOREIGN KEY ("TaxonomyId") REFERENCES "AcceleratorTaxonomies" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorRecommendations" ADD CONSTRAINT "FK_AcceleratorRecommendations_AcceleratorJourneyContexts_Journ~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorRoadmaps" ADD CONSTRAINT "FK_AcceleratorRoadmaps_AcceleratorJourneyContexts_JourneyConte~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorJourneyContexts_Journey~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261117154417_AddCareerAcceleratorJourneyContexts', '9.0.8');

CREATE TABLE "AcceleratorAchievementDefinitions" (
    "Id" uuid NOT NULL,
    "Code" character varying(80) NOT NULL,
    "Title" character varying(250) NOT NULL,
    "Description" character varying(4000) NOT NULL,
    "IsActive" boolean NOT NULL,
    "Rule" integer NOT NULL,
    "Threshold" numeric(5,2) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorAchievementDefinitions" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_AchievementDefinition_Threshold" CHECK ("Threshold" BETWEEN 0 AND 100)
);

CREATE TABLE "AcceleratorSkillProficiencyPolicies" (
    "Id" uuid NOT NULL,
    "LearningPathId" uuid,
    "Code" character varying(80) NOT NULL,
    "Version" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "ComponentsJson" jsonb NOT NULL,
    "MinimumEvidenceConfidence" numeric(5,4) NOT NULL,
    "CertificateMinimumAssessments" integer NOT NULL,
    "CertificateMinimumScore" numeric(5,2) NOT NULL,
    "CertificateRequiresModules" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorSkillProficiencyPolicies" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ProficiencyPolicy_Bounds" CHECK ("Version" > 0 AND "MinimumEvidenceConfidence" BETWEEN 0 AND 1 AND "CertificateMinimumAssessments" > 0 AND "CertificateMinimumScore" BETWEEN 0 AND 100),
    CONSTRAINT "FK_AcceleratorSkillProficiencyPolicies_AcceleratorLearningPath~" FOREIGN KEY ("LearningPathId") REFERENCES "AcceleratorLearningPaths" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CandidateAcceleratorAchievements" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "JourneyContextId" uuid NOT NULL,
    "AchievementDefinitionId" uuid NOT NULL,
    "AwardedAtUtc" timestamp with time zone NOT NULL,
    "EvidenceJson" jsonb NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_CandidateAcceleratorAchievements" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_CandidateAcceleratorAchievements_AcceleratorAchievementDefi~" FOREIGN KEY ("AchievementDefinitionId") REFERENCES "AcceleratorAchievementDefinitions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CandidateAcceleratorAchievements_AcceleratorJourneyContexts~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CandidateAcceleratorAchievements_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorEnrollmentCertificates" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "JourneyContextId" uuid NOT NULL,
    "PolicyId" uuid NOT NULL,
    "State" integer NOT NULL,
    "EvaluatedAtUtc" timestamp with time zone NOT NULL,
    "IssuedAtUtc" timestamp with time zone,
    "CertificateIdentifier" character varying(150),
    "ArtifactStorageKey" character varying(1024),
    "Revision" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorEnrollmentCertificates" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Certificate_Artifact" CHECK ("State" <> 4 OR ("IssuedAtUtc" IS NOT NULL AND "CertificateIdentifier" IS NOT NULL AND "ArtifactStorageKey" IS NOT NULL)),
    CONSTRAINT "FK_AcceleratorEnrollmentCertificates_AcceleratorJourneyContext~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorEnrollmentCertificates_AcceleratorSkillProficien~" FOREIGN KEY ("PolicyId") REFERENCES "AcceleratorSkillProficiencyPolicies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AcceleratorEnrollmentCertificates_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AcceleratorProficiencyBands" (
    "Id" uuid NOT NULL,
    "PolicyId" uuid NOT NULL,
    "Label" character varying(80) NOT NULL,
    "MinimumScore" numeric(5,2) NOT NULL,
    "MaximumScore" numeric(5,2) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_AcceleratorProficiencyBands" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_ProficiencyBand_Range" CHECK ("MinimumScore" >= 0 AND "MaximumScore" <= 100 AND "MinimumScore" < "MaximumScore"),
    CONSTRAINT "FK_AcceleratorProficiencyBands_AcceleratorSkillProficiencyPoli~" FOREIGN KEY ("PolicyId") REFERENCES "AcceleratorSkillProficiencyPolicies" ("Id") ON DELETE RESTRICT
);

ALTER TABLE "AcceleratorProficiencyBands"
ADD CONSTRAINT "EX_AcceleratorProficiencyBands_NoOverlap"
EXCLUDE USING gist (
  "PolicyId" WITH =,
  numrange("MinimumScore", "MaximumScore", CASE WHEN "MaximumScore" = 100 THEN '[]' ELSE '[)' END) WITH &&
) WHERE ("IsActive" AND NOT "IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorAchievementDefinitions_Code" ON "AcceleratorAchievementDefinitions" ("Code");

CREATE INDEX "IX_AcceleratorAchievementDefinitions_IsDeleted" ON "AcceleratorAchievementDefinitions" ("IsDeleted");

CREATE INDEX "IX_AcceleratorEnrollmentCertificates_IsDeleted" ON "AcceleratorEnrollmentCertificates" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorEnrollmentCertificates_JourneyContextId" ON "AcceleratorEnrollmentCertificates" ("JourneyContextId");

CREATE INDEX "IX_AcceleratorEnrollmentCertificates_PolicyId" ON "AcceleratorEnrollmentCertificates" ("PolicyId");

CREATE INDEX "IX_AcceleratorEnrollmentCertificates_UserId_State" ON "AcceleratorEnrollmentCertificates" ("UserId", "State");

CREATE INDEX "IX_AcceleratorProficiencyBands_IsDeleted" ON "AcceleratorProficiencyBands" ("IsDeleted");

CREATE UNIQUE INDEX "IX_AcceleratorProficiencyBands_PolicyId_MinimumScore" ON "AcceleratorProficiencyBands" ("PolicyId", "MinimumScore");

CREATE UNIQUE INDEX "IX_AcceleratorSkillProficiencyPolicies_Code_Version" ON "AcceleratorSkillProficiencyPolicies" ("Code", "Version");

CREATE INDEX "IX_AcceleratorSkillProficiencyPolicies_IsDeleted" ON "AcceleratorSkillProficiencyPolicies" ("IsDeleted");

CREATE INDEX "IX_AcceleratorSkillProficiencyPolicies_LearningPathId_IsActive" ON "AcceleratorSkillProficiencyPolicies" ("LearningPathId", "IsActive");

CREATE INDEX "IX_CandidateAcceleratorAchievements_AchievementDefinitionId" ON "CandidateAcceleratorAchievements" ("AchievementDefinitionId");

CREATE INDEX "IX_CandidateAcceleratorAchievements_IsDeleted" ON "CandidateAcceleratorAchievements" ("IsDeleted");

CREATE UNIQUE INDEX "IX_CandidateAcceleratorAchievements_JourneyContextId_Achieveme~" ON "CandidateAcceleratorAchievements" ("JourneyContextId", "AchievementDefinitionId");

CREATE INDEX "IX_CandidateAcceleratorAchievements_UserId_AwardedAtUtc" ON "CandidateAcceleratorAchievements" ("UserId", "AwardedAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261117164539_AddCareerAcceleratorJourneyPoliciesAndAwards', '9.0.8');

ALTER TABLE "AcceleratorTutorConversations" ALTER COLUMN "GoalId" DROP NOT NULL;

ALTER TABLE "AcceleratorTutorConversations" ADD "JourneyContextId" uuid;

ALTER TABLE "AcceleratorTutorConversations" ADD "LessonId" uuid;

ALTER TABLE "AcceleratorTutorConversations" ADD "ModuleId" uuid;

ALTER TABLE "AcceleratorPracticeSessions" ALTER COLUMN "GoalId" DROP NOT NULL;

ALTER TABLE "AcceleratorPracticeSessions" ADD "JourneyContextId" uuid;

ALTER TABLE "AcceleratorPracticeSessions" ADD "LessonId" uuid;

ALTER TABLE "AcceleratorPracticeSessions" ADD "ModuleId" uuid;

CREATE TABLE "EmployerHiringPipelines" (
    "Id" uuid NOT NULL,
    "JobApplicationId" uuid NOT NULL,
    "Stage" integer NOT NULL,
    "ScreeningStatus" integer NOT NULL,
    "ScreeningNotes" character varying(2000),
    "ScreeningScore" numeric(5,2),
    "ScreeningStartedAtUtc" timestamp with time zone,
    "ScreenedAtUtc" timestamp with time zone,
    "ScreenedByUserId" uuid,
    "OfferStatus" integer NOT NULL,
    "OfferNotes" character varying(2000),
    "OfferAtUtc" timestamp with time zone,
    "HiredAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_EmployerHiringPipelines" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EmployerHiringPipelines_JobApplications_JobApplicationId" FOREIGN KEY ("JobApplicationId") REFERENCES "JobApplications" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_EmployerHiringPipelines_Users_ScreenedByUserId" FOREIGN KEY ("ScreenedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AcceleratorTutorConversations_JourneyContextId" ON "AcceleratorTutorConversations" ("JourneyContextId");

CREATE INDEX "IX_AcceleratorTutorConversations_LessonId" ON "AcceleratorTutorConversations" ("LessonId");

CREATE INDEX "IX_AcceleratorTutorConversations_ModuleId" ON "AcceleratorTutorConversations" ("ModuleId");

CREATE INDEX "IX_AcceleratorTutorConversations_UserId_JourneyContextId_Creat~" ON "AcceleratorTutorConversations" ("UserId", "JourneyContextId", "CreatedAtUtc");

ALTER TABLE "AcceleratorTutorConversations" ADD CONSTRAINT "CK_AcceleratorTutorConversation_Journey" CHECK ("JourneyContextId" IS NOT NULL);

CREATE INDEX "IX_AcceleratorPracticeSessions_JourneyContextId" ON "AcceleratorPracticeSessions" ("JourneyContextId");

CREATE INDEX "IX_AcceleratorPracticeSessions_LessonId" ON "AcceleratorPracticeSessions" ("LessonId");

CREATE INDEX "IX_AcceleratorPracticeSessions_ModuleId" ON "AcceleratorPracticeSessions" ("ModuleId");

CREATE INDEX "IX_AcceleratorPracticeSessions_UserId_JourneyContextId_Created~" ON "AcceleratorPracticeSessions" ("UserId", "JourneyContextId", "CreatedAtUtc");

ALTER TABLE "AcceleratorPracticeSessions" ADD CONSTRAINT "CK_AcceleratorPracticeSession_Journey" CHECK ("JourneyContextId" IS NOT NULL);

CREATE INDEX "IX_EmployerHiringPipelines_IsDeleted" ON "EmployerHiringPipelines" ("IsDeleted");

CREATE UNIQUE INDEX "IX_EmployerHiringPipelines_JobApplicationId" ON "EmployerHiringPipelines" ("JobApplicationId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_EmployerHiringPipelines_ScreenedByUserId" ON "EmployerHiringPipelines" ("ScreenedByUserId");

CREATE INDEX "IX_EmployerHiringPipelines_Stage_ScreeningStatus" ON "EmployerHiringPipelines" ("Stage", "ScreeningStatus");

ALTER TABLE "AcceleratorPracticeSessions" ADD CONSTRAINT "FK_AcceleratorPracticeSessions_AcceleratorJourneyContexts_Jour~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorPracticeSessions" ADD CONSTRAINT "FK_AcceleratorPracticeSessions_AcceleratorLearningModules_Modu~" FOREIGN KEY ("ModuleId") REFERENCES "AcceleratorLearningModules" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorPracticeSessions" ADD CONSTRAINT "FK_AcceleratorPracticeSessions_AcceleratorLessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES "AcceleratorLessons" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorTutorConversations" ADD CONSTRAINT "FK_AcceleratorTutorConversations_AcceleratorJourneyContexts_Jo~" FOREIGN KEY ("JourneyContextId") REFERENCES "AcceleratorJourneyContexts" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorTutorConversations" ADD CONSTRAINT "FK_AcceleratorTutorConversations_AcceleratorLearningModules_Mo~" FOREIGN KEY ("ModuleId") REFERENCES "AcceleratorLearningModules" ("Id") ON DELETE RESTRICT;

ALTER TABLE "AcceleratorTutorConversations" ADD CONSTRAINT "FK_AcceleratorTutorConversations_AcceleratorLessons_LessonId" FOREIGN KEY ("LessonId") REFERENCES "AcceleratorLessons" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119073819_AddEmployerHiringPipeline', '9.0.8');

CREATE TABLE "EmployerInterviews" (
    "Id" uuid NOT NULL,
    "JobApplicationId" uuid NOT NULL,
    "PipelineId" uuid NOT NULL,
    "RoundNumber" integer NOT NULL,
    "RoundType" integer NOT NULL,
    "Title" character varying(160),
    "ScheduledAtUtc" timestamp with time zone NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "InterviewFormat" integer NOT NULL,
    "MeetingUrl" character varying(2048),
    "Location" character varying(500),
    "InterviewerName" character varying(160),
    "InterviewerEmail" character varying(256),
    "Status" integer NOT NULL,
    "Result" integer NOT NULL,
    "Score" numeric(5,2),
    "Feedback" character varying(4000),
    "InternalNotes" character varying(2000),
    "CompletedAtUtc" timestamp with time zone,
    "CancelledAtUtc" timestamp with time zone,
    "CreatedByUserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_EmployerInterviews" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_EmployerInterviews_DurationMinutes" CHECK ("DurationMinutes" BETWEEN 10 AND 480),
    CONSTRAINT "CK_EmployerInterviews_RoundNumber" CHECK ("RoundNumber" > 0),
    CONSTRAINT "CK_EmployerInterviews_Score" CHECK ("Score" IS NULL OR ("Score" >= 0 AND "Score" <= 100)),
    CONSTRAINT "FK_EmployerInterviews_EmployerHiringPipelines_PipelineId" FOREIGN KEY ("PipelineId") REFERENCES "EmployerHiringPipelines" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_EmployerInterviews_JobApplications_JobApplicationId" FOREIGN KEY ("JobApplicationId") REFERENCES "JobApplications" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_EmployerInterviews_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_EmployerInterviews_CreatedByUserId" ON "EmployerInterviews" ("CreatedByUserId");

CREATE INDEX "IX_EmployerInterviews_IsDeleted" ON "EmployerInterviews" ("IsDeleted");

CREATE UNIQUE INDEX "IX_EmployerInterviews_JobApplicationId_RoundNumber" ON "EmployerInterviews" ("JobApplicationId", "RoundNumber") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_EmployerInterviews_PipelineId_Status" ON "EmployerInterviews" ("PipelineId", "Status");

CREATE INDEX "IX_EmployerInterviews_Status_ScheduledAtUtc" ON "EmployerInterviews" ("Status", "ScheduledAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119080841_AddEmployerInterviews', '9.0.8');

ALTER TABLE "EmployerHiringPipelines" DROP COLUMN "OfferAtUtc";

ALTER TABLE "EmployerHiringPipelines" DROP COLUMN "OfferNotes";

ALTER TABLE "EmployerHiringPipelines" DROP COLUMN "OfferStatus";

CREATE TABLE "EmployerOffers" (
    "Id" uuid NOT NULL,
    "JobApplicationId" uuid NOT NULL,
    "PipelineId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "JobTitle" character varying(300) NOT NULL,
    "AnnualSalary" numeric(18,2),
    "CurrencyCode" character varying(3) NOT NULL,
    "JoiningDateUtc" timestamp with time zone,
    "ExpiresAtUtc" timestamp with time zone,
    "WorkLocation" character varying(300),
    "Notes" character varying(4000),
    "OfferLetterUrl" character varying(2048),
    "SentAtUtc" timestamp with time zone,
    "AcceptedAtUtc" timestamp with time zone,
    "DeclinedAtUtc" timestamp with time zone,
    "WithdrawnAtUtc" timestamp with time zone,
    "ExpiredAtUtc" timestamp with time zone,
    "CreatedByUserId" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_EmployerOffers" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_EmployerOffers_AnnualSalary" CHECK ("AnnualSalary" IS NULL OR "AnnualSalary" >= 0),
    CONSTRAINT "FK_EmployerOffers_EmployerHiringPipelines_PipelineId" FOREIGN KEY ("PipelineId") REFERENCES "EmployerHiringPipelines" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_EmployerOffers_JobApplications_JobApplicationId" FOREIGN KEY ("JobApplicationId") REFERENCES "JobApplications" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_EmployerOffers_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_EmployerOffers_CreatedByUserId" ON "EmployerOffers" ("CreatedByUserId");

CREATE INDEX "IX_EmployerOffers_IsDeleted" ON "EmployerOffers" ("IsDeleted");

CREATE UNIQUE INDEX "IX_EmployerOffers_JobApplicationId" ON "EmployerOffers" ("JobApplicationId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_EmployerOffers_PipelineId" ON "EmployerOffers" ("PipelineId");

CREATE INDEX "IX_EmployerOffers_Status_ExpiresAtUtc" ON "EmployerOffers" ("Status", "ExpiresAtUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119084014_AddEmployerOffers', '9.0.8');

CREATE TABLE "HiringConversations" (
    "Id" uuid NOT NULL,
    "JobApplicationId" uuid NOT NULL,
    "CandidateUserId" uuid NOT NULL,
    "EmployerUserId" uuid NOT NULL,
    "LastMessageAtUtc" timestamp with time zone,
    "CandidateLastReadAtUtc" timestamp with time zone,
    "EmployerLastReadAtUtc" timestamp with time zone,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_HiringConversations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_HiringConversations_JobApplications_JobApplicationId" FOREIGN KEY ("JobApplicationId") REFERENCES "JobApplications" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_HiringConversations_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_HiringConversations_Users_EmployerUserId" FOREIGN KEY ("EmployerUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "HiringMessages" (
    "Id" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "SenderUserId" uuid NOT NULL,
    "Body" character varying(5000) NOT NULL,
    "SentAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_HiringMessages" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_HiringMessages_HiringConversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "HiringConversations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_HiringMessages_Users_SenderUserId" FOREIGN KEY ("SenderUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_HiringConversations_CandidateUserId_LastMessageAtUtc" ON "HiringConversations" ("CandidateUserId", "LastMessageAtUtc");

CREATE INDEX "IX_HiringConversations_EmployerUserId_LastMessageAtUtc" ON "HiringConversations" ("EmployerUserId", "LastMessageAtUtc");

CREATE INDEX "IX_HiringConversations_IsDeleted" ON "HiringConversations" ("IsDeleted");

CREATE UNIQUE INDEX "IX_HiringConversations_JobApplicationId" ON "HiringConversations" ("JobApplicationId") WHERE "IsDeleted" = FALSE;

CREATE INDEX "IX_HiringMessages_ConversationId_SentAtUtc" ON "HiringMessages" ("ConversationId", "SentAtUtc");

CREATE INDEX "IX_HiringMessages_IsDeleted" ON "HiringMessages" ("IsDeleted");

CREATE INDEX "IX_HiringMessages_SenderUserId" ON "HiringMessages" ("SenderUserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119102748_AddHiringMessaging', '9.0.8');

ALTER TABLE "Users" ADD "IsRecruiterVisible" boolean NOT NULL DEFAULT FALSE;

CREATE TABLE "EmployerTalentPools" (
    "Id" uuid NOT NULL,
    "EmployerUserId" uuid NOT NULL,
    "CandidateUserId" uuid NOT NULL,
    "Notes" text,
    "AddedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone,
    "IsDeleted" boolean NOT NULL,
    "DeletedAtUtc" timestamp with time zone,
    CONSTRAINT "PK_EmployerTalentPools" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_EmployerTalentPools_Users_CandidateUserId" FOREIGN KEY ("CandidateUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_EmployerTalentPools_Users_EmployerUserId" FOREIGN KEY ("EmployerUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_EmployerTalentPools_CandidateUserId" ON "EmployerTalentPools" ("CandidateUserId");

CREATE INDEX "IX_EmployerTalentPools_EmployerUserId" ON "EmployerTalentPools" ("EmployerUserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119153702_AddCandidateFinderVisibilityAndTalentPool', '9.0.8');

COMMIT;

