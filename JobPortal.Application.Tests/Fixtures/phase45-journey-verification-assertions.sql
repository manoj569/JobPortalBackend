-- Read-only assertions on named disposable databases only.
DO $$
DECLARE db text := current_database();
BEGIN
    IF db NOT IN ('accelerator_phase45_verify','accelerator_phase45_invalid_verify','accelerator_phase45_legacy_rollback_verify','accelerator_phase45_guard_verify')
    THEN RAISE EXCEPTION 'Refusing verification outside explicitly disposable Phase 4/5 databases.'; END IF;
    IF NOT EXISTS (SELECT 1 FROM "AcceleratorCandidateProjects" WHERE "Id"='45000000-0000-0000-0000-000000000003' AND "GoalId"='2a000000-0000-0000-0000-000000000006' AND "ProgressPercent"=0)
        OR NOT EXISTS (SELECT 1 FROM "AcceleratorInterviewSessions" WHERE "Id"='45000000-0000-0000-0000-000000000006' AND "GoalId"='2a000000-0000-0000-0000-000000000006' AND "OverallScore"=80)
        OR NOT EXISTS (SELECT 1 FROM "AcceleratorReadinessSnapshots" WHERE "Id"='45000000-0000-0000-0000-000000000007' AND "GoalId"='2a000000-0000-0000-0000-000000000006' AND "Total"=56 AND "Explanation"='Historical readiness preserved')
    THEN RAISE EXCEPTION 'Historical values or GoalIds changed.'; END IF;
    IF db IN ('accelerator_phase45_invalid_verify','accelerator_phase45_legacy_rollback_verify') THEN
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='AcceleratorCandidateProjects' AND column_name='JourneyContextId')
            OR EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId"='20261121062437_AddCareerAcceleratorProjectsInterviewsJourneyContext')
        THEN RAISE EXCEPTION 'Failed upgrade or successful rollback left unexpected schema/history.'; END IF;
    ELSE
        IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId"='20261121062437_AddCareerAcceleratorProjectsInterviewsJourneyContext')
        THEN RAISE EXCEPTION 'Migration history missing.'; END IF;
        IF EXISTS (
            SELECT 1 FROM (
                SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorCandidateProjects"
                UNION ALL SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorInterviewSessions"
                UNION ALL SELECT "UserId","GoalId","JourneyContextId" FROM "AcceleratorReadinessSnapshots"
            ) r LEFT JOIN "AcceleratorJourneyContexts" c ON c."Id"=r."JourneyContextId"
            WHERE c."Id" IS NULL OR c."UserId" IS DISTINCT FROM r."UserId" OR c."CareerGoalId" IS DISTINCT FROM r."GoalId"
        ) THEN RAISE EXCEPTION 'Orphan or foreign journey attribution.'; END IF;
        IF db='accelerator_phase45_guard_verify' AND
            (NOT EXISTS (SELECT 1 FROM "AcceleratorCandidateProjects" WHERE "GoalId" IS NULL AND "Status"=5)
            OR NOT EXISTS (SELECT 1 FROM "AcceleratorInterviewSessions" WHERE "GoalId" IS NULL AND "Status"=5)
            OR NOT EXISTS (SELECT 1 FROM "AcceleratorReadinessSnapshots" WHERE "GoalId" IS NULL))
        THEN RAISE EXCEPTION 'Generic evaluated records lost during rejected rollback.'; END IF;
    END IF;
END $$;
SELECT current_database() AS database,
    (SELECT count(*) FROM "AcceleratorCandidateProjects") AS projects,
    (SELECT count(*) FROM "AcceleratorInterviewSessions") AS interviews,
    (SELECT count(*) FROM "AcceleratorReadinessSnapshots") AS readiness_snapshots;
