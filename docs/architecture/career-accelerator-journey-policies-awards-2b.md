# Migration 2B: journey policies, awards and eligibility

Implemented and verified on 2026-11-17. This milestone does not integrate Phase 3–5 ownership or issue certificates.

## Runtime and policy semantics

- Proficiency is derived dynamically from existing assessment results, context-owned evidence and persisted required-lesson completion. No second evidence, scoring, readiness or passport table was introduced.
- Policies are immutable configurations identified by code/version. A path-specific active policy takes precedence over the global fallback; highest version wins, then code orders ties deterministically. No policy is seeded into production automatically.
- Component weights are positive and sum to 100. Required unavailable components, no usable scores, or no qualifying canonical evidence produce `Incomplete` with a null overall score. Missing policy produces `PolicyUnavailable`.
- Available components currently include completed assessment score averages and verified module completion. Optional unavailable components are omitted from the weighted denominator; their configured weights and `Unavailable` status remain visible. Practice, assignment, project and numeric verified-evidence contributions remain unavailable pending safe attribution/normalization. Evidence still supplies existing demonstrated competency levels, confidence and references.
- Bands cover 0–100 continuously. Intervals are lower-inclusive/upper-exclusive, except the final upper endpoint includes 100. Names come from database configuration. Service validation rejects gaps/overlaps; PostgreSQL exclusion constraints reject overlapping active bands even outside the service.
- A module counts complete only when it has required published lessons, all are completed in persisted enrollment progress, and it has no unverified assessment/project/interview requirement. Enrollment progress percentages and client completion flags are not used for recognition.
- Awards evaluate configured minimum proficiency, completed module counts or completed journey criteria. Unique context/definition keys plus the existing advisory-lock transaction boundary prevent duplicates. Awards and their evidence metadata are append-only. Historical definition labels are retrieved even if definitions become inactive.
- Certificate eligibility requires configured minimum completed assessments, minimum score, qualifying evidence and, when configured, completed required modules. One state record exists per journey context; reevaluation uses revision concurrency. Eligibility never sets issuance timestamps, public identifiers or artifact storage keys. Issued state is preserved during reevaluation.
- Existing readiness/passport calculations are unchanged. Recognition complements them with context-owned data.

## API contract

All candidate routes derive actor identity from claims and validate actual active candidate/context ownership. Responses use the existing `ApiResponse<T>` wrapper. No candidate input accepts scores, awards, ownership IDs or issuance flags.

| Method | Route under `/api/accelerator/v1` | Response |
|---|---|---|
| GET | `enrollments/{enrollmentId}/recognition` | `EnrollmentRecognitionResponse`: configured path title/ID, proficiency, earned achievements and certificate state |
| GET | `journeys/{contextId}/proficiency` | `JourneyProficiencyResponse`: policy/status/score/band, components/availability, counts, evidence IDs and demonstrated competencies |
| GET | `journeys/{contextId}/achievements` | Earned award IDs, definition code/title/description and award time |
| POST | `journeys/{contextId}/achievements/evaluate` | Idempotently evaluated earned awards |
| GET | `journeys/{contextId}/certificate` | Current eligibility/state; null issuance metadata until actually issued |
| POST | `journeys/{contextId}/certificate/evaluate` | Server-derived eligibility/state |

Administrator-only publishing routes:

- `POST /api/admin/accelerator/v1/recognition/policies` accepts `CreateProficiencyPolicyRequest`, including bands and certificate criteria.
- `POST /api/admin/accelerator/v1/recognition/achievement-definitions` accepts `CreateAchievementDefinitionRequest`.

Candidate operations require Candidate authorization; publishing also revalidates Administrator status in the service. Private GET responses are not cached. Invalid configuration returns a domain request error; foreign journey/enrollment returns not found. An unavailable policy/component is represented as data, not a fabricated score. No download or certificate-generation endpoint exists.

## Files

Added:

