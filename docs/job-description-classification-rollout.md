# CareerHarbor job description classification: implementation and rollout

Date: 2026-10-12 (Asia/Calcutta). Backend checkout: `you-are-a-principal-net-9`.

Final pre-deployment verification supersedes the acceptance results below: see [baseline evidence and operator commands](job-description-classification-predeployment-verification.md) and [current database/API verification gates](job-description-classification-database-verification.md). **Not production ready:** nine baseline runtime failures and 184 strict analyzer findings remain. Disposable PostgreSQL and backend salary-request compatibility gates pass; deployed schema drift and frontend SHA remain unverified.

## Scope and audit

Implemented against the existing ASP.NET Core 9 application, shared EF persistence and PostgreSQL migrations. The checkout already contained Workday checkpoint, reconciliation, scheduler and provider hardening changes; those changes were preserved. No frontend file, scheduling configuration, source registration, deployment or production database was changed.

The audit established:

- `Job` already has nullable `MinimumExperienceYears` and `MaximumExperienceYears`, nullable salary amounts and a legacy `ExperienceLevel`. The admin requests, response and public summaries/details already expose the numeric experience fields. Reused all of them; retained legacy experience levels without deriving invented levels from numbers.
- Employment/workplace enums use integers, previously with zero as an unnamed unspecified value. Added `Unknown = 0`; existing 1-based values are unchanged.
- `CurrencyCode` was required `character(3)` with an application default of USD. Removed that default and made the existing column nullable.
- Public repository filtering runs before `CountAsync`, sorting and pagination. Extended its existing SQL expressions. Existing single/multiple employment and work-mode filters, location filters and referral/public visibility rules remain in that query.
- Admin Create, full PUT Edit and Compose use `JobService`/`JobMappings`. Existing administrator authorization protects the new analysis endpoint.
- All supported adapters reach `JobIngestionService`; classification runs at its creation and source-refresh persistence boundaries, including checkpoint/batch ingestion. Existing native IDs, deduplication locks, fingerprints, URL safety and private URL handling, India eligibility, source receipts, checkpoints, publication approvals and reconciliation logic remain authoritative.
- Structured locations and multiple-location/geography facts remain in existing source metadata. The job display location is a single string. JD/title location evidence is stored separately in classification JSON; it does not rewrite geographic eligibility, fingerprints or application URLs.
- Existing maintenance already has a separate URL-hash backfill. Added a distinct command to that executable, without changing the existing command's behavior.

## Backend files changed by this task

Existing files:

- `JobPortal.Domain/Entities/Job.cs`, `JobPortal.Domain/Enums/DomainEnums.cs`
- `JobPortal.Persistence/Configurations/EntityConfigurations.cs`, `JobPortal.Persistence/Repositories/PublicJobRepository.cs`
- `JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs`
- `JobPortal.Application/Abstractions/Jobs/RawExternalJob.cs`, `JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Application/Services/JobIngestionService.cs`
- `JobPortal.Application/Features/Jobs/JobDtos.cs`, `JobMappings.cs`, `JobService.cs`, `JobValidators.cs`
- `JobPortal.Application/Features/PublicJobs/PublicJobDtos.cs`, `PublicJobProjections.cs`, `PublicJobValidator.cs`
- `JobPortal.Application/Features/Dashboard/DashboardProjections.cs`
- `JobPortal.API/Controllers/JobsController.cs`, `PublicJobsController.cs`
- `JobPortal.Maintenance/Program.cs`
- `JobPortal.Application.Tests/PublicJobSearchTests.cs` and `SalaryCompatibilityTests.cs` (legacy salary facets use the existing INR/yearly contract; explicit scopes and unknown-unit exclusions are verified)

New files:

- `JobPortal.Application/Services/JobClassificationService.cs`
- `JobPortal.Application/Features/PublicJobs/ExperienceFilterPolicy.cs`
- `JobPortal.Maintenance/ClassificationBackfill.cs`
- `JobPortal.Application.Tests/JobClassificationTests.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261012054803_AddJobDescriptionClassification.cs` and its `.Designer.cs`
- This guide and local verification artifacts outside the backend source tree.

