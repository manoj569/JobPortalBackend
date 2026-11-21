-- Read-only assertions for explicitly disposable Docker verification databases.
DO $$
DECLARE db text := current_database();
BEGIN
    IF db NOT IN ('accelerator_phase3_verify','accelerator_phase3_invalid_verify','accelerator_phase3_legacy_rollback_verify','accelerator_phase3_guard_verify')
    THEN RAISE EXCEPTION 'Refusing verification outside the named disposable databases.'; END IF;

    IF NOT EXISTS (SELECT 1 FROM "AcceleratorPracticeEvaluations" WHERE "Id"='3a000000-0000-0000-0000-000000000008' AND "Score"=1 AND "Explanation"='Historical evaluation')
        OR NOT EXISTS (SELECT 1 FROM "AcceleratorPracticeQuestions" WHERE "Id"='3a000000-0000-0000-0000-000000000005' AND "SessionId"='3a000000-0000-0000-0000-000000000003')
    THEN RAISE EXCEPTION 'Historical question/evaluation data was not preserved.'; END IF;
    IF NOT EXISTS (SELECT 1 FROM "AcceleratorTutorConversations" WHERE "Id"='3a000000-0000-0000-0000-000000000001' AND "GoalId"='2a000000-0000-0000-0000-000000000006')
        OR NOT EXISTS (SELECT 1 FROM "AcceleratorPracticeSessions" WHERE "Id"='3a000000-0000-0000-0000-000000000003' AND "GoalId"='2a000000-0000-0000-0000-000000000006')
    THEN RAISE EXCEPTION 'Legacy GoalId was not preserved.'; END IF;

    IF db IN ('accelerator_phase3_invalid_verify','accelerator_phase3_legacy_rollback_verify') THEN
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='AcceleratorTutorConversations' AND column_name='JourneyContextId')
            OR EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" IN ('20261119073819_AddEmployerHiringPipeline','20261121051344_AddCareerAcceleratorPhase3JourneyContext'))
        THEN RAISE EXCEPTION 'Failed upgrade or successful rollback left unexpected Phase 3 schema/history.'; END IF;
    ELSE
        IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId"='20261121051344_AddCareerAcceleratorPhase3JourneyContext')
        THEN RAISE EXCEPTION 'Phase 3 migration history missing.'; END IF;
        IF EXISTS (
            SELECT 1 FROM (
                SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorTutorConversations"
                UNION ALL SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorPracticeSessions"
            ) r LEFT JOIN "AcceleratorJourneyContexts" c ON c."Id"=r."JourneyContextId"
            WHERE c."Id" IS NULL OR c."UserId" IS DISTINCT FROM r."UserId" OR c."CareerGoalId" IS DISTINCT FROM r."GoalId"
        ) THEN RAISE EXCEPTION 'Orphaned or foreign journey attribution.'; END IF;
        IF NOT EXISTS (
            SELECT 1 FROM "AcceleratorPracticeSessions" s JOIN "AcceleratorPracticeAttempts" a ON a."SessionId"=s."Id"
            JOIN "AcceleratorPracticeEvaluations" e ON e."AttemptId"=a."Id"
            WHERE s."GoalId" IS NULL AND s."JourneyContextId" IS NOT NULL AND a."UserId"=s."UserId"
        ) THEN RAISE EXCEPTION 'Generic evaluated practice data missing after migration/rollback refusal.'; END IF;
    END IF;
END $$;
SELECT current_database() AS database,
    (SELECT count(*) FROM "AcceleratorTutorConversations") AS conversations,
    (SELECT count(*) FROM "AcceleratorPracticeSessions") AS practice_sessions,
    (SELECT count(*) FROM "AcceleratorPracticeAttempts") AS attempts,
    (SELECT count(*) FROM "AcceleratorPracticeEvaluations") AS evaluations;
