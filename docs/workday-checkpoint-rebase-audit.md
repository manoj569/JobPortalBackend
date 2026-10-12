# Workday durable checkpoint replacement audit

## Root cause and evidence

The provider validates the entire listing before calling JobSourceRunCheckpoint.PrepareAsync. Previously that method rejected any change in source identity, ordered listing fingerprint, or listing total. The production checkpoint recorded 1094 observed postings and cursor 200. The next independently healthy census had 1095 postings, so PrepareAsync threw ListingSnapshotIncompatible **before** Details.Begin, pending traversal, or any yield. Consequently the provider's finally block logged Fetched/Parsed/DetailAttempted zero and generic Incomplete. The runner/worker recovered the unchanged 1094 checkpoint and its committed counters. The healthy listing log and unsafe summary described different stages, not contradictory listing validation.

A second issue was permanent restriction: an earlier ingestion-safe but drifted checkpoint's diagnostics remained reconciliation-unsafe even when later listing evidence was stable. Simply lifting that flag would mix traversals and be unsafe. Simply keeping offset 200 would skip jobs moved across the prefix. Simply restarting details without resetting receipts would collide with ordinal primary keys and DetailIdentityChanged checks. The runner also seeds its census/selection buffers before lazy provider enumeration; those buffers must be reset after the provider rebases.

## Strategy and safety

WorkdayCheckpointRebasePolicy separates immutable **source identity** (source/company, Workday origin/tenant/site, geography/test policy) from replaceable **ordered listing identity** (paths/title/location/bullet ID) and positively observed total. Source identity changes still fail closed. Stable identical snapshots resume the existing cursor.

A changed ordered fingerprint/total, or an authoritative replacement of previously restricted evidence, can rebase only when fresh diagnostics establish IngestionSafe, SnapshotStable and ReconciliationSafe, exact raw/unique counts, and zero duplicates/placeholders. An unstable incompatible replacement remains rejected. An unchanged unstable snapshot may continue positive ingestion but never certify absence. The same membership becoming stable after a drifted capture also restarts from zero; authority is not upgraded on the old cursor.

For a safe replacement, the checkpoint transaction:

1. Archives all receipt-backed cumulative counters in existing JSON, including prior archives.
2. Stores the new listing identity/total/pages/diagnostics, increments RebaseCount, clears cursor, observed jobs, provider/detail skips and CompletedResult.
3. Retires the old ordinal receipts (JobSourceRunItems only), preserving all Job rows and previously committed ingestion/publication.
4. Commits the JSON update and receipt retirement atomically under the existing running-status/lease-owner/expiry fence and EF execution strategy. Lost lease or failure rolls back both. Retried transaction uses the same replacement payload, preventing counter double-archival after uncertain commit.

Current receipts resume their existing exactly-once replay behavior. On replacement, existing stable external identities and normal source/external-ID/URL/fingerprint deduplication ingest repeated jobs as Existing; they do not create duplicate jobs. Old Created/Published counters are archived; current Updated/Unchanged/Matched and genuinely new Created/Published outcomes are added. Counters describe cumulative committed operations, so reprocessed Existing outcomes can exceed the final board's membership. Census/detail cursor/eligibility counts describe the CURRENT traversal, not historical membership.

The runner clears its old observed/accepted/selection snapshot when RebaseCount changes on the first provider yield. Provider Details.Begin resets detail progress to the replacement cursor. Worker recovery detects a rebase that happened before the next yield (e.g. budget/cancellation), uses the current checkpoint's census, provider skips and diagnostics, and retains cumulative committed counters.

Intermediate batches always deny reconciliation. Completion still requires the entire current census/details, unique external IDs, no detail/ingestion/publication failures or rejected items, selection safety, and a second full listing scan with matching current identity before existing reconciliation/source-success transaction. Historic receipt-backed ingestion failures remain conservative blockers; rebasing does not silently erase them. Detail skips are traversal facts and are reset so a replacement can refetch those details. Source policy and maximum execution limits are unchanged.

Version 3 JSON stores RebaseCount and ArchivedCounters. Versions 1 and 2 remain readable with their existing safety validation. Older binaries reject version 3 rather than interpreting retired ordinal receipts as a legacy checkpoint. New version-3 records require ingestion-safe diagnostics, a positive rebase count and archived counters. No schema/index/migration is needed. No provider/company-specific branch was added.

## Tests and coverage

WorkdayResilienceTests now exercise stable same-total continuation, growth, shrinkage, reversed order, and stable replacement of drifted evidence using real provider, runner and ingestion with an in-memory journal test double. They verify no duplicate Jobs, cumulative Created/Existing counts, exact current census/cursor, and refetch from zero only when necessary.

