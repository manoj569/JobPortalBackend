# Generic Workday ingestion resilience

Local implementation and verification, 2026-10-12. Baseline commit: `e99692626ee68aedf04f7d05c791e4df57d76cd2`. No production requests, ingestion, source/job changes, push, deployment, migration, backfill or scheduler changes were performed. Earlier uncommitted Mastercard verification fixtures/docs were retained.

## Existing assumptions and supplied production evidence

The generic provider captures offset zero's total, reads fixed 20-slot pages, validates paths/schema, deduplicates listing paths, detects repeated ordered page signatures and stops at the advertised boundary. It does not probe a wrapped terminal page. Continuation `total:0` is treated as omitted when nonempty; those scans already re-read page zero. Duplicates/placeholders, the generic suspected search cap, failed details and duplicate detail identities cannot certify completeness.

Durable production execution freezes a source-policy identity and a listing fingerprint, journals item/publication receipts, commits item writes before advancing the cursor, and refuses incompatible inventories on resume. Before authoritative completion, it already performs a second full listing scan and compares the complete fingerprint. The runner independently checks provider/selection completeness, observed identities, failures/skips and configured test caps before staging absence closures with completion bookkeeping.

Supplied production evidence: PTC completed a 174-slot / 174-identity run successfully with all 174 details and safe reconciliation. Mastercard advertised 1,086, but pagination reached offset 1,080 with only five slots: 1,085 observations. The old provider threw `ListingEndedEarly` before re-reading the anchor or processing details; the runner treated its InvalidDataException as generic `InfrastructureFailure`, producing zero checkpoint/ingestion counts. That correctly blocked reconciliation but discarded usable observations and obscured the snapshot-specific reason.

Live-board mutation during the approximately 76-second scan is plausible; the supplied log alone does not prove it. An incomplete server response could produce the same discrepancy. This implementation distinguishes those observations using a bounded fresh anchor and never assumes that a matching new total proves final membership.

Historical patterns retained: repeated/wrapped pages, overlapping paths, omitted continuation totals, process/time-budget interruption, failed details and changing advertised counts. No company, source, tenant, site, total or tolerance is hard-coded into the new runtime behavior.

## Generic solution and snapshot states

`WorkdayJobSourceProvider` now captures an initial anchor, bounded pagination observations and, when needed, one final anchor using the existing bounded HTTP retries/time budgets.

| State | Positive ingestion | Reconciliation | Outcome |
|---|---|---|---|
| Trusted total, exact counts, structural checks pass | Yes | Eligible for the existing stronger pipeline checks | Normal behavior; no added anchor request for explicit stable totals |
| Continuation total is zero or genuinely absent | Yes | Eligible only when the final anchor is unchanged and other checks pass | Supported generically; first-page total remains mandatory |
| Legitimate nonempty short page before the initial boundary | Yes, if schema/paths/pagination are sound | No | Fetch final anchor; classify drift or persistent early ending |
| Total changes, including growth or changes that later reverse | Yes, if structurally sound | No | `ListingTotalDrift`; matching final count never upgrades authority |
| Same total but anchor path/requisition membership changes | Yes, if structurally sound | No | `ListingMembershipChanged` |
| Final-anchor HTTP/schema/retry failure | Yes, for structurally sound observed listings | No | `AnchorRevalidationFailed` |
| Repeated page / overlapping paths / placeholders | No fresh durable checkpoint or shared-runner ingestion | No | Preserve structural failure and specific diagnostics |
| Unexpected empty page / malformed listing / missing initial total | No ingestion from that failed discovery | No | Reject immediately; retain provider snapshot reason rather than infrastructure classification |
| Listing sound but details fail / duplicate detail IDs | Successful positive items remain durable | No | Failed/skipped details or duplicate identity block completion |
| Cancellation / execution budget / host shutdown | Only already committed items survive | No | Existing paused/cancelled continuation and journal behavior |

The provider's legacy `FetchSnapshotAsync` can still expose deduplicated partial observations for structural anomaly diagnostics, as existing tests require. The shared runner rejects any batch marked `IngestionSafe:false` before ingesting it. Production durable processing rejects such inventories before preparing a cursor or fetching details.

Initial/final count equality is evidence about quantity only. A drifted collection could include removed records and omit newly added ones while keeping the same count. Accordingly, observed shrink/growth **never** becomes `Succeeded` or `ReconciliationSafe` merely because final total equals collected count. Positively fetched jobs retain existing geography, classification, manual overrides, deduplication, publication and URL behavior.

## Checkpoint and membership safety

For a sound but non-authoritative observation set, checkpoint `ListingTotal` is the frozen observed path count, so cursor/remaining-work accounting has a reachable bound. `ListingDiagnostics.InitialReportedTotal` separately retains the actual initial advertised count. Source/listing fingerprint fencing and item ordinals are unchanged; no incompatible inventory is rebound or receipts silently reset.