- `JobPortal.Domain/Entities/CareerAccelerator/AcceleratorJourneyRecognitionEntities.cs`
- `JobPortal.Application/Features/CareerAccelerator/AcceleratorRecognitionContracts.cs`
- `JobPortal.Application/Features/CareerAccelerator/AcceleratorRecognitionService.cs`
- `JobPortal.Persistence/Configurations/AcceleratorJourneyRecognitionConfigurations.cs`
- `JobPortal.Persistence/Repositories/AcceleratorRecognitionStore.cs`
- `JobPortal.API/Controllers/AcceleratorRecognitionController.cs`
- `JobPortal.Application.Tests/AcceleratorRecognitionTests.cs`
- `JobPortal.Application.Tests/AcceleratorRecognitionPostgresTests.cs`
- Migration/Designer `20261117164539_AddCareerAcceleratorJourneyPoliciesAndAwards`
- This report.

Modified for 2B: Application/Persistence DI registrations, DbContext DbSets, append-only persistence policy, and PostgreSQL model snapshot. Earlier dirty working-tree changes were preserved.

## Migration review

Five new tables: `AcceleratorSkillProficiencyPolicies`, `AcceleratorProficiencyBands`, `AcceleratorAchievementDefinitions`, `CandidateAcceleratorAchievements`, `AcceleratorEnrollmentCertificates`.

| Operation | Up | Down |
|---|---:|---:|
| CreateTable | 5 | 0 |
| DropTable | 0 | 5 |
| AddColumn / DropColumn / AlterColumn / Rename | 0 | 0 |
| Standalone AddForeignKey / DropForeignKey | 0 | 0 |
| CreateIndex | 15 | 0 |
| DropIndex | 0 | 0 |
| Standalone AddCheckConstraint | 0 | 0 |
| Sql | 1 | 1 |

CreateTable operations include 5 PKs, 8 restrictive FKs and 4 checks. The Up SQL adds one exclusion constraint/index to the newly created band table. There are no existing-table changes. The snapshot adds exactly these five entities and removes none of the 2A models.

Down first refuses rollback if earned awards or issued certificates exist. Otherwise it removes only the five 2B tables. It never substitutes fake ownership/issuance data.

## Verification

- Affected Domain/Application/Persistence/Persistence.Postgres/API/Application.Tests chain: successful; 0 warnings, 0 errors.
- Recognition unit tests: 8 passed, 0 failed, 0 skipped.
- Dedicated recognition PostgreSQL test: passed. Eight independent retry-enabled PostgreSQL contexts concurrently evaluated awards/certificate eligibility; one award per context/definition and one certificate state remained. PostgreSQL rejected band overlap (`23P01`) and duplicate awards (`23505`).
- Existing JourneyContext PostgreSQL tests: 3 passed in the final Accelerator regression.
- Final Accelerator regression: **174 total, 173 passed, 0 failed, 1 skipped**. This includes all 9 new recognition cases. The remaining skip is the separate Market Intelligence PostgreSQL fixture.
- PostgreSQL: Docker 17.12, loopback `127.0.0.1:55439`, dedicated database `accelerator_2b_verify`, cloned from the real migration chain through approved 2A. Migration 2B applied successfully.
- Empty recognition rollback: separate `accelerator_2b_empty_rollback_verify` reverted to 2A; all five 2B tables were absent afterward.
- Populated rollback: separate `accelerator_2b_populated_rollback_verify` refused with expected `P0001`; earned award, eligible/unissued certificate state and 2B history remained intact.
- TRX files: `journey-recognition-unit.trx`, `journey-recognition-postgres.trx`, `journey-policies-awards-regression.trx` under Application.Tests/TestResults. Generated artifacts remain unstaged.
- Opt-in integration variable: `ACCELERATOR_RECOGNITION_TEST_POSTGRES`; guard requires the exact disposable host, port and database above. Existing 2A tests use their own dedicated opt-in variable/database.

Migration 1, Migration 2A, frontend and `accelerator-production-migration.sql` were not edited. No Neon/shared/production database was used. Nothing was staged, committed or pushed.

Remaining limitations are explicit: unavailable Phase 3–5 contributions, no external certificate issuer/artifact/download, no full-suite production certification, and the unrelated Market Intelligence database skip. Stop after 2B.
