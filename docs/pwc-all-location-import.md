# PwC all-location import policy

This supersedes the India-selection and no-unknown-category-fallback policy described in the earlier `pwc-category-ingestion-verification.md`. The earlier report and its test evidence remain historical records.

## Scope and behavior

- Only ATS Workday, identifier `pwc/Global_Experienced_Careers`, and the matching HTTPS board `pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers` opt in. A supported locale prefix is allowed. Company-name matching does not authorize the exception. Other sources retain their existing geographic policies.
- PwC selection admits India, international, remote, hybrid, mixed and unknown geography without altering provider facts or manufacturing India eligibility. The Workday provider already requests all locations for PwC; its endpoints, SSRF validation, paging, stable requisition IDs, retries, pacing and budgets are unchanged.
- Existing explicit valid job categories, external mappings and job-level classification retain precedence. Unrecognized/conflicting classification or unavailable classified taxonomy falls back to an existing, unambiguous category named **Other**, then **General**. A canonical matching slug is preferred; every fallback ID is checked against the repository. No category is created and no GUID is hard-coded. An unrelated source-wide category default cannot turn an unknown PwC vacancy into IT. If neither valid fallback exists, category validation still fails closed with `PwcFallbackCategoryMissingOrAmbiguous` / `MissingCategory` diagnostics.
- Existing source/external ID and URL/fingerprint duplicate prevention is unchanged. Updated, unchanged and matched records count as Existing, never Created. No publication or logo-approval code changed; persistence continues through the existing Draft and publication pipeline.
- Provider completeness, omissions, duplicate/missing identities, validation skips and persistence failures still prevent reconciliation. An incomplete scan may persist valid records without closing missing jobs or marking the scan successful.

## Location storage boundary

The provider and selection preserve primary location, additional locations and structured country codes, including non-India codes; the normalizer preserves these raw country/additional-location collections. The existing Job entity persists primary Location and WorkplaceType. It has no structured CountryCodes or AdditionalLocations columns. This source-policy change does not add columns, infer countries from city names or fabricate missing locations. Persisting structured country/additional-location collections would require a separately scoped schema change. No migrations are required for this implementation.

## Configuration and remaining exclusions

- Keep `JobAggregation:SourceApprovals:<source-id>:TestImportLimit` at **0** for a full import. A positive configured cap is deliberately still honored and makes the scan incomplete; it reports `TestImportLimit`. No new environment variable is needed. `IndiaOnly=true` no longer filters the verified PwC board, while other sources still honor it.
- The prior read-only taxonomy lookup verified an existing General category (`df7f6710-bd28-41ea-aa97-7994c97f182a`); this implementation resolves its current ID by name/slug and verifies existence, rather than relying on that historical GUID. Ensure Other or General remains available and unambiguous.
- Review existing `JobAggregation:CategoryMappings` overrides for accuracy. Explicit mappings continue to take precedence over automatic classification.
- Existing invalid/empty listing/detail schemas, missing Workday title/description/identity, unsafe paths/URLs, exhausted requests and rate limits, invalid salary/experience, unresolved employers, invalid category IDs and persistence failures remain excluded or failed. These are integrity/operational checks, not geographic exclusions. The public source validation and canonical-link construction remain unchanged; no links are fabricated.
- Missing geography or category metadata alone is not a PwC rejection when a valid fallback exists. Unknown country metadata stays unknown.

## Counts and verification

Report `Created`, `Existing`, `IngestionRejected`, `Failed`, `Skipped`, `ProviderSkipped` and `SelectionSkipped` separately. `Skipped` is the legacy aggregate of provider omissions and ingestion rejections; selection skips are separate. Do not sum Rejected and Skipped as disjoint totals. Existing reason dictionaries and `JobSourcePersistenceSummary` remain available. Category-resolution diagnostics may explain successful fallback and must not be mistaken for rejection counts.