Other files shown in `git status` were already modified/untracked before this task. Nothing was staged, committed or pushed.

## Classification design

`IJobClassificationService.Analyze` produces deterministic suggestions, per-field confidence, source snippets, conflicts, review fields and a rules version. Its input accepts title/description, provider location and structured classification metadata. `JobClassificationPolicy` separately resolves suggestions against persisted values and manual locks. Classification JSON stores evidence and provenance; it is distinct from accepted queryable columns and from provider metadata.

The existing HTML parser is reused, including encoded HTML handling and script/style suppression. Input is bounded and regexes are shared, compiled, culture-independent and have timeouts. There is no paid AI dependency. Classification errors at ingestion log source ID, external job ID and persisted job ID; they leave existing classification fields available for review rather than inventing fallback values.

Rules are deliberately conservative:

- Prefer explicit structured metadata in suggestions. Record disagreements with description evidence. Ambiguous/conflicting automatic replacements require review.
- Do not turn city names or an office-address sentence into mandatory on-site work. Do not infer remote India eligibility.
- Required integer experience ranges remain exact. Minimum-only requirements have no upper bound. Repeated identical requirements collapse. Preferred sections, education duration and company-history statements are excluded. Unsupported fractions are not silently rounded. `More than X` maps to `X+1` under the documented whole-year contract.
- Salary is extracted only from disclosed currency/amount evidence. Signing bonuses, benefits and unrelated counts are excluded. Annual LPA/lakh units are expanded into currency units. Currency/period remain unknown when unsupported. Multiple or contradictory disclosures require review.
- Location evidence currently recognizes explicitly named major Indian cities and those city names in titles. The conservative city dictionary is an extension point; unfamiliar locations remain provider-owned or require admin acceptance. Conflicting source/JD/title values are preserved in evidence.
- Office-location and other unresolved evidence is available to admins. No background review or automatic AI operation is introduced.

## Migration

`20261012054803_AddJobDescriptionClassification` only changes `Jobs`:

1. Existing `CurrencyCode` becomes nullable.
2. Adds nullable `SalaryPeriod` (Yearly=1, Monthly=2, Hourly=3).
3. Adds nullable text `ClassificationJson`.
4. Adds nullable `ManualClassificationOverrides` (maximum 200 characters).

No duplicate numeric-experience fields, new classification tables or data rewrite is added. Migration code and SQL are prepared; no production migration was applied. Prefer roll-forward after null currency values exist; the generated downgrade loses classification metadata and cannot recover the old currency meaning.

## Admin APIs

- Existing `POST /api/admin/jobs`, `PUT /api/admin/jobs/{id}` and Compose accept additive `salaryPeriod` and `manualOverrideFields`. Currency can be null. Numeric experience fields remain unchanged in name and meaning.
- `JobResponse` adds salary period, classification JSON and manual override groups.
- Groups are `EmploymentType`, `WorkplaceType`, `Experience`, `Salary`, `Location`. Salary includes both amounts, currency and period; experience includes both bounds. Locks preserve explicit nulls as well as non-null selections.
- For legacy full PUT, omission of `manualOverrideFields` treats submitted values as explicit manual selections and locks all groups. A new client can submit an explicit list; an empty list opts into automatic classification for unlocked fields. Clearing a lock is therefore an explicit admin action.
- On Create/Compose, explicitly supplied known values are locked by default; missing classification fields can be populated from the JD. Currency is null when salary is undisclosed, even if an old client sends USD. Manual salary amounts are retained; missing currency/period still require review.
- `POST /api/admin/jobs/analyze-description` accepts `JobClassificationInput` and returns `JobClassificationResult` in the existing API envelope. It does not persist. It inherits `Administrator` authorization. Clients can show suggestions, evidence, confidence and review flags before sending accepted edits.
- A requested location correction must be accepted via the normal admin API, which retains the existing manual normalization/deduplication safeguards. Analysis and backfill do not silently rewrite location or URL identity.

