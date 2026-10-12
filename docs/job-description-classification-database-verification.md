# CareerHarbor classification: disposable database verification

Date: 2026-10-12. Backend HEAD `7af5b11c1745dca04693c430f8a4cbece50c3d87`; implementation remains uncommitted. This follow-up supersedes the database and salary-request blockers in `job-description-classification-predeployment-verification.md`. **NOT PRODUCTION READY:** the nine established runtime baseline failures and 184 strict analyzer failures still require disposition. No frontend file, production connection, production migration/backfill, push, deployment or live ingestion was used.

## Gates

| Gate | Status | Evidence |
|---|---|---|
| Disposable PostgreSQL available | PASS | Docker Desktop became available; cached PostgreSQL 17.11 image. Two owned containers bind only 127.0.0.1:54761/54762, without host volumes. No native PostgreSQL installation was available. |
| Populated prior-schema migration and resulting schema | PASS | Applied all prior migrations, inserted eight old-schema jobs, then upgraded through `20261012054803_AddJobDescriptionClassification`. Existing row comparison found zero changes. Idempotent migration SQL replay succeeded. |
| Previously skipped PostgreSQL integrations | PASS | All 15 previously skipped methods passed (16 expanded cases), plus the new salary API case: 17 distinct passing cases, zero final skips. Exact run breakdown below. |
| Real CLI dry-run/bounds/interruption/resume/replay/audit | PASS | Seven eligible jobs, bounded two-row dry-run, no database writes, controlled process termination during the second atomic update, successful resume, zero-change replay and SELECT-only dry-run. |
| Transactional Workday checkpoint and incomplete reconciliation | PASS | Final full database run passed every checkpoint/ingestion case, including 5,001 records, crash/commit boundaries, bounded retries and unsafe-snapshot closure prevention. |
| All-provider snapshot/reconciliation acceptance | FAIL | Four pre-existing SuccessFactors pagination tests still fail: invalid supplied last-page links on a declared single-page board are not rejected. This prevents an unconditional all-provider safety claim. |
| Legacy salary-request compatibility | PASS | Both missing scope fields select the existing frontend's INR/yearly contract. Partial explicit scope is invalid. Unknown salary units never enter numeric comparisons. Controller/service/repository fixture passes on PostgreSQL. |
| Explicit new-client currency and period | PASS | Explicit USD/yearly, INR/monthly and INR/hourly requests select only matching records; invalid scope values remain invalid. |
| Provider routing, manual correction and nonclassification preservation | PASS | Shared classification boundary retained; database backfill compares all other columns exactly, preserves manual locks, duplicate URLs, status, source metadata and timestamps. Provider refresh regressions cover five override groups. |
| API and maintenance normal Release builds | PASS | Zero warnings/errors in both final builds; analyzers enabled with normal strict settings. |
| Runtime regression release acceptance | FAIL | Final selection: 786 passed, 10 failed, 0 skipped (796). Nine established baseline failures plus one timing-sensitive Workday request timeout; the latter passes alone on both current and baseline binaries. |
| Strict analyzer release acceptance | FAIL | Exactly 184 baseline findings; zero added/removed diagnostics by file/rule/message occurrence count. No analyzer weakening. |
| Actual deployed-schema drift and deployed frontend SHA | BLOCKED | No production access was authorized. Local migration-chain and request-contract evidence does not establish the deployed database's history/drift or frontend commit. |

## Isolated PostgreSQL and schema evidence

Owned containers: `careerharbor-classification-verify-20261012` (54761) and `careerharbor-classification-isolation-20261012` (54762), image `postgres:17-alpine`, PostgreSQL 17.11. Trust authentication is confined to the loopback disposable fixtures. Existing Docker containers were left alone. Test connection variables were set only in the invoking test process, then removed. Workday and run-store fixtures create/drop random schemas; generic-India has a dedicated disposable database. A second database with no preinstalled extension exercised parallel extension setup.

