namespace JobPortal.Persistence.Postgres.Migrations;

// Shared by the migration which first introduced the columns and the dedicated Phase 3 validation migration.
// This never invents goals or ownership, and fails the transaction when historical ownership is ambiguous.
internal static class Phase3JourneyBackfillSql
{
    internal const string Up = """
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM (
                    SELECT "UserId", "GoalId" FROM "AcceleratorTutorConversations"
                    UNION ALL SELECT "UserId", "GoalId" FROM "AcceleratorPracticeSessions"
                ) r LEFT JOIN "AcceleratorCareerGoals" g ON g."Id" = r."GoalId"
                WHERE r."GoalId" IS NOT NULL AND (g."Id" IS NULL OR g."UserId" IS DISTINCT FROM r."UserId")
            ) THEN RAISE EXCEPTION 'Phase 3 backfill blocked: missing goal or candidate ownership mismatch; explicit historical resolution required.';
            END IF;
            IF EXISTS (
                SELECT 1 FROM "AcceleratorJourneyContexts" c JOIN "AcceleratorCareerGoals" g ON g."Id" = c."CareerGoalId"
                WHERE NOT c."IsDeleted" AND (c."UserId" IS DISTINCT FROM g."UserId" OR c."TaxonomyId" IS DISTINCT FROM g."TaxonomyId")
                AND (EXISTS (SELECT 1 FROM "AcceleratorTutorConversations" r WHERE r."GoalId" = g."Id")
                    OR EXISTS (SELECT 1 FROM "AcceleratorPracticeSessions" r WHERE r."GoalId" = g."Id"))
            ) THEN RAISE EXCEPTION 'Phase 3 backfill blocked: existing journey ownership or taxonomy mismatch.';
            END IF;
            IF EXISTS (
                SELECT 1 FROM "AcceleratorCareerGoals" g JOIN "AcceleratorJourneyContexts" occupied ON occupied."Id" = g."Id"
                WHERE NOT EXISTS (SELECT 1 FROM "AcceleratorJourneyContexts" c WHERE c."CareerGoalId" = g."Id" AND NOT c."IsDeleted")
                AND (EXISTS (SELECT 1 FROM "AcceleratorTutorConversations" r WHERE r."GoalId" = g."Id")
                    OR EXISTS (SELECT 1 FROM "AcceleratorPracticeSessions" r WHERE r."GoalId" = g."Id"))
            ) THEN RAISE EXCEPTION 'Phase 3 backfill blocked: retired or conflicting journey identity requires explicit resolution.';
            END IF;
        END $$;

        INSERT INTO "AcceleratorJourneyContexts"
            ("Id", "UserId", "ScopeType", "CareerGoalId", "LearningPathEnrollmentId", "TaxonomyId", "Revision", "CreatedAtUtc", "IsDeleted")
        SELECT g."Id", g."UserId", 1, g."Id", NULL, g."TaxonomyId", g."Id", g."CreatedAtUtc", false
        FROM "AcceleratorCareerGoals" g
        WHERE NOT EXISTS (SELECT 1 FROM "AcceleratorJourneyContexts" c WHERE c."CareerGoalId" = g."Id" AND NOT c."IsDeleted")
          AND (EXISTS (SELECT 1 FROM "AcceleratorTutorConversations" r WHERE r."GoalId" = g."Id")
            OR EXISTS (SELECT 1 FROM "AcceleratorPracticeSessions" r WHERE r."GoalId" = g."Id"));

        UPDATE "AcceleratorTutorConversations" r SET "JourneyContextId" = c."Id"
        FROM "AcceleratorJourneyContexts" c WHERE r."JourneyContextId" IS NULL AND r."GoalId" = c."CareerGoalId"
            AND r."UserId" = c."UserId" AND NOT c."IsDeleted";
        UPDATE "AcceleratorPracticeSessions" r SET "JourneyContextId" = c."Id"
        FROM "AcceleratorJourneyContexts" c WHERE r."JourneyContextId" IS NULL AND r."GoalId" = c."CareerGoalId"
            AND r."UserId" = c."UserId" AND NOT c."IsDeleted";

        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM (
                    SELECT "UserId", "GoalId", "JourneyContextId" FROM "AcceleratorTutorConversations"
                    UNION ALL SELECT "UserId", "GoalId", "JourneyContextId" FROM "AcceleratorPracticeSessions"
                ) r LEFT JOIN "AcceleratorJourneyContexts" c ON c."Id" = r."JourneyContextId"
                WHERE c."Id" IS NULL OR c."UserId" IS DISTINCT FROM r."UserId" OR c."CareerGoalId" IS DISTINCT FROM r."GoalId"
            ) THEN RAISE EXCEPTION 'Phase 3 backfill blocked: unresolved journey attribution; no fallback ownership was assigned.';
            END IF;
        END $$;
        """;

    internal const string AssertLegacyOnly = """
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM "AcceleratorTutorConversations" WHERE "GoalId" IS NULL OR "ModuleId" IS NOT NULL OR "LessonId" IS NOT NULL)
                OR EXISTS (SELECT 1 FROM "AcceleratorPracticeSessions" WHERE "GoalId" IS NULL OR "ModuleId" IS NOT NULL OR "LessonId" IS NOT NULL)
            THEN RAISE EXCEPTION 'Phase 3 rollback blocked: generic enrollment or learning-content attribution exists; no records were deleted or assigned fake goals.';
            END IF;
        END $$;
        """;
}
