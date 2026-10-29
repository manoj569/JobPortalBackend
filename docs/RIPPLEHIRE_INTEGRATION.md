# RippleHire integration

RippleHire is appended as `AtsType.RippleHire = 8`; persisted values 0–7 are unchanged. No migration is needed. No database was reset, no production migrations were applied, and nothing was pushed or deployed.

## Verified mechanism (29 October 2026)

The public LTM candidate page was fetched without login or cookies:
https://ltm.ripplehire.com/candidate/?lang=en&source=INDEED&token=pwHdNly1GEtOKNRuAGnS

Its RequireJS application v738 loads these public modules:
- https://ltm.ripplehire.com/candidate/candresource/candidate/entities/job.js
- https://ltm.ripplehire.com/candidate/candresource/candidate/apps/candidates/controller/candidatejoblist_controller.js

The controller initializes `page=0`, `pagesize=10`, `search=*:*`, public `token` and optional `source`. The model sends form-encoded `careerSiteUrlParams` (JSON) and `lang=en`, with Accept: application/json:
- POST `/candidate/candidatejobsearch`: `errorStatus`, `totalJobCount`, `jobVoList`.
- POST `/candidate/candidatejobsearchpage`: array for subsequent zero-based pages.
- GET `/candidate/candidatejobdetail?token=...&jobSeq=...&source=...&lang=en`: `jobVO` envelope.

The observed search returned 1,416 listings. Page 1 and job 905121 detail were also fetched. This verifies one deployment's public frontend contract, not a vendor-supported universal API. No candidate submissions, authentication endpoints, CAPTCHA bypasses or private candidate links were used.

Fixtures retain actual public listing, next-page and job detail responses. The detail fixture excludes company administration/contact metadata. Tests alter counts/IDs to exercise bounded scenarios; these test variations are never imported or presented as live jobs.

## Supported configuration

`https://{tenant}.ripplehire.com/candidate/?token={public-career-token}` with optional `source={value}` and `lang=en`. Supply the employer's public career token, never a private candidate token. AtsIdentifier may be null or match that token. Fragment-bearing job URLs cannot configure a source. Legacy `/ripplehire/candidate`, custom domains, non-English variants, unknown query parameters, unsafe characters and alternate ports are rejected.

Tenant validation plus the existing connection-pinned public-IP transport blocks private/loopback/link-local/reserved DNS resolutions and DNS rebinding. Redirects, proxies and cookies are disabled. The named client's HTTP logging is disabled so token-bearing URLs do not enter standard HTTP logs.

## Admin API example

Use an existing employer CompanyId. This is an example request, not an executed production operation.

```http
POST /api/admin/job-sources
Authorization: Bearer <administrator-token>
Content-Type: application/json

{
  "companyId": "<existing-LTM-company-guid>",
  "careerPageUrl": "https://ltm.ripplehire.com/candidate/?lang=en&source=INDEED&token=pwHdNly1GEtOKNRuAGnS",
  "atsType": 8,
  "atsIdentifier": null,
  "isActive": true,
  "scanIntervalMinutes": 720,
  "autoPublishEnabled": false
}
```

Existing PUT updates, source search/GET and DELETE remain applicable. Set IsActive with PUT to enable/disable. `POST /api/admin/job-sources/{id}/test` verifies the listing without importing. `POST /api/admin/job-sources/{id}/run` queues manual synchronization; scheduled runs use the existing worker. Poll `GET /api/admin/job-sources/{id}/runs/{runId}`.

Example response fields (illustrative placeholders, not measured imports):

```json
{
  "data": {
    "runId": "<run-guid>",
    "jobSourceId": "<source-guid>",
    "status": "Queued",
    "totalReceived": 0,
    "created": 0,
    "updated": 0,
    "skipped": 0,
    "failed": 0,
    "succeeded": false
  }
}
```

Completed responses use existing created/updated/unchanged/matched/skipped/failed/publication and India-eligibility counters. Existing run logs include provider, source, timing, counts and failure category; RippleHire adds pages processed and fetched listing counts.

## Mapping and synchronization

