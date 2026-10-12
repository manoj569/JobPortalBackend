# CareerHarbor classification: final pre-deployment verification

Date: 2026-10-12. Backend checkout HEAD: `7af5b11c1745dca04693c430f8a4cbece50c3d87`, branch `main`. The implementation and earlier Workday/ingestion changes remain uncommitted. This report supersedes acceptance claims in the rollout guide. **NOT PRODUCTION READY.** No push, Render deploy, production connection, migration or backfill was performed.

Follow-up: [disposable database verification](job-description-classification-database-verification.md) records the running PostgreSQL upgrade, CLI crash/resume/replay/audit evidence and backend legacy salary-request fix. The table below reflects current gates; later sections retain the initial baseline/run evidence. Initial database skipped results and the original salary-contract rejection are historical, not the final implementation behavior.

## Verification gates

| Gate | Status | Evidence / remaining work |
|---|---|---|
| 1. Identify the original two failures and establish baseline | PASS | Both fail with identical assertions in the saved pre-classification binary; details below. The broader regression acceptance gate is FAIL because nine baseline failures remain. |
| 2. New analyzer findings | PASS | All 184 locations are pre-existing; zero new or removed diagnostics, including occurrence counts. Strict test compilation remains FAIL. API and maintenance normal Release builds have zero warnings/errors. |
| 3. Existing PostgreSQL schema compatibility | PASS | Populated prior-migration schema upgraded on disposable PostgreSQL 17.11; zero prior-row changes, actual types/nullability verified, idempotent SQL replay succeeded. Deployed schema drift/history remains separately BLOCKED because no production access was authorized. |
| 4. Skipped PostgreSQL integrations | PASS | All 15 previously skipped methods (16 expanded cases), plus the new salary API case, pass across final database runs. Zero final skips; full/corrected run breakdown is retained in the follow-up. |
| 5. Existing provider classification paths | PASS | Greenhouse, Lever, Ashby, SuccessFactors and Workday share `JobSourceRunner` → `JobIngestionService` → classification. Adzuna discovery commits through admin CSV import, which now classifies missing fields. Provider adapters and shared persistence fixtures cover these paths; live provider/DB acceptance remains conditional on gates 3/4. |
| 6. Manual corrections survive imports | PASS | All five override groups are honored, including explicitly cleared salary/experience fields. Six ATS identity cases exercise refresh preservation and idempotency. Legacy admin PUT preserves salary period when salary/currency are unchanged; changed legacy salaries remain unscoped until reviewed. |
| 7. Backfill resume/idempotency/audit/duplicates | PASS | Real CLI dry-run, bounded execution, controlled interruption, resume, SELECT-only role, mode/schema fencing, duplicate preservation and zero-change replay pass. PostgreSQL concurrent admin edit is rejected. Durable intent/outcome audit limitations are documented. |
| 8. URLs/status/source identities/checkpoints | PASS | Full-row database comparison verifies every nonclassification column unchanged. Transactional checkpoint, crash/recovery, complete/incomplete reconciliation and 5,001-record fixtures pass. Existing provider refresh may legitimately replace a changed provider URL under its previous logic. Nine unrelated baseline regressions still prevent overall release acceptance. |
| 9. Backend request compatibility with available frontend | PASS | Backend legacy annual-rupee requests default to INR/yearly, explicit pairs work, partial pairs are rejected and unknown job units are excluded; real PostgreSQL controller/service/repository contract passes. Frontend files were not changed. |
| Deployed frontend/schema acceptance | BLOCKED | Deployed frontend SHA/display behavior and production schema drift/history remain unverified; no production access was used. |
| 10. Operator commands | PASS | Exact PowerShell commands below, with required operator-supplied service ID/credentials. Commands are documentation only. Render service configuration, linked branch and auto-deploy mode remain unverified. |

All-provider snapshot/reconciliation acceptance remains **FAIL** because of the four pre-existing SuccessFactors pagination cases; the Workday database checkpoint gate passes independently. Latest runtime selection is 786 passed / 10 failed / 0 skipped: nine documented baseline failures plus one synthetic Workday request timeout that passes in isolation on both current and baseline binaries. Strict test build remains 184 baseline errors; final API/maintenance Release builds have zero warnings/errors. Detailed raw results and corrected reruns are in the follow-up.

## Regression evidence

The two originally reported failures:

1. `ExternalJobCoverageTests.ActualProviderOutputPassesThroughSharedRunnerAndCategoryMapping`: expected Created `1`, actual `0`, line 50.
2. `ExternalJobEnrichmentTests.BroadDepartmentsDoNotImplyTechnologyRoles(title: "Civil Engineer", department: "Infrastructure")`: expected null, actual `"general"`, line 148. Existing category classifier has an explicit general-discipline fallback; classification did not introduce it.

