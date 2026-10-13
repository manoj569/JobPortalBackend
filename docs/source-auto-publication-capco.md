# Source publication control and Capco validation

## Investigation: behavior before this change

`JobSource` and the Admin source request/response had no persisted `AutoPublishEnabled` property. Only the legacy global `JobAggregationOptions.AutoPublishEnabled` and Oracle's configuration-only setting existed. `JobAutoPublishService` explicitly ignored the global option in both single-item publication and batch review.

All five live providers use `JobSourceRunner`: provider snapshot → `JobSourcePublicationPolicy.Select` (existing India evidence rules) → normalizer/enrichment/category → `JobIngestionService` → `JobAutoPublishService` → `JobService.PublishAsync`. Workday follows this same path. No provider independently assigns published status.

New imports were saved as `Draft`. Source identity updates preserved status. The runner attempted publication after Created, Updated, Unchanged and cross-source/manual duplicate matches. The publisher skipped manual jobs, hidden jobs and archived jobs, but otherwise allowed quality-eligible imported drafts/closed jobs without a source opt-in. `JobService.PublishAsync` validated references, source activity/company, location and expiry, then set Published, publication time and visibility, with audit and save.

Other status writers: manual composition/creation creates Draft; admin unpublish sets Draft and clears PublishedAtUtc; admin close/archive sets Closed/Archived; repository expiry sets Expired; verified source reconciliation sets Closed. Referral approval has its separate existing publication path and is untouched. Public/candidate queries require Published, non-hidden/non-deleted and valid expiry.

Crucially, unpublish cleared the timestamp: an existing source-linked draft did not prove that the source still owned publication. Import timestamps and source identity alone did not distinguish admin suppression.

## Behavior after this change

The shared `JobAutoPublishService` is the automatic publication decision point for Greenhouse (1), Lever (2), Ashby (3), SuccessFactors (4) and Workday (5). It requires an active, non-deleted, matching source with `AutoPublishEnabled=true`; existing quality/expiry/India checks still apply. The legacy global flag cannot override this decision. Oracle (6) remains blocked at validation, scheduling, execution and automatic publication.

Source settings default to false. Imports remain Draft while disabled. Enabling the source allows a new-generation source-managed draft to publish on a subsequent scan. Disabling the source prevents further automatic transitions; it does not retroactively unpublish already visible jobs.

`Job.SourceOwnsPublication` is set only when creating a provider import, never inferred on existing rows or duplicate matches. Admin unpublish, close, archive and hide revoke it; ingestion updates preserve it. Editorial state changes serialize against the existing publication lock. Pre-migration drafts have unknown ownership and remain held for explicit admin review/publishing. This deliberately avoids retrospectively guessing editorial intent. No new status enum is introduced.

Cross-source/URL/fingerprint/fuzzy duplicate matches no longer trigger publication of the matched row. Source-owned updates still can. The existing import policy may store a missing-location draft, but automatic publication requires positive India evidence; generic Remote and missing location do not qualify. Manual publishing retains its existing rules.

Snapshot completeness, skipped/failed record guards, full observed identities, bounded provider requests and reconciliation remain unchanged. Unsafe/incomplete scans cannot close existing jobs. No adapters, ATS enum values, frontend or public-query filters are changed.

## Migration and operations

A required PostgreSQL migration adds two boolean columns with false defaults: `JobSources.AutoPublishEnabled` and `Jobs.SourceOwnsPublication`. No production migration or database write is performed by this task. Existing published jobs retain their status. All existing sources require explicit source opt-in for future automatic publication. Existing unpublished rows require admin review because their publication ownership is unknown.

Admin source create/update JSON now accepts `autoPublishEnabled` (default false), and list/detail responses return it. `isActive` is the actual API field corresponding to the requested IsEnabled. Full source updates must include `autoPublishEnabled: true` to retain opt-in. The existing frontend is untouched; it may need separate UI work to expose this setting.

For Capco, reuse the existing Capco company ID and existing source if present. Otherwise configure through the authorized Admin workflow:

```json
{
  "companyId": "<existing Capco company ID>",
  "careerPageUrl": "https://job-boards.greenhouse.io/capco",
  "atsType": 1,
  "atsIdentifier": "capco",
  "isActive": true,
  "autoPublishEnabled": true,
  "scanIntervalMinutes": 1440
}
```

Do not create a duplicate company/source or bypass configuration to force publication. No production source is silently created. Production readiness requires an explicitly configured safe persisted run and verification through the public Browse/Discover API.

## Capco read-only results (2026-10-13)

Greenhouse's [official Job Board API documentation](https://docs.greenhouse.io/job-board.html) describes public, unauthenticated GET endpoints. The list endpoint returns all job posts; `content=true` includes descriptions. Job post `id` is the application identity. The existing adapter uses that stable ID, official URLs, the configured company and a detail GET to verify availability/deadline before admitting each record to a complete snapshot. No second adapter or Capco-specific location rules were introduced.

