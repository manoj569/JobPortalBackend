# Generic employer long-form content persistence

Status: backend implementation complete; production migration and AMD retest have not been performed. No worker files, production data, commits, pushes, stashes or unrelated referral edits were changed by this task.

## Root cause and mapping trace

The reported PostgreSQL 22001 is caused by `Jobs.Responsibilities` being `varchar(8000)`. The supplied production lengths (15,792; 26,734; 11,641) exceed it. Requirements had the same 8,000 limit; Benefits had a 4,000 limit. Description is already PostgreSQL `text`, established by `20261015175159_AllowLongJobDescriptions`.

The worker JSON `jobs` array deserializes directly into `RawExternalJob` in `GenericEmployerScanner.WorkerObservation`. Its Description, Requirements, Responsibilities and Benefits are separate nullable strings. `EmployerDirectSnapshotProvider` dispatches canonical generic sources to the scanner (and retains the prior submitted-snapshot path). `EmployerDirectSnapshotRequest.Jobs` likewise carries `RawExternalJob` values; it performs no section extraction. The scanner returns the worker jobs unchanged after source/run binding and completeness checks. JobSourceRunner attaches the configured company/source identity, records metadata, filters eligibility, then calls the normalizer and ingestion. ExternalJobNormalizer normalizes Description separately (HTML extraction only when DescriptionIsHtml is true) and normalizes each of the other three sections independently. JobIngestionService create and owned-update paths assign each raw section to its matching Job property. JobConfiguration previously capped three of these columns; the PostgreSQL snapshot/migrations made those caps physical.

**No backend mapper copies Description into Responsibilities, duplicates sections, or manufactures HTML.** Distinct-field worker-response tests verify that. The supplied evidence contains lengths, not AMD payload contents; it cannot establish whether upstream Responsibilities already contains full-description or repeated content. Worker code was not changed. No heuristic section removal/deduplication was introduced: long responsibilities and requirements are legitimate employer content, and guessing section boundaries could discard information.

## Change and safety

Responsibilities, Requirements and Benefits are PostgreSQL `text`, with no EF maximum length. Description remains text. Content is not shortened, discarded or copied between fields. Whitespace/HTML normalization retains its pre-existing behavior. Authoritative/incomplete snapshot rules, response size budgets, SSRF/source binding, identity/deduplication, publication/NeedsReview decisions and item exception isolation were not changed.

Migration: `20261020191010_ExpandEmployerJobContentToText`. Up contains only three ALTER COLUMN TYPE text operations plus EF history bookkeeping. Nullability is unchanged; no row updates, deletes or substring expressions occur. Down obtains a transaction-held table lock and refuses narrowing if any content exceeds the legacy limits; it never silently truncates long data. Retain text columns when rolling back application binaries.

## Complete generic-to-Job string audit

| Stored value | Storage | Handling |
|---|---|---|
| Description, Responsibilities, Requirements, Benefits | text | Independent fields, full normalized content retained |
| Title | 250 | Explicit validation before mutation |
| Location | 250 | Validate normalized value; classification does not replace it |
| ApplicationUrl, DiscoveredUrl, CanonicalJobUrl | 2048 each | Validate actual stored length, including whitespace retained in provenance URLs; never truncate URLs |
| ExternalJobId from ExternalId; AcquisitionExternalId | 255 each | Explicit validation; never truncate identities |
| EducationRequirement | 200 | Validate normalized/canonicalized value |
| CurrencyCode | 3 | Validate structured input; classifier-inferred currencies come from fixed three-letter rules |
| ReferenceNumber | 50 | Generated JOB-date-8hex value, 21 characters |
| Slug | 270 | Generated title slug budget 240 + hyphen + 8hex, at most 249 |
| FingerprintHash, CanonicalApplicationUrlHash | 64 | Fixed SHA-256 hexadecimal hashes |
| SourceMetadataJson | jsonb | No varchar maximum; contains original source evidence under existing HTTP/metadata budgets |
| ClassificationJson | text | Existing generated classification evidence; no varchar maximum |
| Department, RoleCategory (150); ManualClassificationOverrides (200) | bounded | Not assigned by generic ingestion |
| CompanyName/CompanyWebsiteUrl | lookup only | Generic runner binds an existing configured Company; ingestion does not create or mutate Company strings |
| Skills -> Skill.Name / NormalizedName | 150 each | Validate using the same whitespace normalization and invariant uppercase as skill persistence, before adding any rows |

