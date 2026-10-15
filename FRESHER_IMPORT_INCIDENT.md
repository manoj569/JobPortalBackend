# Fresher bulk-import incident investigation

The supplied Render log confirms the production exception and failing stage. Production database contents have not been queried. No production import, deployment, push, SerpApi call, worker edit or frontend edit was authorized or performed.

## Evidence

The retained payload was exported from a filesystem copy of the worker SQLite database. The original worker database and outbox were not opened for writing. The exported bytes are at `../retained-fresher-payload.json`: 83,060 bytes, 30 jobs, SHA-256 `05cebf2d5c8f0797a63a8020224efc7e84e1ce7cf86e62fd4157905f2c9ba879`. Its ImportId remains `ch-v1-03d13df0fb5a5b6fbaa601a4ffd391ca1f340524ca87ba63e8bcc02126a745b6`.

The local production registration enables Npgsql retry-on-failure. The fresher store starts explicit chunk transactions outside an execution-strategy delegate. EF Core rejects database operations within such a transaction when the retry strategy is enabled. The old PostgreSQL tests configure Npgsql without retries, missing this configuration difference. A regression now exercises the exact retained payload with retries enabled. The supplied Render log shows this exact InvalidOperationException in ResolveCompanyAsync for item index 0. The failing company query is rejected by EF before execution. This logged attempt cannot have committed job/receipt changes from its first chunk, although StartRunAsync saved a run before the chunk began. The other three attempts were not independently traced.

## Pipeline and persistence

The authenticated controller validates batch size and delegates to the importer. The importer resolves an enabled acquisition source, derives candidate identities, obtains a session advisory run lock, starts or resumes a durable run, and processes bounded chunks. Each chunk holds transaction advisory identity locks and commits jobs and receipts together. Publication runs after commit. Publication failures leave retryable receipts. Completion aggregates durable receipts; exact completed replay reads those receipts without reinserting jobs.

The foundation migration seeds provider `serpapi-google-jobs`, name `SerpApi Google Jobs`, enabled and with AutoPublish disabled. A missing/disabled source returns `ACQUISITION_SOURCE_DISABLED` outcomes without opening a run. Production seed presence and enabled state have not been queried.

Company resolution uses explicit ID, otherwise normalized website domain, then name/slug. It does not automatically create companies. Missing companies produce `COMPANY_NOT_RESOLVED`; no identity rules or owner requirements were weakened. Unknown explicit categories produce `CATEGORY_RESOLUTION_FAILED`; omitted categories remain nullable. Skills are resolved/created, and now reuse skills staged earlier in the same unsaved chunk.

Expected schema migrations include `20261014212715_AddFresherAcquisitionFoundation`, `20261014214058_AddFresherRunRejectionCounters`, `20261014214340_AddNormalizedCompanyWebsiteDomain`, `20261015040449_HardenFresherAcquisitionRecovery`, and `20261015040944_AddFresherPublicationRetryState`. The fix changes no model or migration. Actual production migration history and schema were not available for verification.

## Changes

- Enter an explicit zero-retry EF execution strategy around fresher imports. This accepts explicit chunk transactions under production retry configuration without transparently replaying committed chunks, tracked entities or publication side effects. Recovery remains a fresh request with the same ImportId and durable receipts. Global ATS retry configuration is unchanged.
- Reuse locally staged skills when resolving normalized skill names, preventing duplicate new skill rows within one chunk.
- Bound diagnostic receipt fields for rejected oversized candidates, so recording a rejection does not itself overflow database columns. Job identity fields are not truncated to make invalid jobs acceptable.
- Add correlation/import/source/count scope and safe stage/external-ID/type/SQLSTATE diagnostics. Bulk-import middleware does not log raw exception messages or provider details. HTTP errors remain sanitized.
- Add PostgreSQL cases for the exact payload with production retries, a 30-item shared-skill batch/replay, missing companies/categories, oversized rejections, and missing sources. Existing tests cover unique constraints, job/receipt rollback, replay/conflict, concurrent inserts, reordered replay and publication retry.

## Limits and operational decision

New runs fingerprint candidate content as well as source fields, detecting changes even when external identities are unchanged. Reordered identical candidate sets still replay. Legacy runs only retained identity-set fingerprints; those hashes are accepted for backward-compatible recovery of the retained ImportId. Their original full content cannot be reconstructed from the historical hashes, so full-content conflict detection cannot be retroactively guaranteed for legacy runs. Keep the retained bytes unchanged.

The historical production run state and item count are unknown. In this code, a run is saved before the first chunk, so a failed attempt can leave a run with no receipts. Earlier successfully committed chunks and publication-side effects can survive a later error. A generic HTTP 500 alone does not establish zero persistence.

The Render exception is confirmed and the corrected path is reproduced locally. After deployment of this fix, the same retained bytes and same ImportId are safe for idempotent replay through the durable receipt recovery path. This is not a guarantee that all companies resolve or all 30 jobs publish. Before an authorized replay, verify the deployed revision and reconcile production migration history, source flags and run/items. No production replay was performed. Do not launch a general worker import/discovery run as a diagnostic command.

