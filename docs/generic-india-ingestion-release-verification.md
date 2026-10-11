# Generic India ingestion: release verification

Verification date: 2026-10-11. Production was not queried or changed. The reported inventory reset is user-provided, not independently verified. No real emails, production ingestion, activation, commits, staging, pushes or deployment were performed.

## Root causes and implementation

The old Workday Accenture facet restricted upstream fetches to India and synthesized Indian country metadata. The selection layer mixed source-specific geography exceptions with eligibility. The PwC all-location override bypassed the shared geography policy. Category was mandatory in the domain, persistence, ingestion and publication paths; a missing mapping caused MissingCategory rejection. Legacy AutoPublishEnabled and optional expiry/mode/category quality gates left otherwise valid imports unpublished. Existing matches were not uniformly rechecked for publication. These gates have been replaced by shared eligibility and optional-category handling.

### Global eligibility

* Accept confirmed Indian country or recognized Indian city/state labels; accept explicit India remote eligibility.
* Accept truly absent geography with the exact display label `Location not specified` and reason `MissingLocationAccepted`. Never manufacture a country or classify this as India verified.
* Accept genuine multiple hiring locations including India. Keep original primary location, all additional locations and supplied countries in SourceMetadataJson; a London primary plus Pune additional remains a London display with the original multi-location facts retained.
* Reject known foreign-only geography and explicit India exclusions. Hold ambiguous geography, generic Remote, unknown country codes and contradictory country/location facts in run diagnostics; do not persist or auto-publish held feed items.
* No inference from employer, headquarters, tenant, provider host, verification status or source approval geography. Common aliases are normalized/recognized. Unrecognized geography is conservatively held; the city vocabulary is intentionally not an exhaustive world gazetteer.

The shared selection runs for every registered provider, including Workday, Greenhouse, Lever, Ashby and SuccessFactors. Workday retains bounded detail fetching, pagination, retry limits, URI validation and completeness checks. It now fetches without the Accenture India facet and preserves native location/country values. Provider-specific parsing remains format extraction only. Existing source configuration, ATS identifiers, licensed-logo rights checks, scheduler settings, transport budgets, authentication and source/company authorization remain in place. Existing provider adapters are exercised by focused regression fixtures; no assertion is made about current live upstream availability.

### Integrity, duplicate protection and reset behavior

Meaningful title, real company/source association and safe original HTTP(S) application URL remain mandatory for external imports. No links, details, countries or categories are invented. Invalid supplied salary/experience values and explicitly expired vacancies remain ineligible for publication. Category, salary, skills, experience, workplace mode, description and other optional metadata may be absent. Supplied categories are still validated; deleted/mismatched references remain protected.

Source/external identity is authoritative for owned updates. Canonical URLs remove tracking parameters. Conservative company/title/location fingerprint and fuzzy matching remain fallback layers, but cannot collapse two known distinct source/requisition identities. Shared PostgreSQL advisory locks and fresh checks protect concurrent creation and publication. Manual Create/Compose uses the same creation lock and fresh deduplication checks; duplicate vacancy requests return conflict. Blank draft URLs do not match all other blank URLs. Manual drafts remain draft; explicit foreign manual creation is rejected, ambiguous manual drafts may be saved but cannot be published.

Identity matches query actual Jobs rows, not prior run counters. Deleted/reset Jobs rows are recreated rather than treated as ghost existing records. No users, companies, categories, payments, memberships or applications were reset. Source-null legacy rows are protected as manual/unowned records; they are not automatically taken over.

### Publication and reconciliation

Eligible source-owned creations, updates and matches are checked for automatic publication even when the legacy AutoPublishEnabled flag is false. JobService remains the final publication authority, including source-active/company matching and safe URL checks. Hidden and archived records and unrelated manual drafts are protected. Publication is serialized and reloaded before transition; already-published records do not inflate Published.

Closure requires a native complete snapshot, no provider skips, complete unique observed identities, no held/test-limited effective scan and no ingestion/publication failure. Every observed upstream ID, including filtered foreign items, is retained for reconciliation identity. Incomplete/unsafe scans never close absent jobs. This rollout does not retroactively delete or unpublish observed legacy foreign rows; validate the reported empty inventory before relying on a fresh baseline.

### Diagnostics contract