Both owned containers were removed after their IDs were checked against the creation results, along with their anonymous test volumes. There were no host bind mounts. Logs, TRX, schema comparisons, checkpoints and audits remain in the workspace parent. No unrelated container was stopped or removed.

The CLI fixture `careerharbor_classification_cli_test` was migrated to `20261011164834_AddWorkdayRunCheckpoints` first. `scripts/classification-disposable-fixture.sql` refuses any other database and requires an empty Jobs table. It saves a complete original-row JSON baseline before the classification migration. Eight rows include historic populated fields, two duplicate application URLs with NULL canonical hashes, different publication/deletion states, native source JSON, manual corrections prepared after upgrade, and a future-created row outside the backfill cutoff.

Resulting columns: nullable `CurrencyCode character(3)` with prior values retained; nullable `ClassificationJson text`, `ManualClassificationOverrides varchar(200)`, and `SalaryPeriod integer`. Full prior-row JSON excluding the three added columns is unchanged. The newest migration history includes both the Workday prerequisite and classification migration. Replaying generated idempotent SQL succeeds. This validates the repository migration chain on PostgreSQL, including populated upgrades; it does not inspect unapproved production drift.

Evidence under the repository parent directory:

- `classification-postgres-prior-migrate.log`, `classification-postgres-upgrade.log`.
- `classification-postgres-schema-verification.log`: zero changed existing rows and actual column types/nullability.
- `classification-postgres-idempotent-migration.log` and `classification-predeployment-migration.sql`.

## Backfill and audit evidence

`scripts/verify-classification-disposable.py` invokes the real maintenance DLL using only its fixed localhost fixture connection. It does not load API settings. The final evidence is `../classification-postgres-audit-cold-final/cli-verification-summary.json`, associated checkpoint/audit files, and `../classification-postgres-cli-cold-final.log`. Five fresh-process one-row dry-runs all have zero failures/retries. Earlier successful trials remain in the final/acceptance evidence directories; fresh checkpoint files were used each time.

The bounded dry-run analyzes 2 of 7 eligible rows, leaves 5, and writes no database values. Resuming the same file analyzes all 7 with five proposed corrections and zero applied corrections. A SELECT-only PostgreSQL role also completes dry-run. An eighth row created in 2099 is excluded throughout.

For interruption, a fixture-only BEFORE UPDATE trigger sleeps on the second row. The script observes cursor 1 and one committed row, kills only its own CLI process, terminates only its specifically named database session, and removes the trigger. PostgreSQL rolls back the interrupted second UPDATE. The checkpoint remains at the first completed row. Resume ends with analyzed 7, proposed 5, applied 5, remaining 0 and retry 0. A new complete apply pass proposes/applies zero corrections; full row hashes remain identical.

Audit has eight durable intents and seven outcomes. The one unmatched intent is the deliberately killed operation; outcome attempt IDs, job IDs and before/after values pair exactly. Evidence and review-field names are present. The filesystem JSONL and database are intentionally not one atomic transaction: an intent is flushed before a write, and a crash after commit may leave an unmatched intent. Resume safely repeats using row state and optimistic concurrency. Retain both checkpoint and audit; counts describe completed checkpointed work, and unmatched intents require reconciliation.

All columns outside the nine-column classification whitelist compare exactly with the original baseline. Both duplicate URL records remain separate; no unique identity/index is rewritten. The manually locked row keeps its accepted values. PostgreSQL fixture tests also verify native JSON reading and rejection of an intervening admin edit by whole-row revision comparison. Apply and dry-run checkpoint interchange is rejected, as is switching the same database connection to a different resolved Jobs relation.

Scope now binds host/port/database, resolved Jobs schema/table, mode, rules version and `backfill-v2`. Old v1 local checkpoint files are rejected; use fresh files for this unreleased version. No production backfill had been run. Cumulative proposed/applied counters now account for a recovered analysis failure and a committed update followed by audit-outcome failure; the unit fixture checks replay does not double-count writes.

