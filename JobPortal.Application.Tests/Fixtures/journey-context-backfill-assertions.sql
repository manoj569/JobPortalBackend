-- Disposable Docker verification database only; no writes.
DO $$
DECLARE source_goal uuid := '2a000000-0000-0000-0000-000000000006';
        relation_name text; bad_count bigint;
BEGIN
  IF (SELECT count(*) FROM "AcceleratorJourneyContexts" WHERE "CareerGoalId"=source_goal AND NOT "IsDeleted") <> 1 THEN
    RAISE EXCEPTION 'Expected exactly one historical CareerGoal context';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "AcceleratorJourneyContexts" c JOIN "AcceleratorCareerGoals" g ON c."CareerGoalId"=g."Id"
      WHERE g."Id"=source_goal AND c."UserId"=g."UserId" AND c."TaxonomyId"=g."TaxonomyId" AND c."ScopeType"=1 AND c."LearningPathEnrollmentId" IS NULL) THEN
    RAISE EXCEPTION 'Historical context owner/taxonomy/scope mismatch';
  END IF;
  FOREACH relation_name IN ARRAY ARRAY['AcceleratorAssessmentAttempts','AcceleratorSkillEvidence','AcceleratorRoadmaps','AcceleratorRecommendations'] LOOP
    EXECUTE format('SELECT count(*) FROM %I a LEFT JOIN "AcceleratorJourneyContexts" c ON a."JourneyContextId"=c."Id" WHERE c."Id" IS NULL OR a."UserId"<>c."UserId" OR (a."GoalId" IS NOT NULL AND a."GoalId" IS DISTINCT FROM c."CareerGoalId")',relation_name) INTO bad_count;
    IF bad_count <> 0 THEN RAISE EXCEPTION 'Orphan/context mismatch in %: %',relation_name,bad_count; END IF;
    EXECUTE format('SELECT count(*) FROM %I WHERE "GoalId"=$1 AND "JourneyContextId"=$1',relation_name) INTO bad_count USING source_goal;
    IF bad_count <> 1 THEN RAISE EXCEPTION 'Historical GoalId/backfill mismatch in %',relation_name; END IF;
  END LOOP;
  IF EXISTS (SELECT "CareerGoalId" FROM "AcceleratorJourneyContexts" WHERE "CareerGoalId" IS NOT NULL AND NOT "IsDeleted" GROUP BY "CareerGoalId" HAVING count(*)>1) THEN
    RAISE EXCEPTION 'Duplicate active CareerGoal context';
  END IF;
  IF (SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId"='20261117154417_AddCareerAcceleratorJourneyContexts')<>1 THEN
    RAISE EXCEPTION 'Migration 2A history must occur once';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"='2a000000-0000-0000-0000-000000000001' AND "FirstName"='Journey' AND "Email"='journey-verify@example.invalid') THEN
    RAISE EXCEPTION 'Unrelated candidate fixture data changed';
  END IF;
END $$;
