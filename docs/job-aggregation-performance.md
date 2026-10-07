# Large-source ingestion: local measurements and safety

The generic SuccessFactors provider is unchanged by this pipeline optimization.
No migration, production configuration change or AI enrichment is required.

## Findings

Metadata enrichment is synchronous bounded regex/text normalization; it makes no
network requests. The original first-import path for a source-owned job used a
source-identity lookup, an unlocked URL/fingerprint/fuzzy pass, and a second full
dedup pass under creation locks. Category resolution and new-job category validation
also repeated their database checks. Company lookup already had a scoped cache.

Each insertion opened a new unpooled physical PostgreSQL session for the creation
advisory locks, acquired the existing two sorted URL/fingerprint keys, saved the job,
unlocked both keys and physically closed that session. EF retained the saved jobs;
subsequent SaveChanges/audit guards repeatedly traversed the growing tracker.

There was no run-wide database transaction. Saves were already per item. Provider
pacing and EF transient retries are not ingestion delays and are not weakened here.

## Optimization

- Preload only incoming source-owned identities in database chunks of 500. Snapshots
  are not tracked until used; saved jobs are detached. Pending modifications,
  source bookkeeping and audit entities are not globally cleared by this cleanup.
- Scope successful company/category validations and URL/source caches to the run.
  Clear ingestion caches after a failed item and all run caches at completion.
- For a bulk URL miss, omit the redundant unlocked dedup pass. A fresh source
  identity check and the full existing URL/fingerprint/fuzzy check still run under
  the exact existing creation locks. Cached absence never authorizes creation.
- Reuse one lazy, unpooled physical advisory connection per sequential run. Acquire
  and explicitly release the same sorted keys per item. Keep each item lease until
  its save is durable. An acquisition/unlock failure physically closes the session;
  the next item uses a fresh session. Cancellation/end-of-run also closes it.
- Keep per-item commits: deferring new inserts to a batch would require holding
  cross-source creation locks over the batch and would change failure isolation.
  This change does not introduce that risk or a giant transaction.

For 1,500 new source-owned jobs, ordinary job lookup calls fall from 10,500
(1,500 source checks + 9,000 dedup lookups) to 6,000 (1,500 locked source checks +
4,500 locked dedup lookups), plus bounded preload queries. New-job category existence
checks fall from 1,500 to one, with one resolver validation per distinct category.
First imports still make 1,500 job saves plus final bookkeeping; repeats avoid
per-job source/dedup reads but retain per-item saves. Creation-session opens fall
from up to 1,500 to one on a healthy run; lock keys and per-item lock commands are
unchanged. Optional automatic publication can add its existing reads/saves.

## Measurement and observability

`LargeSourceIngestionTests` uses 1,500 deterministic jobs, the actual normalizer,
enricher, dedup and EF in-memory repositories, and fake advisory locks. It reports
repository call counts, saves, peak tracked jobs and first/repeat elapsed time.
The pre-change first+repeat test took approximately 243 seconds; the final optimized
run took approximately 56 seconds including test assertions (20.6 seconds first
processing, 13.0 seconds repeat processing). An earlier optimized run took 74 seconds
(30.3 seconds first processing, 15.5 seconds repeat). Host load/JIT can vary these timings.
These are local measurements, not a Neon latency benchmark or production SLA.
No live source or production database was used. Connection churn is demonstrated
separately with fake DbConnections exercising the production creation-session code.

Run logs contain timings for provider fetch, DB preload, enrichment, insert/update
processing, item SaveChanges attempts, reconciliation and total duration. Processing
includes saves; the item-save timing is a subset, not an additive phase. Logs do not
contain full descriptions, credentials or provider response bodies.

## Recovery and reconciliation

Already committed jobs survive an interrupted run and are reused by the exact
`JobSourceId + ExternalJobId` identity. Pending cancelled/failed changes are discarded,
not falsely counted as a successful complete scan. Complete-source providers with
skipped, failed, duplicate or missing identities do not advance LastSuccessfulRunAtUtc.
An interrupted process may still show Last Run = Never because bookkeeping is
committed only at the end; it does not imply that no item was durably saved.

Tests cancel after exactly 475 durable saves and then rerun all 1,500 jobs. The rerun
reuses those 475 and inserts 1,025, with no duplicate identities. Incomplete runs do
not close stale jobs; only a subsequent complete clean snapshot can do so. Manual
jobs matched by URL are not adopted or overwritten. Existing publication authority,
dedup thresholds, normalization and retry policy remain unchanged.

Run-scoped taxonomy/source snapshots are refreshed on the next run. Provider fetching
remains deliberately paced and new-job writes/lock commands remain per item; actual
production phase timings should be reviewed later through the new instrumentation.