## Salary compatibility

Read-only frontend inspection found the legacy UI describes salary as annual rupees/LPA (`ActiveFilterChips.tsx` and `CandidateJobFilters.tsx`) while omitting both query scope fields. `SalaryQueryPolicy` therefore normalizes only that legacy request contract to INR/yearly. It never fills missing currency/period on stored jobs, converts currency, annualizes monthly/hourly values or changes amounts. A salary amount filter, alias or salary sort sees only known matching values; ordinary browsing retains all jobs.

Unscoped salary facets use the same INR/yearly contract. Other facet counts remain based on their existing scope. An explicit pair overrides the default. Supplying just currency or just period fails validation; direct repository salary calls with partial scope fail closed. Mixed fixtures contain annual INR, annual USD, monthly/hourly INR and unknown currency/period; exact result IDs/counts and salary-facet range are checked in memory and through real PostgreSQL EF queries.

Existing route and query names are retained. New clients can use, for example:

```text
GET /api/jobs/search?minAmount=800000&currencyCode=INR&salaryPeriod=Yearly
GET /api/jobs/search?minAmount=1000&currencyCode=USD&salaryPeriod=Monthly
```

Response fields for currency and salary period remain additive/nullable; zero enum values mean Unknown. This fixes request compatibility but does not claim the deployed UI can display every new classification feature. The deployed frontend SHA remains unknown; frontend files were not edited.

## New verification failures and baseline failures

Final PostgreSQL evidence is split deliberately rather than hiding failed trials: `../classification-test-results/classification-postgres-acceptance.trx` has 16 passed, 1 timestamp-precision failure, 0 skipped (25m16s, including the 5,001-record fixture); `classification-postgres-cold-final.trx` has 2 passed, 0 failed/skipped, rerunning the corrected classification fixture and the salary API fixture on the latest backend. The union contains 17 distinct passing cases. The old skipped experience-crash theory expands into two cases, explaining why 15 skipped methods become 16 existing executable cases. The salary PostgreSQL fixture adds the seventeenth. General regex engine changes do not alter checkpoint/persistence logic; final classifier/backfill, salary and provider regression fixtures verify the updated parser.

Newly exposed issues were fixed without removing assertions or increasing parser timeouts:

- Real PostgreSQL classification fixture now seeds a valid company owner/role.
- Parallel fixture extension installation uses `pg_advisory_xact_lock` inside an explicit transaction, releasing only after CREATE EXTENSION commits. `btree_gist` lives in public and random fixture schemas include public in their search path. Cleanup tolerates initialization failing before schema creation.
- The timestamp assertion compares exact persisted PostgreSQL instants normalized to UTC instead of pre-save .NET ticks or local JSON timezone presentation; PostgreSQL microsecond precision is retained and unchanged timestamps are still required.
- Salary parsing uses the nonbacktracking engine and skips sentences without any supported currency token. The existing 100 ms budget and 100,000-character input cap remain. Named group semantics used here are supported by Microsoft's [regex option documentation](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-options#nonbacktracking-mode). Existing salary cases exercise the unchanged extraction rules. Earlier transient timeout and extension-race logs are retained as investigation evidence, not successful runs.
- Other small patterns use the interpreted engine so each pattern does not JIT a separate compiled runner during the first match's timeout window. Fresh-process verification includes five distinct one-row CLI dry-runs; timeout/input limits and fail-closed retries are unchanged. Salary retains its nonbacktracking engine. Later experience-regex cold-start failures, as well as the initial salary timeout, are preserved in the investigation logs.
- Checkpoint scope now includes the actual Jobs relation, and counters correctly retain recovered proposals/committed changes after an audit failure.
- Historical salary-period disagreement is included in review fields even when amount/currency match. The existing accepted period/amount are preserved, and repeat processing retains identical metadata.

