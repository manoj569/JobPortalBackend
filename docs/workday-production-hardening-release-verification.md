# Workday hardening: local verification and deployment guide

Date: 2026-10-12. Production recommendation: **NO-GO for immediate deployment**. Local acceptance results are recorded below; deployment needs an approved migration window, consistent worker configuration, resolution or explicit disposition of the existing test/analyzer failures, and an authorized production verification run. None of those production operations was performed.

## Verified causes and scope

The audit was recorded before implementation in `workday-production-hardening-audit.md`.

- PwC's approximately 30-minute cancellation matches the existing 1,800-second provider budget. The previous pipeline had no durable listing cursor or item receipts, so retry started detail processing again. The cause of the production request latency is unverified.
- Accenture's shutdown could leave committed jobs intact, but its next execution restarted details and its last heartbeat could underreport committed records. The old three-attempt fault limit was unsuitable for healthy multi-slice continuation.
- Scheduling previously invoked the runner directly, bypassing the durable queue. A single queued worker could hold up other companies.
- The preserved preceding reconciliation correction separates native inventory completeness from geographic selection and retains observed IDs before filtering. Local PTC-style regression tests verify this correction. The exact historical PTC `DiagnosticsJson` and deployed configuration remain unverified: the previous anonymous admin lookup returned 401. Geography, a configured test cap, missing identities and other guards must not be conflated when diagnosing that production run.
- Workday's listing location label is not an exhaustive hiring-location contract. Details remain necessary to distinguish India, foreign, unspecified and conflicting geography safely. No title/company/Remote inference was added.

This work adds durable execution infrastructure; it does not change source registration, India eligibility, categories, publication approval/quality behavior, application URLs, pricing or referral workflows. Existing PTC, PwC and Accenture production records were not accessed or changed.

## Durable behavior

Workday runs retain an ordered listing fingerprint, source and selection-configuration fingerprint, committed cursor, lean observed geography/identity facts and timestamps in `JobSourceRuns.CheckpointJson`. The supported CXS response has no atomic snapshot version; continuation re-reads the full listing and checks compatibility, and completion re-reads it again. A changed listing terminates as unsafe without closures; a subsequent normal queued/scheduled scan rebuilds its inventory from zero.

`JobSourceRunItems` stores original ingestion and publication outcomes. Data receipts join existing job transactions; successful publication receipts join the existing publication transaction. A cursor advances only after the corresponding data and publication outcomes are committed. Replay retains Created/Published outcomes from that run, and a new run counts previously stored records as Existing. A final completion receipt joins the closure/source-success transaction, preserving the exact Closed count if worker finalization crashes.

Healthy continuation uses Paused and a short delay. It resets fault attempts only when the durable cursor advances. Three no-progress attempts and the configurable total execution limit stop retries. Expired leases recover under the existing PostgreSQL source lock; local source guards, source session locks and global session-lock slots prevent overlap. Scheduled work enters the existing durable queue. Delayed continuations yield to due queued companies. The worker awaits execution, heartbeat and cleanup tasks.

Incomplete inventories, request/detail failures, listing anomalies, missing/duplicate identities, validation/persistence failures and configured test caps continue to block reconciliation. Geography exclusions remain in the observed inventory and do not imply absence. Partial slices never close jobs. Existing cross-provider creation locks and uniqueness constraints remain authoritative.

The authoritative worker summary is emitted after journal recovery and persisted finalization. Created, Existing, Published and other result counters are cumulative within one durable run; avoid summing them across continuation log lines. Processed counts eligible item receipts. CheckpointCursor counts acknowledged listing slots including geography exclusions. Detail counters describe the acknowledged prefix plus the current execution's attempts/results; they do not sum all physical HTTP retries or abandoned unacknowledged attempts from previous processes. DetailInFlight exposes outstanding attempts. Status, continuation state, selection/rejection reasons, remaining work, last checkpoint, duration and reconciliation reasons are available in durable run diagnostics. Healthy slices no longer emit a false snapshot-failure warning. Missing category messages are aggregated.

## Validation results

