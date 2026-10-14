# CareerHarbor auto-publication repair — 14 October 2026

## A. Root cause and limits of the diagnosis

The checkout was clean at `d2da65b` and already included source publication control (`f4d8ed8`). Its five implemented ATS providers already invoked the same `JobAutoPublishService` through `JobSourceRunner` for created, updated and unchanged owned jobs. A universal failure for those providers was not reproduced in fixture tests. Production configuration, deployment version, migration history and affected job rows were deliberately not queried, so the exact cause of the reported production incident remains unconfirmed.

The applied migration `20261013112101_AddSourcePublicationControl` adds BOTH `JobSources.AutoPublishEnabled` and `Jobs.SourceOwnsPublication` with false defaults. The legacy global configuration flag is deliberately ignored. Consequently, enabling the old global flag does not enable a source; enabling a source does not establish publication ownership for older jobs. Old rows remain held to avoid interpreting ordinary drafts as admin approval. This is a concrete code-level explanation to check against affected sources, not a claim about uninspected production data. No ownership flags were mass updated.

Confirmed defects repaired:

- The publisher had a fixed five-provider whitelist. A future normalized adapter could import eligible jobs but would always receive `Disabled`. Executable providers now inherit the common policy; the existing Oracle authorization restriction remains intact.
- Source-owned paused, expired or closed jobs could pass quality checks and be published again. Automatic transitions now require Draft status; published jobs retain their state and timestamp.
- A Workday snapshot without listing diagnostics defaulted processing completeness to true, even with incomplete-provider, selection, identity or ingestion blockers. Such runs now require all blockers except the intentional `WorkdayNonAuthoritative` protection to be absent. Real Workday listing-drift processing diagnostics retain their existing behavior, and absence reconciliation remains disabled.
- SuccessFactors validated supplied last-page links only for multi-page boards. Invalid links on single-page boards could incorrectly establish snapshot completeness. Supplied links are now always validated; single-page boards may still omit them.

## B. Files changed

1. `JobPortal.Application/Services/JobAutoPublishService.cs` — universal policy, Draft-only automatic transitions, structured reason logs.
2. `JobPortal.Application/Services/JobSourceRunner.cs` — publication logs, post-save counters, incomplete Workday fallback correction.
3. `JobPortal.Infrastructure/Services/SuccessFactorsJobSourceProvider.cs` — pagination completeness validation.
4. `JobPortal.Application.Tests/SourceAutoPublicationTests.cs` — future-provider ON/OFF, inactive source, preserved timestamp and suppressed-status tests.
5. `JobPortal.Application.Tests/SuccessFactorsJobSourceProviderTests.cs` — omitted single-page pagination link regression.
6. `JobPortal.Application.Tests/JobSourceReconciliationSafetyTests.cs` — align serialized diagnostics assertions with the existing Workday non-authoritative protection already asserted earlier in the same test.
7. `AUTO_PUBLICATION_REPAIR.md` — this report and deployment commands.

## C. Architecture and operational behavior

The existing flow remains: authorized admin/scheduler queue → registered ATS adapter → normalized snapshot and India selection → existing ingestion/upsert and locks → shared auto-publisher → existing `JobService.PublishAsync` validation, audit and SaveChanges → public repository query.

`IJobAutoPublishService` and its batch-review contract are reused rather than adding another publication abstraction. Batch review makes non-publishing decisions only; eligible jobs still pass through the locked publisher and existing publication authority. The job is reloaded after acquiring the publication lock, which is also used by admin suppression. Durable publication receipts and counters remain in the existing checkpoint workflow.

ON publishes eligible new or existing source-owned drafts. OFF imports drafts and preserves existing publication decisions. Manual unpublish, hide, close and archive revoke source publication ownership. False ownership is treated as unverified, not inferred rejection. India eligibility, validity, expiration, source activity/company matching and Oracle authorization checks remain in force. Generic Remote and missing geography do not establish eligibility. Existing published updates preserve IDs and publication timestamps.

Safe backfill uses the existing audited, source-scoped manual run: enable that source, then enqueue its run. Only eligible owned drafts actually observed through that source's current feed can publish. Unknown-ownership legacy rows require individual editorial review; there is no automatic migration or legacy mass backfill.

