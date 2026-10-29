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

## Resolved provider configuration

`https://{tenant}.ripplehire.com/candidate/?token={public-career-token}` with optional `source={value}` and `lang=en`. Supply the employer's public career token, never a private candidate token. AtsIdentifier may be null or match that token. Job-detail fragment URLs cannot configure a source. Public list-view fragments can be normalized during admin discovery as described below. Legacy `/ripplehire/candidate`, custom domains, non-English variants, unknown query parameters, unsafe characters and alternate ports are rejected.

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

## Automatic public source discovery

The admin create/update flow now resolves and verifies RippleHire sources before persisting `CareerPageUrl` and `AtsIdentifier`. The provider and scheduled/manual synchronization code are unchanged: they use the saved resolved values and never perform discovery. No database migration is required.

Supported inputs:

1. Existing complete public token URL. It is normalized and listing-verified without fetching HTML.
2. Public tenant root (`https://{tenant}.ripplehire.com/`) or `/candidate/`. Discovery reads the known public `/candidate/` entry, avoiding the tenant root's employee SSO redirect. A tenant succeeds only if that page exposes a supported public link or metadata; a tenant name alone does not imply a usable token.
3. Public employer HTTPS careers page containing a direct RippleHire anchor/iframe URL, canonical link, or `og:url` metadata. Relative URLs are resolved against the final fetched page.

The bounded static HTML parser inspects quoted `href`, `src`, and metadata `content` attributes. It ignores HTML comments, script/style content, arbitrary JavaScript strings, forms, `shareToken`/hidden input fields and job-detail fragments. It does not execute JavaScript, visit linked employer pages, scrape search engines, or guess tokens. Public `#list`/`#list/...` view fragments can be removed to recover the candidate root; the existing India eligibility policy remains responsible for import geography. Fragment filters are not persisted or treated as supported API filters.

Repeated links with the same tenant and token count as one source. Multiple distinct tenant/token pairs produce an Ambiguous result before any listing calls. A single exposed source is normalized, passed through RippleHireSourceConfiguration and verified with the existing provider's first-page search and response validator. Failure never creates/updates a source. Create/update return the normalized saved URL/token and a transient `discovery` result. Search/GET continue returning the persisted normal source fields; discovery results are not a new database field.

Security limits: HTTPS/default port only; no credentials or literal IP hosts. Every redirect is revalidated and uses the existing DNS-pinned public-IP transport, which rejects private, loopback, reserved and mixed public/private address sets at the actual connection. Automatic redirects, proxying and cookies remain disabled. Discovery permits at most four redirects/five HTML requests, ten seconds per HTML request, 1 MiB HTML bodies, one-second regex timeouts and a 45-second overall budget (including listing verification). Known auth/login/SSO paths, authentication failures, anti-bot pages without usable public evidence and unsafe redirects fail closed. HTTP request logging is disabled on discovery and provider clients. Errors contain fixed actionable messages, without raw response bodies or HTTP exception URLs. Caller cancellation propagates.

### Verified employer evidence

On 29 October 2026:

- `https://www.ltm.com/india-careers` exposed `https://ltimindtree.ripplehire.com/candidate/?token=xviyQvbnyYZdGtozXoNm&lang=en&source=CAREERSITE#list/geo=India`. Its exact token was independently verified using the existing public search POST, returning `errorStatus=success` and 1,416 jobs. The captured public anchor is stored in `Fixtures/RippleHire/employer-careers.html` with HTML entity normalization and link text for the fixture; the URL and token are actual public evidence. Employer identities/URLs are not hardcoded into discovery.
- `https://www.ltm.com/us-careers` exposed more than one distinct token (`nP1GISU2A024qt3otg3V` and `xviyQvbnyYZdGtozXoNm`). This is an ambiguity example, not a claimed successful automatic resolution.
- LTM and UST tenant roots redirected toward employee SSO. The public `/candidate/` entries exposed empty `token` inputs and internal `shareToken` fields; the observed LTM shareToken returned HTTP 500 when independently checked. Those fields are deliberately unsupported. No successful token discovery from these bare tenants is claimed. Tenant-link/metadata success is covered by mocked tests and remains conditional on public exposure.

These observations verify specific public configurations, not universal RippleHire support. No full employer job import was run live.

### Admin API contract

Existing POST/PUT `/api/admin/job-sources` accepts the same JSON fields. For RippleHire, `careerPageUrl` may now be an employer careers URL or tenant entry, and `atsIdentifier` should be null for discovery. An explicitly supplied identifier must match a complete public token URL; discovery never silently replaces a conflicting token.

```json
{
  "companyId": "<existing-employer-company-guid>",
  "careerPageUrl": "https://www.ltm.com/india-careers",
  "atsType": 8,
  "atsIdentifier": null,
  "isActive": true,
  "scanIntervalMinutes": 720,
  "autoPublishEnabled": false
}
```