| Verification | Result |
| --- | --- |
| Final Release API build, normal analyzers | PASS: zero warnings, zero errors; `../workday-hardening-api-release-terminal-verified.log` |
| Release maintenance build, normal analyzers | PASS: zero warnings, zero errors; `../workday-hardening-maintenance-final-verified.log` |
| Final test-project build, normal analyzers | BLOCKED: 184 pre-existing errors; diagnostic signatures/occurrence counts exactly match the recorded baseline, with zero additions; `../workday-hardening-analyzer-comparison-final.json` |
| Focused runtime suite with disposable PostgreSQL | PASS: **359 passed, 0 failed, 0 skipped**, 19m44s; includes **15 real PostgreSQL cases**, all passed; `../workday-hardening-test-results/workday-hardening-focused-final.trx` |
| EF pending-model validation | PASS: no changes since the migration |
| Incremental idempotent migration SQL generation | PASS; actual migration application is also exercised in the PostgreSQL fixtures |
| Diff check and preservation | PASS: no whitespace errors; the five untouched pre-task files match their saved SHA-256 hashes; no staged files |

Runtime test compilation disables analyzers only for the test project via command-line properties and uses already built production references. API and maintenance dependencies were built separately with normal analyzers. No repository analyzer settings were changed. This runtime workaround does not make the normal test/solution build green.

The new checkpoint class contributes 13 passing PostgreSQL cases, including its two crash-theory variants. The existing real run-store migration/lease test and generic India ingestion/publication test contribute two more. The separate persistence-tracking regression also passes in the focused suite. The final runtime source compiled with no test-source analyzer additions: the final normal build's 184 signatures exactly match the recorded pre-task baseline.

Synthetic fixture outcomes (the repeat uses a new durable run against the same records):

| Fixture / run | Created | Existing | Rejected | Failed | Skipped | SelectionSkipped | Published |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5,001 vacancies, first complete run | 64 | 0 | 4,937 | 0 | 0 | 4,937 | 64 |
| Same 5,001 vacancies, repeated complete run | 0 | 64 | 4,937 | 0 | 0 | 4,937 | 0 |
| PTC-style 176 vacancies, first complete run | 44 | 0 | 132 | 0 | 0 | 132 | 44 |
| Same 176 vacancies, repeated complete run | 0 | 44 | 132 | 0 | 0 | 132 | 0 |

These fixtures have zero validation rejections and zero provider/detail failures. Geographic exclusions are all `NonIndiaLocation`. Rejected includes those exclusions, while SelectionSkipped records the same geography decisions; **do not add those columns together**. Skipped is the existing provider/validation counter, distinct from SelectionSkipped. Both large runs finish with cursor/observed identities 5,001, safe reconciliation and exactly 64 stored jobs. Both PTC runs retain exactly 44 jobs, and the public API response contains those 44 published jobs. Published counts transitions performed by a run; it is not the total already-published inventory. Nonpublication outcomes remain separately represented by review, quality rejection, disabled and publication-failure counters.

The corrected 5,001-vacancy test took 17m17s including its first and repeat scans. This is a functional fixture with preserved listing pacing, not a Render throughput measurement. The older standalone synthetic performance-measurement method and LargeSourceIngestionTests are outside this focused filter; the explicit 5,001-vacancy PostgreSQL regression supplies the required large-inventory coverage.

Separately reported pre-existing runtime failures:

- `SuccessFactorsJobSourceProviderTests.WrongBoardOrOffsetPaginationCannotProduceCompleteSnapshot`: four theory cases expect InvalidDataException but receive no exception. All four failures reproduce in an isolated pre-task source copy.
- `JobSourceManualRunTests.NewUnmappedJobIsSkippedWithoutClaimingSuccessfulRun`: expects the old unmapped-category rejection, whereas the existing optional-category policy succeeds. This failure also reproduces in the isolated pre-task source copy.
- `NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation`: expects a nonempty pending model delta, but the already-scaffolded model has no delta.
- `NotificationEligibilityTests.CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment`: fixture setup fails existing session eligibility validation before its assertion.

The first five cases were run separately against the pre-task source: 0 passed, 5 failed, 0 skipped (`../workday-hardening-baseline-failures.log`). They are excluded by exact method names from the final focused pass run, rather than silently ignored. The two notification/career-guidance cases were rerun separately: 0 passed, 2 failed, 0 skipped (`../workday-hardening-unrelated.log`), matching earlier baseline evidence. No unrelated production code or those tests were modified to make them pass.