Run status exposes additive `diagnostics` containing Fetched, Parsed, IndiaEligible, MissingLocationAccepted, NonIndiaRejected, AmbiguousLocationHeld, Created, Existing, Updated, DuplicatesPrevented, Published, InvalidRequiredDataRejected, Failed, ScanComplete and ReconciliationSafe plus reason dictionaries. It is stored durably as PostgreSQL jsonb at progress/finalization. Native fetch completeness is separate from reconciliation safety.

Created counts new persisted rows only. Existing = Updated + Unchanged + Matched, including repeated observations within one feed; it is not a count of distinct database vacancies. DuplicatesPrevented equals existing observations that did not insert. Publication is an independent dimension: a created row can fail publication, and an existing draft can become published. Failed/PublishFailed and provider/validation/selection/quality reasons remain distinct. Fetched includes returned parsed rows plus explicitly counted provider skips; records discarded before an adapter can account for them cannot be reconstructed. Rejected = explicit foreign rejection plus ingestion invalid/company rejection; ambiguous held and test-cap exclusions are separately identified. Legacy top-level Skipped retains its historical grouping; use Diagnostics for the exact breakdown. No production counts are fabricated.

## Required database migration and client compatibility

Migration: `20261011085034_AllowOptionalJobCategoryAndIngestionDiagnostics` in the active JobPortal.Persistence.Postgres assembly.

Only three Up operations: Jobs.CategoryId drops NOT NULL while retaining its FK, Jobs.SourceMetadataJson adds nullable jsonb, and JobSourceRuns.DiagnosticsJson adds nullable jsonb. No category seed changes, backfills, deletes or changes to business data. PostgreSQL uuid/jsonb/ALTER COLUMN DROP NOT NULL are Neon-compatible. The actual migration chain is tested against disposable PostgreSQL 17, not production Neon. Verify the target migration history before applying.

CategoryId becomes nullable in admin/public/candidate/application DTOs. CategoryName displays `Not specified`; clients must stop assuming a GUID is always present and make category selection optional. Existing supplied GUID payloads remain valid. Missing location is a nonempty label. Optional enum fields retain the existing zero/unspecified representation; clients must render unspecified values without forcing invented selections. No frontend files are changed. All nullable-category public queries preserve deleted-category and company visibility rules.

Do not automatically roll Down after null categories exist: making CategoryId NOT NULL must fail rather than invent a zero-GUID category. Down also removes retained JSON evidence. Prefer a reviewed forward repair; old binaries cannot safely read nullable-category records.

## Configuration checklist (Render)

No new production environment variables are required. Keep existing PostgreSQL connection, authentication, outbound-host/SSRF controls, provider limits and request budgets. Existing session advisory locks require a direct Neon endpoint; a transaction-pooler endpoint is rejected. Preserve the established SSL settings. The disposable test connection environment variable is test-only and must not be configured in Render.

* JobAggregation__AutoPublishEnabled no longer disables publication of eligible imports. Do not rely on false as an import safety switch.
* Per-source IndiaOnly/VerifiedIndiaHiringLocations/IndiaGeographyEvidence no longer override eligibility. They may remain for contract compatibility, but cannot broaden or verify location.
* Source IsActive and scheduler enable/interval remain explicit operational controls. No source was activated here.
* Existing SourceApprovals TestImportLimit remains a cap; any configured cap makes reconciliation unsafe even when all fetched jobs fit beneath it. Check and remove test caps only as an authorized production operation.
* Confirm every source has a valid company association, ATS identifier and supported career URL. Category mappings are optional; retain accurate existing mappings, do not map everything to IT.
* Review Workday max-job/page/detail budgets: an exhausted budget is incomplete and must not be reported as a successful full reconciliation.

## Safe deployment and verification

1. Review this diff and migration SQL; validate nullable-category client compatibility. Verify target migration history and the operator-reported empty inventory using authorized read-only access.
2. Take a backup or create an isolated Neon branch. Drain queued/running imports and stop old worker/API instances before schema/application rollout. Disabling the scheduler alone does not stop independently hosted durable workers or already queued runs.
3. Apply the reviewed additive migration through the established release process. This agent has not applied it to production.
4. Deploy the new API to all instances before allowing writes with null categories. Verify health and nullable-category Discover projections; restore worker/scheduler controls deliberately.
5. Operator-run a capped canary for each existing provider/company with reviewed URLs. Compare diagnostics and actual Discover results; capped runs must report ReconciliationSafe=false. Check manual drafts, explicit foreign/remote exclusions, optional category/location and original location retention.
6. Repeat each canary: Created=0 for already-existing records, Existing reflects observations, Published counts only new transitions, and job IDs remain stable. Inspect failures/reasons before removing caps or starting a full scan.
7. Only run a full existing-company ingestion after explicit operational authorization and verified configuration. A complete safe run may reconcile missing upstream identities; review counts before enabling routine schedules.

