# PTC Workday source registration

Verified on 2026-10-11 (Asia/Calcutta) using bounded public read-only requests:

- Company association: [PTC's official careers page](https://www.ptc.com/en/careers) links its Search Open Jobs button to the board below.
- Host: `ptc.wd1.myworkdayjobs.com`.
- Tenant: `ptc`.
- Career-site identifier: `PTC`.
- ATS identifier: `ptc/PTC`; Workday ATS type: `5`.
- Board: https://ptc.wd1.myworkdayjobs.com/PTC — HTTP 200; page configuration explicitly sets `tenant: "ptc"` and `siteId: "PTC"`.
- Public jobs endpoint: **POST** `https://ptc.wd1.myworkdayjobs.com/wday/cxs/ptc/PTC/jobs` — HTTP 200, `application/json`, for `{"appliedFacets":{},"limit":1,"offset":0,"searchText":""}`. This POST is a public search operation, not a registration or ingestion operation.
- Response keys: `total`, `jobPostings`, `facets`, `userAuthenticated`. The response advertised 176 global postings and returned one posting with a nonempty `externalPath`: Senior Software Specialist, `/job/Pune-India/Senior-Software-Specialist_JR112590`. This is point-in-time reachability evidence, not an India vacancy count or a full ingestion.

## Registry lookup and duplicate prevention

No PTC company GUID or source record was found in repository configuration. That does not establish absence from the live registry. A real company GUID must be verified through the existing Administrator-only APIs before registration:

The supplied backend is production, `https://job-portal-lgjc.onrender.com/api`. No authorized admin credential was available in the task context or relevant environment variables, so authenticated registry GETs were not performed. PTC company/source existence and its real GUID remain **unverified**; do not treat this as confirmation that a new company is required. No token was requested or displayed. Prefix the routes below with the backend origin (the routes already include `/api`).

1. `GET /api/admin/companies?Search=PTC` — inspect all matching pages and reuse the existing PTC company GUID. If no matching company exists, use the existing company administration process to create it separately; do not invent a GUID or create a second company to avoid a source conflict.
2. `GET /api/admin/job-sources?CompanyId=<verified-ptc-company-guid>&AtsType=5` — inspect all pages for the canonical `ptc/PTC` identifier. Omit `IsActive` so both active and inactive sources are included. Reuse an existing source if present.

The same company GUID, Workday type and canonical identifier are protected by existing service uniqueness checks and database constraints. Repeated registration returns HTTP 409 / `duplicate_job_source`, including surrounding identifier whitespace. Registration is not an upsert and should not be retried with alternate identifier casing or a different company record.

## Existing admin registration contract

Prepare this payload for CareerHarbor Admin → Job Sources, using the verified existing company GUID. The existing Administrator-only endpoint is `POST /api/admin/job-sources`; it was not executed during this task.

```json
{
  "companyId": "<verified-ptc-company-guid>",
  "careerPageUrl": "https://ptc.wd1.myworkdayjobs.com/PTC",
  "atsType": 5,
  "atsIdentifier": "ptc/PTC",
  "isActive": false,
  "scanIntervalMinutes": 720
}
```

The 720-minute interval is explicit and the source remains inactive. Do not use Activate or Run Now as part of registration preparation.

## Configuration and scope

- Existing Workday routing already handles this host and tenant/site identifier. No provider or ingestion-engine change is required.
- Preserve existing category resolution and all other mappings. If existing setup requires a source-specific mapping, configure only `JobAggregation__SourceCategories__<new-ptc-source-guid>` with the chosen existing category GUID through the established process.
- Existing scheduling, pagination, India eligibility, validation, deduplication, rejection, reconciliation, approval and publication behavior remain unchanged. This global board receives no PTC-specific country facet.
- No migration, seed, new endpoint or global configuration change is required. No source activation, ingestion, publication, production database write or deployment was performed.
- The registration test uses a disposable EF in-memory fixture; its company GUID is not a live registry GUID. No public network requests occur in automated tests.

## Validation results

- Release API build: PASS, 0 warnings and 0 errors with normal analyzers (`dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore`).
- Focused tests: PASS, 40 passed, 0 failed, 0 skipped (PTC/PwC registration, existing Workday provider and source administration). Command: `dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzers=false -p:BuildProjectReferences=false --filter "FullyQualifiedName~PtcWorkdaySourceRegistrationTests|FullyQualifiedName~PwcWorkdaySourceRegistrationTests|FullyQualifiedName~WorkdayJobSourceProviderTests|FullyQualifiedName~JobSourceAdministrationTests"`. This follows the PwC test-only command-line analyzer override; production projects were built separately with normal analyzers. No analyzer configuration files were changed.
- `git diff --check`: PASS. The two new files also have no trailing whitespace. No tracked files were modified or staged; pre-existing PwC documentation/test and `accenture-workday-page1.json` remain untouched.
- Exact repository additions: this file and `JobPortal.Application.Tests/PtcWorkdaySourceRegistrationTests.cs`.
- Manual registration readiness is conditional on an authorized company/source lookup and substitution of the real company GUID. Public endpoint verification does not establish that PTC is absent from the registry.
