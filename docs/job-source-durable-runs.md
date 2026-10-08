# Durable manual job-source runs

`POST /api/admin/job-sources/{sourceId}/run` requires the existing Administrator role.
It now persists a PostgreSQL `JobSourceRuns` record and returns **202 Accepted** without
fetching the provider or importing jobs in the HTTP request. The response retains the
existing counter names and adds `runId`, `status`, `statusUrl`, timestamps, attempt count,
phase and processed count. `Location` points to the status endpoint.

`GET /api/admin/job-sources/{sourceId}/runs/{runId}` requires the same Administrator role.
The source/run pair must match. Responses never expose lease tokens or provider errors.

Example initial response (inside the existing `data` envelope):

```json
{
  "runId": "<run-guid>",
  "jobSourceId": "<source-guid>",
  "status": "Queued",
  "statusUrl": "/api/admin/job-sources/<source-guid>/runs/<run-guid>",
  "attemptCount": 0,
  "processed": 0,
  "succeeded": false,
  "error": null
}
```

`succeeded` is a **completed-run** result, not an acceptance flag. Existing admin clients
need a small behavior update: treat 202 as accepted, poll `statusUrl` (e.g. every 5 seconds),
and display final counters only for Succeeded/Failed or Interrupted with `retryPending=false`.
No visual redesign is required. No frontend changes are included in this backend change.

## Execution and recovery

- Queued -> Running -> Succeeded / Failed / Interrupted.
- A filtered unique index coalesces duplicate requests into the same active run.
- Workers acquire the existing process-local guard and **exact existing PostgreSQL
  source advisory lock** before an atomic compare-and-set claim. Busy sources do not
  consume an attempt. No Task.Run, in-memory queue or HTTP request token drives execution.
- Each instance executes one queued run at a time and polls every 5 seconds. Different
  instances can process different sources. The worker remains active when the automatic
  scheduler is disabled. Scheduled runs skip active durable manual runs and share the locks.
- Running claims have a 120-second lease, renewed every 20 seconds using a separate
  scoped DbContext. Renewal failure/lost ownership cancels execution. Status counters are
  heartbeat snapshots of the current attempt, not a transactionally exact audit of every item.
- Shutdown cancels the runner, awaits the heartbeat and attempts bounded cleanup. A crash
  leaves Running recoverable after lease expiry. Recovery requires the same source lock,
  persists Interrupted and waits `max(source scan interval, configured interrupted-run
  cooldown, 60 minutes)` before retry. Maximum **3 execution attempts** per run.
- Failed provider/unsafe snapshot outcomes are terminal; they do not auto-retry. After
  exhausted interruptions, admins can deliberately request a new run. This is not an
  immediate retry loop. Progress from a hard crash can lag by up to one heartbeat interval.
- Recovery rescans the source, rather than resuming a partial pagination snapshot. Existing
  source/external-ID deduplication and per-item commits make partially imported jobs reusable.
  The existing runner alone decides successful completion and stale reconciliation.
- No incomplete scan is treated as successful. No job insert/update/closure is performed
  by the queue store. Provider parsing/validation, retries, spacing and ingestion caches
  remain unchanged. Admin source updates/deletes still share the execution lock.

## Deployment (operator actions; not performed by implementation)

1. Review `20261008180129_AddDurableJobSourceRuns` and back up the target database.
   Its Up creates only JobSourceRuns plus its constraints/FKs/indexes. No historical
   migration or business-data update is required.
2. Check the target migration history, then apply **through this exact migration** using
   securely configured `ConnectionStrings__DefaultConnection`:

   ```powershell
   dotnet ef database update 20261008180129_AddDurableJobSourceRuns --project .\JobPortal.Persistence.Postgres\JobPortal.Persistence.Postgres.csproj --startup-project .\JobPortal.API\JobPortal.API.csproj --context JobPortalDbContext
   ```

3. Apply the additive schema **before** rolling out the new API. Drain any legacy
   synchronous runs before rollout; keep automatic scheduling disabled if desired.
4. Deploy/restart the API. Preserve the direct/session-capable PostgreSQL endpoint required
   by existing advisory locks; known Neon `-pooler.` endpoints remain rejected.
5. Wire the existing admin Run Now action to 202/status polling, then request a source run.
   Confirm prompt acceptance, Running heartbeats, eventual final status/counters and
   source LastSuccessfulRunAtUtc only after safe complete ingestion.
6. For an optional local PostgreSQL SQL/concurrency test, set
   `JOB_SOURCE_RUN_TEST_POSTGRES` to an explicitly disposable **localhost** database named
   `jobsource_run_test`. The test creates/removes only its random isolated schema and
   uses the actual new migration/store. It never reads application/Neon settings.

No production execution, migration application or scraping is part of local validation.
