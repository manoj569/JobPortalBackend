# Talentd durable import remediation

## 1. Root cause

Before this change, `POST /api/internal/v1/jobs/bulk-import` in `InternalFresherBulkImportController.Import` called `IFresherBulkImportService.ImportAsync(request, cancellationToken)` and awaited the complete import. ASP.NET supplies `HttpContext.RequestAborted` as that action token. `FresherBulkImportService.ImportAsync` passed it through `ExecuteImportAsync` to `ImportCoreAsync`; the per-item loop then passed it to company resolution, ingestion, EF queries/transactions/`SaveChanges`, and publication.

Talentd uses one-item transaction chunks. Item 0 therefore committed its Job and receipt before publication committed. The synchronous action then entered item 1 and its company resolution/ingestion. The worker's roughly 30-second client timeout closed the request; Kestrel cancelled `RequestAborted`, and the propagated cancellation stopped the loop before indices 1–4. `OperationCanceledException` escaped the service, so the final `CompleteRunAsync` call after the loop never ran. The parent run's counters were written only during finalization, which explains why its Created/Published counters stayed zero even though the item-0 receipt and public Job were durable. The request handler returned 499 after 30.38 seconds.

The production evidence proves request-lifetime coupling and skipped finalization. It does not identify one proven source of the long service times. The import path inspected here uses local database lookups for company/category/identity and does not fetch the employer's application URL. Existing company/category caches are present. No production network call or query-plan trace was available, so no narrower performance cause is claimed.

## 2. Architecture implemented

For Talentd, the POST now validates the bounded batch, resolves the enabled source and `AutoPublishEnabled`, verifies the complete payload fingerprint against the ImportId, and commits the full serialized request to `AcquisitionImportRuns.StagedRequestJson` before acknowledging. A fresh request returns `202 Accepted` with ImportId, RunId, status and durable counts. Exact completed replays can still return the existing receipt. Same ImportId with changed content returns 409.

`FresherImportHostedService` polls PostgreSQL and passes only its application-stopping token to durable processing. The database row is the work queue; no request task or in-memory channel owns the job. The processor takes a PostgreSQL session advisory lock keyed by source and ImportId, rechecks the run after ownership, persists attempt/backoff state, and processes one staged run at a time. Session closure releases ownership after process failure or restart. Item commits remain bounded transactions. Unique `(RunId, ItemIndex)` and existing `(RunId, CandidateIdentityKey)` indexes prevent duplicate receipts.

Same-ImportId exact resubmission returns the existing run. If the old run has no staged JSON, it verifies the original fingerprint, expected count and any existing receipt indices/identities, then hydrates the same run with the retained request. Completed item receipts are skipped; pending/failed publication receipts are retried without creating a second Job. Stable `TALENTD:ID:<id>` acquisition identity, distinct IDs, content-neutral policy, direct ATS/employer ApplicationUrl priority, DiscoveredUrl and auto-publication remain in the existing service path.

Run counters are reconciled from durable receipts in the same transaction as receipt and publication updates, protected by a per-run transaction advisory lock. GET status remains read-only and projects the counts from receipts. Terminal Completed/Failed states have a completion timestamp; bounded retry failures retain a diagnostic summary. The worker already handles 202, polls status with the same ImportId after timeouts, validates terminal identity/counts, and does not acknowledge the outbox before terminal success.

## 3. Database changes

Migration `20261016141701_AddDurableFresherImportProcessing` adds:

- `AcquisitionImportRuns.StagedRequestJson` (`text`, nullable), `ProcessingAttempts` (`integer`, default 0), and `NextAttemptAtUtc` (`timestamp with time zone`, nullable).
- Filtered index `IX_AcquisitionImportRuns_NextAttemptAtUtc_Id` for staged, nonterminal runs.
- Filtered unique index `IX_AcquisitionImportItems_AcquisitionImportRunId_ItemIndex`.

The migration was applied, rolled back and reapplied on the disposable PostgreSQL 17.11 test databases. Existing rows remain compatible; legacy partial-run recovery was exercised. No production database was accessed. No lease columns are required because PostgreSQL session advisory locks provide exclusive ownership and are released when the session closes.

## 4. Backend files changed

The backend change covers the bulk-import controller and API DI registration; new `FresherImportHostedService` and durable processor interface; staged/replay flow in `FresherBulkImportService`; run entity and EF configuration; PostgreSQL durable queue/store and counter reconciliation; the migration and model snapshot; and acceptance/status/PostgreSQL recovery tests. Operator procedures are in [DURABLE_OPERATIONS.md](DURABLE_OPERATIONS.md).