All HTTP responses in the new integration fixture are synthetic. PostgreSQL 16 runs in an owned disposable Docker container, bound only to `127.0.0.1:55439`; test databases are `careerharbor_workday_test`, `careerharbor_generic_india_test` and `jobsource_run_test`. The new fixture requires an explicit localhost connection and owns a unique schema. It applies the actual PostgreSQL migrations and checks for pending model changes. No production Neon connection, production import, API host/email dispatcher or real email was used.

After the final suite finished, the container ownership label was verified, the container was stopped, and its automatic removal was confirmed by an empty exact-name Docker inventory. All three disposable databases and exploratory fixture data were removed with it.

The 5,001-vacancy fixture processes every detail across six bounded executions of 1,000. It intentionally contains 64 India-eligible jobs and 4,937 foreign exclusions. It verifies large inventory continuation, not a throughput benchmark of 5,001 eligible database writes. A prior exploratory all-India fixture was aborted; a subsequent 500-per-slice fixture failed because its 120-second budget could not cover two fully paced listing passes at completion. Neither is counted as a passing acceptance test. The corrected fixture uses a 300-second local provider budget; production defaults and request pacing were not increased.

The PTC-style fixture contains 176 observed vacancies and 44 eligible jobs. Its publication check uses PostgreSQL, the real public repository/service and the `PublicJobsController.Search` action backing `GET /api/jobs`. It verifies the returned API response; it does not start an HTTP server or exercise deployed output-cache behavior.

Acceptance coverage (new cases are in WorkdayCheckpointPostgresTests unless indicated):

| Required scenario | Regression coverage |
| --- | --- |
| PTC complete scan with geography filtering | Ptc176Observed44IndiaExistingReconcilesAndOptionalCategoryPublishesIntoDiscover |
| Safe complete reconciliation | CompleteInventoryCanCloseMissingJobButPartialSlicesCannot |
| Incomplete scan cannot close jobs | FailedDetailsCannotReconcileOrCloseJobsAndRetryAttemptsAreBounded; preserved JobSourceReconciliationSafetyTests |
| Budget expiry and continuation | ProviderBudgetPauseIsBoundedAndSnapshotChangesFailClosedOnResume |
| Shutdown/recovery | ShutdownAfterCommittedCheckpointResumesOnlyRemainingDetails |
| Crash after commit, before checkpoint | CrashBeforeOrAfterDataCommitNeverAdvancesCursorAndRecoveryCountsActualCreations(true) |
| Crash before commit | The same theory with false |
| Repeated execution/idempotence | PTC and 5,001-vacancy repeated full runs |
| Repeated pagination/missing pages | Preserved WorkdayBatchIngestionTests repeated-page and early-empty/short-page cases |
| Listing total or same-total content changes | ProviderBudgetPauseIsBoundedAndSnapshotChangesFailClosedOnResume; SameTotalListingChangeAtCompletionCannotCertifyReconciliation; existing changed-total cases |
| Detail failure and bounded retries | FailedDetailsCannotReconcileOrCloseJobsAndRetryAttemptsAreBounded |
| India, remote, unspecified, foreign, ambiguous/conflicting geography | ResumeRetainsIndiaRemoteUnspecifiedForeignAndReviewGeographyFacts |
| Optional category | PTC fixture and GenericIndiaJobIngestionPostgresTests |
| Existing publication policy | Real JobAutoPublishService/JobService in PostgreSQL fixtures; existing quality/publication tests |
| Concurrent same-source attempts | PausedRunsCoalesceRequestsClaimsAreExclusiveAndThreeNoProgressAttemptsStop; JobSourceRunPostgresTests |
| Company fairness and global concurrency | ActualWorkersEnforceGlobalSlotsPreventSourceOverlapAndYieldToQueuedCompanies |
| Large inventory | LargeInventoryProcesses5001VacanciesAcrossSixExecutionsAndRepeatsWithoutDuplicates |
| Public API visibility | PTC fixture's real controller/service/repository response |
| Finalization crash preserves exact closure count | CrashAfterReconciliationCommitRecoversExactCompletionWithoutAnotherFetchOrClosure |
| Terminal status/continuation agreement | No-progress exhaustion, maximum executions and inactive-source cancellation in real store/worker tests |