## Verification results

* Broad focused run: 346 cases, 344 passed, 2 failed (local PostgreSQL connection timeout and the existing minimum request-spacing timing assertion). These failures were retained in the original TRX, not relabelled as passes.
* Isolated follow-up after the final Workday country-descriptor fix: **84 passed, 0 failed, 0 skipped**, including both failed cases, actual PostgreSQL migrations/concurrent creation and publication, JSONB repeat-import equality, Discover visibility, lifecycle and Workday regressions.
* Consolidating the latest outcome for each distinct case across these two runs gives **347 passing cases**. This is a consolidation of overlapping runs, not one claimed 347-case run. The final pipeline/store check is recorded below.
* The PostgreSQL concurrency case used eight independent contexts/sources targeting the same canonical vacancy: **Created=1, Existing=7, Published=1, Failed=0**, one actual Discover result. Repeating the owning source produced **Created=0, Unchanged=1, Updated=0, Published=0**. These are synthetic disposable-database results, not production counts.
* Runtime tests used `-p:RunAnalyzers=false` because the normal test-project build has **184 pre-existing analyzer errors**. A normal build was rerun and its per-file/per-rule diagnostic counts exactly match the saved pre-task `pwc-all-location-tests-normal-verified.log` baseline (184). Two newly introduced assertion-style errors were corrected; no new-file analyzer errors remain. The baseline checkout itself was not separately rebuilt. Existing test analyzer settings/files were not suppressed or changed in the repository.
* Release API build with normal analyzers: **PASS, 0 warnings, 0 errors** after all source edits (generic-india-api-release-verified.log, elapsed 1m46s).
* EF pending-model check: **PASS**, no model changes since the scaffolded migration. Reviewed idempotent incremental SQL contains only the three Up schema operations plus migration history insertion. Full migration-chain application and public query/concurrency were exercised on disposable PostgreSQL 17.
* `git diff --check`: PASS (Git emitted only an existing LF/CRLF conversion notice, no whitespace errors). No staged changes.
* Final pipeline/store check: **19 passed, 0 failed, 0 skipped**, including the existing durable PostgreSQL enqueue/claim/recovery fencing test with the new diagnostics column. Across all overlapping runs, the latest result for **348 distinct cases is passing**.

