START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorBillingCycles" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "PaymentId" uuid NOT NULL,
        "PlanVersion" character varying(80) NOT NULL,
        "StartsAtUtc" timestamp with time zone NOT NULL,
        "EndsAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorBillingCycles" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_AcceleratorBillingCycles_Id_UserId" UNIQUE ("Id", "UserId"),
        CONSTRAINT "CK_AcceleratorCycle_Bounds" CHECK ("EndsAtUtc" > "StartsAtUtc"),
        CONSTRAINT "FK_AcceleratorBillingCycles_Payments_PaymentId" FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorBillingCycles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorProfiles" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Revision" uuid NOT NULL,
        "ExperienceYears" numeric(3,1) NOT NULL,
        "EducationSummary" character varying(2000) NOT NULL,
        "WeeklyStudyMinutes" integer NOT NULL,
        "LinkedResumeUploadedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProfiles" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProfile_Experience" CHECK ("ExperienceYears" BETWEEN 0 AND 60),
        CONSTRAINT "CK_AcceleratorProfile_Study" CHECK ("WeeklyStudyMinutes" BETWEEN 15 AND 10080),
        CONSTRAINT "FK_AcceleratorProfiles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorTaxonomies" (
        "Id" uuid NOT NULL,
        "Version" character varying(80) NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "PublishedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorTaxonomies" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorAllowances" (
        "Id" uuid NOT NULL,
        "CycleId" uuid NOT NULL,
        "Feature" integer NOT NULL,
        "Allocated" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorAllowances" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorAllowance_Bounds" CHECK ("Allocated" BETWEEN 1 AND 10000 AND "Feature" BETWEEN 1 AND 5),
        CONSTRAINT "FK_AcceleratorAllowances_AcceleratorBillingCycles_CycleId" FOREIGN KEY ("CycleId") REFERENCES "AcceleratorBillingCycles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorUsageReservations" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CycleId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Feature" integer NOT NULL,
        "State" integer NOT NULL,
        "Revision" uuid NOT NULL,
        "Provider" character varying(80) NOT NULL,
        "Model" character varying(80) NOT NULL,
        "ReservedAtUtc" timestamp with time zone NOT NULL,
        "LeaseExpiresAtUtc" timestamp with time zone NOT NULL,
        "FinishedAtUtc" timestamp with time zone,
        "BudgetDayUtc" timestamp with time zone NOT NULL,
        "ReservedCostInr" numeric(18,6) NOT NULL,
        "ActualCostInr" numeric(18,6),
        "InputTokens" integer NOT NULL,
        "OutputTokens" integer NOT NULL,
        "ReservedInputTokens" integer NOT NULL,
        "ReservedOutputTokens" integer NOT NULL,
        "PolicyViolation" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorUsageReservations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorUsage_Cost" CHECK ("ReservedCostInr" >= 0 AND ("ActualCostInr" IS NULL OR "ActualCostInr" >= 0)),
        CONSTRAINT "CK_AcceleratorUsage_Lease" CHECK ("LeaseExpiresAtUtc" > "ReservedAtUtc"),
        CONSTRAINT "CK_AcceleratorUsage_State" CHECK ("State" BETWEEN 1 AND 5 AND "Feature" BETWEEN 1 AND 5),
        CONSTRAINT "CK_AcceleratorUsage_Tokens" CHECK ("InputTokens" >= 0 AND "OutputTokens" >= 0 AND "ReservedInputTokens" >= 0 AND "ReservedOutputTokens" >= 0),
        CONSTRAINT "FK_AcceleratorUsageReservations_AcceleratorBillingCycles_Cycle~" FOREIGN KEY ("CycleId", "UserId") REFERENCES "AcceleratorBillingCycles" ("Id", "UserId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorProfileSkills" (
        "Id" uuid NOT NULL,
        "ProfileId" uuid NOT NULL,
        "SkillId" uuid NOT NULL,
        "SelfReportedProficiency" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProfileSkills" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProfileSkill_Proficiency" CHECK ("SelfReportedProficiency" BETWEEN 0 AND 5),
        CONSTRAINT "FK_AcceleratorProfileSkills_AcceleratorProfiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "AcceleratorProfiles" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorProfileSkills_Skills_SkillId" FOREIGN KEY ("SkillId") REFERENCES "Skills" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorCareerRoles" (
        "Id" uuid NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "Code" character varying(80) NOT NULL,
        "Name" character varying(120) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorCareerRoles" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_AcceleratorCareerRoles_Id_TaxonomyId" UNIQUE ("Id", "TaxonomyId"),
        CONSTRAINT "FK_AcceleratorCareerRoles_AcceleratorTaxonomies_TaxonomyId" FOREIGN KEY ("TaxonomyId") REFERENCES "AcceleratorTaxonomies" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorCompetencies" (
        "Id" uuid NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "SkillId" uuid NOT NULL,
        "SkillNameSnapshot" character varying(150) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "Category" character varying(100) NOT NULL,
        "Difficulty" integer NOT NULL,
        "ParentId" uuid,
        "AssessmentMethodsJson" jsonb NOT NULL,
        "ResourcesJson" jsonb NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorCompetencies" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_AcceleratorCompetencies_Id_TaxonomyId" UNIQUE ("Id", "TaxonomyId"),
        CONSTRAINT "CK_AcceleratorCompetency_Difficulty" CHECK ("Difficulty" BETWEEN 1 AND 5),
        CONSTRAINT "FK_AcceleratorCompetencies_AcceleratorCompetencies_ParentId_Ta~" FOREIGN KEY ("ParentId", "TaxonomyId") REFERENCES "AcceleratorCompetencies" ("Id", "TaxonomyId") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorCompetencies_AcceleratorTaxonomies_TaxonomyId" FOREIGN KEY ("TaxonomyId") REFERENCES "AcceleratorTaxonomies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorCompetencies_Skills_SkillId" FOREIGN KEY ("SkillId") REFERENCES "Skills" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorUsageEvents" (
        "Id" uuid NOT NULL,
        "ReservationId" uuid NOT NULL,
        "State" integer NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CostInr" numeric(18,6) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorUsageEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorUsageEvent_State" CHECK ("State" BETWEEN 1 AND 5 AND "CostInr" >= 0),
        CONSTRAINT "FK_AcceleratorUsageEvents_AcceleratorUsageReservations_Reserva~" FOREIGN KEY ("ReservationId") REFERENCES "AcceleratorUsageReservations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorCareerGoals" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Revision" uuid NOT NULL,
        "Status" integer NOT NULL,
        "WeeklyStudyMinutes" integer NOT NULL,
        "TargetAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorCareerGoals" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorGoal_Status" CHECK ("Status" BETWEEN 1 AND 3),
        CONSTRAINT "CK_AcceleratorGoal_Study" CHECK ("WeeklyStudyMinutes" BETWEEN 15 AND 10080),
        CONSTRAINT "FK_AcceleratorCareerGoals_AcceleratorCareerRoles_RoleId_Taxono~" FOREIGN KEY ("RoleId", "TaxonomyId") REFERENCES "AcceleratorCareerRoles" ("Id", "TaxonomyId") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorCareerGoals_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorPrerequisites" (
        "Id" uuid NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "RequiredCompetencyId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorPrerequisites" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorPrerequisite_NotSelf" CHECK ("CompetencyId" <> "RequiredCompetencyId"),
        CONSTRAINT "FK_AcceleratorPrerequisites_AcceleratorCompetencies_Competency~" FOREIGN KEY ("CompetencyId", "TaxonomyId") REFERENCES "AcceleratorCompetencies" ("Id", "TaxonomyId") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorPrerequisites_AcceleratorCompetencies_RequiredCo~" FOREIGN KEY ("RequiredCompetencyId", "TaxonomyId") REFERENCES "AcceleratorCompetencies" ("Id", "TaxonomyId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE TABLE "AcceleratorRoleRequirements" (
        "Id" uuid NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "ExpectedProficiency" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorRoleRequirements" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorRequirement_Proficiency" CHECK ("ExpectedProficiency" BETWEEN 1 AND 5),
        CONSTRAINT "FK_AcceleratorRoleRequirements_AcceleratorCareerRoles_RoleId_T~" FOREIGN KEY ("RoleId", "TaxonomyId") REFERENCES "AcceleratorCareerRoles" ("Id", "TaxonomyId") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorRoleRequirements_AcceleratorCompetencies_Compete~" FOREIGN KEY ("CompetencyId", "TaxonomyId") REFERENCES "AcceleratorCompetencies" ("Id", "TaxonomyId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAllowances_CycleId_Feature" ON "AcceleratorAllowances" ("CycleId", "Feature");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorAllowances_IsDeleted" ON "AcceleratorAllowances" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorBillingCycles_IsDeleted" ON "AcceleratorBillingCycles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorBillingCycles_PaymentId" ON "AcceleratorBillingCycles" ("PaymentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorBillingCycles_UserId_StartsAtUtc_EndsAtUtc" ON "AcceleratorBillingCycles" ("UserId", "StartsAtUtc", "EndsAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCareerGoals_IsDeleted" ON "AcceleratorCareerGoals" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCareerGoals_RoleId_TaxonomyId" ON "AcceleratorCareerGoals" ("RoleId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCareerGoals_UserId_CreatedAtUtc_Id" ON "AcceleratorCareerGoals" ("UserId", "CreatedAtUtc", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorCareerGoals_UserId_RequestKey" ON "AcceleratorCareerGoals" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCareerRoles_IsDeleted" ON "AcceleratorCareerRoles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorCareerRoles_TaxonomyId_Code" ON "AcceleratorCareerRoles" ("TaxonomyId", "Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCompetencies_IsDeleted" ON "AcceleratorCompetencies" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCompetencies_ParentId_TaxonomyId" ON "AcceleratorCompetencies" ("ParentId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorCompetencies_SkillId" ON "AcceleratorCompetencies" ("SkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorCompetencies_TaxonomyId_SkillId" ON "AcceleratorCompetencies" ("TaxonomyId", "SkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPrerequisites_CompetencyId_RequiredCompetencyId" ON "AcceleratorPrerequisites" ("CompetencyId", "RequiredCompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorPrerequisites_CompetencyId_TaxonomyId" ON "AcceleratorPrerequisites" ("CompetencyId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorPrerequisites_IsDeleted" ON "AcceleratorPrerequisites" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorPrerequisites_RequiredCompetencyId_TaxonomyId" ON "AcceleratorPrerequisites" ("RequiredCompetencyId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorProfiles_IsDeleted" ON "AcceleratorProfiles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProfiles_UserId" ON "AcceleratorProfiles" ("UserId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorProfileSkills_IsDeleted" ON "AcceleratorProfileSkills" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProfileSkills_ProfileId_SkillId" ON "AcceleratorProfileSkills" ("ProfileId", "SkillId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorProfileSkills_SkillId" ON "AcceleratorProfileSkills" ("SkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorRoleRequirements_CompetencyId_TaxonomyId" ON "AcceleratorRoleRequirements" ("CompetencyId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorRoleRequirements_IsDeleted" ON "AcceleratorRoleRequirements" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorRoleRequirements_RoleId_CompetencyId" ON "AcceleratorRoleRequirements" ("RoleId", "CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorRoleRequirements_RoleId_TaxonomyId" ON "AcceleratorRoleRequirements" ("RoleId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorTaxonomies_IsDeleted" ON "AcceleratorTaxonomies" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorTaxonomies_PublishedAtUtc" ON "AcceleratorTaxonomies" ("PublishedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorTaxonomies_Version" ON "AcceleratorTaxonomies" ("Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageEvents_IsDeleted" ON "AcceleratorUsageEvents" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageEvents_OccurredAtUtc" ON "AcceleratorUsageEvents" ("OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorUsageEvents_ReservationId_State" ON "AcceleratorUsageEvents" ("ReservationId", "State");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageReservations_BudgetDayUtc_State" ON "AcceleratorUsageReservations" ("BudgetDayUtc", "State");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageReservations_CycleId_Feature_State" ON "AcceleratorUsageReservations" ("CycleId", "Feature", "State");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageReservations_CycleId_UserId" ON "AcceleratorUsageReservations" ("CycleId", "UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageReservations_IsDeleted" ON "AcceleratorUsageReservations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageReservations_State_LeaseExpiresAtUtc" ON "AcceleratorUsageReservations" ("State", "LeaseExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorUsageReservations_UserId_RequestKey" ON "AcceleratorUsageReservations" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    CREATE INDEX "IX_AcceleratorUsageReservations_UserId_ReservedAtUtc" ON "AcceleratorUsageReservations" ("UserId", "ReservedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113113821_AddCareerAcceleratorPhase1') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261113113821_AddCareerAcceleratorPhase1', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorAssessmentAttempts" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "TemplateId" uuid NOT NULL,
        "TemplateVersion" character varying(80) NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Status" integer NOT NULL,
        "AttemptNumber" integer NOT NULL,
        "StartedAtUtc" timestamp with time zone,
        "SubmittedAtUtc" timestamp with time zone,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "Score" numeric(5,2),
        "SelectionStrategy" character varying(80) NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorAssessmentAttempts" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorAssessmentAttempt_Number" CHECK ("AttemptNumber" >= 1),
        CONSTRAINT "FK_AcceleratorAssessmentAttempts_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorAssessmentTemplates" (
        "Id" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        "Version" character varying(80) NOT NULL,
        "SelectionStrategy" character varying(80) NOT NULL,
        "TimeLimitMinutes" integer NOT NULL,
        "MaximumQuestions" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorAssessmentTemplates" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorAssessmentTemplate_Limits" CHECK ("TimeLimitMinutes" BETWEEN 1 AND 240 AND "MaximumQuestions" BETWEEN 1 AND 100),
        CONSTRAINT "FK_AcceleratorAssessmentTemplates_AcceleratorCareerRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AcceleratorCareerRoles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorQuestions" (
        "Id" uuid NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "Version" character varying(80) NOT NULL,
        "Type" integer NOT NULL,
        "Difficulty" integer NOT NULL,
        "Weight" numeric(9,4) NOT NULL,
        "Prompt" character varying(8000) NOT NULL,
        "OptionsJson" jsonb NOT NULL,
        "AnswerKeyJson" jsonb NOT NULL,
        "RubricJson" jsonb NOT NULL,
        "Explanation" character varying(8000) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorQuestions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorQuestion_Bounds" CHECK ("Difficulty" BETWEEN 1 AND 5 AND "Weight" > 0),
        CONSTRAINT "FK_AcceleratorQuestions_AcceleratorCompetencies_CompetencyId_T~" FOREIGN KEY ("CompetencyId", "TaxonomyId") REFERENCES "AcceleratorCompetencies" ("Id", "TaxonomyId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorRecommendations" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "RoadmapNodeId" uuid,
        "Type" character varying(100) NOT NULL,
        "Reason" character varying(4000) NOT NULL,
        "Priority" integer NOT NULL,
        "EvidenceJson" jsonb NOT NULL,
        "GeneratedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorRecommendations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorRecommendation_Priority" CHECK ("Priority" BETWEEN 1 AND 100)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorRoadmapEdges" (
        "Id" uuid NOT NULL,
        "RoadmapId" uuid NOT NULL,
        "FromNodeId" uuid NOT NULL,
        "ToNodeId" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorRoadmapEdges" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorRoadmapEdge_NotSelf" CHECK ("FromNodeId" <> "ToNodeId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorRoadmaps" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "Status" integer NOT NULL,
        "GenerationStrategy" character varying(80) NOT NULL,
        "TaxonomyId" uuid NOT NULL,
        "PlannedStartAtUtc" timestamp with time zone NOT NULL,
        "TargetAtUtc" timestamp with time zone NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "Explanation" character varying(8000) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorRoadmaps" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorRoadmap_Timeline" CHECK ("TargetAtUtc" > "PlannedStartAtUtc" AND "EstimatedMinutes" >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorSkillEvidence" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "Source" integer NOT NULL,
        "Proficiency" integer NOT NULL,
        "Confidence" numeric(5,4) NOT NULL,
        "ScoringVersion" character varying(80) NOT NULL,
        "AttemptId" uuid,
        "EvaluatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorSkillEvidence" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorSkillEvidence_Confidence" CHECK ("Confidence" BETWEEN 0 AND 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorAssessmentAnswers" (
        "Id" uuid NOT NULL,
        "AttemptId" uuid NOT NULL,
        "QuestionId" uuid NOT NULL,
        "QuestionVersion" character varying(80) NOT NULL,
        "Sequence" integer NOT NULL,
        "SubmittedAnswerJson" jsonb NOT NULL,
        "IsCorrect" boolean,
        "Score" numeric(5,2),
        "EvaluationEvidenceJson" jsonb NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorAssessmentAnswers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorAssessmentAnswers_AcceleratorAssessmentAttempts_~" FOREIGN KEY ("AttemptId") REFERENCES "AcceleratorAssessmentAttempts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorRoadmapNodes" (
        "Id" uuid NOT NULL,
        "RoadmapId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "StableKey" character varying(120) NOT NULL,
        "Sequence" integer NOT NULL,
        "Status" integer NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "ProgressPercent" integer NOT NULL,
        "CompletionEvidenceJson" jsonb NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorRoadmapNodes" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorRoadmapNode_Progress" CHECK ("EstimatedMinutes" >= 0 AND "ProgressPercent" BETWEEN 0 AND 100),
        CONSTRAINT "FK_AcceleratorRoadmapNodes_AcceleratorRoadmaps_RoadmapId" FOREIGN KEY ("RoadmapId") REFERENCES "AcceleratorRoadmaps" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorLearningActivities" (
        "Id" uuid NOT NULL,
        "RoadmapNodeId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Title" character varying(500) NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "ScheduledAtUtc" timestamp with time zone NOT NULL,
        "Status" integer NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorLearningActivities" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorLearningActivity_Effort" CHECK ("EstimatedMinutes" BETWEEN 1 AND 1440),
        CONSTRAINT "FK_AcceleratorLearningActivities_AcceleratorRoadmapNodes_Roadm~" FOREIGN KEY ("RoadmapNodeId") REFERENCES "AcceleratorRoadmapNodes" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE TABLE "AcceleratorProgressEvents" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "ActivityId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "Kind" integer NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "EvidenceJson" jsonb NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProgressEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorProgressEvents_AcceleratorLearningActivities_Act~" FOREIGN KEY ("ActivityId") REFERENCES "AcceleratorLearningActivities" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAssessmentAnswers_AttemptId_QuestionId" ON "AcceleratorAssessmentAnswers" ("AttemptId", "QuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAssessmentAnswers_AttemptId_Sequence" ON "AcceleratorAssessmentAnswers" ("AttemptId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorAssessmentAnswers_IsDeleted" ON "AcceleratorAssessmentAnswers" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorAssessmentAttempts_GoalId" ON "AcceleratorAssessmentAttempts" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorAssessmentAttempts_IsDeleted" ON "AcceleratorAssessmentAttempts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorAssessmentAttempts_UserId_GoalId_Status" ON "AcceleratorAssessmentAttempts" ("UserId", "GoalId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAssessmentAttempts_UserId_RequestKey" ON "AcceleratorAssessmentAttempts" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorAssessmentTemplates_IsDeleted" ON "AcceleratorAssessmentTemplates" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorAssessmentTemplates_RoleId_IsActive" ON "AcceleratorAssessmentTemplates" ("RoleId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAssessmentTemplates_RoleId_Version" ON "AcceleratorAssessmentTemplates" ("RoleId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorLearningActivities_IsDeleted" ON "AcceleratorLearningActivities" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorLearningActivities_RoadmapNodeId" ON "AcceleratorLearningActivities" ("RoadmapNodeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorLearningActivities_UserId_ScheduledAtUtc_Status" ON "AcceleratorLearningActivities" ("UserId", "ScheduledAtUtc", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorProgressEvents_ActivityId_OccurredAtUtc" ON "AcceleratorProgressEvents" ("ActivityId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorProgressEvents_IsDeleted" ON "AcceleratorProgressEvents" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProgressEvents_UserId_RequestKey" ON "AcceleratorProgressEvents" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorQuestions_CompetencyId_TaxonomyId" ON "AcceleratorQuestions" ("CompetencyId", "TaxonomyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorQuestions_CompetencyId_Version" ON "AcceleratorQuestions" ("CompetencyId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorQuestions_IsDeleted" ON "AcceleratorQuestions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorQuestions_TaxonomyId_IsActive_Difficulty" ON "AcceleratorQuestions" ("TaxonomyId", "IsActive", "Difficulty");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRecommendations_IsDeleted" ON "AcceleratorRecommendations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRecommendations_UserId_GoalId_GeneratedAtUtc" ON "AcceleratorRecommendations" ("UserId", "GoalId", "GeneratedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRoadmapEdges_IsDeleted" ON "AcceleratorRoadmapEdges" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorRoadmapEdges_RoadmapId_FromNodeId_ToNodeId" ON "AcceleratorRoadmapEdges" ("RoadmapId", "FromNodeId", "ToNodeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRoadmapEdges_RoadmapId_ToNodeId" ON "AcceleratorRoadmapEdges" ("RoadmapId", "ToNodeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRoadmapNodes_IsDeleted" ON "AcceleratorRoadmapNodes" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorRoadmapNodes_RoadmapId_Sequence" ON "AcceleratorRoadmapNodes" ("RoadmapId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorRoadmapNodes_RoadmapId_StableKey" ON "AcceleratorRoadmapNodes" ("RoadmapId", "StableKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorRoadmaps_GoalId_Version" ON "AcceleratorRoadmaps" ("GoalId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRoadmaps_IsDeleted" ON "AcceleratorRoadmaps" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorRoadmaps_UserId_Status" ON "AcceleratorRoadmaps" ("UserId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorSkillEvidence_AttemptId" ON "AcceleratorSkillEvidence" ("AttemptId") WHERE "AttemptId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_IsDeleted" ON "AcceleratorSkillEvidence" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_UserId_GoalId_CompetencyId_Evaluat~" ON "AcceleratorSkillEvidence" ("UserId", "GoalId", "CompetencyId", "EvaluatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261113135725_AddCareerAcceleratorPhase2') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261113135725_AddCareerAcceleratorPhase2', '9.0.8');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "AcceleratorRefundReviewAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "AcceleratorRefundReviewReason" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "AcceleratorRefundReviewRequired" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "PlanVersion" character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "ProviderCheckoutExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "ProviderCheckoutUrl" character varying(2048);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "Payments" ADD "PurchaseRequestKey" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "AssignmentId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "CandidateProjectId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "InterviewEvaluationId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "InterviewSessionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "ProjectEvaluationId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "ProjectVersionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "Score" numeric(9,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "SessionQuestionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD "SubmissionId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewDefinitions" (
        "Id" uuid NOT NULL,
        "StableKey" character varying(120) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "TargetRoleId" uuid NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewDefinitions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorInterviewDefinitions_AcceleratorCareerRoles_Targ~" FOREIGN KEY ("TargetRoleId") REFERENCES "AcceleratorCareerRoles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorPassportShares" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "PublicToken" character varying(64) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "Revision" uuid NOT NULL,
        "EnabledAtUtc" timestamp with time zone,
        "RevokedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorPassportShares" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorPassportShares_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorPracticeEvaluations" (
        "Id" uuid NOT NULL,
        "AttemptId" uuid NOT NULL,
        "Score" numeric(9,2) NOT NULL,
        "MaximumScore" numeric(9,2) NOT NULL,
        "Classification" character varying(80) NOT NULL,
        "StrengthsJson" jsonb NOT NULL,
        "WeaknessesJson" jsonb NOT NULL,
        "Explanation" character varying(8000) NOT NULL,
        "SuggestedImprovement" character varying(4000) NOT NULL,
        "Method" integer NOT NULL,
        "Confidence" numeric(5,4) NOT NULL,
        "RubricVersion" character varying(80) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorPracticeEvaluations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorPracticeEvaluation_Score" CHECK ("Score" >= 0 AND "MaximumScore" > 0 AND "Score" <= "MaximumScore" AND "Confidence" BETWEEN 0 AND 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorPracticeSessions" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "RoadmapNodeId" uuid,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Difficulty" integer NOT NULL,
        "QuestionType" integer NOT NULL,
        "Status" integer NOT NULL,
        "GenerationStrategy" character varying(80) NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorPracticeSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorPracticeSessions_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorPracticeSessions_AcceleratorRoadmapNodes_Roadmap~" FOREIGN KEY ("RoadmapNodeId") REFERENCES "AcceleratorRoadmapNodes" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectDefinitions" (
        "Id" uuid NOT NULL,
        "StableKey" character varying(120) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectDefinitions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorTutorContextSnapshots" (
        "Id" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "ContextVersion" character varying(80) NOT NULL,
        "ContentJson" jsonb NOT NULL,
        "TokenEstimate" integer NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorTutorContextSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorTutorContextSnapshot_Tokens" CHECK ("TokenEstimate" BETWEEN 0 AND 32000)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorTutorConversations" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "RoadmapNodeId" uuid,
        "CompetencyId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Topic" character varying(500) NOT NULL,
        "Title" character varying(500) NOT NULL,
        "Status" integer NOT NULL,
        "LastMessageAtUtc" timestamp with time zone NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorTutorConversations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorTutorConversations_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorTutorConversations_AcceleratorCompetencies_Compe~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorTutorConversations_AcceleratorRoadmapNodes_Roadm~" FOREIGN KEY ("RoadmapNodeId") REFERENCES "AcceleratorRoadmapNodes" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorTutorMemorySummaries" (
        "Id" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "Summary" character varying(8000) NOT NULL,
        "IsInvalidated" boolean NOT NULL,
        "SourceRange" character varying(500) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorTutorMemorySummaries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "CandidateMailboxConnections" (
        "Id" uuid NOT NULL,
        "CandidateId" uuid NOT NULL,
        "Provider" character varying(30) NOT NULL,
        "EmailAddress" character varying(254) NOT NULL,
        "ProviderAccountIdentifier" character varying(200) NOT NULL,
        "GrantedScopes" character varying(500) NOT NULL,
        "TokenReference" character varying(300) NOT NULL,
        "ConnectedAtUtc" timestamp with time zone NOT NULL,
        "RevokedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_CandidateMailboxConnections" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CandidateMailboxConnections_Users_CandidateId" FOREIGN KEY ("CandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "EmployerContacts" (
        "Id" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "FullName" character varying(160) NOT NULL,
        "Title" character varying(160) NOT NULL,
        "Department" character varying(100) NOT NULL,
        "ProfessionalEmail" character varying(254) NOT NULL,
        "NormalizedEmail" character varying(254) NOT NULL,
        "Role" integer NOT NULL,
        "Verification" integer NOT NULL,
        "Source" character varying(60) NOT NULL,
        "VerificationEvidence" character varying(500),
        "LastVerifiedAtUtc" timestamp with time zone,
        "DiscoveryExpiresAtUtc" timestamp with time zone,
        "IsSuppressed" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_EmployerContacts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_EmployerContacts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketJobProcessingStates" (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "TaxonomyRevision" bigint NOT NULL,
        "ProcessedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketJobProcessingStates" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketJobProcessingStates_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketProcessingCheckpoints" (
        "Id" uuid NOT NULL,
        "Name" character varying(80) NOT NULL,
        "Revision" uuid NOT NULL,
        "LastJobId" uuid,
        "LastJobCreatedAtUtc" timestamp with time zone,
        "LastSuccessfulAtUtc" timestamp with time zone,
        "LeaseExpiresAtUtc" timestamp with time zone,
        "LeaseOwner" uuid,
        "JobsProcessed" bigint NOT NULL,
        "Failures" bigint NOT NULL,
        "TaxonomyRevision" bigint NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketProcessingCheckpoints" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketRoles" (
        "Id" uuid NOT NULL,
        "Revision" uuid NOT NULL,
        "AcceleratorRoleId" uuid,
        "Name" character varying(160) NOT NULL,
        "NormalizedName" character varying(160) NOT NULL,
        "Slug" character varying(160) NOT NULL,
        "Family" character varying(100) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "Status" integer NOT NULL,
        "MergedIntoId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketRoles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketRoles_AcceleratorCareerRoles_AcceleratorRoleId" FOREIGN KEY ("AcceleratorRoleId") REFERENCES "AcceleratorCareerRoles" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_MarketRoles_MarketRoles_MergedIntoId" FOREIGN KEY ("MergedIntoId") REFERENCES "MarketRoles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketSkills" (
        "Id" uuid NOT NULL,
        "Revision" uuid NOT NULL,
        "SkillId" uuid NOT NULL,
        "CompetencyId" uuid,
        "Slug" character varying(160) NOT NULL,
        "Category" character varying(100) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "Status" integer NOT NULL,
        "MergedIntoId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketSkills" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketSkills_AcceleratorCompetencies_CompetencyId" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_MarketSkills_MarketSkills_MergedIntoId" FOREIGN KEY ("MergedIntoId") REFERENCES "MarketSkills" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_MarketSkills_Skills_SkillId" FOREIGN KEY ("SkillId") REFERENCES "Skills" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "TalentShareLinks" (
        "Id" uuid NOT NULL,
        "CandidateId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "JobId" uuid,
        "CompanyId" uuid,
        "TokenHash" character varying(64) NOT NULL,
        "Purpose" character varying(50) NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "RevokedAtUtc" timestamp with time zone,
        "LastAccessedAtUtc" timestamp with time zone,
        "AccessCount" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_TalentShareLinks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TalentShareLinks_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentShareLinks_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentShareLinks_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentShareLinks_Users_CandidateId" FOREIGN KEY ("CandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewVersions" (
        "Id" uuid NOT NULL,
        "DefinitionId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "Type" integer NOT NULL,
        "Difficulty" integer NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "TargetExperienceLevel" character varying(100) NOT NULL,
        "Instructions" character varying(12000) NOT NULL,
        "Status" integer NOT NULL,
        "PublishedAtUtc" timestamp with time zone,
        "RetiredAtUtc" timestamp with time zone,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewVersion_Bounds" CHECK ("Version" > 0 AND "EstimatedMinutes" > 0 AND "Status" BETWEEN 1 AND 3),
        CONSTRAINT "FK_AcceleratorInterviewVersions_AcceleratorInterviewDefinition~" FOREIGN KEY ("DefinitionId") REFERENCES "AcceleratorInterviewDefinitions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorPracticeQuestions" (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Prompt" character varying(12000) NOT NULL,
        "PublicOptionsJson" jsonb NOT NULL,
        "AnswerKeyJson" jsonb NOT NULL,
        "RubricJson" jsonb NOT NULL,
        "GenerationVersion" character varying(80) NOT NULL,
        "IsQualityApproved" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorPracticeQuestions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorPracticeQuestions_AcceleratorPracticeSessions_Se~" FOREIGN KEY ("SessionId") REFERENCES "AcceleratorPracticeSessions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectVersions" (
        "Id" uuid NOT NULL,
        "DefinitionId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "TargetRoleId" uuid NOT NULL,
        "Type" integer NOT NULL,
        "Difficulty" integer NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "Title" character varying(200) NOT NULL,
        "ShortDescription" character varying(2000) NOT NULL,
        "BusinessContext" character varying(12000) NOT NULL,
        "Instructions" character varying(12000) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CatalogStatus" integer NOT NULL,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectVersion_Bounds" CHECK ("Version" > 0 AND "EstimatedMinutes" > 0 AND "CatalogStatus" BETWEEN 1 AND 3),
        CONSTRAINT "FK_AcceleratorProjectVersions_AcceleratorCareerRoles_TargetRol~" FOREIGN KEY ("TargetRoleId") REFERENCES "AcceleratorCareerRoles" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorProjectVersions_AcceleratorProjectDefinitions_De~" FOREIGN KEY ("DefinitionId") REFERENCES "AcceleratorProjectDefinitions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorTutorMessages" (
        "Id" uuid NOT NULL,
        "ConversationId" uuid NOT NULL,
        "Role" integer NOT NULL,
        "Status" integer NOT NULL,
        "Content" character varying(16000) NOT NULL,
        "MessageType" character varying(80) NOT NULL,
        "Provider" character varying(80),
        "Model" character varying(120),
        "InputTokens" integer NOT NULL,
        "OutputTokens" integer NOT NULL,
        "EstimatedCostInr" numeric(18,6),
        "FailureCode" character varying(100),
        "DispatchId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorTutorMessages" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorTutorMessage_Tokens" CHECK ("InputTokens" >= 0 AND "OutputTokens" >= 0),
        CONSTRAINT "FK_AcceleratorTutorMessages_AcceleratorTutorConversations_Conv~" FOREIGN KEY ("ConversationId") REFERENCES "AcceleratorTutorConversations" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketJobRoles" (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "MarketRoleId" uuid NOT NULL,
        "Source" integer NOT NULL,
        "Confidence" numeric(4,3) NOT NULL,
        "IsPrimary" boolean NOT NULL,
        "ObservedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketJobRoles" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketJobRole_Confidence" CHECK ("Confidence" BETWEEN 0 AND 1),
        CONSTRAINT "FK_MarketJobRoles_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_MarketJobRoles_MarketRoles_MarketRoleId" FOREIGN KEY ("MarketRoleId") REFERENCES "MarketRoles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketRoleAliases" (
        "Id" uuid NOT NULL,
        "MarketRoleId" uuid NOT NULL,
        "Text" character varying(160) NOT NULL,
        "NormalizedText" character varying(160) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketRoleAliases" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketRoleAliases_MarketRoles_MarketRoleId" FOREIGN KEY ("MarketRoleId") REFERENCES "MarketRoles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketRoleDiscoveries" (
        "Id" uuid NOT NULL,
        "Revision" uuid NOT NULL,
        "RawTitle" character varying(160) NOT NULL,
        "NormalizedTitle" character varying(160) NOT NULL,
        "Status" integer NOT NULL,
        "ResolvedMarketRoleId" uuid,
        "FirstSeenAtUtc" timestamp with time zone NOT NULL,
        "LastSeenAtUtc" timestamp with time zone NOT NULL,
        "ReviewReason" character varying(1000),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketRoleDiscoveries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketRoleDiscoveries_MarketRoles_ResolvedMarketRoleId" FOREIGN KEY ("ResolvedMarketRoleId") REFERENCES "MarketRoles" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketJobSkills" (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "MarketSkillId" uuid NOT NULL,
        "Source" integer NOT NULL,
        "Confidence" numeric(4,3) NOT NULL,
        "FirstObservedAtUtc" timestamp with time zone NOT NULL,
        "LastObservedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketJobSkills" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketJobSkill_Confidence" CHECK ("Confidence" BETWEEN 0 AND 1),
        CONSTRAINT "FK_MarketJobSkills_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_MarketJobSkills_MarketSkills_MarketSkillId" FOREIGN KEY ("MarketSkillId") REFERENCES "MarketSkills" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketSkillAliases" (
        "Id" uuid NOT NULL,
        "MarketSkillId" uuid NOT NULL,
        "Text" character varying(160) NOT NULL,
        "NormalizedText" character varying(160) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketSkillAliases" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketSkillAliases_MarketSkills_MarketSkillId" FOREIGN KEY ("MarketSkillId") REFERENCES "MarketSkills" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketSkillDiscoveries" (
        "Id" uuid NOT NULL,
        "Revision" uuid NOT NULL,
        "SuggestedSkillId" uuid,
        "RawTerm" character varying(160) NOT NULL,
        "NormalizedTerm" character varying(160) NOT NULL,
        "SuggestedCategory" character varying(100) NOT NULL,
        "Status" integer NOT NULL,
        "ResolvedMarketSkillId" uuid,
        "FirstSeenAtUtc" timestamp with time zone NOT NULL,
        "LastSeenAtUtc" timestamp with time zone NOT NULL,
        "ReviewReason" character varying(1000),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketSkillDiscoveries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MarketSkillDiscoveries_MarketSkills_ResolvedMarketSkillId" FOREIGN KEY ("ResolvedMarketSkillId") REFERENCES "MarketSkills" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_MarketSkillDiscoveries_Skills_SuggestedSkillId" FOREIGN KEY ("SuggestedSkillId") REFERENCES "Skills" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "TalentOutreachDrafts" (
        "Id" uuid NOT NULL,
        "CandidateId" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "ShareLinkId" uuid,
        "MailboxConnectionId" uuid,
        "UsageReservationId" uuid,
        "Subject" character varying(180) NOT NULL,
        "Body" character varying(10000) NOT NULL,
        "Status" integer NOT NULL,
        "SendIdempotencyKey" character varying(100) NOT NULL,
        "ProviderMessageId" character varying(300),
        "FailureEvidenceReference" character varying(500),
        "DispatchStartedAtUtc" timestamp with time zone,
        "SentAtUtc" timestamp with time zone,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_TalentOutreachDrafts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TalentOutreachDrafts_AcceleratorUsageReservations_UsageRese~" FOREIGN KEY ("UsageReservationId") REFERENCES "AcceleratorUsageReservations" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentOutreachDrafts_CandidateMailboxConnections_MailboxCon~" FOREIGN KEY ("MailboxConnectionId") REFERENCES "CandidateMailboxConnections" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentOutreachDrafts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentOutreachDrafts_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentOutreachDrafts_TalentShareLinks_ShareLinkId" FOREIGN KEY ("ShareLinkId") REFERENCES "TalentShareLinks" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentOutreachDrafts_Users_CandidateId" FOREIGN KEY ("CandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "TalentSubmissions" (
        "Id" uuid NOT NULL,
        "CandidateId" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "ShareLinkId" uuid,
        "Status" integer NOT NULL,
        "SubmittedAtUtc" timestamp with time zone NOT NULL,
        "ViewedAtUtc" timestamp with time zone,
        "WithdrawnAtUtc" timestamp with time zone,
        "InterviewMessage" character varying(2000),
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_TalentSubmissions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TalentSubmissions_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentSubmissions_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentSubmissions_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentSubmissions_TalentShareLinks_ShareLinkId" FOREIGN KEY ("ShareLinkId") REFERENCES "TalentShareLinks" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentSubmissions_Users_CandidateId" FOREIGN KEY ("CandidateId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewCompetencies" (
        "Id" uuid NOT NULL,
        "InterviewVersionId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "ExpectedProficiency" integer NOT NULL,
        "Weight" numeric(7,3) NOT NULL,
        "IsRequired" boolean NOT NULL,
        "QuestionCoverageTarget" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewCompetencies" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewCompetency_Bounds" CHECK ("ExpectedProficiency" BETWEEN 0 AND 4 AND "Weight" > 0 AND "QuestionCoverageTarget" BETWEEN 0 AND 100),
        CONSTRAINT "FK_AcceleratorInterviewCompetencies_AcceleratorCompetencies_Co~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorInterviewCompetencies_AcceleratorInterviewVersio~" FOREIGN KEY ("InterviewVersionId") REFERENCES "AcceleratorInterviewVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewRubricCriteria" (
        "Id" uuid NOT NULL,
        "InterviewVersionId" uuid NOT NULL,
        "CompetencyId" uuid,
        "Sequence" integer NOT NULL,
        "Name" character varying(120) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "Weight" numeric(7,3) NOT NULL,
        "MaximumScore" numeric(9,2) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewRubricCriteria" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewCriterion_Bounds" CHECK ("Sequence" > 0 AND "Weight" > 0 AND "MaximumScore" > 0),
        CONSTRAINT "FK_AcceleratorInterviewRubricCriteria_AcceleratorCompetencies_~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorInterviewRubricCriteria_AcceleratorInterviewVers~" FOREIGN KEY ("InterviewVersionId") REFERENCES "AcceleratorInterviewVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewSections" (
        "Id" uuid NOT NULL,
        "InterviewVersionId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Type" integer NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "IsRequired" boolean NOT NULL,
        "QuestionCount" integer NOT NULL,
        "MaxFollowUpsPerQuestion" integer NOT NULL,
        "AllowStaticFallback" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewSections" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewSection_Bounds" CHECK ("Sequence" > 0 AND "EstimatedMinutes" > 0 AND "QuestionCount" BETWEEN 1 AND 50 AND "MaxFollowUpsPerQuestion" BETWEEN 0 AND 3),
        CONSTRAINT "FK_AcceleratorInterviewSections_AcceleratorInterviewVersions_I~" FOREIGN KEY ("InterviewVersionId") REFERENCES "AcceleratorInterviewVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewSessions" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "InterviewVersionId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Status" integer NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "SubmittedAtUtc" timestamp with time zone,
        "CompletedAtUtc" timestamp with time zone,
        "OverallScore" numeric(9,2),
        "ReadinessImpact" numeric(9,2),
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewSession_Score" CHECK ("OverallScore" IS NULL OR ("OverallScore" BETWEEN 0 AND 100)),
        CONSTRAINT "FK_AcceleratorInterviewSessions_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorInterviewSessions_AcceleratorInterviewVersions_I~" FOREIGN KEY ("InterviewVersionId") REFERENCES "AcceleratorInterviewVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorPracticeAttempts" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "QuestionId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "AnswerJson" jsonb NOT NULL,
        "SubmittedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorPracticeAttempts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorPracticeAttempts_AcceleratorPracticeQuestions_Qu~" FOREIGN KEY ("QuestionId") REFERENCES "AcceleratorPracticeQuestions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorPracticeAttempts_AcceleratorPracticeSessions_Ses~" FOREIGN KEY ("SessionId") REFERENCES "AcceleratorPracticeSessions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectCompetencies" (
        "Id" uuid NOT NULL,
        "ProjectVersionId" uuid NOT NULL,
        "CompetencyId" uuid NOT NULL,
        "IsPrerequisite" boolean NOT NULL,
        "RequiredProficiency" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectCompetencies" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectCompetency_Proficiency" CHECK ("RequiredProficiency" BETWEEN 0 AND 4),
        CONSTRAINT "FK_AcceleratorProjectCompetencies_AcceleratorCompetencies_Comp~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorProjectCompetencies_AcceleratorProjectVersions_P~" FOREIGN KEY ("ProjectVersionId") REFERENCES "AcceleratorProjectVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectMilestones" (
        "Id" uuid NOT NULL,
        "ProjectVersionId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Title" character varying(200) NOT NULL,
        "Description" character varying(8000) NOT NULL,
        "EstimatedMinutes" integer NOT NULL,
        "IsRequired" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectMilestones" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectMilestone_Bounds" CHECK ("Sequence" > 0 AND "EstimatedMinutes" > 0),
        CONSTRAINT "FK_AcceleratorProjectMilestones_AcceleratorProjectVersions_Pro~" FOREIGN KEY ("ProjectVersionId") REFERENCES "AcceleratorProjectVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectRubrics" (
        "Id" uuid NOT NULL,
        "ProjectVersionId" uuid NOT NULL,
        "Version" integer NOT NULL,
        "Name" character varying(200) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectRubrics" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectRubric_Version" CHECK ("Version" > 0),
        CONSTRAINT "FK_AcceleratorProjectRubrics_AcceleratorProjectVersions_Projec~" FOREIGN KEY ("ProjectVersionId") REFERENCES "AcceleratorProjectVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "MarketDiscoveryObservations" (
        "Id" uuid NOT NULL,
        "JobId" uuid NOT NULL,
        "CompanyId" uuid NOT NULL,
        "SkillDiscoveryId" uuid,
        "RoleDiscoveryId" uuid,
        "ObservedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MarketDiscoveryObservations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MarketObservation_OneConcept" CHECK (("SkillDiscoveryId" IS NULL) <> ("RoleDiscoveryId" IS NULL)),
        CONSTRAINT "FK_MarketDiscoveryObservations_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_MarketDiscoveryObservations_Jobs_JobId" FOREIGN KEY ("JobId") REFERENCES "Jobs" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_MarketDiscoveryObservations_MarketRoleDiscoveries_RoleDisco~" FOREIGN KEY ("RoleDiscoveryId") REFERENCES "MarketRoleDiscoveries" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_MarketDiscoveryObservations_MarketSkillDiscoveries_SkillDis~" FOREIGN KEY ("SkillDiscoveryId") REFERENCES "MarketSkillDiscoveries" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "TalentOutreachRecipients" (
        "Id" uuid NOT NULL,
        "DraftId" uuid NOT NULL,
        "ContactId" uuid NOT NULL,
        "EmailSnapshot" character varying(254) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_TalentOutreachRecipients" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TalentOutreachRecipients_EmployerContacts_ContactId" FOREIGN KEY ("ContactId") REFERENCES "EmployerContacts" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentOutreachRecipients_TalentOutreachDrafts_DraftId" FOREIGN KEY ("DraftId") REFERENCES "TalentOutreachDrafts" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "TalentSubmissionEvents" (
        "Id" uuid NOT NULL,
        "SubmissionId" uuid NOT NULL,
        "ActorId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "OccurredAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_TalentSubmissionEvents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TalentSubmissionEvents_TalentSubmissions_SubmissionId" FOREIGN KEY ("SubmissionId") REFERENCES "TalentSubmissions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_TalentSubmissionEvents_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewQuestions" (
        "Id" uuid NOT NULL,
        "SectionId" uuid NOT NULL,
        "CompetencyId" uuid,
        "Sequence" integer NOT NULL,
        "Kind" integer NOT NULL,
        "Type" integer NOT NULL,
        "EvaluationMode" integer NOT NULL,
        "Difficulty" integer NOT NULL,
        "PublicPrompt" character varying(12000) NOT NULL,
        "ExpectedConceptsJson" jsonb NOT NULL,
        "HiddenEvaluatorGuidance" character varying(8000) NOT NULL,
        "DeterministicRulesJson" jsonb NOT NULL,
        "FollowUpPrompt" character varying(8000) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewQuestions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewQuestion_Sequence" CHECK ("Sequence" > 0),
        CONSTRAINT "FK_AcceleratorInterviewQuestions_AcceleratorCompetencies_Compe~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorInterviewQuestions_AcceleratorInterviewSections_~" FOREIGN KEY ("SectionId") REFERENCES "AcceleratorInterviewSections" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorReadinessSnapshots" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "TriggerInterviewSessionId" uuid,
        "CalculationVersion" character varying(80) NOT NULL,
        "SkillCoverage" numeric(9,2) NOT NULL,
        "PracticePerformance" numeric(9,2) NOT NULL,
        "ProjectPerformance" numeric(9,2) NOT NULL,
        "InterviewPerformance" numeric(9,2) NOT NULL,
        "Total" numeric(9,2) NOT NULL,
        "Explanation" character varying(4000) NOT NULL,
        "CalculatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorReadinessSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_Readiness_Bounds" CHECK ("Total" BETWEEN 0 AND 100 AND "SkillCoverage" BETWEEN 0 AND 100 AND "PracticePerformance" BETWEEN 0 AND 100 AND "ProjectPerformance" BETWEEN 0 AND 100 AND "InterviewPerformance" BETWEEN 0 AND 100),
        CONSTRAINT "FK_AcceleratorReadinessSnapshots_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorReadinessSnapshots_AcceleratorInterviewSessions_~" FOREIGN KEY ("TriggerInterviewSessionId") REFERENCES "AcceleratorInterviewSessions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectAssignments" (
        "Id" uuid NOT NULL,
        "MilestoneId" uuid NOT NULL,
        "Sequence" integer NOT NULL,
        "Type" integer NOT NULL,
        "EvaluationMode" integer NOT NULL,
        "Prompt" character varying(12000) NOT NULL,
        "CompletionRequirements" character varying(8000) NOT NULL,
        "DeterministicRulesJson" jsonb NOT NULL,
        "MaximumAttempts" integer NOT NULL,
        "RetryAllowed" boolean NOT NULL,
        "CooldownMinutes" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectAssignments" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectAssignment_Mode" CHECK ("EvaluationMode" BETWEEN 1 AND 3),
        CONSTRAINT "CK_AcceleratorProjectAssignment_Retry" CHECK ("Sequence" > 0 AND "MaximumAttempts" BETWEEN 1 AND 20 AND "CooldownMinutes" BETWEEN 0 AND 10080),
        CONSTRAINT "FK_AcceleratorProjectAssignments_AcceleratorProjectMilestones_~" FOREIGN KEY ("MilestoneId") REFERENCES "AcceleratorProjectMilestones" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectRubricCriteria" (
        "Id" uuid NOT NULL,
        "RubricId" uuid NOT NULL,
        "CompetencyId" uuid,
        "Sequence" integer NOT NULL,
        "Name" character varying(120) NOT NULL,
        "Description" character varying(2000) NOT NULL,
        "Weight" numeric(7,3) NOT NULL,
        "MaximumScore" numeric(9,2) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectRubricCriteria" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectRubricCriterion_Weight" CHECK ("Sequence" > 0 AND "Weight" > 0 AND "MaximumScore" > 0),
        CONSTRAINT "FK_AcceleratorProjectRubricCriteria_AcceleratorCompetencies_Co~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorProjectRubricCriteria_AcceleratorProjectRubrics_~" FOREIGN KEY ("RubricId") REFERENCES "AcceleratorProjectRubrics" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorCandidateProjects" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "GoalId" uuid NOT NULL,
        "ProjectVersionId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Status" integer NOT NULL,
        "ProgressPercent" integer NOT NULL,
        "CurrentMilestoneId" uuid,
        "RequiredRevisionAssignmentId" uuid,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        "CompletedAtUtc" timestamp with time zone,
        "FinalScore" numeric(9,2),
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorCandidateProjects" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorCandidateProject_Progress" CHECK ("ProgressPercent" BETWEEN 0 AND 100),
        CONSTRAINT "FK_AcceleratorCandidateProjects_AcceleratorCareerGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "AcceleratorCareerGoals" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorCandidateProjects_AcceleratorProjectAssignments_~" FOREIGN KEY ("RequiredRevisionAssignmentId") REFERENCES "AcceleratorProjectAssignments" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorCandidateProjects_AcceleratorProjectMilestones_C~" FOREIGN KEY ("CurrentMilestoneId") REFERENCES "AcceleratorProjectMilestones" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorCandidateProjects_AcceleratorProjectVersions_Pro~" FOREIGN KEY ("ProjectVersionId") REFERENCES "AcceleratorProjectVersions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectSubmissions" (
        "Id" uuid NOT NULL,
        "CandidateProjectId" uuid NOT NULL,
        "AssignmentId" uuid NOT NULL,
        "AttemptNumber" integer NOT NULL,
        "RequestKey" uuid NOT NULL,
        "ContentHash" character varying(64) NOT NULL,
        "Content" character varying(32000) NOT NULL,
        "Status" integer NOT NULL,
        "SubmittedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectSubmissions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectSubmission_Attempt" CHECK ("AttemptNumber" > 0),
        CONSTRAINT "FK_AcceleratorProjectSubmissions_AcceleratorCandidateProjects_~" FOREIGN KEY ("CandidateProjectId") REFERENCES "AcceleratorCandidateProjects" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorProjectSubmissions_AcceleratorProjectAssignments~" FOREIGN KEY ("AssignmentId") REFERENCES "AcceleratorProjectAssignments" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectEvaluations" (
        "Id" uuid NOT NULL,
        "SubmissionId" uuid NOT NULL,
        "Score" numeric(9,2) NOT NULL,
        "MaximumScore" numeric(9,2) NOT NULL,
        "Evaluator" integer NOT NULL,
        "Confidence" numeric(5,4) NOT NULL,
        "Feedback" character varying(8000) NOT NULL,
        "EvaluatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectEvaluations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectEvaluation_Bounds" CHECK ("Score" >= 0 AND "MaximumScore" > 0 AND "Score" <= "MaximumScore" AND "Confidence" BETWEEN 0 AND 1),
        CONSTRAINT "FK_AcceleratorProjectEvaluations_AcceleratorProjectSubmissions~" FOREIGN KEY ("SubmissionId") REFERENCES "AcceleratorProjectSubmissions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorProjectEvaluationScores" (
        "Id" uuid NOT NULL,
        "EvaluationId" uuid NOT NULL,
        "CriterionId" uuid NOT NULL,
        "Score" numeric(9,2) NOT NULL,
        "Feedback" character varying(4000) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorProjectEvaluationScores" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorProjectEvaluationScore_Nonnegative" CHECK ("Score" >= 0),
        CONSTRAINT "FK_AcceleratorProjectEvaluationScores_AcceleratorProjectEvalua~" FOREIGN KEY ("EvaluationId") REFERENCES "AcceleratorProjectEvaluations" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorProjectEvaluationScores_AcceleratorProjectRubric~" FOREIGN KEY ("CriterionId") REFERENCES "AcceleratorProjectRubricCriteria" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorAiDispatchRecords" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "ConversationId" uuid,
        "CandidateProjectId" uuid,
        "ProjectSubmissionId" uuid,
        "InterviewSessionId" uuid,
        "InterviewAnswerId" uuid,
        "InterviewSectionId" uuid,
        "GenerationSlot" integer,
        "RequestKey" uuid NOT NULL,
        "ReservationId" uuid NOT NULL,
        "Operation" character varying(80) NOT NULL,
        "Provider" character varying(80) NOT NULL,
        "Model" character varying(120) NOT NULL,
        "Status" integer NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "ProviderRequestId" character varying(200),
        "ResultJson" jsonb,
        "InputTokens" integer,
        "OutputTokens" integer,
        "ActualCostInr" numeric(12,6),
        "FailureCode" character varying(100),
        "FinishedAtUtc" timestamp with time zone,
        "NextReconciliationAtUtc" timestamp with time zone,
        "ReconciliationAttemptCount" integer NOT NULL,
        "LastReconciliationAtUtc" timestamp with time zone,
        "LastReconciliationError" character varying(1000),
        "RequiresManualReview" boolean NOT NULL,
        "ManualReviewReason" character varying(2000),
        "ReconciliationClaimId" uuid,
        "ReconciliationClaimExpiresAtUtc" timestamp with time zone,
        "Revision" uuid NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorAiDispatchRecords" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_AcceleratorAiDispatch_ReconciliationAttempts" CHECK ("ReconciliationAttemptCount" >= 0),
        CONSTRAINT "FK_AcceleratorAiDispatchRecords_AcceleratorCandidateProjects_C~" FOREIGN KEY ("CandidateProjectId") REFERENCES "AcceleratorCandidateProjects" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewSections_I~" FOREIGN KEY ("InterviewSectionId") REFERENCES "AcceleratorInterviewSections" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewSessions_I~" FOREIGN KEY ("InterviewSessionId") REFERENCES "AcceleratorInterviewSessions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorAiDispatchRecords_AcceleratorProjectSubmissions_~" FOREIGN KEY ("ProjectSubmissionId") REFERENCES "AcceleratorProjectSubmissions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorSessionQuestions" (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "SourceQuestionId" uuid NOT NULL,
        "SectionId" uuid NOT NULL,
        "CompetencyId" uuid,
        "ParentQuestionId" uuid,
        "Sequence" integer NOT NULL,
        "Type" integer NOT NULL,
        "PublicPrompt" character varying(12000) NOT NULL,
        "AssignedAtUtc" timestamp with time zone NOT NULL,
        "GenerationDispatchId" uuid,
        "GenerationRequestKey" uuid,
        "GenerationRequestHash" character varying(64) NOT NULL,
        "GenerationProvider" character varying(80) NOT NULL,
        "GenerationModel" character varying(120) NOT NULL,
        "CompetencyIdsJson" jsonb NOT NULL,
        "GenerationMetadataJson" jsonb NOT NULL,
        "FollowUpEligible" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorSessionQuestions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_SessionQuestion_Sequence" CHECK ("Sequence" > 0),
        CONSTRAINT "FK_AcceleratorSessionQuestions_AcceleratorAiDispatchRecords_Ge~" FOREIGN KEY ("GenerationDispatchId") REFERENCES "AcceleratorAiDispatchRecords" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorSessionQuestions_AcceleratorCompetencies_Compete~" FOREIGN KEY ("CompetencyId") REFERENCES "AcceleratorCompetencies" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorSessionQuestions_AcceleratorInterviewQuestions_S~" FOREIGN KEY ("SourceQuestionId") REFERENCES "AcceleratorInterviewQuestions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorSessionQuestions_AcceleratorInterviewSections_Se~" FOREIGN KEY ("SectionId") REFERENCES "AcceleratorInterviewSections" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorSessionQuestions_AcceleratorInterviewSessions_Se~" FOREIGN KEY ("SessionId") REFERENCES "AcceleratorInterviewSessions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorSessionQuestions_AcceleratorSessionQuestions_Par~" FOREIGN KEY ("ParentQuestionId") REFERENCES "AcceleratorSessionQuestions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewAnswers" (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "SessionQuestionId" uuid NOT NULL,
        "RequestKey" uuid NOT NULL,
        "RequestHash" character varying(64) NOT NULL,
        "Content" character varying(16000) NOT NULL,
        "SubmittedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewAnswers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AcceleratorInterviewAnswers_AcceleratorInterviewSessions_Se~" FOREIGN KEY ("SessionId") REFERENCES "AcceleratorInterviewSessions" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorInterviewAnswers_AcceleratorSessionQuestions_Ses~" FOREIGN KEY ("SessionQuestionId") REFERENCES "AcceleratorSessionQuestions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewEvaluations" (
        "Id" uuid NOT NULL,
        "AnswerId" uuid NOT NULL,
        "Evaluator" integer NOT NULL,
        "Score" numeric(9,2) NOT NULL,
        "Confidence" numeric(5,4) NOT NULL,
        "Feedback" character varying(8000) NOT NULL,
        "EvaluatedAtUtc" timestamp with time zone NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewEvaluations" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewEvaluation_Bounds" CHECK ("Score" BETWEEN 0 AND 100 AND "Confidence" BETWEEN 0 AND 1),
        CONSTRAINT "FK_AcceleratorInterviewEvaluations_AcceleratorInterviewAnswers~" FOREIGN KEY ("AnswerId") REFERENCES "AcceleratorInterviewAnswers" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE TABLE "AcceleratorInterviewCriterionScores" (
        "Id" uuid NOT NULL,
        "EvaluationId" uuid NOT NULL,
        "CriterionId" uuid NOT NULL,
        "Score" numeric(9,2) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "IsDeleted" boolean NOT NULL,
        "DeletedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_AcceleratorInterviewCriterionScores" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_InterviewScore_Nonnegative" CHECK ("Score" >= 0),
        CONSTRAINT "FK_AcceleratorInterviewCriterionScores_AcceleratorInterviewEva~" FOREIGN KEY ("EvaluationId") REFERENCES "AcceleratorInterviewEvaluations" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_AcceleratorInterviewCriterionScores_AcceleratorInterviewRub~" FOREIGN KEY ("CriterionId") REFERENCES "AcceleratorInterviewRubricCriteria" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_Payments_UserId_PurchaseRequestKey" ON "Payments" ("UserId", "PurchaseRequestKey") WHERE "PurchaseRequestKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_AssignmentId" ON "AcceleratorSkillEvidence" ("AssignmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_CandidateProjectId" ON "AcceleratorSkillEvidence" ("CandidateProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorSkillEvidence_InterviewEvaluationId_CompetencyId" ON "AcceleratorSkillEvidence" ("InterviewEvaluationId", "CompetencyId") WHERE "InterviewEvaluationId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_InterviewSessionId" ON "AcceleratorSkillEvidence" ("InterviewSessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorSkillEvidence_ProjectEvaluationId_CompetencyId" ON "AcceleratorSkillEvidence" ("ProjectEvaluationId", "CompetencyId") WHERE "ProjectEvaluationId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_ProjectVersionId" ON "AcceleratorSkillEvidence" ("ProjectVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_SessionQuestionId" ON "AcceleratorSkillEvidence" ("SessionQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSkillEvidence_SubmissionId" ON "AcceleratorSkillEvidence" ("SubmissionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorAiDispatchRecords_CandidateProjectId" ON "AcceleratorAiDispatchRecords" ("CandidateProjectId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAiDispatchRecords_InterviewAnswerId" ON "AcceleratorAiDispatchRecords" ("InterviewAnswerId") WHERE "InterviewAnswerId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorAiDispatchRecords_InterviewSectionId" ON "AcceleratorAiDispatchRecords" ("InterviewSectionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAiDispatchRecords_InterviewSessionId_InterviewSe~" ON "AcceleratorAiDispatchRecords" ("InterviewSessionId", "InterviewSectionId", "GenerationSlot") WHERE "GenerationSlot" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorAiDispatchRecords_IsDeleted" ON "AcceleratorAiDispatchRecords" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAiDispatchRecords_ProjectSubmissionId" ON "AcceleratorAiDispatchRecords" ("ProjectSubmissionId") WHERE "ProjectSubmissionId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAiDispatchRecords_ReservationId" ON "AcceleratorAiDispatchRecords" ("ReservationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorAiDispatchRecords_Status_NextReconciliationAtUtc" ON "AcceleratorAiDispatchRecords" ("Status", "NextReconciliationAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorAiDispatchRecords_Status_ReconciliationClaimExpi~" ON "AcceleratorAiDispatchRecords" ("Status", "ReconciliationClaimExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorAiDispatchRecords_UserId_RequestKey" ON "AcceleratorAiDispatchRecords" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorCandidateProjects_CurrentMilestoneId" ON "AcceleratorCandidateProjects" ("CurrentMilestoneId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorCandidateProjects_GoalId" ON "AcceleratorCandidateProjects" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorCandidateProjects_IsDeleted" ON "AcceleratorCandidateProjects" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorCandidateProjects_ProjectVersionId" ON "AcceleratorCandidateProjects" ("ProjectVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorCandidateProjects_RequiredRevisionAssignmentId" ON "AcceleratorCandidateProjects" ("RequiredRevisionAssignmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorCandidateProjects_UserId_CreatedAtUtc" ON "AcceleratorCandidateProjects" ("UserId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorCandidateProjects_UserId_RequestKey" ON "AcceleratorCandidateProjects" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewAnswers_IsDeleted" ON "AcceleratorInterviewAnswers" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewAnswers_SessionId_RequestKey" ON "AcceleratorInterviewAnswers" ("SessionId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewAnswers_SessionQuestionId" ON "AcceleratorInterviewAnswers" ("SessionQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewCompetencies_CompetencyId" ON "AcceleratorInterviewCompetencies" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewCompetencies_InterviewVersionId_Compete~" ON "AcceleratorInterviewCompetencies" ("InterviewVersionId", "CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewCompetencies_IsDeleted" ON "AcceleratorInterviewCompetencies" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewCriterionScores_CriterionId" ON "AcceleratorInterviewCriterionScores" ("CriterionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewCriterionScores_EvaluationId_CriterionId" ON "AcceleratorInterviewCriterionScores" ("EvaluationId", "CriterionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewCriterionScores_IsDeleted" ON "AcceleratorInterviewCriterionScores" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewDefinitions_IsDeleted" ON "AcceleratorInterviewDefinitions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewDefinitions_StableKey" ON "AcceleratorInterviewDefinitions" ("StableKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewDefinitions_TargetRoleId_IsActive" ON "AcceleratorInterviewDefinitions" ("TargetRoleId", "IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewEvaluations_AnswerId" ON "AcceleratorInterviewEvaluations" ("AnswerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewEvaluations_IsDeleted" ON "AcceleratorInterviewEvaluations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewQuestions_CompetencyId" ON "AcceleratorInterviewQuestions" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewQuestions_IsDeleted" ON "AcceleratorInterviewQuestions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewQuestions_SectionId_Sequence" ON "AcceleratorInterviewQuestions" ("SectionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewRubricCriteria_CompetencyId" ON "AcceleratorInterviewRubricCriteria" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewRubricCriteria_InterviewVersionId_Seque~" ON "AcceleratorInterviewRubricCriteria" ("InterviewVersionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewRubricCriteria_IsDeleted" ON "AcceleratorInterviewRubricCriteria" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewSections_InterviewVersionId_Sequence" ON "AcceleratorInterviewSections" ("InterviewVersionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewSections_IsDeleted" ON "AcceleratorInterviewSections" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewSessions_GoalId" ON "AcceleratorInterviewSessions" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewSessions_InterviewVersionId" ON "AcceleratorInterviewSessions" ("InterviewVersionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewSessions_IsDeleted" ON "AcceleratorInterviewSessions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewSessions_UserId_RequestKey" ON "AcceleratorInterviewSessions" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewSessions_UserId_StartedAtUtc" ON "AcceleratorInterviewSessions" ("UserId", "StartedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorInterviewVersions_DefinitionId_Version" ON "AcceleratorInterviewVersions" ("DefinitionId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorInterviewVersions_IsDeleted" ON "AcceleratorInterviewVersions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPassportShares_GoalId" ON "AcceleratorPassportShares" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPassportShares_IsDeleted" ON "AcceleratorPassportShares" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPassportShares_PublicToken" ON "AcceleratorPassportShares" ("PublicToken");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPassportShares_UserId_GoalId" ON "AcceleratorPassportShares" ("UserId", "GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeAttempts_IsDeleted" ON "AcceleratorPracticeAttempts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeAttempts_QuestionId" ON "AcceleratorPracticeAttempts" ("QuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeAttempts_SessionId_QuestionId" ON "AcceleratorPracticeAttempts" ("SessionId", "QuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPracticeAttempts_UserId_RequestKey" ON "AcceleratorPracticeAttempts" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPracticeEvaluations_AttemptId" ON "AcceleratorPracticeEvaluations" ("AttemptId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeEvaluations_IsDeleted" ON "AcceleratorPracticeEvaluations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeQuestions_IsDeleted" ON "AcceleratorPracticeQuestions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPracticeQuestions_SessionId_Sequence" ON "AcceleratorPracticeQuestions" ("SessionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeSessions_GoalId" ON "AcceleratorPracticeSessions" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeSessions_IsDeleted" ON "AcceleratorPracticeSessions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeSessions_RoadmapNodeId" ON "AcceleratorPracticeSessions" ("RoadmapNodeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorPracticeSessions_UserId_CompetencyId_CreatedAtUtc" ON "AcceleratorPracticeSessions" ("UserId", "CompetencyId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorPracticeSessions_UserId_RequestKey" ON "AcceleratorPracticeSessions" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectAssignments_IsDeleted" ON "AcceleratorProjectAssignments" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectAssignments_MilestoneId_Sequence" ON "AcceleratorProjectAssignments" ("MilestoneId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectCompetencies_CompetencyId" ON "AcceleratorProjectCompetencies" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectCompetencies_IsDeleted" ON "AcceleratorProjectCompetencies" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectCompetencies_ProjectVersionId_CompetencyId" ON "AcceleratorProjectCompetencies" ("ProjectVersionId", "CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectDefinitions_IsDeleted" ON "AcceleratorProjectDefinitions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectDefinitions_StableKey" ON "AcceleratorProjectDefinitions" ("StableKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectEvaluations_IsDeleted" ON "AcceleratorProjectEvaluations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectEvaluations_SubmissionId" ON "AcceleratorProjectEvaluations" ("SubmissionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectEvaluationScores_CriterionId" ON "AcceleratorProjectEvaluationScores" ("CriterionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectEvaluationScores_EvaluationId_CriterionId" ON "AcceleratorProjectEvaluationScores" ("EvaluationId", "CriterionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectEvaluationScores_IsDeleted" ON "AcceleratorProjectEvaluationScores" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectMilestones_IsDeleted" ON "AcceleratorProjectMilestones" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectMilestones_ProjectVersionId_Sequence" ON "AcceleratorProjectMilestones" ("ProjectVersionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectRubricCriteria_CompetencyId" ON "AcceleratorProjectRubricCriteria" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectRubricCriteria_IsDeleted" ON "AcceleratorProjectRubricCriteria" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectRubricCriteria_RubricId_Sequence" ON "AcceleratorProjectRubricCriteria" ("RubricId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectRubrics_IsDeleted" ON "AcceleratorProjectRubrics" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectRubrics_ProjectVersionId_Version" ON "AcceleratorProjectRubrics" ("ProjectVersionId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectSubmissions_AssignmentId" ON "AcceleratorProjectSubmissions" ("AssignmentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectSubmissions_CandidateProjectId_Assignment~" ON "AcceleratorProjectSubmissions" ("CandidateProjectId", "AssignmentId", "AttemptNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectSubmissions_CandidateProjectId_RequestKey" ON "AcceleratorProjectSubmissions" ("CandidateProjectId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectSubmissions_IsDeleted" ON "AcceleratorProjectSubmissions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorProjectVersions_DefinitionId_Version" ON "AcceleratorProjectVersions" ("DefinitionId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectVersions_IsDeleted" ON "AcceleratorProjectVersions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorProjectVersions_TargetRoleId_IsActive_Difficulty" ON "AcceleratorProjectVersions" ("TargetRoleId", "IsActive", "Difficulty");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorReadinessSnapshots_GoalId" ON "AcceleratorReadinessSnapshots" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorReadinessSnapshots_IsDeleted" ON "AcceleratorReadinessSnapshots" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorReadinessSnapshots_TriggerInterviewSessionId" ON "AcceleratorReadinessSnapshots" ("TriggerInterviewSessionId") WHERE "TriggerInterviewSessionId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorReadinessSnapshots_UserId_GoalId_CalculatedAtUtc" ON "AcceleratorReadinessSnapshots" ("UserId", "GoalId", "CalculatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSessionQuestions_CompetencyId" ON "AcceleratorSessionQuestions" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorSessionQuestions_GenerationDispatchId" ON "AcceleratorSessionQuestions" ("GenerationDispatchId") WHERE "GenerationDispatchId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSessionQuestions_IsDeleted" ON "AcceleratorSessionQuestions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSessionQuestions_ParentQuestionId" ON "AcceleratorSessionQuestions" ("ParentQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSessionQuestions_SectionId" ON "AcceleratorSessionQuestions" ("SectionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorSessionQuestions_SessionId_GenerationRequestKey" ON "AcceleratorSessionQuestions" ("SessionId", "GenerationRequestKey") WHERE "GenerationRequestKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorSessionQuestions_SessionId_SectionId_Sequence" ON "AcceleratorSessionQuestions" ("SessionId", "SectionId", "Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSessionQuestions_SessionId_SourceQuestionId_Pare~" ON "AcceleratorSessionQuestions" ("SessionId", "SourceQuestionId", "ParentQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorSessionQuestions_SourceQuestionId" ON "AcceleratorSessionQuestions" ("SourceQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorContextSnapshots_ConversationId_CreatedAtUtc" ON "AcceleratorTutorContextSnapshots" ("ConversationId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorContextSnapshots_IsDeleted" ON "AcceleratorTutorContextSnapshots" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorConversations_CompetencyId" ON "AcceleratorTutorConversations" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorConversations_GoalId" ON "AcceleratorTutorConversations" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorConversations_IsDeleted" ON "AcceleratorTutorConversations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorConversations_RoadmapNodeId" ON "AcceleratorTutorConversations" ("RoadmapNodeId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorConversations_UserId_CreatedAtUtc" ON "AcceleratorTutorConversations" ("UserId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorConversations_UserId_GoalId_Status" ON "AcceleratorTutorConversations" ("UserId", "GoalId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorTutorConversations_UserId_RequestKey" ON "AcceleratorTutorConversations" ("UserId", "RequestKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorTutorMemorySummaries_ConversationId_Version" ON "AcceleratorTutorMemorySummaries" ("ConversationId", "Version");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorMemorySummaries_IsDeleted" ON "AcceleratorTutorMemorySummaries" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorMessages_ConversationId_CreatedAtUtc" ON "AcceleratorTutorMessages" ("ConversationId", "CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_AcceleratorTutorMessages_DispatchId" ON "AcceleratorTutorMessages" ("DispatchId") WHERE "DispatchId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_AcceleratorTutorMessages_IsDeleted" ON "AcceleratorTutorMessages" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_CandidateMailboxConnections_CandidateId_Provider_ProviderAc~" ON "CandidateMailboxConnections" ("CandidateId", "Provider", "ProviderAccountIdentifier");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_CandidateMailboxConnections_IsDeleted" ON "CandidateMailboxConnections" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_EmployerContacts_CompanyId_IsSuppressed_Verification" ON "EmployerContacts" ("CompanyId", "IsSuppressed", "Verification");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_EmployerContacts_CompanyId_NormalizedEmail" ON "EmployerContacts" ("CompanyId", "NormalizedEmail") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_EmployerContacts_IsDeleted" ON "EmployerContacts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketDiscoveryObservations_CompanyId" ON "MarketDiscoveryObservations" ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketDiscoveryObservations_IsDeleted" ON "MarketDiscoveryObservations" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketDiscoveryObservations_JobId_RoleDiscoveryId" ON "MarketDiscoveryObservations" ("JobId", "RoleDiscoveryId") WHERE "RoleDiscoveryId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketDiscoveryObservations_JobId_SkillDiscoveryId" ON "MarketDiscoveryObservations" ("JobId", "SkillDiscoveryId") WHERE "SkillDiscoveryId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketDiscoveryObservations_RoleDiscoveryId_CompanyId" ON "MarketDiscoveryObservations" ("RoleDiscoveryId", "CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketDiscoveryObservations_SkillDiscoveryId_CompanyId" ON "MarketDiscoveryObservations" ("SkillDiscoveryId", "CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketJobProcessingStates_IsDeleted" ON "MarketJobProcessingStates" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketJobProcessingStates_JobId" ON "MarketJobProcessingStates" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketJobRoles_IsDeleted" ON "MarketJobRoles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketJobRoles_JobId" ON "MarketJobRoles" ("JobId") WHERE "IsPrimary" = TRUE AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketJobRoles_JobId_MarketRoleId" ON "MarketJobRoles" ("JobId", "MarketRoleId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketJobRoles_MarketRoleId_JobId" ON "MarketJobRoles" ("MarketRoleId", "JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketJobSkills_IsDeleted" ON "MarketJobSkills" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketJobSkills_JobId_MarketSkillId" ON "MarketJobSkills" ("JobId", "MarketSkillId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketJobSkills_MarketSkillId_JobId" ON "MarketJobSkills" ("MarketSkillId", "JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketProcessingCheckpoints_IsDeleted" ON "MarketProcessingCheckpoints" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketProcessingCheckpoints_LeaseExpiresAtUtc" ON "MarketProcessingCheckpoints" ("LeaseExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketProcessingCheckpoints_Name" ON "MarketProcessingCheckpoints" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoleAliases_IsDeleted" ON "MarketRoleAliases" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoleAliases_MarketRoleId" ON "MarketRoleAliases" ("MarketRoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketRoleAliases_NormalizedText" ON "MarketRoleAliases" ("NormalizedText") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoleDiscoveries_IsDeleted" ON "MarketRoleDiscoveries" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketRoleDiscoveries_NormalizedTitle" ON "MarketRoleDiscoveries" ("NormalizedTitle");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoleDiscoveries_ResolvedMarketRoleId" ON "MarketRoleDiscoveries" ("ResolvedMarketRoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoleDiscoveries_Status_LastSeenAtUtc" ON "MarketRoleDiscoveries" ("Status", "LastSeenAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoles_AcceleratorRoleId" ON "MarketRoles" ("AcceleratorRoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoles_IsDeleted" ON "MarketRoles" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoles_MergedIntoId" ON "MarketRoles" ("MergedIntoId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketRoles_NormalizedName" ON "MarketRoles" ("NormalizedName") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketRoles_Slug" ON "MarketRoles" ("Slug") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketRoles_Status_Family" ON "MarketRoles" ("Status", "Family");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkillAliases_IsDeleted" ON "MarketSkillAliases" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkillAliases_MarketSkillId" ON "MarketSkillAliases" ("MarketSkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketSkillAliases_NormalizedText" ON "MarketSkillAliases" ("NormalizedText") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkillDiscoveries_IsDeleted" ON "MarketSkillDiscoveries" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketSkillDiscoveries_NormalizedTerm" ON "MarketSkillDiscoveries" ("NormalizedTerm");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkillDiscoveries_ResolvedMarketSkillId" ON "MarketSkillDiscoveries" ("ResolvedMarketSkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkillDiscoveries_Status_LastSeenAtUtc" ON "MarketSkillDiscoveries" ("Status", "LastSeenAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkillDiscoveries_SuggestedSkillId" ON "MarketSkillDiscoveries" ("SuggestedSkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkills_CompetencyId" ON "MarketSkills" ("CompetencyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkills_IsDeleted" ON "MarketSkills" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkills_MergedIntoId" ON "MarketSkills" ("MergedIntoId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketSkills_SkillId" ON "MarketSkills" ("SkillId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_MarketSkills_Slug" ON "MarketSkills" ("Slug") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_MarketSkills_Status_Category" ON "MarketSkills" ("Status", "Category");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_CandidateId_JobId_Status" ON "TalentOutreachDrafts" ("CandidateId", "JobId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_TalentOutreachDrafts_CandidateId_SendIdempotencyKey" ON "TalentOutreachDrafts" ("CandidateId", "SendIdempotencyKey") WHERE "SendIdempotencyKey" <> '' AND "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_CompanyId" ON "TalentOutreachDrafts" ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_IsDeleted" ON "TalentOutreachDrafts" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_JobId" ON "TalentOutreachDrafts" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_MailboxConnectionId" ON "TalentOutreachDrafts" ("MailboxConnectionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_ShareLinkId" ON "TalentOutreachDrafts" ("ShareLinkId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachDrafts_UsageReservationId" ON "TalentOutreachDrafts" ("UsageReservationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachRecipients_ContactId" ON "TalentOutreachRecipients" ("ContactId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_TalentOutreachRecipients_DraftId_ContactId" ON "TalentOutreachRecipients" ("DraftId", "ContactId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentOutreachRecipients_IsDeleted" ON "TalentOutreachRecipients" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentShareLinks_CandidateId_ExpiresAtUtc" ON "TalentShareLinks" ("CandidateId", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentShareLinks_CompanyId" ON "TalentShareLinks" ("CompanyId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentShareLinks_GoalId" ON "TalentShareLinks" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentShareLinks_IsDeleted" ON "TalentShareLinks" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentShareLinks_JobId" ON "TalentShareLinks" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_TalentShareLinks_TokenHash" ON "TalentShareLinks" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissionEvents_ActorId" ON "TalentSubmissionEvents" ("ActorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissionEvents_IsDeleted" ON "TalentSubmissionEvents" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissionEvents_SubmissionId_OccurredAtUtc" ON "TalentSubmissionEvents" ("SubmissionId", "OccurredAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE UNIQUE INDEX "IX_TalentSubmissions_CandidateId_JobId" ON "TalentSubmissions" ("CandidateId", "JobId") WHERE "IsDeleted" = FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissions_CompanyId_Status_SubmittedAtUtc" ON "TalentSubmissions" ("CompanyId", "Status", "SubmittedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissions_GoalId" ON "TalentSubmissions" ("GoalId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissions_IsDeleted" ON "TalentSubmissions" ("IsDeleted");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissions_JobId" ON "TalentSubmissions" ("JobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    CREATE INDEX "IX_TalentSubmissions_ShareLinkId" ON "TalentSubmissions" ("ShareLinkId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorCandidateProjects_Candi~" FOREIGN KEY ("CandidateProjectId") REFERENCES "AcceleratorCandidateProjects" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorInterviewEvaluations_In~" FOREIGN KEY ("InterviewEvaluationId") REFERENCES "AcceleratorInterviewEvaluations" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorInterviewSessions_Inter~" FOREIGN KEY ("InterviewSessionId") REFERENCES "AcceleratorInterviewSessions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorProjectAssignments_Assi~" FOREIGN KEY ("AssignmentId") REFERENCES "AcceleratorProjectAssignments" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorProjectEvaluations_Proj~" FOREIGN KEY ("ProjectEvaluationId") REFERENCES "AcceleratorProjectEvaluations" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorProjectSubmissions_Subm~" FOREIGN KEY ("SubmissionId") REFERENCES "AcceleratorProjectSubmissions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorProjectVersions_Project~" FOREIGN KEY ("ProjectVersionId") REFERENCES "AcceleratorProjectVersions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorSkillEvidence" ADD CONSTRAINT "FK_AcceleratorSkillEvidence_AcceleratorSessionQuestions_Sessio~" FOREIGN KEY ("SessionQuestionId") REFERENCES "AcceleratorSessionQuestions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    ALTER TABLE "AcceleratorAiDispatchRecords" ADD CONSTRAINT "FK_AcceleratorAiDispatchRecords_AcceleratorInterviewAnswers_In~" FOREIGN KEY ("InterviewAnswerId") REFERENCES "AcceleratorInterviewAnswers" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261115175122_AddCareerAcceleratorRemainingPhases') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261115175122_AddCareerAcceleratorRemainingPhases', '9.0.8');
    END IF;
END $EF$;
COMMIT;