## Required migration

Migration: `20261011164834_AddWorkdayRunCheckpoints`, after `20261011085034_AllowOptionalJobCategoryAndIngestionDiagnostics`.

It adds nullable CheckpointJson, ExecutionCount defaulting to zero, and JobSourceRunItems with a run FK and composite ordinal key. It expands the run-status check to preserve existing values 0–4 and add Paused=5, Unsafe=6, Cancelled=7. The existing active-source unique index also covers paused runs. It does not update/delete job records, source settings or old run history. Legacy uncheckpointed attempts cannot have historical receipts reconstructed retrospectively; stable identity deduplication still protects their already committed jobs.

The EF model matches the generated migration. An incremental idempotent SQL script was generated locally at `../workday-hardening-migration.sql`. Review that script against the production migration history before any authorized application. No production migration was executed.

## Render configuration checklist

Defaults are valid without additional keys. These are conservative initial operator recommendations, not changes applied to Render:

| Environment variable | Default | Suggested initial value |
| --- | ---: | ---: |
| `JobAggregation__Workday__JobsPerExecution` | 500 | 300 |
| `JobAggregation__Workday__ExecutionSeconds` | 600 | 600 |
| `JobAggregation__Workday__ContinuationDelaySeconds` | 60 | 60 |
| `JobAggregation__Workday__MaximumExecutions` | 200 | 200 |
| `JobAggregation__Workday__BatchSize` | 100 | 50 |
| `JobAggregation__Workday__DetailConcurrency` | 8 | 4 |
| `JobAggregation__Workday__RequestTimeoutSeconds` | 15 | 15 |
| `JobAggregation__Workday__RunBudgetSeconds` | 1800 | 1800 |
| `JobAggregation__Workday__MaximumAttempts` | 3 | 3 |
| `JobAggregation__Workday__RequestSpacingMilliseconds` | 250 | 250 |
| `JobAggregation__Scheduler__MaxConcurrentSources` | 3 | 2 |

- Use the **same MaxConcurrentSources on every instance**. Global slot locks assume a consistent slot count and the same database. Leave Scheduler.Enabled and source scan intervals operator-controlled; the durable admin queue works even when automatic scheduling is disabled.
- Retain existing source approvals, category mappings, geographic settings and credentials. No source was activated. A positive TestImportLimit remains incomplete and cannot reconcile.
- `ConnectionStrings__DefaultConnection` must support session-level PostgreSQL advisory locks. Use a direct PostgreSQL/Neon connection rather than a transaction-pooler endpoint; preserve the existing SSL/authentication requirements. The existing lock implementation pins non-pooled sessions. Account for source, slot and cross-provider creation-lock connections when sizing the database connection budget.
- Startup validates execution limits. Batch/time/concurrency settings need tuning against actual Render/database latency; reduce a batch if it cannot commit comfortably within an execution budget. Long listing scans themselves must fit the budget to reach a durable detail checkpoint.
- CXS inventory safeguards retain the 10,000-item bound and the suspected exact-2,000 search-cap guard. Such capped/unsafe sources require an independently verified complete upstream inventory before reconciliation.
- Workday does not provide a transactional listing/detail snapshot. Fingerprints detect observed listing changes; they cannot prove atomicity of detail-only updates that leave listing metadata unchanged. No safeguard is relaxed to claim otherwise.
- Receipts/checkpoints accumulate with run history. Establish an authorized retention/archival policy separately; this task does not delete history.

## Deployment and production verification (not executed)

