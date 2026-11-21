namespace JobPortal.Persistence.Postgres.Migrations;

internal static class Phase45JourneyBackfillSql
{
    internal const string Up = """
        DO $$ BEGIN
            IF EXISTS (
                SELECT 1 FROM (
                    SELECT "UserId","GoalId" FROM "AcceleratorCandidateProjects"
                    UNION ALL SELECT "UserId","GoalId" FROM "AcceleratorInterviewSessions"
                    UNION ALL SELECT "UserId","GoalId" FROM "AcceleratorReadinessSnapshots"
                ) r LEFT JOIN "AcceleratorCareerGoals" g ON g."Id"=r."GoalId"
                WHERE r."GoalId" IS NOT NULL AND (g."Id" IS NULL OR g."UserId" IS DISTINCT FROM r."UserId")
            ) THEN RAISE EXCEPTION 'Phase 4/5 backfill blocked: missing goal or foreign historical ownership.'; END IF;
        END $$;
        INSERT INTO "AcceleratorJourneyContexts" ("Id","UserId","ScopeType","CareerGoalId","LearningPathEnrollmentId","TaxonomyId","Revision","CreatedAtUtc","IsDeleted")
        SELECT g."Id",g."UserId",1,g."Id",NULL,g."TaxonomyId",g."Id",g."CreatedAtUtc",false FROM "AcceleratorCareerGoals" g
        WHERE NOT EXISTS (SELECT 1 FROM "AcceleratorJourneyContexts" c WHERE c."CareerGoalId"=g."Id" AND NOT c."IsDeleted") AND
            (EXISTS (SELECT 1 FROM "AcceleratorCandidateProjects" r WHERE r."GoalId"=g."Id") OR
             EXISTS (SELECT 1 FROM "AcceleratorInterviewSessions" r WHERE r."GoalId"=g."Id") OR
             EXISTS (SELECT 1 FROM "AcceleratorReadinessSnapshots" r WHERE r."GoalId"=g."Id"));
        UPDATE "AcceleratorCandidateProjects" r SET "JourneyContextId"=c."Id" FROM "AcceleratorJourneyContexts" c
            WHERE r."JourneyContextId" IS NULL AND r."GoalId"=c."CareerGoalId" AND r."UserId"=c."UserId" AND NOT c."IsDeleted";
        UPDATE "AcceleratorInterviewSessions" r SET "JourneyContextId"=c."Id" FROM "AcceleratorJourneyContexts" c
            WHERE r."JourneyContextId" IS NULL AND r."GoalId"=c."CareerGoalId" AND r."UserId"=c."UserId" AND NOT c."IsDeleted";
        UPDATE "AcceleratorReadinessSnapshots" r SET "JourneyContextId"=c."Id" FROM "AcceleratorJourneyContexts" c
            WHERE r."JourneyContextId" IS NULL AND r."GoalId"=c."CareerGoalId" AND r."UserId"=c."UserId" AND NOT c."IsDeleted";
        UPDATE "AcceleratorSkillEvidence" e SET "JourneyContextId"=p."JourneyContextId" FROM "AcceleratorCandidateProjects" p
            WHERE e."CandidateProjectId"=p."Id" AND e."UserId"=p."UserId" AND e."GoalId" IS NOT DISTINCT FROM p."GoalId" AND e."JourneyContextId" IS NULL;
        UPDATE "AcceleratorSkillEvidence" e SET "JourneyContextId"=s."JourneyContextId" FROM "AcceleratorInterviewSessions" s
            WHERE e."InterviewSessionId"=s."Id" AND e."UserId"=s."UserId" AND e."GoalId" IS NOT DISTINCT FROM s."GoalId" AND e."JourneyContextId" IS NULL;
        DO $$ BEGIN
            IF EXISTS (
                SELECT 1 FROM (
                    SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorCandidateProjects"
                    UNION ALL SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorInterviewSessions"
                    UNION ALL SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorReadinessSnapshots"
                ) r LEFT JOIN "AcceleratorJourneyContexts" c ON c."Id"=r."JourneyContextId"
                LEFT JOIN "AcceleratorCareerGoals" g ON g."Id"=c."CareerGoalId"
                WHERE c."Id" IS NULL OR c."UserId" IS DISTINCT FROM r."UserId" OR c."CareerGoalId" IS DISTINCT FROM r."GoalId"
                    OR (g."Id" IS NOT NULL AND c."TaxonomyId" IS DISTINCT FROM g."TaxonomyId")
            ) OR EXISTS (
                SELECT 1 FROM "AcceleratorSkillEvidence" e JOIN "AcceleratorCandidateProjects" p ON p."Id"=e."CandidateProjectId"
                WHERE e."UserId" IS DISTINCT FROM p."UserId" OR e."GoalId" IS DISTINCT FROM p."GoalId" OR e."JourneyContextId" IS DISTINCT FROM p."JourneyContextId"
            ) OR EXISTS (
                SELECT 1 FROM "AcceleratorSkillEvidence" e JOIN "AcceleratorInterviewSessions" s ON s."Id"=e."InterviewSessionId"
                WHERE e."UserId" IS DISTINCT FROM s."UserId" OR e."GoalId" IS DISTINCT FROM s."GoalId" OR e."JourneyContextId" IS DISTINCT FROM s."JourneyContextId"
            ) OR EXISTS (
                SELECT 1 FROM "AcceleratorReadinessSnapshots" r JOIN "AcceleratorInterviewSessions" s ON s."Id"=r."TriggerInterviewSessionId"
                WHERE r."UserId" IS DISTINCT FROM s."UserId" OR r."GoalId" IS DISTINCT FROM s."GoalId" OR r."JourneyContextId" IS DISTINCT FROM s."JourneyContextId"
            ) THEN RAISE EXCEPTION 'Phase 4/5 backfill blocked: unresolved context or evidence ownership; no fallback ownership assigned.'; END IF;
        END $$;
        """;
    internal const string Down = """
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM "AcceleratorCandidateProjects" WHERE "GoalId" IS NULL)
                OR EXISTS (SELECT 1 FROM "AcceleratorInterviewSessions" WHERE "GoalId" IS NULL)
                OR EXISTS (SELECT 1 FROM "AcceleratorReadinessSnapshots" WHERE "GoalId" IS NULL)
            THEN RAISE EXCEPTION 'Phase 4/5 rollback blocked: enrollment-owned data exists; no fake goals or destructive fallback permitted.'; END IF;
        END $$;
        """;
}