## Candidate Browse Jobs contract

Existing employment, workplace, location, legacy experience and public visibility filters are retained. Numeric filters are `minExperienceYears`/`maxExperienceYears`, inclusive whole-year overlap:

- A requirement `[3,5]` overlaps a request `[3,5]` and `[5,8]`.
- A minimum-only requirement `3+` means `[3,infinity)` and overlaps `[3,5]` or `[10,12]`. It is never stored as a fabricated `3–5` requirement.
- Unknown numeric requirements do not match an explicit numeric filter.
- For compatibility, historical maximum-only values retain the existing single-known-bound behavior. Both bounds are inclusive.

`GET /api/jobs/experience-ranges` exposes the one canonical UI mapping policy: 0, 1–2, 3–5, 6–10, 11+. These are proposed backend synchronization bins, not claims about unavailable frontend options. A suggestion's `experienceFilterKey` maps its actual minimum to a bin; it does not change the stored maximum or the SQL interval semantics.

Salary amounts remain in disclosed native currency units and period. Amount filters (`minAmount`/`maxAmount` and legacy aliases), salary sorting and salary facets accept older requests omitting both `currencyCode` and `salaryPeriod` by using the existing frontend's INR/yearly slider contract. New clients can specify both explicitly; providing only one fails validation. No conversion, annualization or persisted-value inference is performed. Unknown periods/currencies are excluded from numeric salary comparisons. Other unscoped facet counts and ordinary browsing retain their existing scope. Filtering/counts run in PostgreSQL before pagination, and combined filters remain ANDed.

Example:

```text
GET /api/jobs/search?minExperienceYears=3&maxExperienceYears=5&workplaceType=Remote&employmentType=FullTime&location=Hyderabad&minAmount=800000&currencyCode=INR&salaryPeriod=Yearly
```

Use the existing configured enum serialization conventions; numeric equivalents remain stable.

## Future ingestion

The shared `JobIngestionService` invokes classification for creation and source refresh. `RawExternalJob` adds optional currency and salary period for provider metadata adaptation. All existing Workday, Greenhouse, Lever, Ashby, SuccessFactors and custom/bulk imports reach this boundary. Manual override groups survive later provider refreshes. Existing provider location/eligibility and identity mechanics run as before; the classifier does not write an application URL or source identity. No provider-specific classifier, provider fetch, source activation, scheduling change or production ingestion was introduced.

## Existing-job backfill

`job-classification-backfill` processes existing rows only, across companies and providers, including soft-deleted rows. It never creates, deletes, merges, publishes, expires or reconciles jobs.

- Default dry-run plus PostgreSQL `default_transaction_read_only=on` defense.
- Default 100 jobs per batch, 10 batches per invocation; validated bounds 1–500 and 1–1000.
- Stable keyset cursor, initial GUID upper watermark and creation-time cutoff. A local checkpoint binds database host/port/name, resolved Jobs schema/table, apply/dry-run mode, rules version and backfill-v2 format. Pre-release v1 files are rejected; use fresh files. Separate checkpoint files are mandatory for dry-run and apply. An exclusive local lease prevents two processes sharing one cursor.
- Retry IDs retain failed/concurrently edited jobs. Retry work is bounded. Checkpoints use write-to-temp and atomic replacement after each completed item. An interrupted uncheckpointed item can repeat idempotently.
- Per-field before/after values, changed field names, evidence, source/job identifiers and outcomes are appended to a local JSONL audit. Summaries include total, analyzed, proposed/applied correction counts, unchanged jobs, conflicts, failures, remaining and retry jobs. Applied corrections count accepted data changes; evidence-only writes do not inflate that count. Counters are cumulative across resumes; after a crash the audit is the authoritative attempt history.
- Updates use a hard-coded classification-column whitelist and write changed columns only. A hash of the whole original PostgreSQL row prevents concurrent admin edits or provider refreshes from being overwritten. URLs, status, identities, fingerprint, location, creation/update timestamps and provider metadata are untouched.
- Historic populated values without provenance remain intact and are flagged when evidence disagrees. Missing fields can be filled. A stored USD with no salary, unexplained OnSite/FullTime or unsupported numeric bounds are not assumed to be wrong solely because they resemble defaults. Recording a suggestion never upgrades uncertain historical provenance to automatic ownership. Values already owned by this rules system can be corrected on reprocessing. Manual locks always apply.
- Location suggestions/conflicts are evidence-only; reviewed admin corrections are a separate workflow. This preserves geographic eligibility and deduplication identity.

