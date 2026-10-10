# Workday ingestion investigation (2026-10-11)

## Snapshot evidence and correction

The previous completeness expression required `expectedTotal < 2000` as well as stable pagination, unique paths, no placeholders, successful details, unique external IDs and matching raw counts. This guarantees that a 3,879-item PwC listing is incomplete even when every detail succeeds. It is a sufficient code-level explanation for the reported PwC result; production page signatures and counts were not available to rule out additional causes.

The correction limits that conservative cap check to an advertised total of exactly 2,000. Larger advertised inventories can certify completeness only when every existing pagination, detail and identity check also passes. The existing 10,000-record safety ceiling, bounded concurrency, timeouts, retries, request pacing, URL/SSRF checks and cancellation budget are unchanged. Incomplete final snapshots now log a fixed reason for changed listings, duplicate paths, placeholders, suspected search caps, failed details, duplicate external IDs or count mismatches.

Bounded public search verification, using the provider's existing Accenture India country facet (`c4f78be1a8f14da0ab49ce1162348a5e`), returned HTTP 200:

| Source | Offset | Advertised total | Returned rows | Observation |
| --- | ---: | ---: | ---: | --- |
| PwC | 0 | 3,879 | 20 | Normal first page |
| PwC | 20 | 3,879 | 20 | Stable total |
| Accenture | 0 | 2,000 | 20 | Capped advertised total |
| Accenture | 20 | 0 | 20 | Later-page total differs despite valid rows |
| Accenture | 1,980 | 0 | 20 | Last page below boundary |
| Accenture | 2,000 | 2,000 | 20 | First page repeated |
| Accenture | 2,020 | 2,000 | 20 | First page repeated |

Public search endpoints were `https://pwc.wd3.myworkdayjobs.com/wday/cxs/pwc/Global_Experienced_Careers/jobs` and `https://accenture.wd103.myworkdayjobs.com/wday/cxs/accenture/AccentureCareers/jobs`. These were eight small read-only searches, not ingestion; no job details were fetched. The saved local Accenture response also advertises 2,000 while containing larger facet counts. Facet counts alone do not establish a safe complete union of jobs.

Accenture stops because pagination reaches the provider-advertised total; the current live contract repeats the first page beyond that boundary. No evidence supports fetching additional distinct jobs merely by increasing offsets. Do not label that capped inventory complete. Facet partitioning would need independent coverage verification and is not introduced here. Mocked regression coverage demonstrates that an advertised total above 2,000 is supported by the generic pagination loop, without claiming Accenture's current API exposes it.

## Rejection diagnostics and historical limits

Existing ingestion results already distinguish duplicate URL/fingerprint/fuzzy identities, missing companies, invalid source data and invalid application URLs. Existing validation counts distinguish missing title/company, title length, salary/experience ranges, missing/nonexistent category and unavailable matches. Those reason tallies previously logged at Debug; this change reports them at Information with fixed `Stage`, `ReasonCode` and `Count` fields, including partial runs.

Selection counts separately report `NonIndiaLocation`, `UnknownIndiaEligibility`, `AmbiguousRemoteEligibility` and `TestImportLimit`. They are computed from the cumulative snapshot so a batched scan does not count exclusions repeatedly. These items did not reach ingestion and are not retroactively added to existing `Rejected`/`Skipped` totals. Existing eligibility and completeness decisions are unchanged. Provider omissions remain `ProviderError`; fixed provider detail-failure logs carry the more specific cause. Normalization/enrichment exceptions still use the existing isolated failure path; no historical normalization-failure count is established by the supplied totals.

The supplied production evidence establishes PwC's 4 created / 647 rejected of 651 processed and Accenture's 647 created / 1,313 rejected of 2,000 discovered. It does not contain per-reason counts. Repeated `SourceMappingMissing` and the required category for new jobs make missing categories a plausible contributor, but do not prove that all rejections had that cause. Exact historical breakdowns remain unverified; new logs provide counts for subsequent operator-authorized runs. No production registry, job records or credentials were read in this task.

## Category configuration

Source mapping is optional at resolution time and `SourceMappingMissing` is informational. A new persisted job requires a valid existing category; an existing owned job can retain its category when the incoming category is absent. Current precedence is:

1. A valid job-level category GUID.
2. An unambiguous `JobAggregation:CategoryMappings:<external-label>` mapping to an existing category.
3. Existing title/department classification matched to a unique existing taxonomy entry.
4. Configured source fallback for otherwise unclassified jobs.

Strong classification with missing/ambiguous taxonomy fails closed rather than using an unrelated source fallback. Workday currently provides no `ExternalCategory`; title classification and explicit categories remain supported. No category inference was broadened and no blanket IT mapping or category record was added. Existing category code needs configuration, not a schema/API redesign.