The nine unrelated runtime failures and 184 analyzer findings remain the same baseline described, with reproduction evidence, in `job-description-classification-predeployment-verification.md`. A baseline failure is still a release failure. Runtime test compilation alone used `TreatWarningsAsErrors=false`, with analyzers active, to execute fixtures; normal API/maintenance builds use strict settings. No project or analyzer configuration changed.

A late broad-run failure in `WorkdayBatchIngestionTests.OtherWorkdayTenantsReceiveNoAccentureFacetOrGenericRemoteIndiaClaim` is a synthetic HTTP request timeout, before classification is invoked. Both the current binary and preserved pre-classification binary pass that same case in isolation (539 ms / 602 ms). Evidence: `classification-workday-timeout-isolated.trx` and `classification-workday-timeout-baseline.trx`, with matching logs. This is recorded as a timing-sensitive broad-run failure, not silently deleted or claimed as a reproduced persistent baseline failure. Request timeout settings and assertions were unchanged.

The final strict test build `../classification-strict-cold-final-build.log` reports exactly 184 errors; `classification-strict-cold-final-comparison.log` and `classification-predeployment-analyzers.json` confirm baseline/current 184, added 0, removed 0. Normal final production builds are `classification-api-cold-final-release.log` and `classification-maintenance-cold-final-release.log`, both zero warnings/errors.

Final targeted runtime evidence is `../classification-runtime-cold-final.log` and `../classification-test-results/classification-runtime-cold-final.trx`: 786 passed, 10 failed, 0 skipped (796; 10m44s). No final classifier, salary, manual override, audit or backfill case fails. Nine failures match the preserved baseline; the separate provider timeout passes in both isolated reruns. The machine-readable inventory `../classification-database-verification-summary.json` retains the raw broad-run failures, isolated outcomes, all PostgreSQL case names and TRX hashes. This does not convert the broad run into a green build.

## Reproduction

Use the prior report's commit/push/Render/dry-run command examples only after release approval. They were not executed. The release-file manifest includes this report, backend salary compatibility changes and disposable fixture scripts; generated logs/build artifacts are excluded.

To run the database fixtures in an independently created disposable loopback PostgreSQL container, create only the named test databases and set:

```powershell
$env:WORKDAY_CHECKPOINT_TEST_POSTGRES = 'Host=127.0.0.1;Port=54761;Database=careerharbor_workday_test;Username=postgres'
$env:GENERIC_INDIA_TEST_POSTGRES = 'Host=127.0.0.1;Port=54761;Database=careerharbor_generic_india_test;Username=postgres'
$env:JOB_SOURCE_RUN_TEST_POSTGRES = 'Host=127.0.0.1;Port=54761;Database=jobsource_run_test;Username=postgres'
try {
    dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --artifacts-path ../classification-db-cold-start-artifacts -m:1 -p:UseSharedCompilation=false -p:TreatWarningsAsErrors=false
    dotnet vstest ../classification-db-cold-start-artifacts/bin/JobPortal.Application.Tests/release/JobPortal.Application.Tests.dll '--TestCaseFilter:FullyQualifiedName~ClassificationPostgresTests|FullyQualifiedName~WorkdayCheckpointPostgresTests|FullyQualifiedName~GenericIndiaJobIngestionPostgresTests|FullyQualifiedName~JobSourceRunPostgresTests|FullyQualifiedName~SalaryCompatibilityPostgresTests' '--logger:trx;LogFileName=classification-disposable-recheck.trx' --ResultsDirectory:../classification-test-results
} finally {
    Remove-Item Env:WORKDAY_CHECKPOINT_TEST_POSTGRES,Env:GENERIC_INDIA_TEST_POSTGRES,Env:JOB_SOURCE_RUN_TEST_POSTGRES -ErrorAction SilentlyContinue
}
```

The CLI script requires the exact fixed fixture container/database and a newly seeded/reset classification baseline; never run it against another database or overwrite prior checkpoints. Its intentionally interrupted local process does not start ingestion. Create fresh evidence paths for each verification.