The production 1094 -> 1095 regression exercises the real provider and policy against deterministic HTTP fixtures with a journal/positive-ingestion counter test double: first slice cursor 200, 94 committed creations, 80 publications, unsafe drift evidence; second stable slice performs 200 details rather than failing immediately, keeps counters, and completes the 1095 replacement census after bounded slices. A separate serialization test restores the supplied state/counters through the production JobSourceRunCheckpoint.BindAsync and checks multiple archives and unchanged-replacement idempotence. This is local fixture reproduction, not a replay against Mastercard production.

Existing resilience/batch suites cover repeated/duplicate listing paths, short/incomplete pages, placeholders, failed detail calls, cancellation and bounded execution budget. Unsafe replacement/source-policy changes are explicitly tested. PostgreSQL regressions cover growth/shrink/order replacement with existing Job rows, deferred closures, receipt totals/counter recovery, and a lost-lease rebase that must leave checkpoint and receipts untouched. Existing shutdown/reconciliation-commit crash tests remain intact; old tests that asserted permanent rejection/restriction were updated to require a full safe replacement traversal before completion.

## Operational limitations

No production data, network ingestion, backfill, migration, push or deployment was performed. The production source/run identifiers were used only as supplied evidence; no live source was contacted. PostgreSQL tests require WORKDAY_CHECKPOINT_TEST_POSTGRES and guard localhost 127.0.0.1 / careerharbor_workday_test. Without that explicit disposable fixture they skip; neither API nor production connection settings are consulted.

Tradeoffs: a live board changing repeatedly can trigger repeated bounded traversals until the unchanged execution limit is reached. Substantial reprocessing is deliberate. Archived per-item receipt detail is replaced by cumulative counter/metadata summaries; Job rows are retained. Later old binary rollback cannot resume version-3 runs. PostgreSQL atomic transaction/lease behavior requires execution in a disposable database before release; in-memory tests are not proof of database concurrency.

Local validation results follow below.

## Validation results

- Broad Workday validation: **89 passed, 0 failed, 14 skipped** (103 total), covering WorkdayResilienceTests, WorkdayCheckpoint tests, WorkdayBatchIngestionTests and WorkdayIngestionDiagnosticsTests. Runtime: 7m39s. Command: `dotnet test JobPortal.Application.Tests --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~WorkdayResilienceTests|FullyQualifiedName~WorkdayCheckpoint|FullyQualifiedName~WorkdayBatchIngestionTests|FullyQualifiedName~WorkdayIngestionDiagnosticsTests' --logger 'trx;LogFileName=workday-rebase.trx'`.
- Final source regression check: **12 passed, 0 failed, 15 skipped** (27 total), including the complete first-page-changed 1094/1095 fixture, archived JSON restoration, bounded growth/shrink/order/drift continuations, unsafe/source mismatch cases and all PostgreSQL checkpoint tests. Command: `dotnet test JobPortal.Application.Tests --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~DurableRunnerRebases|FullyQualifiedName~Production1094|FullyQualifiedName~ProductionDriftReplacement|FullyQualifiedName~UnsafeReplacement|FullyQualifiedName~LegacyCheckpoints|FullyQualifiedName~WorkdayCheckpointPostgresTests' --logger 'trx;LogFileName=workday-rebase-verified.trx' --logger 'console;verbosity=normal'`. Runtime: 2m27s. These selections overlap; their pass counts must not be added as unique tests.
- PostgreSQL cases skipped because WORKDAY_CHECKPOINT_TEST_POSTGRES is absent and no disposable local installation was found. Final selection includes the added lost-lease case. Database atomicity, receipt retirement and lease concurrency remain unexecuted validation gates.
- API final Release build: **PASS**, `dotnet build JobPortal.API -c Release --no-restore`, **0 warnings / 0 errors**, normal repository warning settings.
- Tests used a command-line-only warning demotion because the existing test project has unrelated analyzer violations (including CA1707, CA1861, CA1869 and xUnit2031). Warnings remain in logs; no project warning policy or unrelated tests changed. A new nullable warning in the fixture was corrected and the final compilation reports no warning for WorkdayResilienceTests.
- Diagnostic resilience pass: 31 passed / 0 failed. A superseded final attempt was explicitly stopped after 11 passes and 15 skips while its new large fixture still used real request timing; it is recorded as **Aborted**, not a successful run or a hidden runtime failure. The rerun uses the existing FastClock fixture style and passed the strengthened first-page mutation case.
- `git diff --check`: PASS (only Git's pre-existing CRLF conversion notices).
- Ignored root logs: workday-rebase-tests.log, workday-rebase-verified-tests.log, workday-rebase-release-final.log, workday-rebase-final-tests.log. TRX evidence retained in the parent workspace: workday-rebase-initial.trx, workday-rebase-superseded.trx and workday-rebase-verified.trx.

Safe-to-push/release recommendation: **NO until the disposable PostgreSQL checkpoint/rebase transaction tests execute successfully**. The local fixture semantics and strict API Release build pass; this does not claim production database validation. No production changes, migrations, push or deployment.