The preserved binary is `../workday-hardening-final-artifacts/bin/JobPortal.Application.Tests/release/JobPortal.Application.Tests.dll`, SHA-256 `41795BBCE60BDC9F37F98E6B33CBEE12A3869A55CC747C1D0E6B6A717F9969FA`. This is a preserved pre-task build artifact, not a clean historical Git checkout; the earlier implementation was also uncommitted. Reproduction against that artifact establishes the baseline without claiming an unavailable historical commit.

Evidence: `../classification-predeployment-baseline.log` and `../classification-test-results/classification-predeployment-baseline.trx`; original `../classification-baseline.log` agrees.

The broader selection also exposes these seven pre-existing failures (same assertions reproduced in saved binaries):

- `ExternalJobNormalizationPipelineTests.UnmappedNewAndMalformedRecordsFailClosedWithoutLosingValidRecords`: skipped expected 3, actual 4.
- `ExternalJobExpiryTests.Greenhouse_DeadlineSurvivesRunnerPersistenceWithoutRefreshingDuplicateExpiry`: expected 1, actual 0.
- `JobSourceManualRunTests.NewUnmappedJobIsSkippedWithoutClaimingSuccessfulRun`: expected false, actual true.
- Four `SuccessFactorsJobSourceProviderTests.WrongBoardOrOffsetPaginationCannotProduceCompleteSnapshot` cases: other host, other board, mismatched numeric board, extra path suffix; expected `InvalidDataException`, none thrown.

Evidence for additional cases: `../classification-predeployment-expanded-baseline.log` and its TRX, together with the first baseline TRX. These are release failures requiring resolution or explicit disposition; baseline status does not make the release green.

One newly failing CSV validation fixture used `Unknown` as an invalid employment value. `Unknown=0` is now deliberately valid. Replaced that fixture input with `NotAnEmploymentType`, retaining every validation assertion. Added positive CSV classification coverage instead of removing a requirement.

## Analyzer investigation and verification fixes

`../classification-predeployment-analyzers.json` inventories each diagnostic by file, source location, rule and message. Comparison deduplicates MSBuild's repeated printing and compares `(file, rule, message)` occurrence counts, allowing shifted line numbers. Baseline and final counts: **184 each, added 0, removed 0**.

| Rule | Findings |
|---|---:|
| CA1707: underscore member names | 126 |
| CA1861: repeated constant arrays | 29 |
| CA1869: uncached serializer options | 12 |
| xUnit2031: filtering before Assert.Single | 7 |
| CA1859: concrete type suggestions | 4 |
| CA1305: culture-sensitive calls | 3 |
| CA1725 / CA1826 / xUnit2030 | 1 each |

All occur in pre-existing test sources; none are introduced by classification or these fixes. No analyzer config, suppression, `NoWarn`, rule severity or project setting was changed. The normal strict diagnostic build fails on the existing 184 errors. Runtime-only compilation used `-p:TreatWarningsAsErrors=false` with analyzers still enabled, preserving all 184 diagnostics; this does not satisfy the strict build gate. Do not use an incremental no-diagnostic build as proof of clearance.

Fixed during this verification:

- Admin CSV import now locks only supplied classification groups and forwards salary period/explicit overrides, allowing JD evidence to fill missing fields. This also repairs Adzuna's CSV-backed commit path.
- Legacy admin edits preserve previously accepted salary period for unchanged salary/currency. Explicit newer-client overrides can clear it; changed legacy salary amounts do not inherit an unsupported period.
- Backfill appends and durably flushes an audit intent before each database update, followed by an outcome with the same attempt ID. Both include actual review-field names. An unavailable audit prevents writes. A crash after commit but before outcome leaves an intent to reconcile, and the row safely repeats; JSONL and PostgreSQL are not one atomic transaction.
- Strict experience bounds are validated after converting “more than X” to `X+1`, preventing an unsupported 61-year value.
- Null classification-version metadata is treated as uncertain historical provenance instead of throwing.

New/strengthened regression fixtures verify the above, manual employment selection, explicit null corrections and duplicate record independence. Existing analyzer debt was not mass-edited as part of classification.

Final runtime selection: **783 passed, 9 failed, 15 skipped (807 total)**. The dedicated classification groups: **77 passed, 0 failed, 1 PostgreSQL skip**. CSV classification also passes in the admin-import group. Evidence: `../classification-predeployment-final-tests.log`, `../classification-test-results/classification-predeployment-final.trx` and `../classification-predeployment-results.json`. Normal API and maintenance builds: `../classification-predeployment-api.log` and `../classification-predeployment-maintenance.log`, zero warnings/errors. Forced strict test compilation: `../classification-predeployment-build.log`; full location/multiplicity comparison: `../classification-predeployment-analyzers.json`. EF model-only and SQL generation evidence: `../classification-predeployment-model.log` and `../classification-predeployment-migration.sql`. Model/SQL commands used a dummy localhost port-1 connection solely to satisfy the design-time factory; they did not connect or apply migrations.

