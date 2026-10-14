# Source Auto Publish configuration — 14 October 2026

## Root cause

The supplied production diagnostics establish the immediate cause: the Johnson & Johnson source had `AutoPublishEnabled=False`. Its 52 created and 17 existing/updated jobs reached the shared publication pipeline, which correctly returned `AUTO_PUBLISH_DISABLED` for 69 jobs. No provider whitelist or Workday ingestion repair is needed for this incident.

The admin contracts already exposed the field, but the shared save request defaulted an omitted field to false. As a result, an older client saving other source settings could silently disable an already-enabled source. That omission hazard is repaired here. The available diagnostics do not establish which caller originally saved the production false value.

## Existing implementation audited

- Entity: `JobPortal.Domain/Entities/JobSource.cs` already has separate non-nullable `IsActive` and `AutoPublishEnabled` booleans.
- Mapping: `JobSourceConfiguration` in `JobPortal.Persistence/Configurations/EntityConfigurations.cs` maps JobSources; AutoPublishEnabled uses conventional EF mapping. No duplicate mapping or column is needed.
- Migration: `20261013112101_AddSourcePublicationControl` adds AutoPublishEnabled with PostgreSQL boolean/default false and SourceOwnsPublication with default false. The current model snapshot contains both. Production application of that migration was supplied by the user; no production migration table was queried.
- DTOs: create and full update share SaveJobSourceRequest. JobSourceResponse already returns the persisted boolean on create/update/GET/list.
- Controller: AdminJobSourcesController retains its existing routes and Administrator authorization.
- Service: JobSourceManagementService already assigns and persists the field; repository Update is bookkeeping-only, so configuration edits correctly use the tracked entity and UnitOfWork.
- Publication: JobAutoPublishService already checks the source toggle and logs AUTO_PUBLISH_DISABLED. JobSourceRunner already calls it for successful owned imports. Both are unchanged by this task.
- Worker/scheduler: existing scopes reload the source and retain execution guards. No slicing, reconciliation, geographic or ownership rules changed.

## API contract before and after

| Operation | Before | After |
|---|---|---|
| POST `/api/admin/job-sources` | Explicit true/false persisted; omission defaulted false | Explicit true/false still persists; omission/null retains the existing new-source opt-in default false |
| PUT `/api/admin/job-sources/{id}` | Explicit true/false persisted; omission silently set false | Explicit true/false persists; omission/null preserves the saved value |
| GET source/detail/list | Returns autoPublishEnabled | Unchanged: returns the actual persisted boolean |
| PATCH `/api/admin/job-sources/{id}/auto-publication` | Unavailable | New Administrator-only toggle endpoint; changes only AutoPublishEnabled |

PATCH bodies:

```json
{ "autoPublishEnabled": true }
```

```json
{ "autoPublishEnabled": false }
```

PATCH rejects missing/null with HTTP 400 (`auto_publish_enabled_required`); invalid JSON types are rejected by JSON model binding. It returns the existing JobSourceResponse envelope, not a new response contract. Activity, provider, identifier, company, URL, scan interval and run bookkeeping are preserved. Existing run/execution guards prevent conflicting edits. Configuration-only Oracle cannot be enabled through the toggle. Full updates validate the effective preserved value, preventing omission from bypassing Oracle restrictions during a provider change.

Create and update audits now record publication configuration; update audits include before/after values and commit with the source edit. Enabling a source does not start a sync or modify jobs. Publication stays in the existing shared service on subsequent runs.

## Defaults and migration decision

No migration required. No entity, EF mapping, model snapshot, database default or existing migration changed.

The prior implementation explicitly documents new sources as operator opt-in, defaulting to drafts. The original repair requirements also required preserving that rule. This is the existing business-rule exception to the requested preferred true default for trusted sources: there is no separate persisted 'trusted source' flag authorizing silent opt-in. New trusted sources can explicitly supply true. Changing all omitted creates to true would alter existing callers' publication behavior and weaken this deliberate opt-in rule.

Historical sources migrated with false remain false unless an administrator explicitly updates them. Existing enabled sources remain enabled when older clients omit the field during an edit. Jobs with unverified legacy publication ownership remain protected independently of the source toggle.