1. Resolve or explicitly approve disposition of the existing analyzer and unrelated test failures. Review the source/configuration diff, migration and generated SQL; take the normal database backup and inspect migration history.
2. Disable scheduling and drain/stop **all old ingestion workers** for a maintenance window. Scheduler.Enabled alone does not stop the durable admin worker. Do not allow old binaries to consume newly paused runs.
3. Apply the reviewed additive migration through the authorized deployment process. Deploy the new application and consistent concurrency configuration to every instance. Check startup validation and worker/database connectivity before resuming scheduling.
4. With separate authorization, perform a capped canary and confirm that it cannot reconcile. Then approve a complete source run. Observe cursor growth, Paused/continuation transitions, actual durable counters, finite retries and no overlapping same-source executions.
5. Verify a planned restart resumes compatible work and retains data. Inspect PTC's original DiagnosticsJson and the current full observed-ID inventory before diagnosing any remaining historical guard. Changed listings must stop unsafe and leave jobs open.
6. For complete PTC/PwC/Accenture scans, compare final advertised totals, unique observed IDs, geography reasons and receipt counts. Confirm eligible jobs satisfy the current publication policy and appear through the public API after the existing cache lifetime/invalidation behavior. A repeated full run must report Created=0 for unchanged existing jobs and must not republish them.
7. Verify that incomplete, detail-failed and capped scans close zero jobs. Reconciliation can close genuinely absent source-owned jobs only after complete compatible inventory verification. Check historical production data/IDs and source settings against the backup; local tests alone do not verify those production records.

## Safe rollback

Prefer roll-forward. Stop/drain workers first and preserve run/checkpoint/receipt history. Rolling back application binaries requires an operator-reviewed conversion of active new-state rows to old-compatible terminal states and cleared leases; old binaries do not understand statuses 5–7. Preserve JSON/receipts and all jobs. Keep the additive schema during an application rollback where possible.

Do not automatically run migration Down: it removes resumable history and its old status constraint cannot accept new statuses. A database rollback needs a separate backup/archival plan and normalization of new statuses while workers are quiescent. No rollback SQL or production operation was executed here.

## Exact repository files changed by this task

Paths are relative to the repository root. Existing unrelated changes and concurrent company-registration artifacts are excluded.

Modified:

- `JobPortal.API/HostedServices/JobSourceRunHostedService.cs`
- `JobPortal.API/Program.cs`
- `JobPortal.API/Services/JobAggregationScheduler.cs`
- `JobPortal.API/Services/JobSourceRunWorker.cs`
- `JobPortal.Application.Tests/JobSourceDurableRunTests.cs`
- `JobPortal.Application.Tests/JobSourceRunPostgresTests.cs`
- `JobPortal.Application/Abstractions/Jobs/ExternalJobSourceSnapshot.cs`
- `JobPortal.Application/Abstractions/Jobs/JobSourceRunResult.cs` (extends preserved earlier edits)
- `JobPortal.Application/Abstractions/Jobs/RawExternalJob.cs`
- `JobPortal.Application/Features/JobAggregation/JobAggregationOptions.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceRunService.cs`
- `JobPortal.Application/Services/JobAutoPublishService.cs`
- `JobPortal.Application/Services/JobIngestionService.cs`
- `JobPortal.Application/Services/JobSourceCategoryResolver.cs`
- `JobPortal.Application/Services/JobSourceRunner.cs` (extends preserved earlier edits)
- `JobPortal.Domain/Entities/JobSourceRun.cs`
- `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs`
- `JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs`
- `JobPortal.Persistence/Configurations/JobSourceRunConfiguration.cs`
- `JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Persistence/Repositories/JobSourceRunStore.cs`

Added:

- `JobPortal.Application.Tests/WorkdayCheckpointPostgresTests.cs`
- `JobPortal.Application.Tests/WorkdayPersistenceTrackingTests.cs`
- `JobPortal.Application/Abstractions/Jobs/IJobSourceRunCheckpoint.cs`
- `JobPortal.Domain/Entities/JobSourceRunItem.cs`
- `JobPortal.Persistence/Configurations/JobSourceRunItemConfiguration.cs`
- `JobPortal.Persistence/Repositories/JobSourceRunCheckpoint.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261011164834_AddWorkdayRunCheckpoints.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261011164834_AddWorkdayRunCheckpoints.Designer.cs`
- `docs/workday-production-hardening-audit.md`
- `docs/workday-production-hardening-release-verification.md`

Pre-task changes were backed up before implementation. SHA-256 verification confirms the earlier MultiCompanyProviderTests, WorkdayIngestionDiagnosticsTests, JobSourcePublicationPolicy, JobSourceReconciliationSafetyTests and reconciliation verification document remain byte-identical. No files were staged, committed, pushed or deployed. Local build/test evidence and isolated baseline copies are outside the repository root.
