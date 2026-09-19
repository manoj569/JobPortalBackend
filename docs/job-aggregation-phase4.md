# Phase 4: automatic scheduled job aggregation

The scheduler is **disabled by default**. Deploying this code alone does not
start scans. No schema changes, migrations, queue tables or distributed leases
are introduced.

## Architecture and timing

`JobAggregationSchedulerHostedService` is a thin BackgroundService. It calls
the independently testable `JobAggregationScheduler.RunOnceAsync`, waits using
`Task.Delay` with the registered TimeProvider, and repeats until shutdown.
An enabled worker makes its first check on startup. Polling waits after each
completed batch, so slow batches never overlap or trigger a catch-up loop.
Disabled workers log once and exit without querying or invoking a runner.

The singleton scheduler holds no scoped dependencies. Each iteration opens a
scope for the due query, disposes it, and creates an independent scope for each
source execution. Providers, ingestion, dedup and source status updates remain
inside the existing `IJobSourceRunner` pipeline.

## Due sources

The repository performs filtering, ordering and limiting in the database:

```text
IsActive AND NOT IsDeleted AND
(LastRunAtUtc IS NULL OR LastRunAtUtc + ScanIntervalMinutes <= nowUtc)
```

The Npgsql-translatable query uses `DateTime.AddMinutes` with the interval
column. It is tested with `ToQueryString` without opening a database connection.
Results are AsNoTracking because each runner reloads its source by ID.
Never-run sources come first, followed by oldest LastRunAtUtc, then ID for
deterministic ordering. The query applies Take(BatchSize), never loading all
sources to filter in memory.

Scheduling uses the **last attempt**, not LastSuccessfulRunAtUtc. Provider
failures respect the source interval. Unsupported ATS types also now record a
failed attempt in the runner while preserving their existing no-provider error
response, so they cannot repeatedly occupy the due batch on every poll.

After taking the run guard, the scheduler reloads and rechecks the source. This
avoids rerunning a stale selection after a manual run or configuration change.
Missing, inactive, deleted, or no-longer-due sources are skipped.

## Configuration and deployment

Strongly typed `JobAggregation.Scheduler` settings are validated at startup:

| Setting | Default | Valid range |
| --- | --- | --- |
| Enabled | false | boolean |
| PollIntervalSeconds | 60 | 10–3600 |
| BatchSize | 25 | 1–100 |
| MaxConcurrentSources | 3 | 1–10 |

Invalid values are rejected rather than creating a tight polling loop. To enable
the scheduler intentionally, configure the deployment environment and restart:

```text
JobAggregation__Scheduler__Enabled=true
JobAggregation__Scheduler__PollIntervalSeconds=60
JobAggregation__Scheduler__BatchSize=25
JobAggregation__Scheduler__MaxConcurrentSources=3
```

Set Enabled=false and restart to disable it. Settings are read using the existing
options/configuration composition; no production appsettings are modified.
PollIntervalSeconds controls checks, whereas each source's ScanIntervalMinutes
controls eligibility. A 60-second poll does not turn a 720-minute source into a
one-minute source.

## Concurrency, cancellation and failures

`Parallel.ForEachAsync` bounds the batch to MaxConcurrentSources and awaits all
work. Each execution acquires the exact singleton `JobSourceRunGuard` used by
admin manual runs, updates and deletes. Busy sources are skipped; leases release
on success, exception or cancellation. Different sources may run concurrently.
The existing admin-only manual endpoint, audit events and source CRUD remain.

Shutdown cancellation propagates through queries, runner calls and async delay.
One source exception or unsuccessful result does not stop other sources. Query
or iteration failures are logged and retried on the next poll. Logs use source
IDs, counters and fixed messages; raw exception details, connection strings,
external descriptions and provider error bodies are never logged by the scheduler.
Zero-source polls do not emit routine informational logs.

If persistence itself is unavailable, the runner may be unable to save an attempt;
such sources can be retried on the next poll. There is no durable claim or backoff
beyond saved LastRunAtUtc. Due ordering is bounded but not a fair distributed queue.

## Unchanged behavior and limitations

Source category configuration remains:
`JobAggregation__SourceCategories__<SOURCE_ID>=<CATEGORY_ID>`.
No guessing, category creation or hardcoded IDs are introduced. Missing mappings
still allow duplicate refresh; new unmapped jobs are skipped. Matched jobs retain
curated fields, company/category, status, referrals and recruiter contacts; new
jobs remain Draft. Greenhouse/Lever public providers and AI Apply adapters are
unchanged. Adzuna discovery and scheduling remain separate.

Scheduler execution coordination is process-local in Phase 4.
Multi-instance deployments may execute the same source concurrently.
Before enabling, account for this operationally (for example, enable scheduling
on one instance); this does not coordinate manual runs across instances.
Phase 2's concurrent job creation risk remains because fingerprints are not
database-unique. These limitations require a separately approved future design.