The restriction joins the existing checkpoint JSON. Non-authoritative checkpoints use **version 2** with `IngestionSafe:true` and `ReconciliationSafe:false`. The current reader requires those safety fields. Old binaries reject version 2 rather than silently dropping the restriction on rollback. Existing stable version-1 checkpoints, including JSON without diagnostics, remain supported. If a compatible version-1 inventory develops observed drift on resume, it is downgraded to restricted version 2; it cannot later be upgraded to authoritative by a subsequent stable anchor.

Each resumed slice can process the same frozen positive set using existing receipts/cursors. When the last slice finishes, the restricted run terminates **Unsafe**, retaining positive ingestion/publication and preserving prior jobs/success time. It cannot stage an authoritative CompletedResult. A later **separate run** may capture a stable census and, after the existing full final membership rescan and all runner gates, reconcile normally. An actually incompatible inventory remains Unsafe and requires a new run.

No EF model, table, column, index or migration changed. The only persistent addition is optional JSON evidence and the safety version fence. Rollback to an older backend cannot resume restricted version-2 runs; resolve those runs deliberately instead of rewriting their version or clearing their restriction.

## Bounded revalidation and retry

There is one final-anchor phase for a structurally sound scan when continuation totals were omitted, a nonempty short page ended early or a reported total changed. It uses existing MaximumAttempts, HTTP pacing, caller cancellation and run/execution budgets. Cancellation is not swallowed by the anchor-failure handler.

No immediate full rescan loop was added. Pagination remains bounded by the initial safe record limit and page signatures; a short ending stops scanning. Repeated pages terminate discovery, and duplicate/placeholder anomalies do not trigger additional anchor retries. Stable explicit-total cases issue the same discovery requests as before. Stable omitted-total cases retain their previous single final anchor. The existing durable final full inventory validation remains required for authoritative completion, and is skipped when the frozen run is already restricted because it cannot authorize closure anyway.

`RetryRecommended` is diagnostic advice, not an immediate retry command. Existing bounded slices, delayed continuation, execution/no-progress caps and scheduling cooldowns remain unchanged. Restricted runs finish Unsafe after processing their frozen set; a new execution/run should be explicitly scheduled later through the existing workflow. No scheduler settings were changed.

## Reasons and structured observability

Specific reasons include `ListingTotalDrift`, `ListingMembershipChanged`, `ListingEndedEarly`, `AnchorRevalidationFailed`, `RepeatedPage`, `DuplicateListingPaths`, `ListingPlaceholders`, `FailedDetails`, `DuplicateExternalIds`, `SnapshotIncomplete` and the existing `ListingSnapshotChangedAtCompletion`, checkpoint incompatibility and continuation reasons. Existing malformed-response enum reasons are retained. Genuine initial HTTP/infrastructure failures still remain infrastructure failures.

Generated structured event **4390, WorkdaySnapshotValidation** records Source, InitialReportedTotal, FinalReportedTotal (nullable when no anchor was required/completed), RawSlots, UniquePaths, DuplicatePaths, PlaceholderCount, FirstPageChanged, LastOffset, LastPageSize, SnapshotStable, IngestionSafe, ReconciliationSafe, ReasonCode and RetryRecommended. The terminal record reflects detail/identity failure and actual provider completion. Existing listing-validation, HTTP failure, detail progress and worker summaries remain.

The same optional `ListingDiagnostics` is carried through provider batches, aggregate snapshots, checkpoint JSON and `JobSourceRunResult`/Admin diagnostics. Worker snapshot-error handling preserves captured counters/evidence instead of replacing them with an empty result. No descriptions, application bodies, credentials or candidate data are logged.

`SnapshotStable` describes listing evidence. Listing authority is only one gate: details, selected jobs, persistence, test limits and runner identity checks can still make the actual run unsafe. During early batches, ReconciliationSafe remains false until completion. Count fields must not be used independently as authorization.

## Regression matrix