The existing generic connection-test action additionally accepts POST `/api/admin/job-sources/test` with an unsaved configuration body. This permits preview before a source exists, using the same tester implementation. Existing POST `/{id}/test` still tests the saved configuration without a body. Both are Administrator-only and neither persists anything or imports jobs.

```http
POST /api/admin/job-sources/test
Content-Type: application/json
Authorization: Bearer <administrator-token>

{"atsType":8,"careerPageUrl":"https://www.ltm.com/india-careers","atsIdentifier":null}
```

Successful test response shape (example derived from the verified source; not a live API invocation):

```json
{
  "data": {
    "provider": 8,
    "configurationValid": true,
    "providerReachable": true,
    "listingReachable": true,
    "reasonCode": "RIPPLEHIRE_SOURCE_VERIFIED",
    "message": "Public source verified; save the resolved configuration to persist it.",
    "statusCode": null,
    "discovery": {
      "status": "Resolved",
      "resolvedCareerPageUrl": "https://ltimindtree.ripplehire.com/candidate/?lang=en&source=CAREERSITE&token=xviyQvbnyYZdGtozXoNm",
      "manualConfigurationRequired": false,
      "reasonCode": "RIPPLEHIRE_SOURCE_VERIFIED",
      "validationErrors": [],
      "succeeded": true
    }
  }
}
```

Discovery statuses: `Resolved`, `ManualUrlVerified`, `UnsafeInput`, `InvalidConfiguration`, `Unsupported`, `NotFound`, `Ambiguous`, `VerificationFailed`. Failure sets `manualConfigurationRequired=true`, `resolvedCareerPageUrl=null` and supplies `validationErrors`. Test failures are returned as structured test results; create/update failure returns HTTP 400 with `code`, `message`, and `discovery`, leaving the saved configuration untouched. Transport/verification failures may be transient: the message also suggests retrying.

### Frontend work required

No frontend files were changed. The existing free-text career URL field can submit these inputs immediately. To expose the improved onboarding flow, change the RippleHire field hint to allow employer/tenant URLs and make the token optional; use the generic unsaved `/test` action for preview; display `discovery.status`, the resolved URL, validation errors and manual fallback guidance. Show the saved normalized URL after create/update. Keep a complete-token URL input as the manual fallback. Avoid rejecting employer URLs with frontend-only tenant validation. Other ATS forms and payloads remain unchanged.

### Discovery files changed

- JobPortal.Application/Abstractions/Jobs/IRippleHireSourceDiscovery.cs (new contract, result and structured exception)
- JobPortal.Application/Abstractions/Jobs/IJobSourceConnectionTester.cs
- JobPortal.Application/Features/JobAggregation/RippleHireSourceConfiguration.cs
- JobPortal.Application/Features/JobAggregation/JobSourceManagementDtos.cs
- JobPortal.Application/Features/JobAggregation/JobSourceManagementValidators.cs
- JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs
- JobPortal.Infrastructure/Services/RippleHireSourceDiscovery.cs (new implementation)
- JobPortal.Infrastructure/Services/JobSourceConnectionTester.cs
- JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.API/Controllers/AdminJobSourcesController.cs
- JobPortal.API/Middleware/GlobalExceptionMiddleware.cs
- JobPortal.Application.Tests/RippleHireSourceDiscoveryTests.cs (new)
- JobPortal.Application.Tests/Fixtures/RippleHire/employer-careers.html (new)
- JobPortal.Application.Tests/JobPortal.Application.Tests.csproj
- JobPortal.Application.Tests/JobSourceAdministrationTests.cs (fixture injection helper)
- docs/RIPPLEHIRE_INTEGRATION.md

### Discovery verification results

- Final analyzer-enabled backend build passed with zero warnings and zero errors: `../ripplehire-discovery-build-final.log`.
- Final mocked discovery/security/persistence and existing ATS/admin regression run: **274 passed, zero failed, zero skipped**, `../ripplehire-discovery-results/ripplehire-discovery-final.trx`.
- Tests use the previously documented `-p:RunAnalyzers=false` workaround for unrelated existing test analyzer diagnostics. Production code passed the normal analyzer-enabled build.
- Discovery tests include verified employer-link extraction, conditional tenant-entry discovery, relative metadata, missing token, ambiguous/equivalent links, unsafe inputs/discovered URLs/redirects, private DNS rejection through the shared transport, redirect loops/limits, body size, verification failures, cancellation, full-token compatibility, structured connection results, failed-save/failed-edit preservation and persisted configuration reused for repeated synchronizations.
- No push, deployment, migration or live job import was performed.