The production adapter was invoked using its actual DI registration, production HTTP transport, three-attempt retry policy, ten-second per-attempt timeout and two-request detail concurrency. A diagnostic observer paced requests at least 500 ms apart. The list request exhausted three attempts with `TaskCanceledException`; two attempts received HTTP 200 headers but did not complete payload processing. There were **zero detail requests**. This did not establish a usable complete snapshot; no persistence or reconciliation was attempted.

A separate, explicitly diagnostic GET downloaded the actual official response (7,674,987 bytes; about 35.7 seconds including PowerShell overhead) using a longer request timeout. The existing `ExternalJobNormalizer` and `IndiaJobEligibilityPolicy` then processed that downloaded response locally. This is not a successful production-adapter run or a replay of invented responses.

| Metric | Verified result |
| --- | ---: |
| Advertised total | 720 |
| Actual returned records | 720 |
| Records parsed | 720 |
| India-eligible listing records | 166 |
| Non-India rejected | 533 |
| Ambiguous under existing policy | 21 |
| Parsing failures | 0 |
| Duplicate external IDs | 0 |
| Missing application URLs | 0 |
| Missing titles | 0 |
| Missing descriptions | 0 |
| Prospect posts | 0 |
| Details requested by production adapter | 0 |

The 21 held locations are Czech Republic/Brno/Prague and Middle East/Doha combinations not recognized by the existing policy. They remain held; no location rules were changed. The 166 positive listings contain explicit India evidence, including Bengaluru, Mumbai, Pune, Chennai, Hyderabad and multi-city India listings. All 554 non-India/ambiguous records are excluded from automatic publication.

**Published count: not applicable. No persisted Capco run occurred.** Detail validation, final publication eligibility and production public Browse/Discover visibility are unverified. No production database was accessed, so Capco source existence/configuration was not asserted. Do not describe Capco as production-ready.

Read-only evidence and reproducible harness are outside the repository, in sibling directory `capco-readonly-validation`: `Program.cs`, `CapcoValidation.csproj`, `capco-validation.json` (production attempt), `capco-list-response.json` (actual downloaded response), and `capco-list-diagnostic.json` (policy counts). The harness has no database connection or persistence code.

## Tests, build and baseline comparison

- Initial focused run: **75 passed, 0 failed, 0 skipped**.
- Broader relevant regression run: **642 passed, 10 failed, 17 skipped** (669 total). One failure was the old runner test expecting publication of an unowned URL match. That expectation was corrected to assert no publication, covered additionally by the cross-source draft integration test.
- Final focused run including the corrected runner tests: **89 passed, 0 failed, 15 skipped** (104 total). `RunAsync_DuplicateJob_DoesNotPublishAnUnownedMatch` passes.
- Isolated unchanged baseline `586c3bc`: **17 passed, 9 failed, 0 skipped** across the selected reproduction cases. All nine remaining broader failures match the baseline by test name: four `WrongBoardOrOffsetPaginationCannotProduceCompleteSnapshot` cases, two `PaginationChangesReportTheFailingOffset` cases, one malformed `last-link` case, one `ListingLastOffsetMismatch` case, and `NewUnmappedJobIsSkippedWithoutClaimingSuccessfulRun`. These unrelated SuccessFactors/category failures were not changed.
- PostgreSQL-dependent tests were skipped because the disposable database environment is not configured. Migration execution and real PostgreSQL publication-lock/checkpoint behavior were not verified in this task.
- Test compilation used `-p:TreatWarningsAsErrors=false` for existing test-analyzer warnings. The production build retained its normal strict settings.
- Final exact command `dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release`: **succeeded, 0 warnings, 0 errors**, 24.06 seconds.
- EF `migrations has-pending-model-changes`: **no pending model changes**. Generated migration `20261013112101_AddSourcePublicationControl` was not applied to a database.
- `git diff --check`: **passed**; only existing line-ending conversion advisories for two test files.

The new `SourceAutoPublicationTests` covers both source flag values for all five providers, public-query visibility, idempotency, false→true opt-in, preservation of admin unpublish/hide/close/archive, legacy ownership ambiguity, missing-location hold, cross-source duplicate protection, no retroactive unpublish, Admin configuration round-trip, Oracle blocking and enum stability. Existing provider, reconciliation, ingestion, quality and public-query tests were included in the broader run. Fixture constructors now supply the source repository and explicit publication opt-in. Missing-location test expectations now distinguish persistence from unattended publication.

Logs/TRX files are in sibling `source-publication-results` and `source-publication-*.log`. The baseline export is `source-publication-baseline-586c3bc`. HEAD advanced independently during the task from `0dc27d1` to `586c3bc`; the separate Workday commit was preserved. This task made no commits.

## Remaining steps and limits