Reproduce the two originally reported failures in the preserved binary:

```powershell
dotnet vstest ../workday-hardening-final-artifacts/bin/JobPortal.Application.Tests/release/JobPortal.Application.Tests.dll '--TestCaseFilter:FullyQualifiedName~ExternalJobCoverageTests.ActualProviderOutputPassesThroughSharedRunnerAndCategoryMapping|FullyQualifiedName~ExternalJobEnrichmentTests.BroadDepartmentsDoNotImplyTechnologyRoles' '--logger:trx;LogFileName=classification-original-baseline-recheck.trx' --ResultsDirectory:../classification-test-results
```

Reproduce the final runtime selection (after compiling the runtime test artifact with analyzers enabled and the baseline errors recorded separately):

```powershell
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:TreatWarningsAsErrors=false
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Classification|FullyQualifiedName~AdminImportTests|FullyQualifiedName~JobDiscovery|FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~AdminJobLifecycleTests|FullyQualifiedName~ExternalJob|FullyQualifiedName~JobIngestion|FullyQualifiedName~JobSource|FullyQualifiedName~Workday|FullyQualifiedName~MultiCompanyProviderTests' --logger 'trx;LogFileName=classification-predeployment-final.trx' --results-directory ../classification-test-results
```

## Persistence and rollout limitations

`20261012054803_AddJobDescriptionClassification` follows the pending `20261011164834_AddWorkdayRunCheckpoints` migration. The snapshot includes both. Do not ship the snapshot/classification migration without its pending prerequisite implementation.

Incremental SQL only drops `CurrencyCode` NOT NULL (retaining `character(3)`) and adds nullable `ClassificationJson` text, `ManualClassificationOverrides` varchar(200), and `SalaryPeriod` integer. It has no UPDATE/DELETE, deduplication or changes to indexes/identities/status/URLs. Old populated currency values are retained. Previously valid positive enum integers keep their meanings; Unknown adds zero. Generated idempotent SQL and no-pending-model validation are structural evidence, not proof of compatibility with the deployed schema's migration history or manual drift.

The down migration drops classification provenance and replaces null currency with an empty required value. Treat downgrade as lossy; prefer a reviewed roll-forward and database backup. The application has no newly added automatic migration call; deploy after separately approved schema rollout.

Backfill's cutoff/keyset excludes later-created records; new imports are classified normally. Retry IDs retain row failures/concurrent edits. Checkpoint mode/version/database binding and exclusive file lease prevent accidental mixed-mode/concurrent resume. Duplicate records are processed independently by primary ID; no rows are merged/deleted and no unique identity columns are written. Audit contains job evidence and must be retained securely alongside its checkpoint. Historic populated fields without provenance remain uncertain, preserving existing corrections.

Initially, Docker launch failed with a host log about renaming `sailor-ingest.sock`, followed by shutdown and an absent engine pipe. The follow-up subsequently found Docker running and completed the disposable database gates; both owned containers and their anonymous test volumes have now been removed. No Docker reset, installation, production fallback or Neon connection was used.

Initial frontend evidence is the readable checkout under `D:/career-portal-frontend/career-portal-frontend-Run`: `candidateJobSearchParams.ts` has unscoped `minAmount`/`maxAmount`; `jobModels.ts` uses `currencyCode: string`; `jobFormatting.ts` falls back to USD. Follow-up inspection established the existing slider's annual rupee/LPA contract. The backend now defaults both-missing request scope to INR/yearly, requires both fields for explicit scope, and excludes unknown stored salary units. It does not invent persisted currency/period. See the follow-up for PostgreSQL contract tests; deployed frontend SHA remains unverified.

## Operator commands — do not execute until critical gates are closed

The manifest includes classification and its pending Workday/ingestion prerequisites, and excludes generated build artifacts, test results, fetched provider payloads and source-expansion data. Review all entries and the staged diff. If prerequisites must be separate commits, stage their shared-file hunks separately first; do not omit their migration/model types. This task did not stage or commit anything.

```powershell
Set-Location 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9'
git switch -c job-classification-predeployment
$releaseFiles = @(Get-Content 'docs/job-description-classification-release-files.txt')
git add -- $releaseFiles
git diff --cached --check
git diff --cached --stat
git diff --cached
git commit -m 'Add job description classification and required ingestion checkpoints'
$releaseCommit = (git rev-parse HEAD).Trim()
git push --set-upstream origin job-classification-predeployment
```

