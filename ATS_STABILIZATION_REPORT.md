# CareerHarbor ATS ingestion and accounting report

Backend repository: `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9`

This pass enforces positive India eligibility before external ATS records can create or update a `Job`, stabilizes Greenhouse board identity, and aligns run-status progress counters. No frontend source was changed, no production database was queried or changed, and nothing was deployed or pushed.

## India-only persistence rule

The source runner now partitions the complete provider census using `IndiaJobEligibilityPolicy` before constructing the ingestion list. Only `Accepted` records reach persistence. `Rejected` (non-India or explicitly India-excluded) and `Review` (unknown, ambiguous, or contradictory evidence) records remain in the provider census for diagnostics and identity reconciliation, but they are not inserted, updated, published, or allowed to mutate a pre-existing job.

`JobIngestionService.IngestAsync` repeats the eligibility check before deduplication, lookup-driven mutation, or creation. This is the persistence-boundary guard for callers that bypass the source runner; bulk ingestion delegates through that same guarded method. The rule applies generically to Greenhouse, Lever, Ashby, Workday, and SuccessFactors.

Regression coverage checks foreign, generic remote, unknown, contradictory, and positive India cases across all five ATS types. A direct ingestion test also tries to update an existing India job with a foreign posting at the same canonical URL and verifies the existing row remains unchanged. No draft, review, unpublished, or auto-publish state is used as a substitute for the persistence guard.

## Greenhouse board configuration

The shared resolver is used both by create/update validation and by the provider. For trusted Greenhouse board hosts, a board token derived from the URL must exactly match the supplied `AtsIdentifier`; otherwise the request is rejected with `GREENHOUSE_BOARD_CONFIGURATION_CONFLICT` before any HTTP request. The resolver preserves provider path casing and does not guess a company-to-board mapping from an employer-hosted URL.

Tests cover the canonical Airbnb, Stripe, and Enterpret forms, mismatched URL/identifier pairs, case-sensitive board tokens, and admin update rejection. For employer-hosted career URLs with no trusted Greenhouse token in the path, the identifier remains the board identity; this is intentionally generic and contains no company-specific branch.

The repository establishes that a stale identifier paired with a token-bearing board URL would be rejected before provider HTTP. A generic employer URL plus stale identifier could instead direct the provider to that identifier’s board. Production `CareerPageUrl` and `AtsIdentifier` values were not available, so this code-path analysis does not claim which configuration was persisted for a particular employer. Production verification remains required.

## Run-counter root cause and correction

The source-level defect was a mixed counter scope. `Created` is cumulative across committed Workday checkpoint receipts, but the legacy `Processed` value could represent a traversal/checkpoint progress count. The worker persisted that `Processed` value separately from run-wide `TotalReceived`, and the status response exposed both. A status consumer reading `Processed` as “Total Received” could therefore display a smaller, traversal-local value beside cumulative `Created` (for example, 300 beside 400).

The traced persistence path has no second `Created` increment during publication. A successful ingestion receipt increments `Created` once; publication only changes publication counters. Durable checkpoint receipts are keyed by provider ordinal and external identity. A replay of the same committed ordinal reuses its receipt, and Workday’s resume path excludes checkpointed identities from the new batch. Rebase archives receipt outcomes and retires old receipts atomically; it does not add a second creation receipt for the same stable item.

The fix makes `TotalReceived` the single run-status progress scope: progress snapshots normalize `Processed` to `Counters.TotalReceived`, the worker persists both fields from `TotalReceived`, and the admin run-status DTO returns `Processed` from `row.TotalReceived`. The DTO also exposes parsed and India eligibility diagnostics from the same cumulative result. Running and final status therefore use the same scope.

The literal production tuple `TotalReceived=300, Created=400` cannot be reproduced from the repository test data, and production run rows/diagnostics were not supplied. The mixed-field-scope defect is reproducible from the old worker/DTO mapping and is fixed; no claim is made that a different undocumented production data corruption is ruled out without inspecting that run’s saved diagnostics.

Regression tests verify:

- Three batches of 100 eligible unique jobs produce `Created=300`, never 600.
- A replay of those committed identities produces `Created=0` and `Existing=300`.
- Publication increments `Published` without changing `Created`.
- Durable interrupted/retry counters preserve committed receipts without adding creation outcomes twice.
- Running progress, persisted status fields, and final API response keep `Processed == TotalReceived`.
- API response diagnostics remain cumulative and `Created <= IndiaEligible` in the 300-job example.

