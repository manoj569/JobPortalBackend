# Fresher acquisition company creation and audited recovery

This change has not been deployed. No production request, production database write, SerpApi search, worker change, ATS ingestion change, push or production migration was performed by this investigation.

## Root cause and contracts

The shared JobIngestionService.ResolveCompanyAsync only looks up explicit CompanyId, then company website domain, then company name/slug. It returns null when no company exists. IngestAsync maps null to CompanyNotFound, and FresherBulkImportService records COMPANY_NOT_RESOLVED. It never created missing companies. The supplied production reconciliation confirms 30 such terminal failures with no linked jobs.

The actual retained request has 30 company names, zero CompanyIds, zero CompanyWebsiteUrls, zero categories and zero category IDs. It also has ApplyUrl, CanonicalJobUrl and DiscoveredUrl, which identify vacancies rather than employer websites. CompanyLogoUrl is supported by the DTO but is not trusted or copied by this fallback. Arbitrary SourceMetadata is not treated as verified company identity.

Companies permits null WebsiteUrl, NormalizedWebsiteDomain and LogoUrl. OwnerUserId is a required user FK. An existing active administrator is selected as custodian, using the same oldest-active-administrator pattern as candidate company onboarding. No fake owner or employer account is created. If no active administrator exists, the item receives COMPANY_OWNER_UNAVAILABLE; existing-company resolution remains available.

## New fresher-only resolution

After existing eligibility, URL, data and category validation, the fresher importer asks its acquisition store to resolve the company before calling the unchanged shared ingestion service. It passes the resolved CompanyId to ingestion.

1. An explicit CompanyId must identify a live company. Invalid IDs receive COMPANY_ID_NOT_FOUND; they do not silently select another employer.
2. A safe explicit employer website can match an already-known normalized employer domain. Aggregator/ATS hosting domains are excluded, including regional variants/subdomains. Multiple existing domain matches receive COMPANY_DOMAIN_AMBIGUOUS rather than an arbitrary match.
3. Otherwise resolve by the existing CompanyNameNormalizer: collapse whitespace, trim and lowercase. Existing companies are not overwritten or upgraded by provider claims.
4. Otherwise create a minimal unverified company with a valid plain-text name (2–160 characters), normalized name, unique slug, active administrative custodian and CreatedAtUtc. Placeholder identities such as Confidential or Important Company of the Sector receive COMPANY_NAME_INVALID. Blank names retain existing invalid-data validation.
5. WebsiteUrl/domain are nullable. New domains are saved only when the trusted internal producer explicitly supplies CompanyWebsiteVerified=true and the URL passes employer-domain checks. This optional DTO attestation defaults false; it is omitted when false so existing full-content fingerprints do not change. It does not mark the Company itself verified. Producers must not derive this attestation from an aggregator application URL. The current worker requires no change because its retained batch supplies no websites. LogoUrl and career URLs are never fabricated. No vacancy URL is inferred as a company website.

All ATS ingestion semantics, geography/experience checks, category failures, acquisition identity, job deduplication and AutoPublish safeguards remain in their existing paths.

## Concurrency and transactions

Each bounded chunk acquires its existing acquisition-job locks plus sorted normalized company-name and employer-domain PostgreSQL transaction advisory locks. These coordinate concurrent fresher imports, including different sources. Companies, jobs and receipts share the existing chunk transaction.

The company insert uses parameterized INSERT ... ON CONFLICT DO NOTHING, then re-queries the winning normalized name. This avoids flushing staged jobs/skills early and keeps the transaction usable when another writer wins an existing unique constraint. Existing unique normalized-name and slug constraints are unchanged. The existing domain index is nonunique: fresher domain locks serialize this path; ambiguous pre-existing domains are rejected. No claim is made that unrelated writers are newly required to enforce unique domains.

No model migration is required: company ownership, nullable website/logo columns, normalized name/domain and constraints already exist. Tests use existing migrations only in disposable local PostgreSQL databases.

## Terminal receipt semantics

A completed ImportId returns its original durable receipts. Deploying this fix does NOT reprocess the original 30 COMPANY_NOT_RESOLVED receipts. Do not delete/reset the original run, alter its fingerprint, change worker acknowledgement or simply submit that ID again expecting new work.

The worker CLI currently exposes DRY_RUN/IMPORT and --plan, not a retry-only/requeue-completed-failures command. General IMPORT can proceed to paid discovery. Do not use it for this recovery.

## Exact recovery procedure after a reviewed deployment

1. Keep the original run and all 30 failed receipts untouched. Preserve the cached original request and verify SHA-256 05cebf2d5c8f0797a63a8020224efc7e84e1ce7cf86e62fd4157905f2c9ba879 (83,060 bytes). Confirm an active administrator exists for company custody and the acquisition source remains enabled. Do not enable AutoPublish as part of this fix.
2. From the backend root, prepare a separate request using PowerShell 7.5 or later:

```powershell
.\tools\New-FresherCompanyRetry.ps1 `
  -PayloadPath ..\retained-fresher-payload.json `
  -OutputPath ..\fresher-company-retry-v1.json
```

