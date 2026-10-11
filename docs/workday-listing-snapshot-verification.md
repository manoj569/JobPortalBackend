# Workday listing snapshot validation

Verified 2026-10-11. No CareerHarbor production API/database was accessed, no production ingestion was executed, and no source was activated. Existing untracked TestResults, accenture-workday-page1.json and generic-india-migration.sql were preserved. No commits, staging, push or deployment.

## Root cause and evidence

The provider compared every page's `total` with page zero's advertised total. Workday CXS can return `total=0` on nonempty continuation pages; that value does not mean the inventory became empty. The old comparison set `listingChanged=true`, despite an exact raw count, unique paths and successful details. Final completeness then became false and safe reconciliation correctly refused to close jobs.

The final `WorkdaySourceFailure Stage=snapshot Offset=176` passes **rawEntriesRead**, not the offset of a listing request. With a 20-row page size, requests for 176 jobs stop at offset 160 after its 16 rows. There is no terminal request at 176 in this implementation. The existing early break at rawEntriesRead >= expectedTotal already handles exact boundaries; it is retained.

Five bounded public read-only search POSTs against [PTC's public Workday board](https://ptc.wd1.myworkdayjobs.com/PTC) / `https://ptc.wd1.myworkdayjobs.com/wday/cxs/ptc/PTC/jobs`, each with empty facets, limit=20 and empty searchText, returned:

| Offset | HTTP | Reported total | Rows | Observation |
| --- | --- | --- | --- | --- |
| 0 | 200 | 176 | 20 | Global advertised total |
| 20 | 200 | 0 | 20 | Valid next page, different first/last job paths |
| 160 | 200 | 0 | 16 | Valid final partial page |
| 176 | 200 | 176 | 20 | Wraps to the initial page; same first/last paths |
| 180 | 200 | 176 | 20 | Wraps to the initial page; same first/last paths |

These searches do not write data, fetch job details, or run CareerHarbor ingestion. The observed behavior reproduces the false change flag and explains the supplied production metrics without needing a real inventory change. Historical raw production responses were not available; an additional genuine change in that historical run cannot be independently excluded. New structured diagnostics expose actual request offsets and totals to resolve future cases.

## Generic fix and retained guards

* Only a **nonempty continuation page** with `total=0`, after a positive initial advertised total, is treated as omitted total metadata. Page-zero zero is still a genuine empty board and cannot coexist with nonempty rows. Empty continuation pages before the expected count still fail closed.
* For scans with omitted continuation totals, one extra page-zero request revalidates the initial total, raw page size and ordered original external paths before details/certification. It does not contribute new slots, jobs, IDs, duplicates or counters. A changed anchor marks the scan incomplete.
* Positive/negative changed continuation totals, repeated pages, cross-page duplicate paths, placeholders, count overshoots/undershoots, failed details, duplicate external IDs and the suspected 2,000 search cap remain unsafe. The final completeness predicate remains derived from these checks; IsComplete is never forced true.
* No probes beyond the advertised count are introduced. Empty-tail and wrapped-tail tenants are both supported by stopping at the advertised boundary.
* The anchor uses the existing validated public endpoint, request pacing, timeout, MaximumAttempts, backoff/retry handling, bounded response size and run budget. No new setting or unbounded snapshot restart. Anchor HTTP exhaustion fails closed; changed census/anchor uses the existing incomplete-scan path. Valid parsed jobs can still be retained while reconciliation is blocked.
* Logs add WorkdayListingValidation (actual offset, expected/reported totals, raw slots, total omission and page/anchor stage), plus ListingTotalChanged / ListingAnchorChanged at the actual request offset. Existing final summary and ListingSnapshotChanged conventions remain.

Workday offers no atomic snapshot/version token in these responses. Counts, uniqueness, existing detail checks and the additional first-page anchor detect the covered unsafe conditions; they do not prove the absence of an unobserved same-count change confined to later pages. No claim of a transactional upstream snapshot is made.

Company-independent code only. Pagination size/order, request budgets, stable IDs, canonical URLs, duplicate persistence prevention, India eligibility, category handling, publication authority and safe reconciliation code are unchanged. No company registration/configuration changes, schema changes or migrations.

## Regression coverage and results

Added coverage: 20/40 exact boundaries and 176 partial boundary; empty/wrapped tail endpoints not requested; zero continuation totals with a stable anchor; changed nonzero totals; changed anchor count and same-count paths; repeated zero-total pages; premature empty pages; bounded anchor HTTP retries; and actual repository ingestion where an unsafe scan with a missing upstream job retains it, repeat processing is idempotent, and a later certified scan safely closes only the missing source-owned vacancy.

* Release API build: **PASS, 0 warnings, 0 errors**, normal analyzers, elapsed 2m55s. Evidence: parent-workspace workday-snapshot-api-release.log.
* Focused correctness regressions: **79 passed, 0 failed, 0 skipped**, elapsed 2m02s. Includes the new cases, existing Workday adapter retry/timeout/pagination/details cases, PTC/PwC source registration and source-runner regressions. Evidence: workday-snapshot-regressions.log and JobPortal.Application.Tests/TestResults/workday-snapshot-regressions.trx.
* Actual repository safety regression (synthetic upstream, EF in-memory): an initial 46-job feed including one foreign vacancy creates/publishes 45. A subsequent 45-job feed with a changed anchor reports **Created=0, Existing=44, Published=0, Closed=0**, preserves the previous successful-run timestamp and all Jobs IDs. The same feed with a stable anchor reports **Created=0, Existing=44, Published=0, Closed=1** for the genuinely missing source-owned vacancy. No production counts are claimed.
* Diff whitespace validation: PASS. No staged changes.
* Normal test-project analyzer check: still **184 pre-existing errors**, with per-file/per-rule diagnostic counts identical to the saved prior normal-build baseline; no new errors introduced by these changes. Focused runtime tests compiled only the test project with RunAnalyzers=false and BuildProjectReferences=false, using the separately normally built production assemblies.

Commands (repository root):

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:BuildProjectReferences=false -p:RunAnalyzers=false -p:UseSharedCompilation=false --filter '(FullyQualifiedName~WorkdayBatchIngestionTests&FullyQualifiedName!~MeasureSyntheticWorkdayPipelineWithRealRepositories)|FullyQualifiedName~WorkdayJobSourceProviderTests|FullyQualifiedName~WorkdayIngestionDiagnosticsTests|FullyQualifiedName~PtcWorkdaySourceRegistrationTests|FullyQualifiedName~PwcWorkdaySourceRegistrationTests|FullyQualifiedName~JobSourceRunnerTests'
```

Release build uses normal analyzers. Focused runtime tests use the established command-line test-project analyzer override; no analyzer configuration is edited. The repository's test project retains 184 verified baseline analyzer errors, separate from a normal API build. Synthetic large throughput benchmark cases are excluded from the focused correctness filter; no full production/live ingestion is tested.

## Files changed

1. JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs — continuation total interpretation, bounded anchor validation and diagnostic logs.
2. JobPortal.Application.Tests/WorkdayBatchIngestionTests.cs — targeted regressions; existing changed-total fixture now uses a real differing positive total rather than Workday's zero sentinel.
3. docs/workday-listing-snapshot-verification.md — this evidence and release report.

## Deployment requirements

No database migration, environment variable, source re-registration or provider setting changes are required. Deploy the reviewed API fix through the usual approved release process. Drain existing runs/old worker instances before replacing binaries; disabling the scheduler alone does not stop queued durable runs. Preserve current provider budgets and source settings.

After deployment approval, an operator may run the existing PTC source once and inspect WorkdayListingValidation page/anchor logs, count/identity/detail checks, ScanComplete and ReconciliationSafe. Reconciliation safety still depends on the shared pipeline, including no ambiguous eligibility or validation/persistence/publication failures and no configured test cap. A complete listing does not guarantee reconciliation is safe. Repeat a successful run to verify no duplicate inserts and no inflated Published count; Existing reflects actual matches. The supplied New=0, Existing=44, Published=0 are historical production evidence, not newly claimed counts.

Do not execute a production run until deployment and operational approval. This task performs neither operation.
