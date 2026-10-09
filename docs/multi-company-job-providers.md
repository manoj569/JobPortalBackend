# Multi-company public feeds and company logos

See the [integration audit](multi-company-integration-audit.md) for the subsequent Saved Jobs contract fix, final publication/expiry checks, referral privacy/pagination fixes and refreshed aggregate inventory. The original implementation and safe local capped-run procedure below remain in place.

## Architecture and safety

Reuse the existing ATS enums (Greenhouse=1, Lever=2, Ashby=3, SuccessFactors=4), `JobSource`, durable PostgreSQL `JobSourceRun`, shared locks, cooldown, heartbeat/recovery, normalization, deduplication, per-item durable saves and reconciliation. No new scheduler, source seed, database field or migration.

Greenhouse uses the documented all-jobs response and validates `meta.total`. Detail expiry enrichment remains bounded (1–4 requests); a disappearing/unavailable detail cannot establish a complete snapshot. Lever explicitly pages in batches of 100, requires terminal pagination, rejects duplicate IDs/pages and caps total records at 20,000. EU boards use only `api.eu.lever.co`. Ashby reads the documented complete response, requires known version/visibility for completeness, excludes unlisted posts, and derives the stable UUID only from the official board-owned posting URL. Unknown/invalid identities or official application URLs fail closed. All three implement `ICompleteExternalJobProvider`; the runner stamps their persistent source/company identities.

Public clients reuse the existing DNS-pinned/public-address-only HTTPS transport, TLS validation and retry handler. No redirects/proxy or source-controlled API hosts. Each request has bounded retries/timeout; response buffering is limited to 16 MiB. SuccessFactors parsing, spacing/concurrency and fixtures are unchanged.

Plaintext/HTML handling stays in the existing normalizer. Lever extra HTML lists are converted to text separately without corrupting plaintext such as `List<T>`. Source experience/education/salary are not guessed. Ashby publication timestamps require explicit timezone offsets and convert to UTC; they do not become expiry dates. Application URLs are official board links (or reviewed company-hosted Greenhouse `gh_jid` links), not arbitrary external URLs.

## Rights approvals (required before execution)

Greenhouse prospect posts (`internal_job_id=null`) are not individual vacancies and are never imported by the strict snapshot reader. Their exclusion conservatively prevents reconciliation rather than converting a filtered response into a complete vacancy snapshot.

Strict Greenhouse snapshots also omit unavailable/malformed details rather than inventing a null deadline. These failures leave the snapshot incomplete, preserve existing jobs and prevent stale closures; the legacy enrichment reader remains compatible.

Reconciliation retains the complete validated provider identity set **before** India filtering. A still-observed foreign posting is not falsely closed merely because it is no longer eligible for new imports. Truly absent source-owned jobs can be reconciled only after the full snapshot and all existing safety checks pass.

Create any newly discovered source **inactive** using the admin API. Verify its company through the existing admin workflow; that is company-record verification, not an employer partnership. Obtain/review publication rights and separately logo rights. Configure `JobAggregation:SourceApprovals:<persistent-source-guid>` only after review. Do not use the following illustrative approval as actual evidence:

```json
{
  "JobAggregation": {
    "SourceApprovals": {
      "<persistent-source-guid>": {
        "CompanyId": "<existing-verified-company-guid>",
        "AtsType": "Greenhouse",
        "AtsIdentifier": "<exact-verified-board-token>",
        "CareerPageUrl": "<same-reviewed-https-url-as-JobSource>",
        "RightsEvidence": "<reviewed-grant-reference>",
        "RightsExpireAtUtc": "<actual-permission-expiry-UTC>",
        "IndiaOnly": true,
        "VerifiedIndiaLocations": [],
        "AllowExplicitWorldwideRemote": false,
        "TestImportLimit": 20,
        "LogoUrl": null,
        "LogoRightsEvidence": null
      }
    }
  }
}
```

Expiry is mandatory. Binding is to source ID, company ID, ATS type, exact identifier and career URL; changing them invalidates approval. Admin activation, manual enqueue and actual worker execution check approval. Missing/expired/mismatched approvals cannot fetch or import. Configuration cannot prove legal rights: evidence must be reviewed by the operator; the code enforces that decision. No existing source is silently exempt. Plan an approval/configuration review before deploying this gate, or existing sources will be unable to run.

India-only selection accepts structured IN/IND/India or an explicit India location label when structured country is absent. Exact alternative location labels can be configured in `VerifiedIndiaLocations` only after independent geographic verification. City-name similarity alone is not evidence (for example Hyderabad is not unique to India). Structured foreign countries override conflicting location hints. Generic Remote, unknown geography, or unrecognized country values are excluded and make the snapshot incomplete. Explicit worldwide remote requires a separate operator approval and an exact Worldwide/Global/Anywhere location plus structured remote workplace. Mixed-country/ambiguous secondary-location exclusions conservatively block reconciliation. Verified foreign-only postings can be excluded intentionally after complete enumeration; non-India filtering does not mean a failed transport was complete.