`IndiaEligible` and persistence outcomes are separate dimensions: geographic eligibility is counted before persistence, while validation failures/skips are retained as explicit ingestion diagnostics. `Published` is also separate from `Created`; disabled auto-publish and review outcomes do not alter creation totals.

## Root-cause summary

The India leakage path was that geography was evaluated only as a later publication/review decision, after a `Job` could already be inserted or updated. The shared source-selection step and a second guard in `JobIngestionService` now stop every non-accepted geography decision before persistence.

The counter mismatch path was that legacy `Processed` and run-wide `TotalReceived` were stored and surfaced as separate values, even though the run’s `Created` count was cumulative. `TotalReceived` is now authoritative for both fields and the API status value.

For Greenhouse, create/update validation and provider execution now share the same board resolver, so token-bearing URL/identifier conflicts fail deterministically before HTTP instead of silently selecting one value.

## Legacy rows and safe cleanup

The number of legacy non-India or unknown ATS rows is **unknown** because no production database access or export was provided. Do not delete rows based on a location-only SQL predicate: eligibility considers structured country codes, additional locations, explicit remote eligibility, exclusions, and contradictions.

Use a read-only application-side audit first: enumerate source-owned jobs (`JobSourceId` plus external identity), reconstruct the original geography facts from stored source metadata where available, evaluate each with the deployed `IndiaJobEligibilityPolicy`, and export grouped counts by `Accepted` / `Rejected` / `Review` with IDs only in a protected admin report. Manually curated or source-unowned jobs should be excluded. Any later cleanup should be a separately reviewed, source-scoped operation with a dry-run count and reversible archival plan; this pass performs no cleanup.

## Verification

The full solution Release build passed with **0 warnings and 0 errors**. The focused ATS/India/counter/checkpoint suite passed **315/315**. The provider diagnostics/Workday recheck passed **61/61**, and the isolated Workday `Retry-After` regression passed **1/1**.

The last full solution run completed with **2,752 passed, 84 failed, 32 skipped** (2,868 total). The failures were outside the changed ATS paths: 29 `CareerSessionTests`, 29 `CareerTrustTests`, 11 `CareerSessionReviewTests`, 5 `CareerAnalyticsTests`, 3 `CareerFinanceTests`, 3 `CareerReservationExpiryTests`, 1 `CareerSchedulingMigrationTests`, 1 `ExternalJobEnrichmentTests`, 1 `NotificationEligibilityTests`, and 1 `NotificationOutboxTests`. No test in the final run failed in the Greenhouse, Workday, SuccessFactors, India eligibility, source runner, or run-counter groups. The skips include opt-in PostgreSQL tests for which this environment has no configured disposable database. These broad failures were not altered as part of ATS stabilization, and this report does not claim the full backend suite is green.

Build/test evidence is under `Logs/IndiaGreenhouse/`: `ats-solution-release-build-final.log`, `ats-focused-final.trx`, `ats-provider-diagnostics-recheck.trx`, `workday-retry-after-final.trx`, and `ats-solution-broad-final-confirmed.trx`.

Production saved Greenhouse/Workday configuration, the exact historic 300/400 status row, and hosting behavior still need post-deployment verification. No production writes were made.

The existing authenticated admin script reads safe source fields (ID, provider, career URL, identifier, active, auto-publish, interval) for Airbus, Walmart, J&J, Stripe, and Airbnb:

```powershell
& 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\VERIFY_ATS_AFTER_DEPLOYMENT.ps1'
```

After deployment, this command reads configuration and enqueues Airbus and Walmart exactly once each, then polls their run-status endpoints:

```powershell
& 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\VERIFY_ATS_AFTER_DEPLOYMENT.ps1' -RunAirbusAndWalmart
```

The script uses the existing Administrator bearer token entered through hidden input, targets `https://job-portal-lgjc.onrender.com`, and does not retry an uncertain enqueue. J&J is read as the control source. No source configuration is changed.

**ATS-SCOPE READY FOR STAGED REVIEW. FULL BACKEND SUITE NOT GREEN. PRODUCTION VERIFICATION REQUIRED.**