## Validation commands

Use only the isolated localhost database `careerharbor_generic_india_test`; never substitute production credentials.

```powershell
$env:GENERIC_INDIA_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=careerharbor_generic_india_test;Username=incident_test;Password=local-test-only'
$env:FRESHER_RETAINED_PAYLOAD=(Resolve-Path ..\retained-fresher-payload.json).Path
dotnet build -c Release
dotnet test JobPortal.Application.Tests -c Release --no-restore -m:1 -nodeReuse:false -p:RunAnalyzers=false --filter 'FullyQualifiedName~FresherAcquisition|FullyQualifiedName~PostgresPendingModelTests'
```

The required solution build reached all production projects, then failed with 187 test analyzer errors, including unrelated existing test files. Analyzer-disabled runtime validation is a separate check and must not be presented as a successful required solution build.

## Files changed by this investigation

- JobPortal.Application/Features/FresherAcquisition/FresherBulkImportService.cs
- JobPortal.Application/Features/FresherAcquisition/IFresherAcquisitionStore.cs
- JobPortal.Application/Features/FresherAcquisition/FresherCandidateIdentity.cs
- JobPortal.Persistence/Repositories/FresherAcquisitionStore.cs
- JobPortal.Persistence/Repositories/JobRepository.cs
- JobPortal.API/Middleware/GlobalExceptionMiddleware.cs
- JobPortal.Application.Tests/FresherAcquisitionPostgresTests.cs
- FRESHER_IMPORT_INCIDENT.md
- docs/fresher-import-readonly-reconciliation.sql

Pre-existing Workday edits were left untouched. No migrations were created.

## Deployment procedure (not performed)

1. Review this backend diff and the validation logs. Resolve or explicitly accept the independent solution test-analyzer build failures before release. No worker or frontend release is needed.
2. Run the read-only SQL in docs/fresher-import-readonly-reconciliation.sql against production through an approved read-only database session. It reports migration history, source flags and the retained run/receipt totals. Do not delete or reset the run or receipts.
3. If any of the five expected migrations are absent, apply the already-existing reviewed migration chain through the normal production migration process. Do not scaffold a new migration for this fix.
4. Release the reviewed backend revision using the existing Render service and JobPortal.API/Dockerfile. Its build restores, builds and publishes JobPortal.API in Release. Keep the existing service token, source identity, source AutoPublish flag and scheduler settings.
5. Verify the deployed revision and readiness. Obtain explicit authorization to replay exactly the retained 83,060 bytes with the same ImportId. Do not generate a new ID, edit the payload, or invoke discovery/SerpApi.
6. Inspect the sanitized import summary and durable receipts. Exact replay must reuse the same run and must not add duplicate jobs. A deterministic COMPANY_NOT_RESOLVED receipt is a mapping outcome, not proof of successful job creation. Resolve company onboarding through the existing identity/ownership workflow; do not silently reprocess terminal receipts or rewrite the retained request.

EF's explicit-transaction/retry restriction is documented in [Connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency). This patch uses a zero-retry execution strategy because the existing durable importer, rather than an automatic replay of the entire delegate and publication side effects, owns recovery.

## Local results available so far

The finalized focused run passed 104 tests (14 fresher PostgreSQL tests included), with one separately opted-in skill-constraint test initially skipped. It includes the real 30-job bytes, production retry configuration, the precise pre-fix exception, job/receipt rollback, duplicate handling, content conflict, partial recovery after a committed chunk, publication retry and cross-provider India eligibility. The model-drift check passed. Test compilation used RunAnalyzers=false because the normal solution test build has existing analyzer errors; this is not a normal full-solution build pass.

Read-only SQL was validated in the disposable database. All five listed migrations and the correctly seeded source are present there. The retained local run is Completed with 30 COMPANY_NOT_RESOLVED receipts and no linked jobs, since real employers are absent locally. The separate 30-job synthetic fixture with an existing company passed creation and exact replay. Production company inventory is unknown.

The original worker SQLite file's SHA-256 still matched the temporary pre-inspection copy after export. The temporary database copy was removed; the exact payload file remains outside the repository for reproducibility.

## Final validation update

The additional PostgreSQL skill-constraint test passed (1/1). Across the focused runs, 105 tests passed, including 16 real PostgreSQL tests. The final normal Release solution build failed with 186 analyzer errors in existing test files; no errors were reported in FresherAcquisitionPostgresTests.cs. The earlier 187-error count included the new uncached JsonSerializerOptions diagnostic, which was corrected. Do not present the solution build as successful.

The separate deployable API Release build with normal analyzers passed: zero warnings, zero errors. Its output is ../fresher-incident-api-release-build.log. The isolated PostgreSQL container careerharbor-fresher-incident-pg was stopped after testing; its local evidence is preserved. No production connection was opened.
