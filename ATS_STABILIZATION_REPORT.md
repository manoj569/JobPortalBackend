# Backend-only ATS ingestion incident report

Backend repository: `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9`

This pass fixes employer-source ingestion and preserves the existing automatic-publication workflow. No frontend or `ats-frontend-patch` files were modified in this pass. No fresher, internship, seniority or experience filtering was added. No deployment, production database mutation, company-specific provider condition, migration or analyzer-setting change was performed.

## 1. Greenhouse Stripe/Airbnb: demonstrated root causes

Two independent repository defects can prevent fetched records from reaching creation:

1. **Whole-board detail buffering.** The earlier implementation awaited detail work for the entire board before returning a snapshot. A later timeout, exception or host cancellation could therefore occur after many HTTP 200s but before the first persistence call. The existing stabilization repair now uses 25-job groups through `IBatchedExternalJobProvider`; the runner processes and commits a yielded group before the next detail group. Regression tests demonstrate that 25 jobs already exist in the database when detail request 26 starts.
2. **Business geography selection before persistence.** `JobSourcePublicationPolicy.Select` evaluated India eligibility and removed foreign/ambiguous records before `JobSourceRunner` invoked ingestion. The Greenhouse adapter also skipped details for records classified as foreign. This violates the clarified contract to ingest all technically valid active employer jobs. This pass removes both geography-based decisions. The shared source policy retains only the established explicit test/execution cap and metadata/display handling. Country facts remain diagnostic and cannot restrict ingestion or mark a technically complete run unsuccessful.

Exact affected files:

- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Infrastructure\Services\GreenhouseExternalJobProvider.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Features\JobAggregation\JobSourcePublicationPolicy.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Services\JobSourceRunner.cs`

The exact end-to-end path is: named Greenhouse client reads the listing -> bounded detail group -> detail identity/deadline parsing -> technical snapshot validation -> yield -> runner captures original metadata once -> cumulative execution-cap handling -> normalizer/enrichment/category mapping -> source identity/canonical URL deduplication -> existing ingestion/unit-of-work saves -> separate existing auto-publication service -> safe reconciliation and source bookkeeping -> worker persists terminal status. Original metadata is serialized once per received record rather than repeatedly serializing all earlier groups as the cumulative board grows.

### Technical exclusions are explicit

`PublicAtsSnapshot` still enforces established technical rules: title/description/stable identity, approved application URL shape and unique identities. It now produces individual codes including:

- `PROVIDER_TITLE_INVALID`
- `PROVIDER_DESCRIPTION_MISSING`
- `PROVIDER_IDENTITY_INVALID`
- `PROVIDER_APPLICATION_URL_UNVERIFIED`
- `PROVIDER_DUPLICATE_IDENTITY`

Greenhouse, Lever and Ashby preserve those codes into run diagnostics. Invalid details, prospect posts, explicit expiry, duplicate identities and malformed snapshots retain their established safe behavior; no business title or seniority threshold was introduced. Unsafe application URLs are not allowed merely to increase Created.

Exact technical-validation file: `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Infrastructure\Services\PublicAtsSnapshot.cs`.

Read-only public listing checks on 2026-10-14 observed **726 Stripe jobs** and **165 Airbnb jobs**, with worldwide locations. [Stripe's public listing](https://boards-api.greenhouse.io/v1/boards/stripe/jobs?content=false) returned links shaped like `https://stripe.com/jobs/search?gh_jid=<id>`. [Airbnb's public listing](https://boards-api.greenhouse.io/v1/boards/airbnb/jobs?content=false) returned links shaped like `https://careers.airbnb.com/positions/<id>?gh_jid=<id>`. Those reads did not ingest or change any jobs.

The existing company-hosted Greenhouse URL rule requires the public career-page host to match the job-link host and `gh_jid` to match the identity. A mismatched saved career URL can therefore still produce zero creations as a technical configuration/URL-verification failure. That rule is preserved, and a regression now demonstrates an explicit `PROVIDER_APPLICATION_URL_UNVERIFIED` count with `ReconciliationSafe=false`. Employer-hosted Stripe/Airbnb-shaped links with matching configuration are proven to reach persistence, including a Principal Engineer job outside India.

