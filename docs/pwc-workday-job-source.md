# PwC Workday source registration

Verified on 2026-10-11 (Asia/Calcutta) using public read-only requests:

- Company: PwC.
- Host: `pwc.wd3.myworkdayjobs.com`.
- Tenant: `pwc`.
- Career-site identifier: `Global_Experienced_Careers`.
- Board: https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers — HTTP 200; its page configuration explicitly sets tenant and siteId above.
- Public jobs endpoint: **POST** `https://pwc.wd3.myworkdayjobs.com/wday/cxs/pwc/Global_Experienced_Careers/jobs` — HTTP 200 for `{"appliedFacets":{},"limit":1,"offset":0,"searchText":""}`; returned one posting and advertised 3,879 global postings. This is a point-in-time reachability check, not an ingestion or India vacancy count.

## Existing registration mechanism

Sources are persisted through the existing Administrator-only `POST /api/admin/job-sources`, not a startup seed/catalog. Add PwC in the existing job-source admin interface with this payload, substituting the existing PwC company GUID:

```json
{
  "companyId": "<existing-pwc-company-guid>",
  "careerPageUrl": "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers",
  "atsType": 5,
  "atsIdentifier": "pwc/Global_Experienced_Careers",
  "isActive": false,
  "scanIntervalMinutes": 720
}
```

Use the same company GUID and canonical identifier on repeated attempts. Existing source uniqueness and database constraints reject duplicates with HTTP 409 / `duplicate_job_source`; use the existing source rather than creating another company record or changing identifier casing. If a disabled source already exists, review it through the existing update interface. No source record or company was written to a live database in this task. The fixture registers PwC only in a disposable EF in-memory test database.

The source is registration-ready. Keep it inactive for registration-only work. The interval uses the existing request default, and no scheduler, category mapping, publication setting, source approval or provider behavior is changed. No Run Now, full ingestion or publishing was performed.

## Existing configuration requirements

- Select/create PwC through the existing company administration process; reuse its real GUID. No company GUID or source GUID is invented here.
- Retain existing category resolution. If a source-specific mapping is required by the existing setup, configure only `JobAggregation__SourceCategories__<new-pwc-source-guid>` to the chosen existing category GUID; other mappings remain untouched.
- Normal administrator activation, approval/publication checks, India eligibility, validation and auto-publish guards still apply. This is a global board; no PwC-specific India facet is injected. The Accenture-only country facet remains unchanged.
- The observed global total exceeds the existing Workday completeness threshold. Existing incomplete-snapshot/reconciliation protections still apply; this addition makes no full-board coverage or automatic-publication claim.
- No migration, seed, new endpoint, provider registration, global settings change or production write is needed. Existing Workday provider routing already handles AtsType 5 and tenant/site identifiers.

Focused verification: `PwcWorkdaySourceRegistrationTests` covers actual existing management-service registration, provider target validation, duplicate rejection, inactive state/default interval and no ingested jobs. Release API build uses normal analyzers. Test compilation may require the separately disclosed command-line analyzer override for this repository's pre-existing test-project analyzer failures; no analyzer settings are changed in source.

## Validation results

- Release API build: PASS, 0 warnings and 0 errors with normal analyzers (`dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore`).
- Focused PwC registration, existing Workday provider and source-administration tests: PASS, 39 passed, 0 failed, 0 skipped.
- Test command: `dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzers=false -p:BuildProjectReferences=false --filter "FullyQualifiedName~PwcWorkdaySourceRegistrationTests|FullyQualifiedName~WorkdayJobSourceProviderTests|FullyQualifiedName~JobSourceAdministrationTests"`. The command-line test-only analyzer override is disclosed; production sources were built separately with normal analyzers.
- `git diff --check`: PASS. No existing tracked source/configuration files were modified or staged. The pre-existing `accenture-workday-page1.json` was preserved.
- Exact repository additions: this file and `JobPortal.Application.Tests/PwcWorkdaySourceRegistrationTests.cs`. No migration, deployment, production data change or real PwC ingestion/publication occurred.
