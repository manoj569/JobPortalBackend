# Talentd operator validation — PREPARED ONLY - NOT EXECUTED

Do not execute until the reviewed backend and migration are deployed. This document does not authorize deployment or production import. The script is operator initiated and has no scheduler.

Use the approved worker virtual environment from its root. Choose a new dedicated ledger path whose parent already exists. The script fails if that ledger exists, including a ledger left by a failed attempt. Never delete it to retry. On ambiguous submission, retain the ledger and reconcile status first.

```powershell
# Protected environment must already supply CH_BACKEND_URL and CH_BACKEND_TOKEN.
# Explicit operator action only:
& 'C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker\.venv\Scripts\python.exe' `
  'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\tools\talentd\validate_five.py' `
  --worker-root 'C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker' `
  --ledger 'C:\Users\Ashish\Documents\Codex\careerharbor-fresher-worker\data\talentd-five-operator-UNIQUE.sqlite3' `
  --execute
```

The script uses only TalentdProvider(max_jobs=5), requires exactly five successful records and exactly one five-item batch, persists the approved worker outbox bytes, saves a payload snapshot beside the ledger, and uses approved dispatch/Backend validation. It never calls SerpApi or FreshersHunt. ImportId is the approved worker content hash, not a timestamp. Network/backend ambiguity stops processing; there is no automatic new-ledger retry.

Read-only reconciliation after timeout (operator command, not executed):

```powershell
$payload = Get-Content -Raw -LiteralPath '<chosen-ledger-with-.payload.json-suffix>' | ConvertFrom-Json
$iid = [Uri]::EscapeDataString($payload.importId)
$status = Invoke-RestMethod -Method Get `
  -Uri ($env:CH_BACKEND_URL.TrimEnd('/') + '/api/internal/v1/jobs/bulk-import/status?sourceType=talentd&importId=' + $iid) `
  -Headers @{ Authorization = 'Bearer ' + $env:CH_BACKEND_TOKEN }
$status
```

404/NOT_FOUND means no durable run is visible; it does not by itself authorize resubmission. IN_PROGRESS includes AwaitingPublication; wait, do not use a new ImportId. COMPLETED contains `receipt` in the existing bulk response shape. Before acknowledging the retained outbox, use the approved worker's `validate_response_identity(request, result)` and `classify_import_result(result)` and require TERMINAL_SUCCESS, received/accepted/published = 5, failed/rejections = 0, and all five indexed JobIds. This task does not modify the worker to poll automatically. FAILED exposes a persisted terminal failure where applicable; never manufacture success from counts.

DB verification: connect using the operator's protected read-only production connection and bind `:importId` as a parameter. Do not paste credentials or issue updates.

```sql
SELECT s."ProviderKey", s."IsEnabled", s."AutoPublishEnabled", s."IsDeleted"
FROM "AcquisitionSources" s WHERE s."ProviderKey" = 'talentd';
SELECT r.* FROM "AcquisitionImportRuns" r JOIN "AcquisitionSources" s
  ON s."Id" = r."AcquisitionSourceId"
WHERE s."ProviderKey" = 'talentd' AND r."BatchId" = :importId;
SELECT i."ItemIndex", i."Outcome", i."PublicationStatus", i."PublicationError",
       i."DiscoveredUrl", i."CanonicalUrl", i."ApplyUrl", j."Id", j."Status",
       j."ApplicationUrl", j."DiscoveredUrl", j."CanonicalJobUrl", j."AcquisitionExternalId",
       j."MinimumSalary", j."MaximumSalary", j."CurrencyCode", j."SalaryPeriod",
       j."EducationRequirement", j."SourceMetadataJson"
FROM "AcquisitionImportItems" i JOIN "AcquisitionImportRuns" r ON r."Id" = i."AcquisitionImportRunId"
JOIN "AcquisitionSources" s ON s."Id" = r."AcquisitionSourceId"
LEFT JOIN "Jobs" j ON j."Id" = i."JobId"
WHERE s."ProviderKey" = 'talentd' AND r."BatchId" = :importId ORDER BY i."ItemIndex";
```

Require five successful receipts, five Published publication statuses, NULL PublicationError, and published Job status using the deployed JobStatus enum (do not guess its numeric value). Compare every URL byte-for-byte with the retained payload. Inspect the public job's Apply link; a direct employer/ATS destination must stay direct. Manually review external destinations in a browser only when operator authorized. Stop after five; this is not permission for a larger rollout.