Raw fields not assigned to Job string columns (AdditionalLocations, CountryCodes, RemoteEligibilityText, EligibilityReasonCode, ExternalCategory, EmploymentTypeText, WorkplaceTypeText, acquisition mapping hints) are selection/classification inputs or metadata, not additional varchar writes. GUIDs, numbers, dates and enum values are not string columns. Oversized bounded inputs produce explicit per-item validation outcomes, leaving content/identifiers unmodified and allowing subsequent jobs to proceed.

## Files changed by this task

- JobPortal.Application/Services/ExternalJobStorageContract.cs (new)
- JobPortal.Application/Services/JobIngestionService.cs (validation boundary)
- JobPortal.Persistence/Configurations/EntityConfigurations.cs (only three Job properties; existing referral hunks retained)
- JobPortal.Persistence.Postgres/Migrations/20261020191010_ExpandEmployerJobContentToText.cs (new)
- JobPortal.Persistence.Postgres/Migrations/20261020191010_ExpandEmployerJobContentToText.Designer.cs (new)
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs (only three Job properties changed by this task; prior referral changes retained)
- JobPortal.Application.Tests/GenericEmployerContentTests.cs (new)
- JobPortal.Application.Tests/WorkdayLongDescriptionPostgresTests.cs (deliberate bounded-field diagnostic now tests EducationRequirement, since Requirements is text)
- artifacts/generic-employer-content-up.sql (generated targeted migration SQL)
- docs/generic-employer-content-persistence.md (this report)

Earlier identifier-fix files still appear locally changed because that fix was published using an isolated index; this task did not modify them. Referral WIP remains unstaged and untouched.

## Verification and remaining database gate

Tests exercise independent fields with Description 30,001; Requirements 19,001; Benefits 11,001; Responsibilities 15,792 / 26,734 / 27,000 / 11,641. They verify generic HTTP-response contract mapping, creation, exact content, identity-repeat deduplication, owned updates and bounded-field per-item rejection. PostgreSQL cases cover actual migration/round-trip persistence, existing-row preservation and refusal of unsafe rollback. Existing ATS, deduplication, quality/publication and runner-isolation/reconciliation suites are also selected.

The disposable PostgreSQL fixture requires explicit `WORKDAY_CHECKPOINT_TEST_POSTGRES` with host 127.0.0.1 and database careerharbor_workday_test. No production/API connection is used. The local Docker engine was unavailable; database tests skip until that fixture is provisioned. Do not treat in-memory round trips or generated SQL as executed PostgreSQL verification.

Test compilation uses `-p:RunAnalyzers=false` only for the invocation because existing unrelated test analyzer failures were recorded in the preceding identifier task. Repository policy is unchanged. Backend build uses normal analyzers.

## Production migration and deployment

1. Validate the PostgreSQL cases against a disposable local/staging database before release. Set WORKDAY_CHECKPOINT_TEST_POSTGRES to the guarded disposable connection and rerun GenericEmployerContentPostgresTests and WorkdayLongDescriptionPostgresTests.
2. Review and release only this change over current main, preserving unrelated local WIP. Confirm production history includes predecessor `20261019175240_AddReferralContactValues`; inspect pending migrations rather than assuming the database state. If older, review/apply required predecessor migrations through the normal release process first.
3. Take the normal database backup/snapshot. Pause new job-source runs and drain active writes for the short ALTER TABLE window. The widening is non-destructive but requires a table lock. Use the operator's approved lock/statement timeout and retry a rolled-back migration if lock acquisition fails.
4. Generate the targeted script from the reviewed build:

```powershell
dotnet ef migrations script 20261019175240_AddReferralContactValues 20261020191010_ExpandEmployerJobContentToText --project JobPortal.Persistence.Postgres --startup-project JobPortal.Persistence.Postgres --output artifacts/generic-employer-content-up.sql
```

5. Apply the reviewed script ONCE with the production migration role (production credentials supplied securely by the operator):

```powershell
psql "$env:PRODUCTION_DATABASE_URL" -v ON_ERROR_STOP=1 -f artifacts/generic-employer-content-up.sql
```

Alternatively use the deployment's normal EF migration runner targeting `20261020191010_ExpandEmployerJobContentToText`; do not run both. The generated targeted script is transactional, not idempotent. Check migration history before reapplying.

