-- Disposable PostgreSQL fixture only: run on a clone at Migration 2B, before Phase 3 JourneyContext migration.
BEGIN;
INSERT INTO "AcceleratorTutorConversations" ("Id","UserId","GoalId","CompetencyId","RequestKey","RequestHash","Topic","Title","Status","LastMessageAtUtc","Revision","CreatedAtUtc","IsDeleted") VALUES
('3a000000-0000-0000-0000-000000000001','2a000000-0000-0000-0000-000000000001','2a000000-0000-0000-0000-000000000006','2a000000-0000-0000-0000-000000000005','3a000000-0000-0000-0000-000000000002',repeat('A',64),'Historical topic','Historical conversation',1,now(),gen_random_uuid(),now(),false);
INSERT INTO "AcceleratorPracticeSessions" ("Id","UserId","GoalId","CompetencyId","RequestKey","RequestHash","Difficulty","QuestionType","Status","GenerationStrategy","Revision","CreatedAtUtc","IsDeleted") VALUES
('3a000000-0000-0000-0000-000000000003','2a000000-0000-0000-0000-000000000001','2a000000-0000-0000-0000-000000000006','2a000000-0000-0000-0000-000000000005','3a000000-0000-0000-0000-000000000004',repeat('B',64),1,1,4,'deterministic-v1',gen_random_uuid(),now(),false);
INSERT INTO "AcceleratorPracticeQuestions" ("Id","SessionId","Sequence","Prompt","PublicOptionsJson","AnswerKeyJson","RubricJson","GenerationVersion","IsQualityApproved","CreatedAtUtc","IsDeleted") VALUES
('3a000000-0000-0000-0000-000000000005','3a000000-0000-0000-0000-000000000003',1,'Historical question','["A","B"]','{"answer":"A"}','{}','legacy-v1',true,now(),false);
INSERT INTO "AcceleratorPracticeAttempts" ("Id","UserId","SessionId","QuestionId","RequestKey","AnswerJson","SubmittedAtUtc","CreatedAtUtc","IsDeleted") VALUES
('3a000000-0000-0000-0000-000000000006','2a000000-0000-0000-0000-000000000001','3a000000-0000-0000-0000-000000000003','3a000000-0000-0000-0000-000000000005','3a000000-0000-0000-0000-000000000007','{"answer":"A"}',now(),now(),false);
INSERT INTO "AcceleratorPracticeEvaluations" ("Id","AttemptId","Score","MaximumScore","Classification","StrengthsJson","WeaknessesJson","Explanation","SuggestedImprovement","Method","Confidence","RubricVersion","CreatedAtUtc","IsDeleted") VALUES
('3a000000-0000-0000-0000-000000000008','3a000000-0000-0000-0000-000000000006',1,1,'Correct','[]','[]','Historical evaluation','Continue learning',1,1,'legacy-v1',now(),false);
COMMIT;