This pushes a review branch. Merge to the Render-linked branch only after release gates and deployment approval. A push to a linked branch can trigger auto-deployment; verify Render's settings before that step.

For an approved Git-backed Render service, set `RENDER_SERVICE_ID` and `RENDER_API_KEY` through your secure operator session, then deploy the approved commit explicitly:

```powershell
if (-not $env:RENDER_SERVICE_ID -or -not $env:RENDER_API_KEY) { throw 'Set the approved Render service ID and API key securely.' }
$releaseCommit = (git rev-parse HEAD).Trim()
$renderHeaders = @{ Authorization = "Bearer $env:RENDER_API_KEY"; Accept = 'application/json' }
$renderBody = @{ commitId = $releaseCommit; clearCache = 'do_not_clear' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "https://api.render.com/v1/services/$($env:RENDER_SERVICE_ID)/deploys" -Headers $renderHeaders -ContentType 'application/json' -Body $renderBody
```

The service ID and actual approved commit cannot be filled in from this checkout. Render's [official deploy API](https://api-docs.render.com/reference/create-deploy) supports `commitId`; it does not disable auto-deploys. Verify service build/start commands and inspect deploy logs/health after authorized execution.

For a staging/disposable database already migrated by an authorized operator, set `ConnectionStrings__DefaultConnection` securely to that staging connection. Do not use production Neon for verification. The command defaults to dry-run and issues a server-side read-only setting:

```powershell
if (-not $env:ConnectionStrings__DefaultConnection) { throw 'Set the explicitly approved staging/disposable connection securely.' }
dotnet build JobPortal.Maintenance/JobPortal.Maintenance.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
New-Item -ItemType Directory -Force 'C:\Users\Ashish\Documents\Codex\2026-07-27\classification-staging-audit' | Out-Null
dotnet run --project JobPortal.Maintenance -c Release --no-build -- job-classification-backfill --checkpoint 'C:\Users\Ashish\Documents\Codex\2026-07-27\classification-staging-audit\dry-run.json' --batch-size 100 --max-batches 10
```

Repeat the identical path/command to resume until RemainingJobs and RetryJobs are zero. Review intents/outcomes, evidence, review fields, failures and concurrency conflicts before any apply authorization. No production migration/apply command is included here.

## Close the disposable PostgreSQL gate

After repairing the local Docker runtime, these commands create only a new loopback-bound disposable container, with no host volumes:

```powershell
$testContainer = 'careerharbor-classification-verify-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
docker run --name $testContainer -e POSTGRES_HOST_AUTH_METHOD=trust -p 127.0.0.1:54761:5432 -d postgres:17
docker exec $testContainer pg_isready -U postgres
# Continue only once pg_isready succeeds.
docker exec $testContainer createdb -U postgres careerharbor_workday_test
docker exec $testContainer createdb -U postgres careerharbor_generic_india_test
docker exec $testContainer createdb -U postgres jobsource_run_test
$env:WORKDAY_CHECKPOINT_TEST_POSTGRES = 'Host=127.0.0.1;Port=54761;Database=careerharbor_workday_test;Username=postgres'
$env:GENERIC_INDIA_TEST_POSTGRES = 'Host=127.0.0.1;Port=54761;Database=careerharbor_generic_india_test;Username=postgres'
$env:JOB_SOURCE_RUN_TEST_POSTGRES = 'Host=127.0.0.1;Port=54761;Database=jobsource_run_test;Username=postgres'
try {
    dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ClassificationPostgresTests|FullyQualifiedName~WorkdayCheckpointPostgresTests|FullyQualifiedName~GenericIndiaJobIngestionPostgresTests|FullyQualifiedName~JobSourceRunPostgresTests|FullyQualifiedName~SalaryCompatibilityPostgresTests' --logger 'trx;LogFileName=classification-local-postgres.trx' --results-directory ../classification-test-results
} finally {
    Remove-Item Env:WORKDAY_CHECKPOINT_TEST_POSTGRES,Env:GENERIC_INDIA_TEST_POSTGRES,Env:JOB_SOURCE_RUN_TEST_POSTGRES -ErrorAction SilentlyContinue
    docker stop $testContainer
    docker rm $testContainer
}
```

Fixtures validate the specified localhost host/database names. Workday and run-store tests use isolated schemas; generic-India uses its explicitly dedicated disposable database. No application production connection setting is read. Also verify populated-schema upgrades, real CLI dry-run/resume/replay and duplicate/concurrent edits before closing gates 3/7/8. The 5,001-record checkpoint case historically takes about 17 minutes, so allow the run to finish.
