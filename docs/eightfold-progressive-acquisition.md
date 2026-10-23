# Eightfold progressive acquisition

## Existing provider patterns

Greenhouse, Lever, Ashby, SuccessFactors, and SmartRecruiters use their public listing APIs and provider-specific pagination. Their providers expose bounded batches or bounded inventories, retain stable provider identifiers, and report completeness only when the provider's listing contract supports it. `JobSourceRunner` applies the existing positive-India selection policy before ingestion, while the ingestion service upserts a job by `(JobSourceId, ExternalJobId)`. Auto-publication runs after the job is accepted by normal validation and remains governed by the existing India evidence and quality policy.

Workday is the mature provider with durable continuation. Its `IJobSourceRunCheckpoint` stores listing identity, cursor, captured inventory identity, and item-level ingestion/publication receipts in `JobSourceRun.CheckpointJson`. It resumes the same run and replays already acknowledged items safely. Ordinary scheduled sources instead use `JobSource.LastSuccessfulRunAtUtc` and `ScanIntervalMinutes`; a successful acquisition makes the next interval eligible.

For all providers, a complete listing and a reconciliation-safe census are separate claims. The runner permits absence reconciliation only when the complete inventory and the existing inventory evidence contract validate. A failed or partial scan cannot contribute an authoritative miss.

## Eightfold modes

The generic employer worker detects the Eightfold protocol automatically. An authoritative public PCSx/PCS census continues to use the existing generation, digest, and two-census evidence contract. This change does not make sitemap evidence authoritative or alter the two-distinct-authoritative-miss closure rule.

When PCSx is blocked and a robots-permitted sitemap is available, the worker returns a bounded set of public job detail observations. It stores an `eightfoldSitemapContinuation` in the existing `JobSource.EmployerStrategyJson`, including the deterministic inventory digest, numeric position, last processed stable public identity (`anchorId`), cycle status, and retained retry positions. The sitemap inventory is normalized and sorted by stable identity before hashing, so harmless reordering does not move the cursor. If the inventory changes, the next position is the first identity lexically after the saved anchor; this order is only a deterministic traversal order and makes no claim about posting chronology. Insertions before the anchor wait for the next completed cycle, while later insertions can be visited during the current cycle. A completed cycle clears the anchor on the next scheduled acquisition, so earlier insertions are revisited. The worker stores one compact identity rather than an inventory of URLs. Stable `eightfold:<numeric-id>` identities make observations idempotent across batches and generations. Detail retries continue to use the worker's existing request, time, byte, page, robots, redirect, and network safety limits.

Sitemap observations always have `IsComplete=false` and `ReconciliationSafe=false`. They can create or update individually eligible India jobs and can invoke normal auto-publication, but absence from the sitemap never closes a job.

## Continuation and execution status

`GenericEmployerScanner` sends the saved source strategy to the worker and copies the returned strategy back onto the tracked `JobSource`. The runner's ordinary unit-of-work save persists the strategy with the run result. The strategy therefore survives worker and backend restarts; no worker-local database is canonical.

A successfully fetched and processed bounded generic sitemap slice is reported as `Succeeded=true` with `StopReason=ExecutionSliceComplete` when more work remains, while `ScanComplete` and `ReconciliationSafe` remain false. The run worker pauses the same durable run for the configured continuation delay (default 60 seconds), then resumes it through the existing queued-run polling and shared source execution lock. Generic progress remains canonical on `JobSource.EmployerStrategyJson`; a successful pending slice resets the run's no-progress attempt counter even though the cursor is source-owned. The worker still applies its execution cap. Once the sitemap traversal finishes, the run succeeds and the normal source scheduler resumes at `ScanIntervalMinutes`. No additional Run Now request or database schema is required.

Eightfold INFO events include `EightfoldAcquisition`, `EightfoldContinuationLoaded`, and `EightfoldContinuationSaved`. They record the source, safe career host, protocol strategy, a short inventory-generation fingerprint, batch positions and size, returned count, next position, stable anchor, `HasMore`, cycle completion, completeness, authority, and a digest of returned external identities. They deliberately omit job content and credentials. The database strategy, rather than these logs, is the progress record.