The production reference is historical: 3,879 fetched, 651 India-selected, 647 MissingCategory rejections, 2,867 NonIndiaLocation and 361 UnknownIndiaEligibility exclusions, with four jobs created in an earlier run. No new production scan was performed. The 3,875-new / 4-existing outcome is verified with an isolated synthetic 3,879-record snapshot, not asserted as a production result.

Release API build with normal analyzers: **PASS**, zero warnings and zero errors (`dotnet build JobPortal.API/JobPortal.API.csproj -c Release`).

The focused Workday/PwC, category/enrichment, runner and reconciliation suite: **223 passed, zero failed, zero skipped**. Normal test-project compilation is blocked by the same 184 pre-existing analyzer errors; canonicalized diagnostics have zero differences from the earlier baseline, including after the final fixture changes. Test execution uses command-line `RunAnalyzers=false` and `BuildProjectReferences=false` with the normally built Release dependencies, without changing repository analyzer settings. An initial run was stopped to correct the synthetic fixture from one oversized batch to the provider's default 100-job bounded batches. The completed suite uses that corrected fixture. The final PwC policy rerun after explicitly omitting country codes in the remote/unknown-location integration fixtures: **14 passed, zero failed, zero skipped**, including the 3,879-record scenario (41 seconds).

| Isolated regression attempt | Created | Existing | Rejected | Failed | Legacy Skipped | Provider skips | Selection skips |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Prepare four existing records, incomplete snapshot | 4 | 0 | 0 | 0 | 0 | 0 | 0 |
| Complete 3,879 unique valid records | 3,875 | 4 | 0 | 0 | 0 | 0 | 0 |
| Repeat the same 3,879 records | 0 | 3,879 | 0 | 0 | 0 | 0 | 0 |
| Six mixed India/international/remote/unknown-category records | 6 | 0 | 0 | 0 | 0 | 0 | 0 |
| Repeat those six records | 0 | 6 | 0 | 0 | 0 | 0 | 0 |
| Partial snapshot with one existing and one new record | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| Invalid title, invalid application URL and one provider omission | 0 | 0 | 2 | 0 | 3 | 1 | 0 |

The invalid-record reason breakdown is `MissingTitle=1`, `InvalidApplicationUrl=1`, `ProviderError=1`; the legacy Skipped count includes the two rejections. None of the valid attempts has a rejection/provider/selection skip reason. The partial and invalid attempts close zero jobs; missing fallback remains a deliberately failing category resolution in its separate regression. Configured test-cap coverage reports `TestImportLimit=3` for a five-record snapshot capped at two and leaves it incomplete.

Regression tests use mocked snapshots/public-detail JSON and disposable EF InMemory databases; they do not execute live Workday ingestion or write production data. New policy tests do not invoke publishing and assert persisted jobs remain Draft with no publication timestamp. The broader suite also verifies existing publication behavior against disposable fixtures. Final diff/whitespace checks passed.

The completed focused-suite command was `dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false -p:RunAnalyzers=false --filter "FullyQualifiedName~PwcAllLocationImportTests|FullyQualifiedName~PwcCategoryIngestionTests|FullyQualifiedName~Workday|FullyQualifiedName~JobSourceRunnerTests|FullyQualifiedName~ExternalJobEnrichmentTests|FullyQualifiedName~DeloitteSourceReconciliationTests|FullyQualifiedName~JobSourceCategoryResolverTests"`. The final policy rerun used the same settings with filter `FullyQualifiedName~PwcAllLocationImportTests`.

## Files changed for this request

1. `JobPortal.Application/Features/JobAggregation/PwcWorkdayImportPolicy.cs` (new)
2. `JobPortal.Application/Features/JobAggregation/JobSourcePublicationPolicy.cs` (selection only)
3. `JobPortal.Application/Services/JobSourceCategoryResolver.cs` (scoped fallback)
4. `JobPortal.Application.Tests/PwcAllLocationImportTests.cs` (new)
5. This document (new)

All pre-existing tracked and untracked work is preserved, including the earlier classifier, provider metadata extraction, counters, PwC category tests/report, and `accenture-workday-page1.json`. No commit, staging, push, deployment, source activation, production configuration change or production write is performed.