## Files changed in this follow-up

1. `JobPortal.API/Controllers/AdminJobSourcesController.cs`
2. `JobPortal.Application/Abstractions/AdminManagement/IJobSourceManagementService.cs`
3. `JobPortal.Application/Features/JobAggregation/JobSourceManagementDtos.cs`
4. `JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs`
5. `JobPortal.Application/Features/JobAggregation/JobSourceManagementValidators.cs`
6. `JobPortal.Application.Tests/JobSourceAdministrationTests.cs`
7. `JobPortal.Application.Tests/SourceAutoPublicationConfigurationTests.cs`
8. `JobPortal.Application/Features/Auditing/AuditServices.cs` — allow the three safe publication-setting audit keys through the existing metadata filter.
9. `SOURCE_AUTO_PUBLICATION_CONFIGURATION.md`

The previous publication repair at commit `03da570` is preserved. No frontend changes, commit, push, deployment or production writes were performed.

## Tests added

The configuration tests cover explicit create true/false for Greenhouse, Lever, Ashby, Workday and SuccessFactors; actual detail/list JSON output; legacy omitted-create default; omitted full-update preservation; explicit full-update transitions in both directions; all four combinations of activity and toggle; unchanged other settings; before/after audit values; missing/null PATCH rejection; source guard conflicts; Oracle authorization; and saved-toggle effects through the runner/public repository for all five ATS providers and a normalized mock future provider. They assert AUTO_PUBLISH_DISABLED in the existing service's logs and prove manual suppression survives toggle changes.

The existing authorization test now covers all eight controller actions, including PATCH, with Administrator/Candidate/Employer/anonymous roles. An audit-policy serialization regression verifies the publication booleans survive persistence/readback while secret fields remain excluded.

## Verification results

| Check | Result |
|---|---|
| Final exact `dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release` | Passed, 0 warnings, 0 errors |
| Final configuration/publication/provider/audit regressions | 334 passed, 0 failed, 1 skipped; 335 total |
| New source configuration tests | All 31 cases passed |
| `git diff --check` | Passed |
| Full-suite attempt on the preceding iteration | Incomplete/cancelled after several minutes without new output; 96 reported failures and 32 reported skips before cancellation; no final pass/total count |

Final evidence: `../source-toggle-final-build.log`, `../source-toggle-final-tests.log`, and `../source-toggle-results/source-toggle-final.trx`. The focused test build retained analyzers, reporting 186 warnings already present in the existing test project, with the invocation-only `-p:TreatWarningsAsErrors=false` override. No new configuration-test analyzer warnings were reported. Repository analyzer configuration was not edited; the standard analyzer gate remains unresolved.

The single focused skip is the explicitly configured disposable localhost PostgreSQL ingestion/concurrency test. No database test prerequisites were supplied, so actual PostgreSQL persistence/concurrency was not reverified. Normal persistence contracts were exercised with EF fixtures; API serialization and authorization were tested without deploying an HTTP server.

The first focused iteration passed 326 tests with 1 skip. The full-suite attempt used that earlier compiled assembly; it preceded the final audit metadata extension and its two added regression cases. Its partial evidence remains `../source-toggle-full-tests.log`. Reported failures were 81 career/session/finance/scheduling assertions, 4 notification assertions/timeouts, and 11 ATS/ingestion/classification assertions. The latter include SuccessFactorsDiagnosticsTests, MastercardWorkdayVerificationTests, JobSourceManualRunTests, WorkdayIngestionDiagnosticsTests, ExternalJobExpiryTests, ExternalJobCoverageTests, ExternalJobNormalizationPipelineTests, ExternalJobEnrichmentTests and DeloitteSourceReconciliationTests. These failures were not hidden, excluded from the full attempt, or repaired by changing the existing ingestion architecture. Their baseline status was not independently rerun, and the full suite is not claimed green. Final focused validation additionally covered the existing AuditLoggingTests after the last audit change.

## Production deployment and verification steps — commands only, not executed

Run the build/test/review commands locally before releasing:

