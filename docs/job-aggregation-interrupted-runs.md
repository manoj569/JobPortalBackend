# Durable attempts and interrupted-source recovery

No schema change or new queue is needed. The generic SuccessFactors provider and
the `fac8b0d` ingestion optimizations are unchanged.
The only added write overhead is one attempt SaveChanges per run, not per job;
preloading, category caching, bounded tracking and reusable lock sessions remain.

## Status semantics

Previously `LastRunAtUtc` was saved on completion/failure, but not at run start.
Host shutdown left it null/old even after hundreds of per-item commits. The due
query used only `LastRunAtUtc + ScanIntervalMinutes`; a restart immediately picked
up those apparently never-attempted sources.

Now, after admin/scheduler acquire the existing local guard and PostgreSQL source
execution lock, the runner durably saves `LastRunAtUtc` **before provider fetching**.
It means last attempt started at (UTC), not completion. Existing admin list/detail
responses expose this field without frontend changes. `UpdatedAtUtc` remains the
ordinary audit timestamp and is not used as a scheduler checkpoint.

`LastSuccessfulRunAtUtc` is the completion time of the latest clean successful run.
A complete-source provider must meet the existing complete/no-skips/no-failures/
unique-identities rules. Legacy providers cannot reconcile and only count as
successful when all returned items process without failures/skips. Previously
legacy partial failures could incorrectly reset failure state and advance success.

`LastError` and `ConsecutiveFailures` retain the last completed outcome during a new
attempt/cancellation. Completed failures record a sanitized error and increment
failures; success clears the error and resets failures. Normal host shutdown does
not count as a provider failure. Hard process termination cannot persist a cancelled
status, but the already committed attempt timestamp survives.

## Automatic scheduling

`JobAggregation__Scheduler__InterruptedRunCooldownMinutes` defaults to **60**;
validated range is 1–10080 minutes. Existing `Scheduler__Enabled` remains false by
default. No appsettings/production configuration was changed.

The database due query and post-lock scheduler recheck use the same predicate:

1. Inactive or deleted: never automatically due.
2. Latest outcome successful (`LastSuccessfulRunAtUtc >= LastRunAtUtc`, or success
   exists with no attempt timestamp): due at success completion + scan interval.
3. Never attempted and no success: due immediately.
4. Later unsuccessful/interrupted attempt: due at attempt start + the **greater of
   scan interval and cooldown**. This preserves the previous failure scan-interval
   backoff while adding a floor for short-interval sources. Failures do not retry
   every poll; no new exponential-backoff mechanism is introduced.

Boundaries are inclusive. For Deloitte's 1440-minute interval, an interrupted attempt
waits 1440 minutes automatically (not merely 60); a successful run waits 1440 minutes
from completion. Operators may manually retry earlier. Existing deployed rows whose
attempt/success timestamps are equal remain compatible; old interrupted attempts
with no durable timestamp cannot be retroactively identified.

Manual `POST /api/admin/job-sources/{id}/run` is unaffected by the automatic feature
flag, scan interval or cooldown. It still requires Administrator authorization and
the same local/distributed execution locks; a busy source returns `job_source_busy`.

## Recovery and reconciliation

Every job save still commits independently while retaining the existing creation
locks through durability. Shutdown stops new work, propagates cancellation and
releases locks/run caches/connections. No already committed job is rolled back.

A retry fetches a fresh snapshot (no external page/offset checkpoints), preloads
source-owned `JobSourceId + ExternalJobId` identities, reuses existing rows and
inserts missing rows. Fresh locked miss rechecks, unique identity constraints,
deduplication, ownership, bounded tracking and reusable lock sessions are unchanged.

Stale closure is allowed only after a clean complete snapshot with all unique
nonblank identities and no skipped/failed items. Closures are staged by the repository
and saved together with source success bookkeeping in the final EF SaveChanges
transaction. Cancellation before this final save clears staged changes, not prior
item commits. A committed final transaction represents a completed run; cancellation
or process termination after that commit cannot undo a completed success. Manual jobs
are never selected by source reconciliation.

Tests use synthetic providers, in-memory EF databases and fake advisory sessions.
They interrupt 1500-item runs after 1, 475, 840 and 1490 durable imports, then manually
rerun with no duplicates. The 840 test creates a new scheduler/guard/EF contexts over
persisted state to prove restart protection does not depend on a process-local flag.
An injected cancellation after reconciliation stages closures proves that no closure
or success is committed. No live website or production database is used.

## Logging

Attempt, success, cancellation and failure logs contain IDs, UTC timestamps, item
counts, duration and safe failure categories only. Existing phase timings remain.
Due sources are selected in SQL, so normally suppressed sources produce no per-poll
logs. A stale due selection rejected by the post-lock check logs
`JobSourceSchedulerSkippedRecentAttempt` with its effective retry boundary. This
avoids repeatedly enumerating/logging every cooling-down source.

This protects data and restart scheduling; it does not prevent Render restarts or
guarantee a long run finishes inside the host's lifetime. A skipped/incomplete provider
snapshot remains unsuccessful even if all parseable jobs were imported.
