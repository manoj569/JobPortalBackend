# Mastercard job source verification

Verification date: **2026-10-12 UTC**. Backend HEAD: `e996926` (classification implementation already committed). Scope: public read-only Mastercard requests, local fixture tests and registration preparation. No production connection, source registration, ingestion, push, deployment, migration or backfill was performed. The operator's successful production PTC run and applied migrations are supplied context, not observations made in this task. No duplicate migrations were added.

## Official careers board and configuration

**PASS.** The [official Mastercard careers page](https://careers.mastercard.com/us/en) returned HTTP 200 and its HTML linked directly to `https://mastercard.wd1.myworkdayjobs.com/CorporateCareers`. Earlier source-expansion notes were independently rechecked. Public request evidence and fixture SHA-256 hashes are in [mastercard-job-source-verification-evidence.json](mastercard-job-source-verification-evidence.json).

| Configuration | Verified value |
|---|---|
| Company | Mastercard; resolve the existing CareerHarbor company GUID before registration |
| Provider | Existing generic Workday; `AtsType.Workday = 5` |
| Host | `mastercard.wd1.myworkdayjobs.com` |
| Tenant | `mastercard` |
| Site | `CorporateCareers` (case preserved) |
| CareerPageUrl | `https://mastercard.wd1.myworkdayjobs.com/CorporateCareers` |
| AtsIdentifier | `mastercard/CorporateCareers` |
| Listing | POST `https://mastercard.wd1.myworkdayjobs.com/wday/cxs/mastercard/CorporateCareers/jobs` |
| Detail | GET `https://mastercard.wd1.myworkdayjobs.com/wday/cxs/mastercard/CorporateCareers` + returned `/job/...` externalPath |

The listing request matched the existing provider: `{"appliedFacets":{},"limit":20,"offset":0,"searchText":""}`, changing only offset to 20 and 40, with Accept `application/json` and Accept-Language `en-US`. No India search or inferred country facet was used. The generic provider currently sends empty facets for every tenant; Mastercard receives no Accenture-specific filtering.

## Bounded listing and pagination verification

**PASS for sampled pagination; full production census unverified.** Requests were made around 10:08–10:10 UTC. All three listing requests and a final first-page anchor returned HTTP 200.

| Offset | ReportedTotal | RawSlots | UniquePaths | DuplicatePaths | Placeholders | TotalOmitted |
|---|---:|---:|---:|---:|---:|---|
| 0 | 1,084 | 20 | 20 | 0 | 0 | false |
| 20 | 0 | 20 | 20 | 0 | 0 | true |
| 40 | 0 | 20 | 20 | 0 | 0 | true |
| 0 anchor | 1,084 | 20 | 20 | 0 | 0 | false |

Combined sample: **60 raw slots / 60 distinct paths / 60 distinct listing requisition references / zero duplicate paths / zero placeholders**. Subsequent pages differ from page zero and from each other. The final anchor matched page-zero paths and total exactly. Continuation `total:0` is an omission, not a zero-vacancy census; this is already supported by the provider's total/anchor validation. A complete run must reach the advertised raw boundary, deduplicate paths, fetch all unique details and satisfy every reconciliation guard. This task did not fetch all 1,084 jobs or certify absence reconciliation for the live board.

## India and detail samples

**PASS.** Thirteen of the 60 listing entries explicitly mention India. Four distinct Indian locations were sampled at the detail endpoint; all returned HTTP 200, country descriptor `India`, nested `alpha2Code:IN`, `posted:true` and `canApply:true`. A fifth detail explicitly returned `United Kingdom` / `GB` and is correctly rejected by India eligibility. These are sample counts, not the board's India total. No live generic-remote example was asserted; deliberately mutated fixture variants test that policy separately.

| Title | Location | Requisition | Returned externalPath |
|---|---|---|---|
| Senior Enterprise Operations Engineer-1 | Pune, India | R-278780 | `/job/Pune-India/Senior-Enterprise-Operations-Engineer-1_R-278780` |
| Senior Data Engineer-1 | Hyderabad, India | R-292031 | `/job/Hyderabad-India/Senior-Data-Engineer-1_R-292031` |
| Senior Full‑Stack Engineer - React, Typescript, Node & Java | Navi Mumbai, India (Finicity) | R-291599 | `/job/Navi-Mumbai-India-Finicity/Senior-Full-Stack-Engineer---React--Typescript--Node---Java_R-291599` |
| Director, Sales Operations & Business Transformation | Gurgaon, India | R-291604 | `/job/Gurgaon-India/Director--Sales-Operations---Business-Transformation_R-291604` |
| Director, Finance Business Partner | London, England (Angel Lane) | R-291339 | `/job/London-England-Angel-Lane/Director--Finance-Business-Partner_R-291339` |

Example detail: `https://mastercard.wd1.myworkdayjobs.com/wday/cxs/mastercard/CorporateCareers/job/Pune-India/Senior-Enterprise-Operations-Engineer-1_R-278780`.

Source-returned application entry: [Senior Enterprise Operations Engineer-1](https://mastercard.wd1.myworkdayjobs.com/CorporateCareers/job/Pune-India/Senior-Enterprise-Operations-Engineer-1_R-278780). The existing provider uses the same posting path with its standard `/en-US/CorporateCareers` locale prefix. That exact provider-generated URL was independently fetched on 2026-10-12 at 10:17:31 UTC and returned HTTP 200 with no redirect. It remains a specific vacancy URL, never the generic company board. `status` is absent in the sampled detail documents; absence was recorded as null, not fabricated as an open-status enum.

## Reused code paths and CareerHarbor field mapping

No Mastercard scraper, provider registration, migration or runtime configuration change is needed. The existing paths are:

- `WorkdayJobSourceProvider.ValidateSource`, `ReadListingAsync`, `RequestListingPageAsync`, `ParseListingPage`, `ParseDetail`, bounded HTTP retries and batched detail processing.
- `JobSourceRunner` → `JobSourcePublicationPolicy` / `IndiaJobEligibilityPolicy` → `ExternalJobNormalizer` → `JobSourceCategoryResolver` → `JobIngestionService` → `JobClassificationService`.
- `JobIngestionService` source identity lookup and `JobDeduplicationService`; `JobSourceRunCheckpoint` / `JobSourceRunStore` for durable execution; `JobAutoPublishService`, `JobQualityGate` and existing publication policy.
- Existing Administrator-only `AdminJobSourcesController` and `JobSourceManagementService` create/update/search APIs; `JobSourceSchedule` and `JobAggregationSchedulerHostedService`.

| Field | Evidence and behavior |
|---|---|
| Title / description | Exact `jobPostingInfo.title` and HTML `jobDescription`; shared normalizer sanitizes HTML |
| Primary / additional location | `location`, `additionalLocations` and explicit country objects; no country inferred from generic Remote |
| Application URL | Validated board host/site plus listing externalPath, existing `en-US` locale; verified against real posting |
| Reference / stable identity | `jobReqId`, falling back to jobPostingId, listing bullet ID then path only if absent; persistence owns `(JobSourceId, ExternalJobId)` |
| Company | Existing registered source CompanyId / company, not the subsidiary hiringOrganization name |
| Employment type | Sample `timeType:"Full time"` maps to FullTime; classification takes structured provider facts |
| Workplace | Sample remoteType absent → Unknown; office city alone is not OnSite; explicit description evidence may classify separately |
| Experience | Only explicit required JD evidence; test minimum 3 stays 3, maximum null; education years are not experience |
| Salary / currency / period | No salary fields supplied by these Workday details; captured sample classification keeps all four null. No INR/USD default is persisted |
| Department / category | No structured department mapping is invented; existing classifier resolves only an unambiguous existing taxonomy match, otherwise source fallback/operator mapping or skip/review |
| Education | Description retained; generic Workday adapter does not extract EducationRequirement into a structured field |
| Deadline | Sample date-only endDate retained in captured evidence but not converted by the existing adapter into an invented UTC expiry |
| Publication | New jobs start Draft; the existing imported-job quality gate and JobService publish eligible jobs. The legacy AutoPublishEnabled flag is ignored by current JobAutoPublishService; absent salary, education, numeric experience and expiry are optional and do not by themselves block publication. Invalid or geographically ambiguous facts still fail/require review |

Explicit India is accepted, explicit foreign country is rejected, generic remote/unknown location is held for review. Truly absent location/country is accepted with `Location not specified` under the existing business rule, not claimed to be confirmed India. Manually overridden classification groups remain protected by the shared ingestion path and existing tests.

Raw duplicate cards are deduplicated; duplicate/placeholder listing anomalies do not certify completeness. Reconciliation requires provider and selection completeness, no provider omissions, unique nonempty observed identities and no ingestion failures/skips. Geographic exclusions are selection counts, not ingestion failures; all observed identities protect held/foreign records from false absence closure. A configured TestImportLimit remains incomplete. Checkpoints preserve bounded continuation and commit persisted items before cursor advancement. No checkpoint implementation changed; previous disposable PostgreSQL verification is recorded in [classification database verification](job-description-classification-database-verification.md).

## Source registration

**Already exists: UNKNOWN / BLOCKED.** No authenticated production source inventory or database was inspected. Historical source-expansion templates do not establish registry absence. The API rejects repeated same-company / ATS / trimmed identifier registrations, but that alone does not exclude duplicate companies or alternate host/locale aliases. Before POST, inspect every relevant active, inactive and soft-deleted source using the operator's existing access; match company, Workday provider, host, tenant/site and normalized identifier. Reuse an existing matching source, including an inactive source; do not create a second company or source.

**Configuration prepared: YES. Ready to register now: NO, pending registry/company resolution. Ready for a manual production run now: NO.** The verified, inactive request template for `POST /api/admin/job-sources` is:

```json
{
  "companyId": "<resolved-existing-mastercard-company-guid>",
  "careerPageUrl": "https://mastercard.wd1.myworkdayjobs.com/CorporateCareers",
  "atsType": 5,
  "atsIdentifier": "mastercard/CorporateCareers",
  "isActive": false,
  "scanIntervalMinutes": 720
}
```

Resolve companies using `GET /api/admin/companies?pageNumber=1&pageSize=100&search=Mastercard&isDeleted=false`, following all pages and checking duplicates/deleted companies as appropriate. The existing source GET search API is `GET /api/admin/job-sources?pageNumber=1&pageSize=100&companyId=<guid>`; follow every page and also inspect provider-wide sources for aliases/another Mastercard company. Soft-deleted sources require the existing operator inventory/database access because normal source search applies query filters. GET `/api/admin/job-sources/<sourceId>` verifies any selected record.

Keep registration inactive. For a later operator-authorized manual run, first verify the deployed `JobAggregation__Scheduler__Enabled=false`; then update only the selected source through PUT with the same fields and `isActive:true`. The durable manual run endpoint rejects inactive sources, so an inactive source cannot be manually run. There is no separate per-source manual-only flag. Activating with the scheduler enabled would make a never-run source immediately due. Configure the existing source category fallback only when an appropriate category is verified; avoid mapping a broad multinational board wholesale to Technology. Verify relevant existing taxonomy options and publication settings before ingestion.

## Tests and Release builds

New Mastercard tests: **11 passed / 0 failed / 0 skipped**, `../mastercard-verification-artifacts/mastercard-fixtures-final.trx`. New bounded fixtures record three live pages, the unchanged anchor and five live details; the reduced five-row fixture census is explicitly synthetic and is never represented as the full production board. Tests cover actual listing/detail parse and identity, generic request payload, country decisions, missing salary, exact minimum experience, duplicate-safe repeated shared-runner ingestion, real shared publication of four eligible India jobs and failed-detail reconciliation protection. One UK job is excluded. Repetition creates and publishes zero additional jobs; a failed detail preserves all existing published jobs, IDs and the previous success timestamp.

The initial test run had five failures because a newly written assertion expected an enum Unknown directly from the nullable adapter field. The adapter correctly returns null; classification/domain represent the absent value as Unknown. That assertion was corrected, and the Pune/hybrid-cloud example explicitly asserts classification Unknown. A missing import for the existing nested contact validator was also corrected while adding the publication fixture. Initial logs are retained as test-authoring evidence, not baseline failures or unresolved runtime implementation defects. No production implementation changed.

Existing regressions: **179 passed / 0 failed / 0 skipped** in 3m23s, `../mastercard-verification-artifacts/mastercard-regressions.trx`. The selection covers Workday provider/batch/diagnostic/persistence tests, PTC/PwC and generic source registration, classification, reconciliation, interruption and durable-run fixtures. It excludes only the synthetic Workday performance benchmark within those classes. Combined final selection: **190 passed / 0 failed / 0 skipped**. PostgreSQL integration classes were not selected or rerun in this Mastercard-only task; their earlier disposable-database evidence remains in the linked classification report. No live ingestion is part of these tests.

Solution restore succeeded using .NET SDK 9.0.302. Normal strict Release builds of **JobPortal.API and JobPortal.Maintenance both succeed with zero warnings / zero errors**. Logs are `../mastercard-verification-artifacts/api-release.log` and `maintenance-release.log`. Test runtime compilation and the separate strict test-project result are described below.

The strict test-project build fails with **184 existing findings**. The final runtime test build succeeds with 184 warnings and zero compiler errors; only its invocation uses `-p:TreatWarningsAsErrors=false` to execute tests, with analyzers enabled. Repository strict/analyzer configuration is unchanged. `../mastercard-verification-artifacts/analyzer-comparison-final.json` compares every diagnostic file, line, column, rule and message against the saved classification inventory: baseline 184, final 184, added 0, removed 0.

Nine unrelated runtime regression failures were established in [classification predeployment verification](job-description-classification-predeployment-verification.md), with preserved pre-classification reproduction evidence. They were not rerun as part of this Mastercard selection: the Lever shared-runner fixture, Civil Engineer category classification, malformed/unmapped normalization, Greenhouse expiry, unmapped manual-run status and four SuccessFactors wrong-board pagination cases. They remain separate baseline release failures, not Mastercard regressions or a green overall backend release. No analyzer settings or assertions are weakened.

Reproduction from the backend root (build/test only):

```powershell
dotnet restore JobPortal.sln --artifacts-path ../mastercard-release-artifacts --ignore-failed-sources
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore --artifacts-path ../mastercard-release-artifacts -m:1 -p:UseSharedCompilation=false
dotnet build JobPortal.Maintenance/JobPortal.Maintenance.csproj -c Release --no-restore --artifacts-path ../mastercard-release-artifacts -m:1 -p:UseSharedCompilation=false
# Strict test build reproduces the 184 baseline errors:
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore --artifacts-path ../mastercard-release-artifacts -m:1 -p:UseSharedCompilation=false
# Runtime test compilation keeps analyzers enabled and reports their baseline warnings:
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore --artifacts-path ../mastercard-release-artifacts -m:1 -p:UseSharedCompilation=false -p:TreatWarningsAsErrors=false
dotnet vstest ../mastercard-release-artifacts/bin/JobPortal.Application.Tests/release/JobPortal.Application.Tests.dll '--TestCaseFilter:FullyQualifiedName~MastercardWorkdayVerificationTests' '--logger:trx;LogFileName=mastercard-fixtures-final.trx' --ResultsDirectory:../mastercard-verification-artifacts
$filter = '(FullyQualifiedName~WorkdayJobSourceProviderTests|FullyQualifiedName~WorkdayBatchIngestionTests|FullyQualifiedName~WorkdayIngestionDiagnosticsTests|FullyQualifiedName~WorkdayPersistenceTrackingTests|FullyQualifiedName~PtcWorkdaySourceRegistrationTests|FullyQualifiedName~PwcWorkdaySourceRegistrationTests|FullyQualifiedName~CompanySourceExpansionRegistrationTests|FullyQualifiedName~JobSourceReconciliationSafetyTests|FullyQualifiedName~JobSourceInterruptedRunTests|FullyQualifiedName~JobSourceDurableRunTests|FullyQualifiedName~JobClassificationTests)&FullyQualifiedName!~MeasureSyntheticWorkdayPipelineWithRealRepositories'
dotnet vstest ../mastercard-release-artifacts/bin/JobPortal.Application.Tests/release/JobPortal.Application.Tests.dll "--TestCaseFilter:$filter" '--logger:trx;LogFileName=mastercard-regressions.trx' --ResultsDirectory:../mastercard-verification-artifacts
```

## Verification gates

| Gate | Result | Scope / evidence |
|---|---|---|
| Official board ownership and endpoint configuration | PASS | Official HTML exact-board link; live public endpoints |
| Actual listing request and distinct paging | PASS | Offsets 0/20/40, 60 distinct paths, stable 1,084-job anchor; remainder unverified |
| India/non-India detail and application entry | PASS | Four IN details, one GB detail, all posted/applyable; specific provider URL HTTP 200 |
| Shared mapping, classification and stable identity | PASS | Live fixtures, exact minimum/no invented salary/workplace, existing provider path |
| Duplicate-safe replay and publication | PASS | Four published local India jobs, repeat zero creations/publications |
| Incomplete reconciliation / durable-run regression behavior | PASS | Failed detail retains published inventory/success time; current generic regression selection passes |
| Current selected runtime tests | PASS | 190 passed / 0 failed / 0 skipped |
| API / maintenance strict Release builds | PASS | Both zero warnings/errors; restore successful |
| Newly introduced analyzer or runtime issues | PASS | Zero remaining; exact baseline diagnostic comparison unchanged |
| Strict test-project build | FAIL | 184 documented pre-existing analyzer findings |
| Independent overall backend regression acceptance | FAIL | Nine previously established baseline failures outside this selection remain unresolved |
| Existing production Mastercard source / duplicate-company inventory | BLOCKED | No authenticated registry inspected; existing source UNKNOWN |
| Ready to register immediately | BLOCKED | Inactive config ready, existing CompanyId/source inventory unresolved |
| Ready for manual production execution immediately | BLOCKED | Registry, selected source activation, deployed version and disabled scheduler must be verified by operator |

Files added: one Mastercard test class, ten bounded JSON fixtures, this report and its public evidence JSON. Runtime provider/application code, frontend, migrations and deployed configuration are unchanged.

## Recommended production procedure and remaining risks

1. **Next action:** an operator performs the read-only source/company inventory above. Record existing source Yes/No and the resolved existing CompanyId. No write is justified while this gate is unknown.
2. If absent, use the verified inactive Admin request; if present, review/reuse its configuration. Registration changes require the operator's explicit action after this task.
3. Confirm deployed generic Workday/classification/checkpoint versions and migration history using existing operator checks; no new migration is required for Mastercard. Resolve or explicitly disposition the independent backend baseline release failures.
4. Verify scheduler disabled, source category behavior and current publication settings; review then activate only Mastercard when the operator authorizes its manual execution.
5. POST `/api/admin/job-sources/<mastercardSourceId>/run` only at that later stage; poll GET `/api/admin/job-sources/<mastercardSourceId>/runs/<runId>`. Inspect advertised/raw/unique/duplicate/placeholder counts, detail success/failure, India selection, new/existing jobs, publication/review, checkpoints, continuation, completeness and reconciliation blockers.
6. Expect bounded durable continuation for a 1,084-job board; do not treat one execution or this 60-row probe as a complete source census. Reconcile only after the unchanged generic guards certify completeness. Verify repeated completed runs do not create duplicate source identities.

Known limitations: mutable public inventory, unverified remainder of the board, unknown production registry and deployed operator settings, date-only deadlines not extracted as UTC expiry, no live remote sample, baseline strict analyzer/runtime failures. Technical sampled compatibility is verified; production registration/run readiness remains blocked by the explicit registry and operator gates. **Production changes: NONE.**
