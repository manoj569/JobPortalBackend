# Talentd durable import operations

## Normal flow

The worker persists each exact payload in its SQLite outbox before POST. Backend `202 Accepted` means the complete batch and fingerprint have committed in PostgreSQL. The backend processor runs independently of the HTTP connection. The worker polls the read-only status endpoint under the same ImportId and acknowledges the outbox only after a validated terminal receipt. The worker's Talentd batch helper defaults to 10 records and caps each batch at 20.

If POST times out, first reconcile `GET /api/internal/v1/jobs/bulk-import/status?sourceType=talentd&importId=<same-id>`. `IN_PROGRESS` means keep polling with bounded backoff; `COMPLETED` must have matching ImportId, RunId and counts; `FAILED` is terminal and needs diagnosis. If status is `NOT_FOUND`, retry only the exact retained payload using the same ImportId. A `409 IMPORT_ID_PAYLOAD_CONFLICT` means stop and investigate; never change payload content under that ID.

Backend restart recovery is automatic: staged request JSON, counters, item receipts, retry time and attempts live in PostgreSQL. PostgreSQL session advisory ownership is released when the owning database session closes. A subsequent processor resumes the same run; committed item receipts are skipped, while pending items or publication receipts continue.

## Read-only checks

Use the production database's approved read-only connection and a read-only transaction for these queries. Do not paste connection strings or credentials into command history.

```sql
SELECT r."Id", r."BatchId", r."Status", r."ReceivedCount", r."EligibleCount",
       r."RejectedCount", r."CreatedCount", r."UpdatedCount", r."ExistingCount",
       r."PublishedCount", r."FailedCount", r."ProcessingAttempts",
       r."NextAttemptAtUtc", r."CompletedAtUtc", r."FailureSummary"
FROM "AcquisitionImportRuns" r
JOIN "AcquisitionSources" s ON s."Id" = r."AcquisitionSourceId"
WHERE s."ProviderKey" = 'talentd' AND r."BatchId" = '<same-import-id>'
  AND NOT r."IsDeleted";

SELECT i."ItemIndex", i."Outcome", i."ReasonCode", i."JobId",
       i."PublicationStatus", i."PublicationError"
FROM "AcquisitionImportItems" i
JOIN "AcquisitionImportRuns" r ON r."Id" = i."AcquisitionImportRunId"
WHERE r."BatchId" = '<same-import-id>' AND NOT i."IsDeleted"
ORDER BY i."ItemIndex";
```

Compare parent counters with item receipts. `Running` with a future `NextAttemptAtUtc` can be active; an overdue `NextAttemptAtUtc` is eligible for polling/recovery. `ProcessingAttempts` is bounded. Review application logs and active database sessions before treating a run as stuck. Never repair it by editing run/item/job rows.

Verify public jobs through the existing read-only public API or public site, and verify the item receipt's `PublicationStatus = 'Published'`. Do not change `Job.Status` manually.

## Existing five-item incident — prepared only

**PREPARED ONLY — NOT EXECUTED.** Run only after the durability migration and backend build are deployed, health checks pass, and an operator has authorized this exact recovery. The worker token and backend URL must already be available through the approved protected environment. This script refuses to continue unless the saved JSON bytes exactly equal the one pending SQLite outbox payload, there is exactly one pending Talentd batch in that ledger, and status still identifies the expected RunId and partial counts. It calls `dispatch` with an empty record list, so it cannot discover or enqueue new jobs.

```powershell
Set-Location 'C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker'
$env:CH_MODE = 'IMPORT'
$env:CH_STATE_PATH = 'C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker\data\talentd-five-operator-20261016-174401.sqlite3'
@'
import json
from pathlib import Path
import httpx
from worker.clients import Backend
from worker.config import Settings
from worker.models import ImportRequest
from worker.state import State
from worker.talentd_import import dispatch
from worker.website_http import SafeWeb

expected_import = "ch-talentd-b719695da815648893270959df7fb6e7ed3338483ace05a534919b7378324415"
expected_run = "64c13827-446f-4cc8-8642-3b223e279bce"
expected_kpmg_job = "8850e6fc-6a97-47c0-9784-09b74d769b34"
ledger_path = Path(r"C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker\data\talentd-five-operator-20261016-174401.sqlite3")
payload_path = Path(r"C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker\data\talentd-five-operator-20261016-174401.payload.json")
payload_bytes = payload_path.read_bytes()
request = ImportRequest.model_validate_json(payload_bytes)
if request.import_id != expected_import or len(request.jobs) != 5 or request.source_type != "talentd":
    raise SystemExit("Refusing recovery: retained payload identity/count/source mismatch")
settings = Settings(mode="IMPORT", state_path=ledger_path)
state = State(ledger_path)
try:
    with state.locked_run():
        pending = [(iid, raw) for iid, raw in state.pending()
                   if json.loads(raw).get("sourceType") == "talentd"]
        if len(pending) != 1 or pending[0][0] != expected_import:
            raise SystemExit("Refusing recovery: expected exactly one pending Talentd outbox row")
        if pending[0][1].encode("utf-8") != payload_bytes:
            raise SystemExit("Refusing recovery: payload file differs from exact SQLite outbox bytes")
        with httpx.Client(follow_redirects=False, trust_env=False) as client:
            status_response = client.get(
                settings.backend_url.rstrip("/") + "/api/internal/v1/jobs/bulk-import/status",
                params={"sourceType": "talentd", "importId": expected_import},
                headers={"Authorization": "Bearer " + settings.backend_token.get_secret_value()},
                timeout=settings.timeout,
            )
            status_response.raise_for_status()
            status = status_response.json()
            if (status.get("runId") != expected_run or status.get("status") != "IN_PROGRESS"
                    or status.get("receivedCount") != 5 or status.get("createdCount") != 1
                    or status.get("publishedCount") != 1 or status.get("failedCount") != 0):
                raise SystemExit("Refusing recovery: backend no longer matches the expected partial run")
            results = dispatch(state, Backend(settings, client), [], SafeWeb().validate,
                               approved=True, batch_size=10)
            if (len(results) != 1 or results[0].import_id != expected_import
                    or str(results[0].run_id) != expected_run or results[0].received != 5
                    or results[0].accepted != 5 or results[0].failed != 0
                    or results[0].rejected_geography + results[0].rejected_experience
                       + results[0].rejected_invalid_data + results[0].invalid_url != 0
                    or results[0].published != 5
                    or next((item.job_id for item in results[0].items if item.index == 0), None)
                       != expected_kpmg_job):
                raise SystemExit("Recovery receipt did not match the required five-item result")
            print(json.dumps(results[0].model_dump(mode="json", by_alias=True), indent=2))
finally:
    state.close()
'@ | .\.venv\Scripts\python.exe -
```