Stable jobSeq is the external ID; the runner scopes it by JobSourceId and CompanyId. Existing ingestion performs upserts and preserves saved-job/application references. Application URLs preserve the original configured public employer URL with the frontend's verified `#detail/job/{jobSeq}` route. Canonicalization retains numeric RippleHire job fragments to prevent URL deduplication from merging unrelated jobs; other providers keep their existing fragment rules.

Only `allowCareerSite=Y`, `jobStatus=ACTIVE` details are eligible. Description HTML, location, listing country/location evidence, employment-type text and skills/requirements HTML are mapped when available. SourcePostedAtUtc uses an explicit UTC `publishDetails.CAREER_SITE` timestamp. Date-only jobPostingDate is not converted into an invented instant. Unknown salary, work mode, experience units, deadline and other unavailable facts remain null. Detail experience numbers differ in units from list numbers, so they are deliberately not interpreted without a verified unit contract.

Existing JobSourcePublicationPolicy and IndiaJobEligibilityPolicy apply. India and explicit India-remote evidence are accepted according to those policies; remote alone is insufficient. The inspected shared ATS import policy has no five-day freshness setting, so none was introduced. Source timestamps remain available for existing downstream freshness queries. Missing dates never become today's date.

Pagination is limited to 2,000 jobs/200 pages, with exact expected page lengths and unique numeric IDs. Requests have 20-second timeouts and a 20-minute overall budget; cancellation propagates. Requests are spaced 250 ms apart. HTTP 429 and 5xx responses get at most three attempts. Retry-After is honored; delays above 30 seconds fail the attempt for later scheduled retry. Network errors/timeouts fail the run for the existing durable retry infrastructure. No live employer requests occur in tests.

All pages/details are fetched before returning to ingestion. Any malformed, oversized, repeated, partial or failed response throws; no partial list is reported as success. Empty responses are rejected because an invalid public token can return the same successful empty shape. Error code `RIPPLEHIRE_SOURCE_UNSUPPORTED` covers unverified contracts; HTTP status failures retain existing provider failure codes.

The provider implements IExternalJobProvider, not the authoritative inventory interface. It never closes jobs merely because they are absent from a live offset-paginated listing. Observed inactive jobs are omitted; automatic closure is intentionally unsupported pending a verified authoritative closure mechanism. A full 1,416-detail live synchronization was not executed against the employer.

## Changed files

- JobPortal.Domain/Enums/DomainEnums.cs
- JobPortal.Application/Features/JobAggregation/JobSourceConfiguration.cs
- JobPortal.Application/Features/JobAggregation/RippleHireSourceConfiguration.cs (new)
- JobPortal.Application/Abstractions/Jobs/UrlCanonicalizer.cs
- JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Infrastructure/Services/RippleHireJobSourceProvider.cs (new)
- JobPortal.Infrastructure/Services/JobSourceConnectionTester.cs
- JobPortal.Application.Tests/JobPortal.Application.Tests.csproj
- JobPortal.Application.Tests/RippleHireJobSourceProviderTests.cs (new)
- JobPortal.Application.Tests/Fixtures/RippleHire/{listing,page,detail}.json (new)
- docs/RIPPLEHIRE_INTEGRATION.md (new)

## Verification

- Analyzer-enabled API build: succeeded, zero warnings and zero errors (`../ripplehire-build-final.log`).
- Expanded mocked regression suite: 269 passed, zero failed (`../ripplehire-test-results/ripplehire-final.trx`), including existing provider, admin, ingestion, reconciliation, and canonicalization tests.
- Test compilation with normal analyzers is blocked by pre-existing analyzer diagnostics in unrelated test files (for example CA1707 in JobQualityGateTests and JobIngestionServiceTests, and CA1869 in MultiCompanyProviderTests). Executed tests use `-p:RunAnalyzers=false`; application production code still passed the normal analyzer-enabled build.
- No full live employer import or PostgreSQL production operation was performed.
- Final RippleHire-specific suite: 23 passed, zero failed, including actual EF in-memory upserts, duplicate identity rejection, verified fixtures, pagination, cancellation, rate limiting, source validation and India geography (`../ripplehire-test-results/ripplehire-focused.trx`).