### Dry-run and resume

Run from the backend checkout, against an explicitly selected staging database first. Supply the connection securely through the environment; do not paste it into logs or commands stored in history. Create the checkpoint directory beforehand.

```powershell
dotnet run --project JobPortal.Maintenance -c Release --no-build -- job-classification-backfill --help

dotnet run --project JobPortal.Maintenance -c Release --no-build -- job-classification-backfill --checkpoint C:\safe-local-audits\classification-dry-run.json --batch-size 100 --max-batches 10
```

Repeat the same command/path until `RemainingJobs` and `RetryJobs` are zero. Review `<checkpoint>.audit.jsonl` and unresolved historical replacements. Preserve the checkpoint and audit together. Resolve failed rows, then rerun; starting a new complete analysis intentionally uses a fresh checkpoint file.

After approved staging verification and a reviewed production dry-run, the corresponding apply command is:

```powershell
dotnet run --project JobPortal.Maintenance -c Release --no-build -- job-classification-backfill --apply --checkpoint C:\safe-local-audits\classification-apply.json --batch-size 100 --max-batches 10
```

This command was not run against production. It requires separately authorized operator execution, the migration already applied and a writable explicitly configured database connection. For dry-run, a read-only database role is recommended in addition to the server-side read-only setting.

## Accenture sample

The tested JD returns:

```json
{
  "minimumExperienceYears": 3,
  "maximumExperienceYears": null,
  "descriptionLocation": "Hyderabad",
  "workplaceType": "Unknown",
  "employmentType": "Unknown",
  "minimumSalary": null,
  "maximumSalary": null,
  "currencyCode": null,
  "salaryPeriod": null
}
```

The repeated 3-year requirement is deduplicated; 15-year education is ignored. The office sentence is a 0.6-confidence workplace signal, not a mandatory OnSite classification. With provider location Bengaluru, the response records both locations and a location conflict. Existing location, application URL, fingerprint, source identity and publication status remain unchanged.

## Validation and remaining acceptance gates

Final local results are appended below after verification. The strict normal test build has an existing 184-finding analyzer baseline; production assemblies are built with normal analyzers and warnings-as-errors. Runtime test compilation retains analyzers and temporarily treats their warnings as warnings via command-line properties; no repository settings or assertions are weakened.

Two unrelated runtime failures were reproduced against the preserved pre-task binaries: `ExternalJobCoverageTests.ActualProviderOutputPassesThroughSharedRunnerAndCategoryMapping` and the Civil Engineer/Infrastructure case of `ExternalJobEnrichmentTests.BroadDepartmentsDoNotImplyTechnologyRoles`. Their baseline comparison is recorded separately. They are not introduced by classification. The existing PostgreSQL checkpoint fixture requires explicit `WORKDAY_CHECKPOINT_TEST_POSTGRES`, verifies host `127.0.0.1` and database `careerharbor_workday_test`, and creates/drops only its random isolated schema. No production API/Neon configuration is used by those tests.

## Production rollout and frontend next phase

