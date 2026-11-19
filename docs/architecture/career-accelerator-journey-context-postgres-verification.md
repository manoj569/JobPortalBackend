# Migration 2A PostgreSQL verification

Verified on 2026-11-17. No production/shared database was used. No migration source was changed during verification.

| Requested check | Observed result |
|---|---|
| 1. Container | `careerharbor-2a-verify-20261117-a91f`, dedicated Docker Linux container; now stopped, retained for review |
| 2. PostgreSQL | 17.12, Debian image `postgres:17` |
| 3. Database/port | `accelerator_2a_verify`, bound exclusively to `127.0.0.1:55439`; separate test-only credentials |
| 4. Pre-2A state | Real repository migration chain applied through `20261117145522_AddCareerAcceleratorLearningContentJourney`; history checked before 2A |
| 5. Historical fixture | Candidate, canonical skill, taxonomy, role/requirement, competency, CareerGoal, template, attempt, evidence, roadmap, recommendation; all constraints enabled |
| 6. Migration 2A | `20261117154417_AddCareerAcceleratorJourneyContexts` applied successfully |
| 7. Context backfill | Exactly one active context for the historical goal; authoritative owner/taxonomy, CareerGoal scope, enrollment NULL |
| 8–11. Four aggregates | Historical attempt, evidence, roadmap and recommendation reference the correct context |
| 12. GoalId preservation | Original GoalId retained on all four historical rows |
| 13. Duplicates/orphans | SQL assertions passed: zero duplicate active CareerGoal contexts and zero orphaned/mismatched Phase 2 rows |
| 14. Skill context | Enrollment context persisted by the actual context service |
| 15–18. Generic artifacts | Actual assessment/scoring/roadmap services persisted attempt, evidence, roadmap and recommendation with context populated and GoalId NULL |
| 19–20. No fake ownership | CareerGoal/CareerRole counts unchanged during Skill Path flow |
| 21. Scope constraints | All four invalid source/scope combinations rejected with SQLSTATE 23514 |
| 22–23. Uniqueness | Duplicate active CareerGoal and enrollment contexts rejected with SQLSTATE 23505 |
| 24. CareerGoal concurrency | Eight independent retry-enabled PostgreSQL contexts called GetOrCreate concurrently; one returned ID/one active row; no escaped unique race |
| 25. Enrollment concurrency | Same eight-caller exercise passed for one enrollment |
| 26. History | Migration 2A appears exactly once |
| 27. Legacy Down | Separate `accelerator_2a_legacy_verify` copy applied 2A then successfully reverted; preceding history restored; all five legacy GoalId/RoleId columns restored NOT NULL; historical goal retained |
| 28. Generic Down | Separate `accelerator_2a_generic_rollback_verify` refused rollback with expected P0001: `Cannot roll back JourneyContexts while generic journey records exist.` History and generic attempts remained intact |
| 29. PostgreSQL tests | 3 total, 3 passed, 0 failed, 0 skipped |
| 30. Accelerator regression | 165 total, 164 passed, 0 failed, 1 skipped; includes the three new PostgreSQL tests |
| 31. Build | Domain, Application, Persistence, Persistence.Postgres, API built; final Application.Tests build passed with 0 warnings/errors |
| 32–34. Protected files | Migration 1, Migration 2A and accelerator-production-migration.sql untouched during verification |
| 35. Artifacts | TRX files remain unstaged/untracked |
| 36. Git | Existing dirty working tree preserved; added PostgreSQL test, two SQL fixtures, this report and generated TRX files; nothing staged/committed/pushed |
| 37. Remaining scope | Market Intelligence opt-in PostgreSQL bootstrap test remains skipped (different fixture/environment variable). This verification does not certify production deployment or Migration 2B. |

## Reproducibility

- Historical seed: `JobPortal.Application.Tests/Fixtures/journey-context-pre2a.sql`.
- Read-only assertions: `JobPortal.Application.Tests/Fixtures/journey-context-backfill-assertions.sql`.
- Dedicated tests: `AcceleratorJourneyPostgresTests`.
- Opt-in variable: `ACCELERATOR_JOURNEY_TEST_POSTGRES`; test guard requires host `127.0.0.1`, port `55439`, database `accelerator_2a_verify`.
- Tests require the historical SQL fixture and actual migration chain; they do not use EnsureCreated or EF InMemory.
- Database test processes ran outside the execution sandbox because the sandbox prevents VSTest/testhost connection.
- Initial service-test run exposed a fixture error (candidate seeded Pending instead of Active). The fixture was corrected to UserStatus.Active; authorization and migration code were unchanged. All reported pass counts are completed reruns after that correction.
- Final TRX: `journey-context-postgres.trx` and `journey-context-docker-regression.trx` under `JobPortal.Application.Tests/TestResults`.

The stopped container retains all three disposable databases. No unrelated Docker containers or volumes were changed or removed.
