# Workday bounded ingestion

Workday uses the existing durable Run Now queue and source locks. No schema,
source identifier, India facet, scheduler setting or production timeout change
is required to use this patch.

The runner consumes bounded detail batches through the existing normalizer,
category resolver, ingestion upserts and publication service. Each ingestion
item retains its existing durable commit; a later failure cannot roll it back.
Enumeration provides backpressure: all detail tasks finish before a batch is
yielded, and the next batch starts only after ingestion consumes the previous one.

Resume is safe replay, not an offset checkpoint. Persisted `(JobSourceId,
ExternalJobId)` identities are the durable checkpoint. A new attempt re-reads
the listing and refreshes/reuses those jobs, inserting only missing jobs. This
also avoids skipping records when Workday listing order changes between attempts.
Requests for previously committed jobs are repeated; there is no claim of
exactly-once HTTP fetching. Existing lease recovery, bounded attempts and source
cooldown remain unchanged. A failed/incomplete attempt needs a new Run Now or
the next scheduled scan; it does not spin in an immediate retry loop.

## Optional Render settings

Defaults below apply without configuration changes. Configure under
`JobAggregation__Workday__` only if needed:

| Setting | Default | Valid range |
| --- | --- | --- |
| BatchSize | 20 | 1–100 |
| DetailConcurrency | 4 | 1–4 |
| RequestTimeoutSeconds | 15 | 1–60 |
| RunBudgetSeconds | 1800 | 1–7200 |
| MaximumAttempts | 3 | 1–5 |
| RequestSpacingMilliseconds | 250 | 250–5000 |

The budget measures wall time, including time the consumer spends ingesting
batches. It cancels provider requests and is checked before the next batch and
final completion. A currently committing ingestion batch finishes under its
existing database/caller cancellation limits, so the budget is not a hard
database transaction deadline. Host shutdown and lost/failed heartbeat cancel
the existing worker token independently. Workday request timeout covers headers
and bounded body reads. Transient retry uses exponential backoff and jitter.
429 Retry-After is honored and remembered per verified Workday host across
clients/runs in this process (not shared with other ATS labels); a delay over 30 seconds fails the request closed
instead of retrying early or holding a worker indefinitely. No retry on 4xx
other than 429, corrupt JSON or invalid schema.

Only the final enumeration message can certify completeness. Capped searches
(2,000 or more advertised jobs), placeholders, duplicate paths/IDs, repeated
pages, changing totals, failed details, configured test limits, cancellation
and failed/skipped ingestion cannot reconcile or advance LastSuccessfulRunAtUtc.
Existing jobs are preserved. Missing descriptions are not replaced with listing
titles. Missing expiry/experience may legitimately mean NeedsReview rather
than Published under the unchanged production quality gate.

## Operator verification after deployment approval

1. Use the existing admin Run Now endpoint; expect 202 and poll its returned
   status URL. Browser disconnection does not cancel execution.
2. Inspect `WorkdayListingSummary` (raw slots, unique paths, duplicates,
   placeholders), `WorkdayBatchReady` (detail attempts/successes/failures),
   `Eligible batch received`, ingestion counters and publication counters.
   `WorkdaySourceFailure` identifies request/status failures; `WorkdayRunEnded`
   distinguishes RunBudgetExceeded and caller cancellation. Worker logs distinguish host shutdown from heartbeat/
   lease cancellation; heartbeat logs further identify lost lease vs failure.
3. Verify source-owned persisted jobs in existing admin job APIs. Check Created,
   Updated, Unchanged/Matched, Skipped/Failed, Published and NeedsReview rather
   than assuming HTTP 200 means import success. Generic remote jobs outside the
   verified Accenture India facet do not gain IN eligibility.
4. After an interrupted run, queue a new attempt after recovery/cooldown. Expect
   reuse/update of previously committed IDs, no duplicate source identities and
   insertion of remaining jobs. A complete verified scan alone may reconcile.
5. For a small validation import use the existing source-specific TestImportLimit
   (e.g. 20). This is an ingestion cap, not a claim of provider completeness or
   a provider-network cap; LastSuccessfulRunAtUtc must not advance.

No live Workday/Neon requests are needed by the automated tests. PostgreSQL
production behavior still depends on its existing unique indexes and advisory
locks; in-memory tests are not a substitute for an operator-approved deployment
verification.