1. Review and apply the required migration through the normal deployment process; no deployment was performed here.
2. Explicitly opt intended sources in through Admin configuration. Review/publish legacy drafts manually; do not mass-backfill ownership from source identity alone.
3. Resolve and revalidate Capco listing retrieval with the production timeout/transport before activating its source. The diagnostic fetch does not validate production reliability or job deadlines.
4. Run the PostgreSQL-dependent checks against a disposable database and address the pre-existing regression failures separately.
5. After confirming an existing Capco company/source, perform an authorized complete persisted run with explicit opt-in, then verify results through the same public Browse/Discover API used by the frontend. Until then, production visibility and the number eligible for final publication are unknown.

No frontend/provider-adapter changes, production database writes, staging, commit, push or deployment were performed. The pre-existing untracked `docs/browse-jobs-production-audit.md` was left untouched.

## Exact files changed and final Git state

The status below lists all task changes plus the pre-existing Browse Jobs audit document explicitly identified above. New migration designer and snapshot files are generated EF artifacts. Everything is unstaged.

```text
 M JobPortal.Application.Tests/GenericIndiaJobIngestionPostgresTests.cs
 M JobPortal.Application.Tests/GenericIndiaJobIngestionTests.cs
 M JobPortal.Application.Tests/JobAggregationPipelineTests.cs
 M JobPortal.Application.Tests/JobAutoPublishServiceTests.cs
 M JobPortal.Application.Tests/JobSourceAdministrationTests.cs
 M JobPortal.Application.Tests/JobSourceRunnerTests.cs
 M JobPortal.Application.Tests/MastercardWorkdayVerificationTests.cs
 M JobPortal.Application.Tests/WorkdayBatchIngestionTests.cs
 M JobPortal.Application.Tests/WorkdayCheckpointPostgresTests.cs
 M JobPortal.Application.Tests/WorkdayPersistencePerformanceTests.cs
 M JobPortal.Application/Abstractions/Jobs/JobQualityGate.cs
 M JobPortal.Application/Features/JobAggregation/JobAggregationOptions.cs
 M JobPortal.Application/Features/JobAggregation/JobSourceManagementDtos.cs
 M JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs
 M JobPortal.Application/Features/JobAggregation/JobSourceManagementValidators.cs
 M JobPortal.Application/Features/Jobs/JobService.cs
 M JobPortal.Application/Services/JobAutoPublishService.cs
 M JobPortal.Application/Services/JobIngestionService.cs
 M JobPortal.Application/Services/JobSourceRunner.cs
 M JobPortal.Domain/Entities/Job.cs
 M JobPortal.Domain/Entities/JobSource.cs
 M JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs
?? JobPortal.Application.Tests/SourceAutoPublicationTests.cs
?? JobPortal.Persistence.Postgres/Migrations/20261013112101_AddSourcePublicationControl.Designer.cs
?? JobPortal.Persistence.Postgres/Migrations/20261013112101_AddSourcePublicationControl.cs
?? docs/browse-jobs-production-audit.md
?? docs/source-auto-publication-capco.md
```

Tracked-file diff stat (Git excludes untracked new test/migration/report files):

```text
 .../GenericIndiaJobIngestionPostgresTests.cs       |  9 +--
 .../GenericIndiaJobIngestionTests.cs               |  3 +-
 .../JobAggregationPipelineTests.cs                 |  3 +-
 .../JobAutoPublishServiceTests.cs                  | 21 +++++--
 .../JobSourceAdministrationTests.cs                |  2 +-
 .../JobSourceRunnerTests.cs                        |  6 +-
 .../MastercardWorkdayVerificationTests.cs          |  2 +-
 .../WorkdayBatchIngestionTests.cs                  |  2 +-
 .../WorkdayCheckpointPostgresTests.cs              |  7 ++-
 .../WorkdayPersistencePerformanceTests.cs          |  2 +-
 .../Abstractions/Jobs/JobQualityGate.cs            |  3 +-
 .../JobAggregation/JobAggregationOptions.cs        |  2 +-
 .../JobAggregation/JobSourceManagementDtos.cs      |  4 +-
 .../JobAggregation/JobSourceManagementService.cs   |  2 +
 .../JobSourceManagementValidators.cs               |  2 +
 JobPortal.Application/Features/Jobs/JobService.cs  | 13 +++++
 .../Services/JobAutoPublishService.cs              | 68 +++++++++++-----------
 .../Services/JobIngestionService.cs                |  1 +
 JobPortal.Application/Services/JobSourceRunner.cs  |  3 +-
 JobPortal.Domain/Entities/Job.cs                   |  3 +
 JobPortal.Domain/Entities/JobSource.cs             |  3 +
 .../Migrations/JobPortalDbContextModelSnapshot.cs  |  6 ++
 22 files changed, 105 insertions(+), 62 deletions(-)
```