The supplied old HTTP-only production logs cannot prove which of buffering/cancellation, geography selection, technical URL validation or another runtime branch actually terminated those specific production runs. The repository defects and their fixes are demonstrated; attribution of the exact historical production branch is **PRODUCTION VERIFICATION REQUIRED**.

### Creation and publication remain separate

All technically valid active jobs reach generic ingestion regardless of country, seniority or experience. `AutoPublishEnabled=false` cannot prevent creation. `AutoPublishEnabled=true` invokes the existing automatic-publication service and its quality/moderation/ownership checks after persistence. Existing publication review may keep a foreign/ambiguous job as a draft; that outcome is counted as NotPublished, not an ingestion rejection. No manual publication or unrelated product policy was redesigned.

Run result and admin status now separately expose **Created**, **Updated/Existing**, **Published**, **NotPublished**, **SkippedInvalid** and **Failed**. `NonIndiaRejected` and `AmbiguousLocationHeld` are computed from actual selection exclusions rather than mislabeling diagnostic country facts as dropped jobs. Standard source ingestion now reports zero such business exclusions.

Exact accounting files:

- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Abstractions\Jobs\JobSourceRunResult.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Features\JobAggregation\JobSourceRunService.cs`

An explicit `TestImportLimit` remains an operational partial-run mechanism: it produces `SELECTION_LIMITED`, unsuccessful full-run accounting and unsafe reconciliation. It does not silently claim a capped traversal ingested the whole source.

### Large-board and failure evidence

| Regression | Demonstrated result |
|---|---|
| Stripe and Airbnb, 200/500 valid jobs, AutoPublish=true | All jobs created/published through real SQLite repositories and existing JobService; first 25 committed before request 26; repeat creates zero and matches all identities. |
| Stripe and Airbnb, 500 valid jobs, AutoPublish=false | Created=500, Published=0, NotPublished=500, AutoPublishDisabled=500. |
| 200 valid foreign jobs | Created=200, ingestion geography rejected/held=0; existing publication review accounts for all 200 as NotPublished. |
| Cancellation after 50 safe details | 50 new committed jobs retained; older published job retained; no closure; cancellation progress is unsuccessful and reconciliation unsafe. |
| One malformed/timed-out/missing detail among 60 | Other 59 records created/published; safe provider reason and skip count; no full success or unsafe closure. |
| Employer-hosted application links | Matching Stripe/Airbnb-style career hosts and gh_jid links persist; unverified host is explicitly counted and cannot authorize closure. |
| Mixed-source scheduler | Successful actual Greenhouse/Lever/Ashby/Workday adapters continue alongside isolated 404/429 sources under the bounded source concurrency setting. |

HTTP request cancellation controls enqueue acceptance, not the durable worker's execution token. Execution scopes, heartbeat cleanup and Workday producer tasks are awaited. Generic partial batches remain durable idempotent work; non-Workday adapters do not claim persisted Workday-style detail cursors. A hard process kill cannot emit a final log, so existing lease recovery handles abandoned Running rows. Workday remains non-authoritative for absence reconciliation; partial, capped, malformed or interrupted runs cannot close missing jobs.

## 2. Workday Airbus/Walmart: what is proven

The supplied intended configurations resolve generically, without company branches:

| Control/source | CareerPageUrl | Canonical AtsIdentifier |
|---|---|---|
| J&J | `https://jj.wd5.myworkdayjobs.com/JJ` | `jj/JJ` |
| Airbus | `https://ag.wd3.myworkdayjobs.com/Airbus` | `ag/Airbus` |
| Walmart | `https://walmart.wd504.myworkdayjobs.com/walmartexternal` | `walmart/walmartexternal` |

The earlier repository contract could accept weak configuration at save time and reject it before HTTP at execution. Its strict identifier contract rejected site-only/tenant-only inputs, while its failure path could reduce InvalidOperationException to a generic InfrastructureFailure. Shared resolution now derives only verified URL metadata, accepts matching canonical/tenant/site shorthand or a blank identifier, preserves URL career-site case, rejects conflicts early, and persists canonical metadata only on explicit create/update. Runtime resolution does not silently edit production source rows.

The hostname structure accepts numeric `wdN` instances, including wd1, wd3, wd5, wd12, wd504 and larger valid numeric forms under the existing myworkdayjobs security model. **wd504 was not proven to be unsupported in the old code**. The supplied intended canonical configurations already match the recognized structure; no claim is made that production actually saved those values or a particular malformed alias.

