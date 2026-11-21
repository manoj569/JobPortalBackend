-- Execute only on the disposable Phase 3 clone, before Phase 4/5 migration.
BEGIN;
INSERT INTO "AcceleratorProjectDefinitions" ("Id","StableKey","IsActive","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000001','legacy-journey-project',true,now(),false);
INSERT INTO "AcceleratorProjectVersions" ("Id","DefinitionId","Version","TargetRoleId","Type","Difficulty","EstimatedMinutes","Title","ShortDescription","BusinessContext","Instructions","IsActive","CatalogStatus","Revision","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000002','45000000-0000-0000-0000-000000000001',1,'2a000000-0000-0000-0000-000000000004',1,1,60,'Legacy project','Configured historical project','Historical context','Historical instructions',true,2,gen_random_uuid(),now(),false);
INSERT INTO "AcceleratorCandidateProjects" ("Id","UserId","GoalId","ProjectVersionId","RequestKey","RequestHash","Status","ProgressPercent","StartedAtUtc","Revision","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000003','2a000000-0000-0000-0000-000000000001','2a000000-0000-0000-0000-000000000006','45000000-0000-0000-0000-000000000002',gen_random_uuid(),repeat('A',64),2,0,now(),gen_random_uuid(),now(),false);
INSERT INTO "AcceleratorInterviewDefinitions" ("Id","StableKey","Name","Description","TargetRoleId","IsActive","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000004','legacy-journey-interview','Legacy interview','Historical interview','2a000000-0000-0000-0000-000000000004',true,now(),false);
INSERT INTO "AcceleratorInterviewVersions" ("Id","DefinitionId","Version","Type","Difficulty","EstimatedMinutes","TargetExperienceLevel","Instructions","Status","Revision","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000005','45000000-0000-0000-0000-000000000004',1,1,1,20,'Entry','Historical instructions',2,gen_random_uuid(),now(),false);
INSERT INTO "AcceleratorInterviewSessions" ("Id","UserId","GoalId","InterviewVersionId","RequestKey","RequestHash","Status","StartedAtUtc","OverallScore","CompletedAtUtc","Revision","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000006','2a000000-0000-0000-0000-000000000001','2a000000-0000-0000-0000-000000000006','45000000-0000-0000-0000-000000000005',gen_random_uuid(),repeat('B',64),5,now(),80,now(),gen_random_uuid(),now(),false);
INSERT INTO "AcceleratorReadinessSnapshots" ("Id","UserId","GoalId","TriggerInterviewSessionId","CalculationVersion","SkillCoverage","PracticePerformance","ProjectPerformance","InterviewPerformance","Total","Explanation","CalculatedAtUtc","CreatedAtUtc","IsDeleted") VALUES
('45000000-0000-0000-0000-000000000007','2a000000-0000-0000-0000-000000000001','2a000000-0000-0000-0000-000000000006','45000000-0000-0000-0000-000000000006','readiness-v1',80,0,0,80,56,'Historical readiness preserved',now(),now(),false);
COMMIT;