| Required case | Local coverage |
|---|---|
| Stable 174 board, including omitted total | WorkdayResilienceTests count matrix; existing generic/PTC tests |
| 1,086 → 1,085, final 1,085 | Positive observations, drift reason, reconciliation false |
| 1,085 collected, final 1,086 | Growth reason, no false reconciliation |
| 1,086 advertised, 1,085 collected, final still 1,086 | ListingEndedEarly, positive-only restriction |
| Larger shrink / growth (1,050 / 1,100) | No tolerance; non-authoritative despite matching fresh count |
| Offset 200 repeats page zero | Bounded discovery, RepeatedPage, no authority |
| Duplicate paths / placeholders | Structural diagnostic; shared-runner ingestion rejected |
| Literal missing continuation total | Supported; trusted initial total and unchanged anchor required |
| Missing initial total, including empty board | Rejected; never turns omission into an authoritative empty census |
| Exact short final page | Completes normally with unchanged request count |
| Unexpected empty / malformed path | Rejected before details; no reconciliation |
| Anchor HTTP failure | Bounded retries; safe positives only, no reconciliation |
| Pagination / detail cancellation | Cancellation propagates; no false completion |
| Failed details | Successful items preserved; failed detail blocks reconciliation |
| Same source rerun | Stable IDs, zero duplicate creations, published inventory preserved |
| Multiple total changes during a scan | One anchor, no infinite rescan, remains unsafe |
| Drifted checkpoint across five database executions | PostgreSQL receipts/cursor/version-2 restriction retained; no closure/success-time advance |
| Later separate stable run | Full membership validation permits legitimate closure once; existing inventory remains duplicate-safe |
| Legacy/invalid restricted JSON | Version 1 readable; version 2 missing the safety evidence rejected |

## Local verification evidence

Evidence is retained outside the repository in `../workday-resilience-artifacts/`; build outputs are in `../mastercard-release-artifacts/` and `../workday-resilience-release/`. No generated binaries, logs or database state need to be committed.

| Gate | Status | Evidence |
|---|---|---|
| New generic resilience matrix | PASS | `resilience-matrix.trx`: 21 passed, zero failed/skipped |
| Previously reported Workday verification suite | PASS | `existing-190.trx`: 190 passed, zero failed/skipped; includes provider, PTC/PwC registration, Mastercard verification, classification and reconciliation tests |
| Shared runner, ingestion, publication and deduplication | PASS | `runner-publication-deduplication.trx`: 70 passed, zero failed/skipped |
| Drifted PostgreSQL checkpoint / later separate reconciliation | PASS | `resilience-postgres-local-transport.trx`: one passed, zero failed/skipped; five slices retain all 43 published jobs and 42 positive receipts; only the subsequent stable run closes the missing job |
| Full PostgreSQL checkpoint suite, single invocation | FAIL | `checkpoint-postgres-full.trx`: 13 passed, one failed, zero skipped; the 5,001-job case stopped after one execution instead of six |
| Stable scans during resume cannot upgrade a restricted run | PASS | `postgres-stable-resume-final.trx`: strengthened assertion passes across five executions; runtime code, budgets and safety assertions were unchanged |
| Isolated 5,001-job completion and duplicate-safe repeat | PASS | `postgres-5001-isolated.trx`: one passed, zero failed/skipped (16m56s); six executions, 5,001 observed identities, 64 created/published, 4,937 geography skips; repeat creates/publishes zero duplicates |
| Strict Release API | PASS | `release-api.log`: zero warnings/errors |
| Strict Release maintenance | PASS | `release-maintenance.log`: zero warnings/errors |
| New Workday analyzer findings | PASS | `analyzer-comparison-final.json`: baseline 184, current 184, added zero, removed zero in the final strict compilation |
| Entire test-project strict analyzer gate | FAIL (baseline) | `strict-test-build.log`: exactly 184 baseline errors; runtime test compilation uses an invocation-only `TreatWarningsAsErrors=false` with analyzers enabled |
| Overall production readiness | BLOCKED | Strict test-project baseline failures and the unexplained initial full PostgreSQL-suite failure prevent an unconditional release sign-off |
| Production/source execution | BLOCKED, intentionally not executed | Deployment and a real-board source run require a later authorized operation |

The first two PostgreSQL attempts failed in Npgsql connection/type-loading initialization, before the feature test ran. Their original logs/TRX files are retained. The owned PostgreSQL 17.11 container was bound only to `127.0.0.1:54762`, used the explicitly disposable `careerharbor_workday_test` database and had no host data volume. Its supported local connection used `SSL Mode=Disable`; the fixture's five-second connection timeout and all provider/regex budgets were unchanged. This connection succeeded, applied the existing migrations in a random fixture schema and executed the positive-only checkpoint/reconciliation assertions. No production Neon connection was inspected or used. After verification, zero fixture schemas remained. The container's exact ID and task label were verified before removing only that container and its anonymous volume; `database-cleanup.log` records cleanup. Other containers were left untouched.

The initial full PostgreSQL run's large-inventory failure took 10m24s and does not include the resulting stop reason. It is a **newly observed local verification failure**, not a confirmed pre-existing failure or a proven regression caused by this implementation. Per-execution test diagnostics were added without changing assertions or runtime budgets. The isolated rerun passed six first-run executions and the duplicate-safe repeat. Its result does not erase the failed full-suite artifact or establish that failure's cause. The full suite has not been rerun green as a single invocation; resolve or explicitly disposition this failure before an unconditional production sign-off.