Tests verify J&J/Airbus/Walmart endpoints, request headers/body, listing/detail parsing, aliases, conflict rejection before HTTP and sanitized unexpected pre-provider failures. Relevant files:

- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Features\JobAggregation\WorkdaySourceConfiguration.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Features\JobAggregation\WorkdayExecutionDiagnostics.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Application\Features\JobAggregation\JobSourceConfiguration.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.Infrastructure\Services\WorkdayJobSourceProvider.cs`
- `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\JobPortal.API\Services\JobSourceRunWorker.cs`

The exact Airbus/Walmart production exception branch cannot be proven from InvalidOperationException + ListingTotal=0 alone. **PRODUCTION VERIFICATION REQUIRED**. It is not a local ATS blocker or justification for company-specific code.

Diagnostics retained for that verification:

- `WorkdayConfigurationInput`: source ID, sanitized identifier, verified public host/path.
- `WorkdayConfigurationResolved`: resolved host, tenant, site and validation result.
- `WorkdayConfigurationRejected`: specific code, type and fixed safe message.
- `WorkdayPreExecutionFailure`: actual stage, original error type, safe code and sanitized exception containing the original server-side stack. Original exception messages/Data and inner exceptions are excluded; browser responses contain no stack.
- Specific configuration/request/response failure codes are persisted in run diagnostics; InvalidOperationException no longer appears only as InfrastructureFailure. Known unsafe-checkpoint reasons now retain their own code rather than being misclassified as unexpected internal errors.

The Workday checkpoint source identity includes the new technical-only ingestion-policy marker. An older paused traversal must not silently acknowledge records omitted by the old geography policy. The existing source-identity fence rejects such a resume with `SourceConfigurationChanged`, without closing or deleting jobs; a fresh explicit run uses normal idempotent ingestion. Existing already-completed receipts/finalization recovery remain non-destructive.

## 3. Backend verification and baseline separation

| Completed run | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Final focused ATS/ingestion/publication/reconciliation regressions | 227 | 0 | 0 |
| Final broad ATS/scheduler/worker/normalization/classification suite | 1033 | 8 | 17 |
| Previously captured unchanged-HEAD baseline reproduction | 2 | 9 | 0 |

All eight final broad failures match the previously reproduced failing tests on unchanged `a81e6e1`; **zero new failing tests** remain. The prior geography-dependent Workday accounting test is now updated for the explicitly changed ingestion contract and passes. Negative geography-selection tests were replaced by positive persistence/metadata/publication-separation assertions, without weakening technical checks or adding skips. PostgreSQL contract expectations were updated to all-location ingestion but remain opt-in; their skips are not claimed as passes.

Baseline-only failures retained:

1. `ExternalJobNormalizationPipelineTests.UnmappedNewAndMalformedRecordsFailClosedWithoutLosingValidRecords`.
2. `ExternalJobExpiryTests.Greenhouse_DeadlineSurvivesRunnerPersistenceWithoutRefreshingDuplicateExpiry`.
3. `ExternalJobEnrichmentTests.BroadDepartmentsDoNotImplyTechnologyRoles` — Civil Engineer/Infrastructure.
4. `JobSourceManualRunTests.NewUnmappedJobIsSkippedWithoutClaimingSuccessfulRun`.
5. `MastercardWorkdayVerificationTests.BoundedFixtureIngestionIsIdempotentAndIncompleteDetailsNeverReconcile`.
6–7. `SuccessFactorsDiagnosticsTests.PaginationChangesReportTheFailingOffset` — total/last-offset.
8. `SuccessFactorsDiagnosticsTests.InitialRecordAndPaginationBoundsRemainFailClosed` — 26/0 diagnostic stage.

Unrelated baseline debt was not repaired to make this task green. Per the requested scope, those baseline failures and existing test analyzer debt do not block backend staging.

**Backend production Release build: passed, 0 warnings, 0 errors.** Test runs use invocation-only `-p:TreatWarningsAsErrors=false`; analyzers remain enabled and no repository setting was changed. The established 186 test analyzer diagnostics remain baseline debt; no new diagnostic is retained after correcting an unused theory-parameter warning with a persisted-metadata assertion. `RunAnalyzers=false` was not used. No frontend build/test was run in this backend-only pass.

Commands from the backend root:

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore -m:1 /nodeReuse:false

dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 /nodeReuse:false -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~AtsStabilizationTests|FullyQualifiedName~GenericWorkdayConfigurationTests|FullyQualifiedName~GenericIndiaJobIngestionTests|FullyQualifiedName~SourceAutoPublicationTests|FullyQualifiedName~JobSourceReconciliationSafetyTests|FullyQualifiedName~WorkdayIngestionDiagnosticsTests|FullyQualifiedName~MultiCompanyProviderTests|FullyQualifiedName~EvenCleanWorkdayRerunsNeverCloseMissingVacancies' --logger 'trx;LogFileName=backend-only-focused-final.trx' --results-directory 'Logs/BackendOnly'

dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-build --no-restore -m:1 /nodeReuse:false --filter 'FullyQualifiedName~AtsStabilizationTests|FullyQualifiedName~JobAggregation|FullyQualifiedName~JobSource|FullyQualifiedName~Workday|FullyQualifiedName~SuccessFactors|FullyQualifiedName~MultiCompanyProviderTests|FullyQualifiedName~ExternalJob|FullyQualifiedName~GenericIndia|FullyQualifiedName~SourceAutoPublication|FullyQualifiedName~OracleRecruitingCloud|FullyQualifiedName~JobQualityGateTests|FullyQualifiedName~JobClassificationTests' --logger 'trx;LogFileName=backend-only-broad-final.trx' --results-directory 'Logs/BackendOnly' --blame-hang-timeout 7m
```