1. Resolve/disposition the known unrelated test/analyzer baseline and run the gated PostgreSQL/checkpoint suite on the disposable localhost fixture. Do not declare deployment acceptance from skipped database tests.
2. Review the incremental migration SQL, back up the selected database and verify rollback/roll-forward plans. Apply the additive migration in staging, then exercise admin/public contracts, all provider import paths and dry-run/resume/concurrency behavior.
3. Coordinate frontend display/admin support for null currency, `Unknown` work/employment enum values, salary period, explicit currency/period selection, the experience-ranges endpoint, numeric admin inputs, Analyze Description UI, evidence/conflict review and explicit override groups. Legacy annual-rupee requests already have backend compatibility; verify the deployed frontend commit and live display behavior separately.
4. After approval, apply the incremental migration in the production change window and deploy the reviewed backend. Keep ingestion/scheduling activation under the existing separate operational approval process.
5. Run bounded production dry-run under an explicitly selected read-only connection. Review audit and uncertain historical defaults; accept disputed fields via existing admin edits. Run approved apply batches with a new mode-bound checkpoint.
6. Verify counts, unresolved conflicts/retries and sample companies (Accenture/PwC/PTC plus other providers), combined public filters, unchanged URLs/source identities/status and idempotent repeated runs. Refresh the existing public-job cache after backfill through the approved operational mechanism or its normal expiry.
7. Retain audits, observe classification failure logs and monitor normal subsequent imports. Extend conservative city/rule coverage only with regression cases; unfamiliar or ambiguous evidence should remain unknown/reviewable.

No production connection, migration, backfill, ingestion, scheduling activation, push or deployment was performed by this task.

## Final local verification results

| Check | Result |
|---|---|
| API Release build with normal analyzers/warnings-as-errors | PASS, zero warnings/errors; `../classification-release-final.log` |
| Maintenance Release build with normal analyzers/warnings-as-errors | PASS, zero warnings/errors; `../classification-maintenance-final.log` |
| Classification tests | 72 passed, zero failed; one PostgreSQL test skipped because the explicit disposable fixture connection is absent |
| Combined classification/admin/public/provider/ingestion/Workday regression selection | 454 passed, two pre-existing failures, 14 gated PostgreSQL tests skipped; `../classification-test-results/classification-regressions-final.trx` |
| Pre-task binary comparison | The same two unrelated failures reproduced; `../classification-test-results/classification-baseline.trx` |
| Test compilation | Runtime build succeeded with 184 existing analyzer warnings; no new analyzer findings, repository rules unchanged. The normal strict test build therefore remains blocked by that existing baseline. Signature comparison found the same 161 distinct file/code/message combinations before/after, representing those 184 locations. |
| EF model consistency | PASS, no pending model changes; `../classification-model-check.log` |
| Incremental idempotent PostgreSQL migration SQL | Generated successfully, Jobs-only schema changes; `../classification-migration.sql`. Generated only, not executed. |
| Maintenance help, argument entry point, diff whitespace | PASS; no database connection needed for help; `../classification-help.log` |

The initial machine-readable summary is `../classification-verification-summary.json`. Current database and salary compatibility evidence is in [disposable database verification](job-description-classification-database-verification.md). Runtime tests required a local test host outside the restricted execution sandbox; they used fixtures/in-memory stores. No test assertion was disabled or relaxed. Salary-facet compatibility verifies the existing annual-rupee default and explicitly scoped ranges, without mixing unknown currency/period values.

Reproduce the build/runtime separation:

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build JobPortal.Maintenance/JobPortal.Maintenance.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:TreatWarningsAsErrors=false
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Classification|FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~AdminJobLifecycleTests'
```

To close the database acceptance gate, provision the existing disposable localhost PostgreSQL fixture, set `WORKDAY_CHECKPOINT_TEST_POSTGRES` securely to its approved connection (host must be `127.0.0.1`, database must be `careerharbor_workday_test`), and rerun `ClassificationPostgresTests` and `WorkdayCheckpointPostgresTests`. Their shared fixture applies migrations inside its isolated random schema and removes that schema afterward. Do not substitute a production connection. Also rerun the other existing PostgreSQL ingestion/run-store tests under their own configured local fixtures.

Implementation is complete across the shared service, persistence, admin/public APIs, ingestion and safe backfill. Production acceptance remains conditional on the documented PostgreSQL gate, existing unrelated failures/analyzer baseline, frontend contract synchronization and authorized staged rollout. The existing production database has not been reprocessed.