## 5. Worker files changed

No worker source file needed changes. The existing `worker/clients.py` already handles 202 acceptance, bounded read-only status polling, same-ImportId timeout reconciliation and terminal receipt validation. `worker/talentd_import.py` already stores exact payloads in the SQLite outbox, uses 10-item bounded batches (maximum 20), resumes pending batches before discovery, and acknowledges only terminal receipts. Those paths passed 125 targeted tests and the 774-test worker suite. This worker checkout remained unchanged.

## 6. Performance

Production observed item 0 at 12.281 seconds including a 2.412-second persistence commit and 5.608-second publication phase; item 1 took 4.068 seconds through ingestion; the partial request reached 28.554 seconds before the client cancellation and returned 499 at 30.38 seconds.

On local PostgreSQL, staging a 20-item request and reading its accepted status took **444 ms**. A separate ten-item synchronous service benchmark created and published all ten in **2.289 seconds** (229 ms/item), with a no-op audit writer. These are local service measurements, not a production before/after comparison; they exclude production HTTP/network/audit overhead. Durable acceptance now completes before item resolution, creation or publication starts, so job-processing time no longer consumes the POST's request window. No timeout was increased and no unsupported throughput claim is made.

## 7. Test results

- Backend/API and test project build during the final focused run: **succeeded**, with the repository's existing command-line `RunAnalyzers=false` workaround. No analyzer rule or assertion was weakened.
- Focused Talentd/acquisition suite: **115 passed, 0 failed, 0 skipped** (24 seconds).
- Talentd PostgreSQL durability tests: **9 passed, 0 failed, 0 skipped**, using only the task-owned loopback PostgreSQL 17.11 `_test` databases. Coverage includes 20-item staging, client-token separation, process-style interruption, legacy five-item/one-published resume, per-run concurrency ownership, bounded failure, same ImportId and changed-payload conflict, counters, publication retry, finalization, migration lifecycle and ten distinct publications.
- Worker targeted client/outbox/Talentd tests: **125 passed**. Full worker suite: **774 passed**.
- Broad backend suite: **2,944 passed, 84 failed, 35 skipped**, 3,063 total, 8 minutes 29 seconds. All 84 failed test names and normalized assertion/error messages match the existing pre-Talentd failure matrix exactly; no new broad-suite regression appeared. The failures are the previously classified career lifecycle/fixture, notification snapshot, scheduling relationship and department-classification issues.

## 8. Existing production run recovery

**PREPARED ONLY — NOT EXECUTED.** Exact PowerShell/Python recovery with the existing payload, ledger, ImportId and RunId is in [DURABLE_OPERATIONS.md](DURABLE_OPERATIONS.md). It preflights the current partial status and demands byte-for-byte equality between the retained payload file and pending SQLite outbox row. It dispatches only that pending Talentd row and requires the same RunId plus Received 5, Failed 0 and Published 5. No recovery request was sent.

## 9. Scale plan

The worker's existing bounded adapter submits 10 jobs by default and never sends more than 20 in one request. Each batch has a content-derived ImportId, durable SQLite outbox payload and backend PostgreSQL staging row. The prepared scale handoff uses separate worker ledgers for stages 20, 100, 500, 2,000 and the full dynamically observed catalog, reconciling every batch before continuing. It refuses incomplete discovery and does not assume a fixed catalog size. The commands remain **PREPARED ONLY — NOT EXECUTED**.

## 10. Risks and remaining blockers

- The 84 unrelated backend test failures remain and exactly match the prior baseline. They may still require the repository's existing CI/release gate to be resolved or explicitly waived.
- Production migration, deployment, recovery and staging/production HTTP validation were prohibited and were not performed. The prepared five-item recovery must happen only after deployment and explicit operator authorization.
- Production phase-level query/lock/network traces were unavailable. Local timings show the acceptance boundary is fast, but they do not prove the cause of high production database/publication latency or production throughput at thousands of jobs.
- The worker's Talentd import dispatch is implemented as a tested library path, while `talentd_cli.py` remains dry-run only. The operator script is prepared as a direct library invocation; wiring it into a guarded production CLI can be handled separately if the operating team requires a packaged command.

## 11. Readiness

**NOT_READY**

The local durable architecture and regression gates pass, but deployment readiness still depends on the release decision for the 84 baseline failures and a staged rollout that validates the prepared operator path. No commit, push, deployment, production write, production import, historical replay, ledger edit or manual job/run status update was performed.