Positive `TestImportLimit` is 1–1000 and **always incomplete**, even if fewer jobs exist. Limit applies to eligible imports; it does not guarantee only 20 HTTP records are fetched. The provider still validates complete pagination before importing. Capped runs preserve committed upserts, cannot close stale jobs or advance LastSuccessfulRun, and intentionally finish with Failed/incomplete status. Do not enable the scheduler during local capped validation.

## Company logos and existing DTOs

The existing `Company.LogoUrl` is the single source for every job. An explicitly reviewed `LogoUrl` + `LogoRightsEvidence` can fill a missing logo during an authorized run. It references an approved HTTPS official/CDN asset; no automatic download, image search, guessed name match or provider-supplied logo is used. Existing admin overrides are never replaced. Missing/invalid/unlicensed assets do not block imports.

Public listing and detail contracts **already** expose this compatible shape; no DTO/schema addition was needed:

```json
{
  "id": "<job-guid>",
  "companyId": "<company-guid>",
  "companyName": "<official-company-name>",
  "companyLogoUrl": "<approved-company-asset-or-null>"
}
```

## Safe local validation/import procedure

No live feed import or database operation was executed for this work. Automated tests use saved synthetic fixtures and EF InMemory, including 1,500 postings through the actual runner and ingestion service.

```powershell
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj --configuration Release --no-restore -p:RunAnalyzers=false --filter "FullyQualifiedName~MultiCompanyProviderTests"
```

For an **authorized** real source, use an isolated local/disposable PostgreSQL database and local API only. Do not reuse an inherited Neon/production connection. Keep `JobAggregation__Scheduler__Enabled=false`, `JobAggregation__AutoPublishEnabled=false`; set the reviewed source approval above with limit 20. Create the company/category/source via existing admin APIs in that local database, initially source inactive; bind actual IDs, then activate only after approval. Do not paste passwords/tokens into chat or logs.

With your existing local admin credential in `$localAdminToken` and local source ID in `$localSourceId`, enqueue and poll (never point this script at production):

```powershell
$localApi = 'http://localhost:5000' # replace port with your actual LOCAL launch port
$headers = @{ Authorization = "Bearer $localAdminToken" }
$queued = Invoke-RestMethod -Method Post -Uri "$localApi/api/admin/job-sources/$localSourceId/run" -Headers $headers
$runId = $queued.data.runId
Invoke-RestMethod -Method Get -Uri "$localApi/api/admin/job-sources/$localSourceId/runs/$runId" -Headers $headers
```

POST returns 202 promptly; a browser disconnect does not cancel the worker. Poll until terminal. Confirm Created <=20, Closed=0, no LastSuccessfulRun advance, sanitized incomplete status. Repeat with the same limit and confirm no duplicate source/external identities. Change the **local** per-source limit to 100, restart the local API so configuration reloads, rerun; repeat at 1000. Previously committed jobs are reused. Do not turn a capped scan into a complete snapshot or interpret its Failed/incomplete status as source disappearance.

Only after rights, eligibility, full-run safety and quality review should operators consider TestImportLimit=0 and publishing. Do not promise a 10,000-active result from fixture counts or global provider totals. Public APIs count visible, published, unhidden, unexpired rows after existing deduplication; active seat counts and cross-board vacancy equivalence cannot be established from a feed's total alone. Keep the disabled inventory and rights review separate from production activation.

## Changed-file manifest

All paths are relative to the backend repository; no frontend, entity mapping, migration, appsettings or production configuration files changed.

```text
JobPortal.Application/Abstractions/Jobs/RawExternalJob.cs
JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs
JobPortal.Application/Features/JobAggregation/JobAggregationOptions.cs
JobPortal.Application/Features/JobAggregation/JobSourcePublicationPolicy.cs
JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs
JobPortal.Application/Features/JobAggregation/JobSourceRunService.cs
JobPortal.Application/Services/JobIngestionService.cs
JobPortal.Application/Services/JobSourceRunner.cs
JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
JobPortal.Infrastructure/Services/AggregationHttpRetryHandler.cs
JobPortal.Infrastructure/Services/PublicAtsSnapshot.cs
JobPortal.Infrastructure/Services/GreenhouseExternalJobProvider.cs
JobPortal.Infrastructure/Services/LeverExternalJobProvider.cs
JobPortal.Infrastructure/Services/AshbyExternalJobProvider.cs
JobPortal.Application.Tests/ExternalJobCoverageTests.cs
JobPortal.Application.Tests/JobPortal.Application.Tests.csproj
JobPortal.Application.Tests/MultiCompanyProviderTests.cs
JobPortal.Application.Tests/Fixtures/PublicAts/greenhouse.json
JobPortal.Application.Tests/Fixtures/PublicAts/lever.json
JobPortal.Application.Tests/Fixtures/PublicAts/ashby.json
docs/company-board-inventory.md
docs/multi-company-job-providers.md
```