Expected final receipt: Received 5, Rejected 0, Failed 0, Published 5; same RunId and ImportId; item 0 retains its existing KPMG JobId; indices 1–4 complete; exactly one KPMG acquisition identity. If any preflight condition fails, stop and investigate without editing the ledger or database.

## Scale stages — prepared only

**PREPARED ONLY — NOT EXECUTED.** Each new stage uses a separate durable worker ledger for outbox rows. Keep backend URL/token in the approved protected environment. The worker library's import loop below always creates bounded 10-item batches (maximum 20), polls each same-ImportId run, and stops on any nonterminal or failed batch before advancing. `full=True` reads the dynamically reported catalog and never assumes a fixed total. A stage refuses to import if discovery is incomplete or has fewer records than the requested bounded stage.

For each stage, set `CH_TALENTD_TARGET` and `CH_STATE_PATH`, then run the following PowerShell command from the worker repository after explicit stage approval:

```powershell
$env:CH_MODE = 'IMPORT'
$env:CH_TALENTD_TARGET = '20' # use 100, 500, 2000, or full for later stages
$env:CH_STATE_PATH = 'data/talentd-scale-20.sqlite3' # use a new path for each stage
@'
import os
import time
from pathlib import Path
import httpx
from worker.clients import Backend
from worker.config import Settings
from worker.state import State
from worker.talentd import TalentdProvider
from worker.talentd_import import dispatch
from worker.website_http import SafeWeb

target_text = os.environ["CH_TALENTD_TARGET"]
target = None if target_text == "full" else int(target_text)
settings = Settings(mode="IMPORT", state_path=Path(os.environ["CH_STATE_PATH"]))
state = State(settings.state_path)
try:
    with state.locked_run():
        discovery = TalentdProvider(state).run(full=True)
        if not discovery["catalog_complete"] or discovery["errors"]:
            raise SystemExit("Discovery is incomplete; no backend batches were sent")
        records = discovery["records"] if target is None else discovery["records"][:target]
        if not records:
            raise SystemExit("No Talentd records were discovered; no backend batches were sent")
        if target is not None and len(records) != target:
            raise SystemExit("Catalog has fewer records than the requested stage; no batches were sent")
        started = time.monotonic()
        with httpx.Client(follow_redirects=False, trust_env=False) as client:
            results = dispatch(state, Backend(settings, client), records, SafeWeb().validate,
                               approved=True, batch_size=10)
        print({"requested": target_text, "discovered": discovery["unique_jobs_discovered"],
               "submitted_batches": len(results),
               "received": sum(x.received for x in results),
               "accepted": sum(x.accepted for x in results),
               "rejected": sum(x.rejected_geography + x.rejected_experience
                               + x.rejected_invalid_data + x.invalid_url for x in results),
               "created": sum(x.created for x in results), "updated": sum(x.updated for x in results),
               "existing": sum(x.existing for x in results), "published": sum(x.published for x in results),
               "failed": sum(x.failed for x in results),
               "dispatch_seconds": round(time.monotonic() - started, 3)})
finally:
    state.close()
'@ | .\.venv\Scripts\python.exe -
```

Use these stage values and fresh ledger paths in order: recovery `5` (the existing ledger and payload above); `20` with `data/talentd-scale-20.sqlite3`; `100` with `data/talentd-scale-100.sqlite3`; `500` with `data/talentd-scale-500.sqlite3`; `2000` with `data/talentd-scale-2000.sqlite3`; then `full` with a newly named durable ledger. Review every stage's counts, timings, failure receipts and duplicate identities before authorizing the next stage. The full-catalog count is read from discovery each time; it is never hard-coded.

## Do not

- Never delete or recreate a worker ledger to retry.
- Never invent a new ImportId after an ambiguous submission.
- Never manually set a run to Completed or modify `Job.Status`.
- Never replay a completed batch or use one scale-stage ledger for a different stage.
- Never replay SerpApi, FreshersHunt or a completed historical acquisition as part of Talentd recovery.