Evidence: `Logs/BackendOnly/backend-only-focused-final.trx`, `Logs/BackendOnly/backend-only-broad-final.trx`, `backend-only-focused-final.log`, `backend-only-broad-final.log`, and `backend-only-release-build.log`, all in the backend repository. Original unchanged-HEAD baseline evidence is retained at `C:\Users\Ashish\Documents\Codex\2026-07-27\ats-stabilization-results\ats-baseline.trx` from the prior pass. No new baseline copy or frontend edits were made.

Lever and Ashby actual-adapter regressions retain pagination/identity validation and publication integration. SuccessFactors actual-adapter regressions retain its secure transport/template validation and bounded detail groups. Generic all-location ingestion now applies equally to all five adapters. Oracle authorization-only and unsupported Custom behavior are unchanged.

## 4. Post-deployment verification (prepared, not executed)

`VERIFY_ATS_AFTER_DEPLOYMENT.ps1` remains syntax-checked and ready. It uses the existing Administrator-authenticated API at `https://job-portal-lgjc.onrender.com`, prompts for a bearer token with hidden input, and reads safe saved fields for J&J, Airbus, Walmart, Stripe and Airbnb. Optional switches enqueue each selected source exactly once and poll persisted status through canonical source/run IDs. It never edits configuration, retries an uncertain POST, follows arbitrary returned status URLs, prints a token or deploys anything.

Inspect saved configuration first:

```powershell
& 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\VERIFY_ATS_AFTER_DEPLOYMENT.ps1'
```

After deployment, verify Workday once each:

```powershell
& 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\VERIFY_ATS_AFTER_DEPLOYMENT.ps1' -RunAirbusAndWalmart
```

After deployment, verify Stripe/Airbnb once each:

```powershell
& 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9\VERIFY_ATS_AFTER_DEPLOYMENT.ps1' -RunGreenhouse
```

Both switches can be combined. J&J remains a read-only control. Output distinguishes SUCCESS, failed/partial execution and an unconfirmed monitoring timeout, and includes Created/Existing/Published/NotPublished/SkippedInvalid/Failed, safe reason/stage, StopReason and reconciliation safety. A configuration rejection is reported even when no run can be enqueued. Check Greenhouse's saved career host against its employer-hosted job-link host if `PROVIDER_APPLICATION_URL_UNVERIFIED` is returned.

## 5. Backend staging decision

No remaining demonstrated local ATS ingestion blocker. Production saved configuration/termination attribution and hosting behavior still require the explicit post-deployment verification above. Opt-in PostgreSQL integration tests remain skipped without a disposable local test database; their behavior is not claimed as executed. Existing technical validity, idempotent replay, moderation/ownership and incomplete-run reconciliation safeguards remain.

READY FOR BACKEND STAGING
