# Oracle Recruiting Cloud: configuration-only support

Investigated 2026-10-13. Provider name `OracleRecruitingCloud`, enum value `6`, display name **Oracle Recruiting Cloud**. Existing enum values are unchanged.

## Status and authorization

This is reusable configuration and registration infrastructure, **not a production-ready ingestion adapter**. Live listing/detail retrieval, normalization and publication are disabled for every tenant. No credentials, partner approval or employer-specific authorization were supplied. No live Oracle REST endpoint was called, no jobs were imported, and no employer was activated or inserted into the database.

Oracle explicitly restricts [Job Site Posted Jobs](https://docs.oracle.com/en/cloud/saas/human-resources/25b/farws/api-job-site-posted-jobs.html) to approved Oracle Cloud Marketplace partners. A publicly visible career page or an administrator-entered status does not satisfy this prerequisite or establish republication rights.

[Partner integration provisioning](https://docs.oracle.com/en/cloud/saas/talent-management/faimh/set-up-partner-integration-provisioning.html) describes employer administrator provisioning/activation, partner reference keys and separately provisioned test/production user credentials. It lists Job Site REST Services duties for Direct Apply and a separate Job Distribution privilege. The applicable integration guide and tenant security configuration must be agreed with Oracle and each employer; these are not interchangeable permissions. No credentials belong in `AtsIdentifier`, career URLs, Admin responses or logs.

An employer-authorized export/feed could be considered separately, but none was supplied or verified. This scaffold has no hidden public-scraping fallback.

## Documented contract, not validated connectivity

The [collection reference](https://docs.oracle.com/en/cloud/saas/human-resources/farws/op-recruitingjobsitepostedjobs-get.html) documents `GET /hcmRestApi/resources/11.13.18.05/recruitingJobSitePostedJobs`. Pagination uses `limit` and `offset`, with response `items`, `count`, `hasMore`, `limit`, `offset`, and links. `totalResults` is an **estimated** count, not a completeness certificate. It also documents requisition identifiers/numbers, publication dates/status, primary geography, employment fields, language children and skills. Optional tenant fields must remain unknown when absent.

The [detail reference](https://docs.oracle.com/en/cloud/saas/human-resources/farws/op-recruitingjobsitepostedjobs-recruitingjobsitepostedjobsuniqid-get.html) documents a resource key obtained from the collection; clients must not generate that composite hash key. A stable business requisition identity and this transport key must not be conflated. No supported application-URL mapping was established from the reviewed contract; preserve an authorized feed's official links after validation, rather than guessing URL templates from requisition numbers.

The collection/detail references do not establish a tenant rate allowance or a transactionally consistent full snapshot. Those remain prerequisites for the live implementation. Any future adapter must respect agreed rate limits, cancellation and retry budgets, validate every page/detail and source identity, and fail closed on authorization errors, missing pages, duplicates, drift, timeouts or partial scans. Never authorize reconciliation merely because discovered count equals an estimate.

## Admin configuration contract

Use existing administrator-only `POST/PUT /api/admin/job-sources` and existing GET/search endpoints. No frontend or schema changes were made.

| Requested field | API/storage mapping |
| --- | --- |
| CompanyId | Existing `companyId`; must identify an existing company |
| OracleHost | Encoded in `atsIdentifier`; optional matching input under `oracleRecruitingCloud.oracleHost`; derived in responses |
| CareerSiteId | Encoded in `atsIdentifier`; optional matching input under `oracleRecruitingCloud.careerSiteId`; derived in responses |
| CareerPageUrl | Existing `careerPageUrl` |
| SourceIdentifier | Existing `atsIdentifier`; also returned as `oracleRecruitingCloud.sourceIdentifier` |
| ScanIntervalMinutes | Existing field, default 720, existing bounds 1–10080 |
| IsEnabled | Existing `isActive`; must be explicitly false for Oracle; effective response `oracleRecruitingCloud.isEnabled` is false |
| AutoPublishEnabled | Optional nested input must be false; effective response is always false |
| AuthorizationStatus | Optional nested input must be `AuthorizationRequired`; response is always `AuthorizationRequired` |

The two safety values are effective capability/status values in this release, not editable persisted approval records. Accepting `Approved` or `autoPublishEnabled=true` would misrepresent an unavailable integration, so validation rejects them. Host/site inputs are optional because the canonical identifier already contains them. If supplied, they must agree with it. This avoids duplicate persisted configuration and an unnecessary migration.

Identifiers are `oracle:<lowercase-host>:<case-preserved-career-site-id>`. Surrounding identifier whitespace is trimmed. The approved structure is one DNS tenant label followed by `.fa.ocs.oraclecloud.com`; arbitrary subdomains, custom domains, IP literals, localhost, ports, credentials, slash/query injection and invalid site IDs are rejected. Only matching HTTPS career URLs under `/hcmUI/CandidateExperience/<locale>/sites/<site>/jobs` or `/requisitions` are accepted, without query, fragment, explicit port or path normalization tricks. This is structural validation, not proof of tenant ownership or access permission.

No HTTP transport exists in this scaffold, so DNS requests, redirects and private-network connections cannot occur. Future network support additionally requires DNS/address validation at connection time, redirect rejection and bounded transport policies; syntactic host validation alone is not enough.

Oracle activation is rejected by validation. The scheduler excludes Oracle even if a row was manually activated. Both Admin run entry points return HTTP 400 with `oracle_authorization_required` before enqueueing or executing. The runner separately blocks pre-existing/queued active sources before checkpoints, ingestion, enrichment, publication or reconciliation, returning `Succeeded=false`, `ScanComplete=false`, `ReconciliationSafe=false`, and a diagnostic stop reason. Direct provider calls throw a sanitized error; they never return fake jobs or a successful empty inventory. Existing run-status endpoints remain available for historical runs.

For other providers, the new nested response is omitted and existing behavior is preserved.

## Four employer examples

These are prepared examples, not database configurations or authorization claims. Resolve the existing company's real GUID before using any request. All examples use site `CX_1`, interval 720, `isActive=false`, `autoPublishEnabled=false`, and `authorizationStatus=AuthorizationRequired`.

| Company | OracleHost | Verified career page |
| --- | --- | --- |
| Hexaware Technologies | `fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com` | [HEXAWARE](https://fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs) |
| Zensar Technologies | `fa-etvl-saasfaprod1.fa.ocs.oraclecloud.com` | [Zensar](https://fa-etvl-saasfaprod1.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs) |
| eClerx | `fa-ewji-saasfaprod1.fa.ocs.oraclecloud.com` | [eClerx Career Site](https://fa-ewji-saasfaprod1.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs) |
| Quess Corp | `fa-eumz-saasfaprod1.fa.ocs.oraclecloud.com` | [Quess Career site](https://fa-eumz-saasfaprod1.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs) |

Verification: [Zensar's corporate careers page](https://www.zensar.com/careers) links its Oracle site. [Quess Careers](https://careers.quesscorp.com/) links its Oracle requisitions page, which redirects to the jobs page above. The Hexaware supplied URL resolves with HEXAWARE branding. The eClerx Oracle requisitions URL resolves to its jobs page with eClerx Career Site branding; a current corporate backlink was not found in the inspected [eClerx careers page](https://eclerx.com/careers/). Reconfirm that tenant with the employer during authorization. None of these observations grants feed or republication rights.

Request template (substitute each row's host, career page and existing company GUID; `6` uses the existing enum serialization contract):

```json
{
  "companyId": "<existing-company-guid>",
  "careerPageUrl": "https://fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com/hcmUI/CandidateExperience/en/sites/CX_1/jobs",
  "atsType": 6,
  "atsIdentifier": "oracle:fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com:CX_1",
  "isActive": false,
  "scanIntervalMinutes": 720,
  "oracleRecruitingCloud": {
    "oracleHost": "fa-etqo-saasfaprod1.fa.ocs.oraclecloud.com",
    "careerSiteId": "CX_1",
    "autoPublishEnabled": false,
    "authorizationStatus": "AuthorizationRequired"
  }
}
```

The other canonical identifiers are:

```text
oracle:fa-etvl-saasfaprod1.fa.ocs.oraclecloud.com:CX_1
oracle:fa-ewji-saasfaprod1.fa.ocs.oraclecloud.com:CX_1
oracle:fa-eumz-saasfaprod1.fa.ocs.oraclecloud.com:CX_1
```

## Architecture findings and remaining blockers

- Providers implement `IExternalJobProvider`; the runner selects by `AtsType`, and Infrastructure registers scoped providers. Workday uses the batched complete-snapshot interface; SuccessFactors validates HTML pagination/details. Neither adapter was modified. Oracle intentionally does not implement `ICompleteExternalJobProvider`.
- Admin source management requires an existing CompanyId and enforces `(CompanyId, AtsType, AtsIdentifier)` uniqueness. Canonicalizing Oracle hosts before this check prevents mixed-case host duplicates. Existing ingestion prioritizes `(JobSourceId, ExternalId)` for owned updates and retains canonical URL/fingerprint deduplication. A future Oracle requisition ID can reuse this architecture; no company or job schema migration is presently required.
- Durable runs/checkpoints, audit logging, enrichment, quality review and verified-complete-snapshot reconciliation already exist. The new execution guard ensures the scaffold never invokes those mutation paths.
- **Publication blocker remains:** `JobAutoPublishService` explicitly ignores legacy `JobAggregation.AutoPublishEnabled`, and existing regression tests assert that behavior. `SourceApprovals` is not a publication authorization gate. No global fix was made because it would change existing providers. Oracle remains blocked regardless of that flag. Supporting authorized Oracle ingestion with `AutoPublishEnabled=false` while preserving drafts independently of the run guard requires a tested source-specific publication control before activation is added.
- **India eligibility prerequisite:** the shared policy supports multiple locations and explicit India evidence, rejects foreign-only jobs and holds generic remote. It also has `MissingLocationAccepted`. A future Oracle adapter must exclude unknown-location jobs or apply a source-specific stricter policy without changing current providers. No Oracle jobs pass the runner today.
- API/feed credentials, source-specific rights, tenant/site filtering, application links, optional field mappings, agreed rate limits and snapshot stability remain unvalidated. No fabricated responses or fixture tests are presented as proof of live access.
- Frontend follow-up, separate from this change: add provider value 6/display name, canonical configuration inputs, the blocked authorization explanation, and disabled Oracle activation/publication/run controls. No frontend file was touched.

## Validation scope

Focused tests cover registration alongside existing providers, multiple tenant/company configuration, canonical duplicate rejection, unsafe host/URL rejection, authorization and publication activation rejection, Admin round trips, safe cancellation, direct-provider refusal, queue/scheduler blocking, repeated blocked runs and preservation of draft/published jobs and successful-run history.

Existing provider, ingestion, India eligibility, scheduler, durable-run, publication and reconciliation tests are also run as regressions. Authorized feed parsing, Oracle pagination fixtures, live requisition deduplication and Oracle field normalization tests are deferred until the authorized feed is supplied and the adapter is implemented. The existing generic tests do not certify Oracle live behavior.

The requested `dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release` passed with **0 warnings and 0 errors** (44.05 seconds). Test compilation uses `-p:TreatWarningsAsErrors=false` for the repository's existing test analyzer debt; the API Release build retains strict warnings-as-errors.

The focused run executed 272 tests: **267 passed, 5 failed, 0 skipped**. All 34 cases in `OracleRecruitingCloudTests` passed, along with the added Oracle DI registration case. Failures: four cases of `SuccessFactorsJobSourceProviderTests.WrongBoardOrOffsetPaginationCannotProduceCompleteSnapshot` and `JobSourceManualRunTests.NewUnmappedJobIsSkippedWithoutClaimingSuccessfulRun`. Results are saved outside the repository at `../oracle-test-results/oracle-focused.trx`.

All five failures were reproduced, with the same assertions, against an isolated `git archive` export of original commit `2b8b013`, without the Oracle changes. Baseline results: `../oracle-test-results/oracle-baseline.trx` (0 passed, 5 failed, 0 skipped; only the five failures were selected). These are verified pre-existing failures and were left unchanged. The broader suite is not fully green.

Focused command:

```powershell
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj -c Release --no-restore -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~OracleRecruitingCloudTests|FullyQualifiedName~JobAggregationDependencyInjectionTests|FullyQualifiedName~JobSourceAdministrationTests|FullyQualifiedName~JobSourceManualRunTests|FullyQualifiedName~JobSourceAuthorizationTests|FullyQualifiedName~JobSourceRunnerTests|FullyQualifiedName~JobSourceReconciliationSafetyTests|FullyQualifiedName~JobSourceDurableRunTests|FullyQualifiedName~JobAggregationDueSourceTests|FullyQualifiedName~JobAggregationScheduler|FullyQualifiedName~GenericIndiaJobIngestionTests|FullyQualifiedName~ExternalJobProviderTests|FullyQualifiedName~WorkdayJobSourceProviderTests|FullyQualifiedName~SuccessFactorsJobSourceProviderTests|FullyQualifiedName~JobAutoPublishServiceTests' --results-directory '..\oracle-test-results' --logger 'trx;LogFileName=oracle-focused.trx'
```

`git diff --check` passed. The index is unchanged/empty for this task. Docker was not run; this task requested the .NET Release build. No claim of live connectivity or production readiness follows from these offline tests.

## Exact repository files changed

Modified:

- `JobPortal.Domain/Enums/DomainEnums.cs`
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceManagementDtos.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceManagementValidators.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceRunService.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceSchedule.cs`
- `JobPortal.Application/Services/JobSourceRunner.cs`
- `JobPortal.Application.Tests/JobAggregationDependencyInjectionTests.cs`

Added:

- `JobPortal.Application/Features/JobAggregation/OracleRecruitingCloudConfiguration.cs`
- `JobPortal.Infrastructure/Services/OracleRecruitingCloudJobSourceProvider.cs`
- `JobPortal.Application.Tests/OracleRecruitingCloudTests.cs`
- `docs/oracle-recruiting-cloud.md`

The pre-existing untracked `docs/browse-jobs-production-audit.md` is untouched. All changes remain unstaged. No migration, frontend edit, commit, push, deployment or production database operation was performed.

## Next steps

1. Obtain approved partner access or an explicitly authorized employer feed, with separate source-specific ingestion/republication permission and secure test credentials.
2. Verify the career-site mapping and full listing/detail contract against that authorized test source, including stable requisition IDs and official application links.
3. Implement source-specific authorization/publication controls and strict India evidence handling; test disabled publication independently of the ingestion guard.
4. Implement the reusable live adapter with bounded requests, cancellation, pagination consistency checks and incomplete-snapshot protection. Add authorized contract fixtures and all deferred data-path tests.
5. Complete an isolated full validation scan with explicit counts and a strict Release build before considering activation. No deployment or activation is part of this task.