Reproduction commands (run from repository root; explicit disposable local test connection environment variables only):

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:RunAnalyzers=false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~GenericIndiaJobIngestion|FullyQualifiedName~AdminJobLifecycleTests|FullyQualifiedName~WorkdayBatchIngestionTests'
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:RunAnalyzers=false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~JobAggregationPipelineTests|FullyQualifiedName~JobSourceRunPostgresTests'
dotnet ef migrations has-pending-model-changes --project JobPortal.Persistence.Postgres/JobPortal.Persistence.Postgres.csproj --startup-project JobPortal.API/JobPortal.API.csproj --configuration Release --no-build
```

Evidence logs are in the parent workspace: generic-india-regression-verified.log, generic-india-final-isolated.log, generic-india-tests-normal-analyzers-final.log, generic-india-model-check.log, generic-india-migration.sql and the final API/pipeline logs. TRX evidence is under JobPortal.Application.Tests/TestResults.

Deployment recommendation: NO-GO for immediate production rollout until the migration, nullable-category clients, target schema/history, existing inventory and worker drain/source configuration checks above are verified. Local code readiness is determined by the final tests/build below. Normal test-project analyzer debt is a separate CI blocker; it is not silently fixed by suppressing production analyzers.

## Exact changed files

The existing untracked `accenture-workday-page1.json` was preserved. Generated test output is under TestResults and is not part of the implementation. No files were staged. Both disposable databases were removed with their verified owned container after testing.

- `JobPortal.Application.Tests/AdminApplicationManagementTests.cs`
- `JobPortal.Application.Tests/AdminJobLifecycleTests.cs`
- `JobPortal.Application.Tests/JobAggregationPipelineTests.cs`
- `JobPortal.Application.Tests/JobAutoPublishServiceTests.cs`
- `JobPortal.Application.Tests/JobIngestionServiceTests.cs`
- `JobPortal.Application.Tests/JobProviderIntegrationAuditTests.cs`
- `JobPortal.Application.Tests/JobQualityGateTests.cs`
- `JobPortal.Application.Tests/JobSourceRunPostgresTests.cs`
- `JobPortal.Application.Tests/JobSourceRunnerTests.cs`
- `JobPortal.Application.Tests/MultiCompanyProviderTests.cs`
- `JobPortal.Application.Tests/PwcAllLocationImportTests.cs` (deleted)
- `JobPortal.Application.Tests/PwcCategoryIngestionTests.cs`
- `JobPortal.Application.Tests/WorkdayBatchIngestionTests.cs`
- `JobPortal.Application.Tests/WorkdayIngestionDiagnosticsTests.cs`
- `JobPortal.Application.Tests/WorkdayPersistencePerformanceTests.cs`
- `JobPortal.Application/Abstractions/Jobs/ExternalJobSourceSnapshot.cs`
- `JobPortal.Application/Abstractions/Jobs/IJobAutoPublishService.cs`
- `JobPortal.Application/Abstractions/Jobs/JobSourceRunResult.cs`
- `JobPortal.Application/Abstractions/Jobs/RawExternalJob.cs`
- `JobPortal.Application/Abstractions/Persistence/CandidateRepositoryContracts.cs`
- `JobPortal.Application/Abstractions/Persistence/JobRepositoryContracts.cs`
- `JobPortal.Application/Features/AdminApplications/AdminApplicationDtos.cs`
- `JobPortal.Application/Features/AdminApplications/AdminApplicationService.cs`
- `JobPortal.Application/Features/Candidates/CandidateDtos.cs`
- `JobPortal.Application/Features/Dashboard/DashboardProjections.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourcePublicationPolicy.cs`
- `JobPortal.Application/Features/JobAggregation/JobSourceRunService.cs`
- `JobPortal.Application/Features/JobAggregation/PwcWorkdayImportPolicy.cs` (deleted)
- `JobPortal.Application/Features/Jobs/JobDtos.cs`
- `JobPortal.Application/Features/Jobs/JobMappings.cs`
- `JobPortal.Application/Features/Jobs/JobService.cs`
- `JobPortal.Application/Features/Jobs/JobValidators.cs`
- `JobPortal.Application/Features/PublicJobs/PublicJobDtos.cs`
- `JobPortal.Application/Features/PublicJobs/PublicJobProjections.cs`
- `JobPortal.Application/Services/ExternalJobCategoryClassifier.cs`
- `JobPortal.Application/Services/JobAutoPublishService.cs`
- `JobPortal.Application/Services/JobIngestionService.cs`
- `JobPortal.Application/Services/JobQualityGate.cs`
- `JobPortal.Application/Services/JobSourceCategoryResolver.cs`
- `JobPortal.Application/Services/JobSourceRunner.cs`
- `JobPortal.Domain/Entities/Job.cs`
- `JobPortal.Domain/Entities/JobSourceRun.cs`
- `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs`
- `JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs`
- `JobPortal.Persistence/Configurations/EntityConfigurations.cs`
- `JobPortal.Persistence/Configurations/JobSourceRunConfiguration.cs`
- `JobPortal.Persistence/Repositories/AdminApplicationRepository.cs`
- `JobPortal.Persistence/Repositories/CandidateRepository.cs`
- `JobPortal.Persistence/Repositories/DashboardRepository.cs`
- `JobPortal.Persistence/Repositories/JobReferralRepository.cs`
- `JobPortal.Persistence/Repositories/JobRepository.cs`
- `JobPortal.Persistence/Repositories/JobSourceRunStore.cs`
- `JobPortal.Persistence/Repositories/PublicJobRepository.cs`
- `JobPortal.Application.Tests/GenericIndiaJobIngestionPostgresTests.cs`
- `JobPortal.Application.Tests/GenericIndiaJobIngestionTests.cs`
- `JobPortal.Application/Services/ImportedJobIntegrity.cs`
- `JobPortal.Application/Services/IndiaJobEligibilityPolicy.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261011085034_AllowOptionalJobCategoryAndIngestionDiagnostics.Designer.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261011085034_AllowOptionalJobCategoryAndIngestionDiagnostics.cs`
- `docs/generic-india-ingestion-release-verification.md`