```powershell
Set-Location 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9'
dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release
$sourceConfigurationFilter = 'FullyQualifiedName~SourceAutoPublication|FullyQualifiedName~JobSourceAdministration|FullyQualifiedName~JobSourceAuthorization|FullyQualifiedName~JobSourceCategory|FullyQualifiedName~JobAutoPublishService|FullyQualifiedName~JobSourceRunner|FullyQualifiedName~JobSourceReconciliationSafety|FullyQualifiedName~GenericIndiaJobIngestion|FullyQualifiedName~JobAggregationPipeline|FullyQualifiedName~OracleRecruitingCloud|FullyQualifiedName~ExternalJobProvider|FullyQualifiedName~SuccessFactorsJobSourceProvider|FullyQualifiedName~WorkdayJobSourceProvider|FullyQualifiedName~JobSourceDurableRun|FullyQualifiedName~JobSourceInterruptedRun|FullyQualifiedName~AuditLoggingTests'
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj -c Release --filter $sourceConfigurationFilter
# If the existing analyzer gate blocks execution, retain analyzers but allow their warnings for this diagnostic invocation:
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 /nodeReuse:false -p:TreatWarningsAsErrors=false --filter $sourceConfigurationFilter --logger 'trx;LogFileName=source-configuration.trx' --results-directory .\TestResults
git diff --check
git diff -- JobPortal.Persistence.Postgres/Migrations JobPortal.Persistence/Configurations JobPortal.Domain/Entities/JobSource.cs
git diff
dotnet publish .\JobPortal.API\JobPortal.API.csproj -c Release --no-build -o .\artifacts\source-configuration
# Alternative package using the repository's existing container definition:
docker build -f .\JobPortal.API\Dockerfile -t careerharbor-backend:source-configuration .
```

Promote the reviewed publish artifact/container through the existing production hosting release process. No hosting target, registry or service name is defined by this task, so an exact host-specific promotion command cannot be supplied safely. No new schema migration step is needed; do not run a database update for this patch. If an environment lacks the already-required `20261013112101_AddSourcePublicationControl`, handle that existing migration under the established deployment process, separately from this no-schema-change patch.

After deployment, the administrator can explicitly enable one reviewed source using the new route. Supply the real API origin, source ID and existing authenticated administrator token; these commands intentionally target only that source:

```powershell
$apiOrigin = Read-Host 'Production API origin, including https://'
$sourceId = [guid](Read-Host 'Reviewed JobSource ID')
$adminToken = Read-Host 'Administrator bearer token' -MaskInput
$adminHeaders = @{ Authorization = "Bearer $adminToken" }
$sourceUri = "$($apiOrigin.TrimEnd('/'))/api/admin/job-sources/$sourceId"
Invoke-RestMethod -Method Get -Uri $sourceUri -Headers $adminHeaders
Invoke-RestMethod -Method Patch -Uri "$sourceUri/auto-publication" -Headers $adminHeaders -ContentType 'application/json' -Body '{"autoPublishEnabled":true}'
Invoke-RestMethod -Method Get -Uri $sourceUri -Headers $adminHeaders
$queuedRun = Invoke-RestMethod -Method Post -Uri "$sourceUri/run" -Headers $adminHeaders
$runId = $queuedRun.data.runId
Invoke-RestMethod -Method Get -Uri "$sourceUri/runs/$runId" -Headers $adminHeaders
```

Poll the existing run-status route until terminal. Verify the response returns autoPublishEnabled=true, inspect committed publication counts and reasons, and read `/api/jobs/{slug}` for an eligible imported job. OFF uses the same PATCH with false. Existing published jobs and manual suppressions retain their prior behavior. Not every one of the 69 previously observed jobs is guaranteed to publish: each must still be observed, source-owned and eligible under current safety checks.

## Existing sources still potentially disabled

The Johnson & Johnson Workday source was false in the supplied diagnostics and remains untouched by this task. Historical migrated sources and sources created by callers omitting the field may also be false. No production inventory was queried, so other source IDs/names or counts are unknown. GET/list reports their persisted setting for individual administrator review. Do not blanket-enable them through SQL or a migration.