Decision logs include source ID, company display name as source name, provider, saved toggle, job ID and reason. Reasons include `AUTO_PUBLISHED`, `AUTO_PUBLISH_DISABLED`, `ALREADY_PUBLISHED`, `SOURCE_INACTIVE`, `PUBLICATION_NOT_AUTHORIZED`, `MODERATION_BLOCKED`, `INDIA_ELIGIBILITY_UNKNOWN`, `INVALID_JOB`, and `MANUALLY_UNPUBLISHED_OR_OWNERSHIP_UNVERIFIED`. The latter deliberately does not claim to distinguish unverified legacy ownership from manual suppression without audit evidence. Final summary logs include imported, updated, published, unpublished, skipped, rejected and publication-failure counts after the final save. UnpublishedCount represents reconciliation closures; it is not a count of all remaining drafts. No secrets, identifiers/tokens, URLs or raw provider configuration are logged.

## D. Provider coverage

| Provider | Coverage |
|---|---|
| Greenhouse | Normalized fixture ingestion, ON/OFF and public repository visibility; provider parser tests |
| Lever | Normalized fixture ingestion, ON/OFF and public repository visibility; provider parser tests |
| Ashby | Normalized fixture ingestion, ON/OFF and public repository visibility; provider parser tests |
| Workday | Normalized fixture ingestion, ON/OFF, parser, durable/interrupted runner and reconciliation protections |
| SuccessFactors | Normalized fixture ingestion, ON/OFF and public visibility; parser and pagination tests |
| Mock future ATS (`AtsType` 99) | Normalized contract through runner, ON/OFF, repeat sync and public visibility; not a real integration |
| Oracle Recruiting Cloud | Configuration-only and authorization-blocked; negative tests. No working live ingestion claimed |
| Adzuna | Separate user discovery flow, not an ATS-owned import into this publication pipeline |

Adding a future adapter still requires its normal provider registration, enum/configuration and normalization work. No publication whitelist or provider-specific publishing implementation is needed. Live ATS calls were not made.

## E. Database

No new migration required. No mappings, migration files or model snapshot changed. Existing Status, PublishedAtUtc and SourceOwnsPublication state suffices. The previously applied migration is preserved. No database update, production writes or production integration tests were performed.

## F. Validation

Final validation on the completed changes:

| Check | Result |
|---|---|
| Exact requested `dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release` | Passed, 0 warnings, 0 errors |
| Relevant fixture/provider/publication/runner regressions | 240 passed, 0 failed, 1 skipped; 241 total |
| SourceAutoPublicationTests subset | All 28 cases passed, including all five providers and mock future provider |
| Standard test build with analyzer errors enforced | Failed before execution; 186 existing analyzer diagnostics |
| Disposable PostgreSQL concurrency/migration test | Skipped: required local database environment variable not configured |
| `git diff --check` | Passed |

Evidence: `../auto-publication-final-build.log`, `../auto-publication-final-tests.log`, and `../auto-publication-results/auto-publication-final.trx`. The regression log retains analyzer warnings. No analyzers were disabled and no test failures were suppressed.

The standard test-project build failed before test execution on 186 analyzer diagnostics treated as errors in existing files, including CA1861, CA1869, CA1859 and xUnit2030/2031. The original log is `../auto-publication-tests.log`. The subsequent regression command keeps analyzers enabled and uses `-p:TreatWarningsAsErrors=false` for that invocation only; repository analyzer settings are unchanged.

The first broad regression run found 11 failures, 227 passes and 1 skip. Four failures exposed the SuccessFactors pagination defect; six exposed the incomplete Workday fallback; one exposed conflicting serialized diagnostics assertions in the Workday test. Those were repaired, not excluded. Its evidence remains `../auto-publication-validated-tests.log` and `../auto-publication-results/auto-publication.trx`.

The PostgreSQL concurrency/migration test requires an explicitly configured disposable localhost database `careerharbor_generic_india_test` through `GENERIC_INDIA_TEST_POSTGRES`; it never reads Neon settings. Without that prerequisite it is reported skipped. Cross-process PostgreSQL concurrency is not claimed verified by in-memory fixtures.

## G. API compatibility

No endpoints, DTOs, enum values or frontend contracts changed. No frontend changes required. Source create/update/detail/list already persist and return AutoPublishEnabled. Update operations and worker runs retain the existing execution guards and queue behavior. Public visibility was verified at the repository layer used by the public API, not with a deployed HTTP server.

## H. PowerShell commands for review and deployment preparation

Run commands sequentially from the checkout. No push or deployment has been performed.