The script performs no HTTP or SQLite operations. It refuses to overwrite files or the original payload. It preserves all candidate fields and ExternalJobIds, changes only ImportId, and emits a parent-ID/hash manifest. If already prepared, inspect and reuse the existing files instead of regenerating/overwriting them.

For the verified retained bytes the new deterministic ImportId is:

```text
ch-cr1-35c6d80459e864cbc904aa44fce408b81ad92de735b3c3e160071839339cecc5
```

Its derivation is ch-cr1- plus lowercase SHA-256 of UTF-8 `company-resolution-v1\n<original ImportId>\n<lowercase SHA-256 of original bytes>`. Re-running the same recovery therefore selects the same new run; it does not create unbounded retry IDs. All acquisition external identities remain unchanged, preserving job deduplication across runs. The prepared retry request SHA-256 is 471cdbf3f8e630d70ceb12bc6dc341283d7acfb9a8a38fbb5c8085702ea78827.

3. After deployment and explicit authorization to perform the production retry, submit only this prepared file using the existing service token from secure configuration. Example (NOT executed here):

```powershell
Invoke-WebRequest `
  -Uri 'https://job-portal-lgjc.onrender.com/api/internal/v1/jobs/bulk-import' `
  -Method Post -ContentType 'application/json' `
  -Headers @{ Authorization = "Bearer $env:FRESHER_SERVICE_TOKEN" } `
  -InFile ..\fresher-company-retry-v1.json `
  -OutFile ..\fresher-company-retry-v1.response.json
```

Do not print tokens or invoke discovery. If transport retries are necessary, reuse these exact retry bytes and new deterministic ID.

4. Reconcile both runs read-only. The old run must still have its original 30 failed receipts and zero linked jobs. Inspect the new run's per-item outcomes and links; HTTP 200 alone is not job acceptance or publication. Keep the manifest and sanitized response with the incident evidence. Invalid employer placeholders require identity review, not fabricated companies or silently altered payloads.

## Changed files

- JobPortal.Application/Features/FresherAcquisition/FresherAcquisitionContracts.cs
- JobPortal.Application/Features/FresherAcquisition/FresherCompanyIdentity.cs
- JobPortal.Application/Features/FresherAcquisition/FresherBulkImportService.cs
- JobPortal.Application/Features/FresherAcquisition/IFresherAcquisitionStore.cs
- JobPortal.Persistence/Repositories/FresherAcquisitionStore.cs
- JobPortal.Persistence/Repositories/FresherAcquisitionStore.Companies.cs
- JobPortal.Application.Tests/FresherAcquisitionPostgresTests.cs
- JobPortal.Application.Tests/FresherAcquisitionCompanyPostgresTests.cs
- tools/New-FresherCompanyRetry.ps1
- docs/fresher-company-resolution.md

## Final validation

- Focused company/ingestion/ATS/model-drift run: 78 passed, zero failures/skips. Includes 24 real PostgreSQL fresher tests and existing Greenhouse, Lever, Ashby, Workday and SuccessFactors India eligibility/idempotency checks. Tests were compiled with RunAnalyzers=false; unrelated analyzer failures were not changed.
- Separate complete fresher PostgreSQL execution: 24 passed, zero failures/skips. Covers concurrent name/domain creation, company/job/receipt rollback, two jobs sharing one new company, normalized existing-company resolution, unverified and aggregator website rejection, explicit invalid IDs, first import/replay, partial recovery and preserved failed audit receipts.
- Exact cached 30-item recovery batch in disposable localhost PostgreSQL: 28 Created, two Failed/COMPANY_NAME_INVALID, zero COMPANY_NOT_RESOLVED, zero CATEGORY_RESOLUTION_FAILED. The rejected employer labels are Confidential and Important Company of the Sector; creating these as universal employers would collapse unrelated unknown identities. They require trustworthy employer identity review. No fake category was created. All 28 created jobs have company links.
- Synthetic 30-job batch with ten previously absent legitimate employers: all 30 created; the old 30 failed receipts remained Completed, unchanged and without linked jobs. Replaying the new deterministic run reused its jobs and receipts.
- API build command `dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release -p:RunAnalyzers=false`: passed, zero warnings/errors.
- `git diff --check`: passed, exit zero (only Git line-ending conversion notices).
- Recovery script: two independent output generations produced identical request SHA-256; attempting recovery-from-recovery was rejected before writing. Original request SHA-256 remains unchanged.

Local evidence lives beside the checkout: fresher-company-final-tests.log, fresher-company-postgres.log, fresher-company-api-build.log and fresher-company-results/*.trx. Only isolated localhost test databases were used. Earlier local failed receipts were preserved in their original test database; a new isolated database was used after correcting the test administrator fixture from Pending to Active.

Concurrent changes to JobPortal.Application.Tests/WorkdayBatchIngestionTests.cs and JobPortal.Persistence/Repositories/JobSourceRepository.cs appeared during this work and were not edited or reverted by this task. They are not part of this fix's changed-file list above.