6. Verify the history entry and `information_schema.columns` for Jobs: Description/Responsibilities/Requirements/Benefits must all report `text`, with null character_maximum_length. Confirm row counts and retained content for known existing jobs.
7. Deploy the reviewed backend API/run-worker binaries containing the updated model and explicit bounded-field validation. No acquisition-worker redeployment or changes are required. Resume scheduling.
8. Run AMD again and inspect item counts, persistence errors, full stored section lengths/content, draft/NeedsReview/publication behavior, and a repeat run for identity stability. Confirm incomplete scans report reconciliation blocked and do not close absent jobs. Only then claim production fixed.

No production migration or AMD retest was performed in this task.

## Recorded verification results

- Final focused suite: **152 passed, 0 failed, 4 skipped** (`generic-content-final-tests.log`). Selection covers GenericEmployerContent, GenericEmployerContractTests, EmployerDirectIntegrationTests, JobIngestionServiceTests, JobSourceRunnerTests, JobAutoPublish, WorkdayLongDescription, PostgresPendingModelTests, ExternalJobNormalizer, JobDeduplication, SourceAutoPublicationTests, JobQualityGateTests and ExternalJobProviderTests.
- Additional explicit safety/provider suite: **59 passed, 0 failed, 0 skipped** (`generic-content-safety-tests.log`): JobSourceReconciliationSafetyTests, GenericIndiaJobIngestionTests, WorkdayJobSourceProviderTests, DeloitteSuccessFactorsJobSourceProviderTests. These include incomplete/non-authoritative no-closure assertions.
- Backend API build with normal analyzers: passed, 0 warnings, 0 errors (`generic-content-build.log`); final rebuild follows the last audit fix.
- EF `migrations has-pending-model-changes`: reports no model changes since the last migration. The snapshot test and migration-operation tests pass. Targeted upgrade SQL was generated and reviewed: three column widenings only.
- `git diff --check`: passed. The four skipped cases are the actual PostgreSQL generic round trip, upgrade/rollback preservation, and existing Workday PostgreSQL checks. They were not run against a production or substitute database.

## Git status after implementation

Nothing is staged. The complete preserved working-tree status is:

```text
 M JobPortal.API/Controllers/ReferralsController.cs
 M JobPortal.Application.Tests/JobReferralServiceTests.cs
 M JobPortal.Application.Tests/ReferralJobSkillsTests.cs
 M JobPortal.Application.Tests/ReferralMarketplaceMigrationTests.cs
 M JobPortal.Application.Tests/ReferralMarketplaceTests.cs
 M JobPortal.Application.Tests/WorkdayLongDescriptionPostgresTests.cs
 M JobPortal.Application/Abstractions/Referrals/IJobReferralService.cs
 M JobPortal.Application/Features/JobAggregation/EmployerDirectSnapshot.cs
 M JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs
 M JobPortal.Application/Features/Referrals/JobReferralService.cs
 M JobPortal.Application/Features/Referrals/ReferralDtos.cs
 M JobPortal.Application/Features/Referrals/ReferralMarketplaceService.cs
 M JobPortal.Application/Services/JobIngestionService.cs
 M JobPortal.Domain/Entities/JobReferral.cs
 M JobPortal.Infrastructure/Services/GenericEmployerScanner.cs
 M JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs
 M JobPortal.Persistence/Configurations/EntityConfigurations.cs
 M JobPortal.Persistence/Repositories/JobSourceRepository.cs
?? JobPortal.Application.Tests/GenericEmployerContentTests.cs
?? JobPortal.Application.Tests/GenericEmployerContractTests.cs
?? JobPortal.Application/Features/JobAggregation/GenericEmployerContract.cs
?? JobPortal.Application/Features/Referrals/ReferralContactValues.cs
?? JobPortal.Application/Services/ExternalJobStorageContract.cs
?? JobPortal.Persistence.Postgres/Migrations/20261019175240_AddReferralContactValues.Designer.cs
?? JobPortal.Persistence.Postgres/Migrations/20261019175240_AddReferralContactValues.cs
?? JobPortal.Persistence.Postgres/Migrations/20261020191010_ExpandEmployerJobContentToText.Designer.cs
?? JobPortal.Persistence.Postgres/Migrations/20261020191010_ExpandEmployerJobContentToText.cs
?? artifacts/generic-employer-content-up.sql
?? docs/generic-employer-content-persistence.md
?? docs/generic-employer-integration.md
```

Final rebuild (`generic-content-final-build.log`) passed with normal analyzers: 0 warnings, 0 errors. Final rollback SQL generation succeeded and contains the table lock and non-truncating guard. Migration snapshot validation passed. Final `git diff --check` passed; the staging index is empty. Both PostgreSQL integration gates remain unexecuted because no disposable local database was available. Production remains unverified pending migration application and AMD retest.