There are **295 distinct passing cases** across the selected suites: 21 resilience, 190 existing Workday/classification/reconciliation, 70 shared runner/publication/deduplication and 14 PostgreSQL checkpoint cases (the union of the full run and successful isolated reruns). No selected case was skipped. The initial full database run still has one failure; the two earlier initialization attempts each have one failure. These are retained as separate investigation attempts, rather than counting their duplicate cases as additional successes. `verification-summary.json` records per-run counts, case names and artifact hashes.

The nine persistent unrelated runtime failures previously reproduced against preserved baseline binaries remain documented in `job-description-classification-predeployment-verification.md` and `job-description-classification-database-verification.md`: coverage/category mapping (one), broad-department enrichment (one), normalization skip accounting (one), Greenhouse expiry (one), manual-run success (one), SuccessFactors pagination (four). They were not rewritten or rerun in this task's targeted suite. The prior timing-sensitive Workday synthetic HTTP test passes in the current 190-test run. This feature's scoped suite passing does not turn the broader repository into a green release.

Concurrent public-search changes appeared in the shared checkout during verification. `PublicJobRepository`, `CandidateJobKeywordQuery`, public-search tests and their audit document belong to that independent work; they were preserved and are excluded from this feature's file manifest. Earlier Mastercard fixtures, verification tests and documents were also preserved.

### Feature file manifest

- `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs`
- `JobPortal.Application/Abstractions/Jobs/ExternalJobSourceSnapshot.cs`
- `JobPortal.Application/Abstractions/Jobs/IJobSourceRunCheckpoint.cs`
- `JobPortal.Application/Abstractions/Jobs/JobSourceRunResult.cs`
- `JobPortal.Application/Services/JobSourceRunner.cs`
- `JobPortal.Persistence/Repositories/JobSourceRunCheckpoint.cs`
- `JobPortal.API/Services/JobSourceRunWorker.cs`
- `JobPortal.Application.Tests/WorkdayBatchIngestionTests.cs`
- `JobPortal.Application.Tests/WorkdayCheckpointPostgresTests.cs`
- `JobPortal.Application.Tests/WorkdayResilienceTests.cs` (new)
- `docs/workday-ingestion-resilience.md` (new)

### Reproduction commands

Run from the repository. The following are local build/test commands, not deployment commands. The strict test-project build is expected to report the existing analyzer failures; the following compilation override is only for executing runtime tests and does not disable analyzers or change repository configuration.

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --artifacts-path ../workday-resilience-release
dotnet build JobPortal.Maintenance/JobPortal.Maintenance.csproj -c Release --artifacts-path ../workday-resilience-release
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --artifacts-path ../workday-resilience-release
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --artifacts-path ../workday-resilience-release -p:TreatWarningsAsErrors=false
$testDll = '../workday-resilience-release/bin/JobPortal.Application.Tests/release/JobPortal.Application.Tests.dll'
dotnet vstest $testDll --TestCaseFilter:'FullyQualifiedName~WorkdayResilienceTests'
```

For PostgreSQL, provision a separately owned loopback-only disposable container/database. Never substitute an API/Neon connection. The fixture rejects a different host or database name, migrates only its random schema and removes that schema afterward.

```powershell
$env:WORKDAY_CHECKPOINT_TEST_POSTGRES = 'Host=127.0.0.1;Port=54762;Database=careerharbor_workday_test;Username=postgres;SSL Mode=Disable'
try {
    dotnet vstest $testDll --TestCaseFilter:'FullyQualifiedName~WorkdayCheckpointPostgresTests'
} finally {
    Remove-Item Env:WORKDAY_CHECKPOINT_TEST_POSTGRES -ErrorAction SilentlyContinue
}
```

## Existing sources and remaining risks

The fix applies automatically to every registered generic Workday source, including PTC, PwC, Accenture, Mastercard and future companies. No source IDs or company configuration changes are required. The stable 174 behavior, application links, India selection, category/classification path, optional salary handling, publication rules and ownership/deduplication guards remain unchanged.

The public board does not supply a transactional snapshot token. Unobserved churn cannot be ruled out from a count/first-page anchor alone; production durable full membership comparison is still required. Revalidation HTTP failures, persistent membership churn, incompatible resume inventories, unsupported/malformed schemas and repeated pages remain conservative failures. Positively observed jobs may no longer exist by the end of a mutable scan; that does not justify deleting other existing jobs.

Independent baseline release failures remain separate from this generic resilience feature. Testing a deployed Mastercard source requires an operator-approved backend push/deployment first, then a manual source run through the existing workflow. Neither push/deployment nor any production run was executed in this task.