```powershell
Set-Location 'C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9'
dotnet build .\JobPortal.API\JobPortal.API.csproj -c Release

# Standard test invocation preserves the repository's analyzer gate.
$publicationFilter = 'FullyQualifiedName~SourceAutoPublicationTests|FullyQualifiedName~JobAutoPublishServiceTests|FullyQualifiedName~GenericIndiaJobIngestion|FullyQualifiedName~JobSourceRunnerTests|FullyQualifiedName~JobSourceReconciliationSafetyTests|FullyQualifiedName~JobSourceAdministrationTests|FullyQualifiedName~JobQualityGateTests|FullyQualifiedName~ExternalJobProviderTests|FullyQualifiedName~SuccessFactorsJobSourceProviderTests|FullyQualifiedName~WorkdayJobSourceProviderTests|FullyQualifiedName~JobSourceDurableRunTests|FullyQualifiedName~JobSourceInterruptedRunTests'
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj -c Release --filter $publicationFilter

# Diagnostic regression invocation when existing analyzer errors block execution.
# Analyzers remain enabled; actual compilation errors and test failures still fail.
dotnet test .\JobPortal.Application.Tests\JobPortal.Application.Tests.csproj -c Release --no-restore -m:1 /nodeReuse:false -p:TreatWarningsAsErrors=false --filter $publicationFilter --logger 'trx;LogFileName=publication-review.trx' --results-directory .\TestResults

# Review migration sources without connecting to a database.
Get-Content .\JobPortal.Persistence.Postgres\Migrations\20261013112101_AddSourcePublicationControl.cs
Get-ChildItem .\JobPortal.Persistence.Postgres\Migrations\*.cs | Sort-Object Name | Select-Object Name
git diff -- .\JobPortal.Persistence.Postgres\Migrations .\JobPortal.Persistence\Configurations
git diff --check
git diff

# Commit only the reviewed files.
git add -- JobPortal.Application/Services/JobAutoPublishService.cs JobPortal.Application/Services/JobSourceRunner.cs JobPortal.Infrastructure/Services/SuccessFactorsJobSourceProvider.cs JobPortal.Application.Tests/SourceAutoPublicationTests.cs JobPortal.Application.Tests/SuccessFactorsJobSourceProviderTests.cs JobPortal.Application.Tests/JobSourceReconciliationSafetyTests.cs AUTO_PUBLICATION_REPAIR.md
git commit -m 'Repair shared ATS publication and snapshot safety'
git branch --show-current
git remote -v
git push origin HEAD
```

Do not run `dotnet ef database update` as part of this repair: there is no new migration. Resolve the standard analyzer gate before treating CI as clean.

## I. Post-deployment verification

1. Read `GET /api/admin/job-sources/{id}` with administrator authorization. Verify IsActive and AutoPublishEnabled; confirm the intended company/provider and source ID.
2. For an explicitly approved source, update using the existing complete source payload with AutoPublishEnabled=true. Do not overwrite other fields with defaults.
3. Call `POST /api/admin/job-sources/{id}/run`. It returns HTTP 202 and a run ID; poll `GET /api/admin/job-sources/{id}/runs/{runId}` until terminal. Inspect diagnostics, published counts and reason logs rather than treating queue acceptance as completion.
4. Inspect an eligible imported job: owning JobSourceId, SourceOwnsPublication=true, Status=Published and non-null PublishedAtUtc. Compare the stable ID and timestamp after a repeated run.
5. Read `GET /api/jobs/{slug}` and `GET /api/jobs?companyId={companyId}`. Confirm the imported ID appears. Check expiry, hidden status and geographic evidence if it does not.
6. On a controlled test source, switch OFF and import a new eligible vacancy: it remains Draft, while earlier published jobs remain visible. Manually unpublish a test job and repeat the run: it must stay held.
7. Review legacy drafts individually. Do not set SourceOwnsPublication=true based only on Draft status; use ownership and editorial audit evidence.

## J. Remaining risks

The production incident cause, deployed HTTP behavior, real ATS feeds and production migration/configuration state were not inspected. Legacy ownership cannot be inferred safely. PostgreSQL concurrent/retried publication requires the disposable database test above. Existing analyzer errors still block the standard test build. This patch does not introduce a new approval/moderation schema; it respects the existing ownership, hidden, deleted and status controls. A future legal-review feature must represent its hold through those controls or explicitly extend the shared policy.