Operator steps (not executed):

1. Use existing Administrator-only `GET /api/admin/categories/options` or Admin → Categories to identify real existing category GUIDs. Check representative job titles against the existing classifier and taxonomy before mapping. Do not create duplicate categories.
2. For mixed PwC/Accenture inventories, preserve job-level classification. Leave source fallback unset if there is no defensible category for every otherwise unclassified job. Such new jobs should remain rejected until adequate category evidence/configuration exists.
3. Configure `JobAggregation__CategoryMappings__<exact-external-label>=<existing-category-guid>` only for labels actually supplied by a provider. This does not help Workday unless it supplies that label; do not guess labels.
4. Only when a source fallback is demonstrably appropriate, use Render `JobAggregation__SourceCategories__6060a29f-b9e2-4f82-9e58-b66133e50e7b=<existing-category-guid>` for PwC or `JobAggregation__SourceCategories__1b5ba6cd-54cd-464c-a59b-e18dca84fde4=<existing-category-guid>` for Accenture. Those values are intentionally not supplied without verified category records and role evidence.
5. The current job-source save DTO has no category field. Mappings are configuration, not an Admin → Job Sources edit field. Leave all publication settings and source activation states unchanged.

No new environment variable or migration is required by the code changes. Existing categories and configuration mechanisms are reused.

## Reconciliation and publication safety

Reconciliation still requires a complete snapshot, no provider omissions, unique nonempty identities and zero ingestion skips/failures. It uses all observed identities, so intentionally filtered known foreign postings are not interpreted as missing inventory. Partial or capped scans cannot close jobs. Source-owned external identities and existing URL/fingerprint/fuzzy deduplication remain unchanged.

No auto-publishing code, settings, review/draft/published transitions, approvals or publication workflows were changed. The selection diagnostic edits in `JobSourcePublicationPolicy` affect only geographic/import filtering metadata; its licensed-logo method and all selection decisions are unchanged. No production write, source activation, live ingestion, migration, commit, push or deployment occurred.

## Validation

- Release API build (`dotnet build ./JobPortal.API/JobPortal.API.csproj -c Release`): PASS, 0 warnings and 0 errors, normal analyzers and restore enabled.
- Normal focused-test compilation: BLOCKED by 184 existing analyzer errors. Compared with `phase3a-predeployment-normal-tests.log`, all diagnostic texts match after ignoring shifted line numbers (zero differences). The sole diagnostic in an edited file is the unchanged `WorkdayBatchIngestionTests.Detail` date-formatting helper, also present in HEAD. No new analyzer diagnostic was introduced.
- Focused execution: PASS, 360 passed, 0 failed, 0 skipped, duration 3 minutes 24 seconds. Used an explicit command-line test-only `-p:RunAnalyzers=false -p:BuildProjectReferences=false` override, not a source configuration change. Production projects were built separately with normal analyzers. No unrelated test failures occurred in this focused run; the full suite was not run.
- Focused filter: `FullyQualifiedName~Workday|FullyQualifiedName~JobSourceRunnerTests|FullyQualifiedName~JobIngestionServiceTests|FullyQualifiedName~DeloitteSourceReconciliationTests|FullyQualifiedName~ExternalJobEnrichmentTests|FullyQualifiedName~ExternalJobNormalizationTests|FullyQualifiedName~JobSourceCategoryResolverTests|FullyQualifiedName~MultiCompanyProviderTests`.
- Diff/whitespace checks: PASS. Existing untracked PwC/PTC registration files and `accenture-workday-page1.json` are preserved. No staging or commit occurred.
- Exact changed repository files: `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs`, `JobPortal.Application/Abstractions/Jobs/ExternalJobSourceSnapshot.cs`, `JobPortal.Application/Abstractions/Jobs/JobSourceRunResult.cs`, `JobPortal.Application/Features/JobAggregation/JobSourcePublicationPolicy.cs`, `JobPortal.Application/Services/JobSourceRunner.cs`, `JobPortal.Application.Tests/WorkdayBatchIngestionTests.cs`, new `JobPortal.Application.Tests/WorkdayIngestionDiagnosticsTests.cs`, and this new document.

Ready for manual review of the bounded PwC completeness correction and reporting changes. Do not present this patch as resolving Accenture's capped full inventory or proving the production rejection breakdown. Production readiness requires operator review of existing taxonomy/configuration and acceptance of the remaining Accenture coverage limitation; normal test-project compilation still has its pre-existing analyzer blocker. No deployment was performed.
