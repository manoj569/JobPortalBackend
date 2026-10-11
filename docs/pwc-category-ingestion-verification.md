# PwC category ingestion verification — 2026-10-11

## Evidence and cause

Production evidence supplied for this task confirms that all 647 rejections were `MissingCategory`. The previous complete provider snapshot does not make the ingestion safe for reconciliation: `JobSourceRunner` additionally requires zero validation skips/failures, unique nonempty observed identities and no provider omissions. The 647 skips explain `IncompleteOrUnsafeScan`. That protection is unchanged.

Two code gaps prevent sufficient configuration-only resolution:

- Workday discarded job-level Specialism. PwC's public detail has no `jobFamily`, `jobFamilyGroup` or `jobCategory` field in the inspected sample; it includes `<b>Specialism</b>` in a labelled HTML metadata section. The inspected real India title was `IN_Manager_SAP GRC_SAP_Advisory_Gurgaon`, with Specialism `SAP`.
- The generic classifier recognizes narrow conventional role names. Underscores count as word characters for regex boundaries, so PwC's delimited titles fail otherwise useful matches. SAP consulting, tax, assurance and other business disciplines also need accurate taxonomy matches beyond software roles.

Only labelled Specialism is extracted for the validated PwC tenant. Free description keywords are never used to classify a job. Extraction is bounded and times out safely. Title underscores are replaced only for PwC classification; stored titles and external identities are preserved. Existing general classifier behavior for other companies is unchanged.

PwC-specific rules reuse existing software, data, finance, HR, security, testing and legal categories. Known consulting/advisory and explicitly identified civil/mechanical/electrical/chemical/industrial/structural engineering roles can use the existing General category when there is no more specific supported category. Strong specific matches take precedence over this General rule. Unknown roles, unknown metadata and conflicting specific evidence remain unclassified. General is not a source-wide default.

## Verified existing taxonomy

An authorized read-only public GET of `https://job-portal-lgjc.onrender.com/api/categories/lookup` confirmed these existing records:

| Category | GUID |
| --- | --- |
| Software Engineering | `10000000-0000-0000-0000-000000000001` |
| Data Science & Analytics | `10000000-0000-0000-0000-000000000002` |
| Cybersecurity | `10000000-0000-0000-0000-000000000005` |
| Human Resources & Recruitment | `10000000-0000-0000-0000-000000000011` |
| Finance & Accounting | `10000000-0000-0000-0000-000000000014` |
| General | `df7f6710-bd28-41ea-aa97-7994c97f182a` |

The lookup exposes names and GUIDs rather than slugs. Existing resolver matching supports the verified names as well as canonical slugs. No category creation, migration or production write is necessary. There is no dedicated Consulting or general Engineering entry in the returned taxonomy, which is why positively identified roles in those disciplines use General rather than IT.

## Counting and persistence

Stable source/external identities and existing URL/fingerprint/fuzzy duplicate checks remain unchanged. `Existing` combines Updated, Unchanged and Matched; a repeat source-owned import is not a new creation. Added runtime counters expose validation rejection, total ingestion rejection (including company resolution), provider skips and selection skips separately. A fixed Information-level `JobSourcePersistenceSummary` logs New, Existing, Rejected, ProviderSkipped, SelectionSkipped and Failed for each attempt. Legacy counters retain their semantics. Detailed reason tallies are runtime results/logs; this patch does not add database columns to the durable run store.

The historical 4-created/647-rejected counts describe the previous attempt, not this change. No production ingestion was performed, so new production counts cannot be reported. The regression pipeline verifies all supported representative jobs are created once, then reports zero new creations and all jobs existing with the same database IDs on repeat import.

## Before another production run

1. Review the patch and deploy through the normal process separately; this task does not deploy.
2. Keep source-wide PwC category fallback unset unless separately justified. Do not map all jobs to IT or General.
3. Review existing `JobAggregation__CategoryMappings__<specialism>` entries. Existing explicit mappings take precedence over automatic classification, so broad or inaccurate operator mappings must not override correct role-level categories. No such production configuration was read or changed here.
4. No new Render setting is required for the verified existing taxonomy. Existing mapping configuration is available for additional independently verified Specialism labels, using real existing GUIDs. Do not invent labels or categories.
5. The complete set of 651 India jobs / 647 rejected identities and their metadata was not supplied or exhaustively fetched. Exact residual unclassified counts cannot be certified from two inspected public detail responses. Validate an offline classification inventory of those exact jobs before claiming every historical rejection is resolved; genuinely unknown/ambiguous disciplines remain a blocker for zero-rejection reconciliation.
6. Preserve India selection, publication settings and all reconciliation protections. Inspect the new summary and reason logs after any separately authorized run; do not interpret a complete listing as permission to reconcile a validation-failed import.

No production jobs were written, sources activated, live ingestion triggered, auto-publishing modified, migration created, commit/push made or deployment performed. The pre-existing `accenture-workday-page1.json` remains untouched.

## Validation

- Release API build with normal analyzers: PASS, zero warnings and zero errors (`dotnet build JobPortal.API/JobPortal.API.csproj -c Release`).
- Normal test-project compilation remains blocked by 184 pre-existing analyzer errors. Canonicalized diagnostics match the recorded baseline; none identify the new PwC test file. Analyzer settings in the repository were not changed.
- The focused suite was compiled with test-only `RunAnalyzers=false` and `BuildProjectReferences=false`, using the normally built Release dependencies. It executed 209 tests: 208 passed, one failed, none skipped. All 22 new PwC cases passed, including category accuracy, source scoping, labelled metadata extraction, persistence, stable identities, repeat-run counts and blocked reconciliation after an unknown category.
- The failure was the existing `CompletePaginationKeepsOnlySuspectedSearchCapIncomplete(3879, true)` fixture: 3,878 mocked detail records returned instead of 3,879. That fixture uses a one-second request timeout and passed in earlier runs. Production timeout and completeness safeguards were not changed. An isolated sandbox rerun aborted before execution because the test host failed to connect within 90 seconds; another sandbox attempt with a 240-second startup timeout was stopped while still waiting for connection.
- The isolated rerun outside the restricted sandbox passed all 26 tests: all 22 PwC cases plus all four pagination cases, including 3,879 records and the intentionally incomplete 2,000-record case. Command: `dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~PwcCategoryIngestionTests|FullyQualifiedName~CompletePaginationKeepsOnlySuspectedSearchCapIncomplete"`. This resolves the observed pagination assertion on rerun; it does not erase the initial failed result or establish the exact cause of that intermittent failure.
- Final `git diff --check` and whitespace checks for the new test/document passed. Exact changed files: `JobPortal.Application/Services/ExternalJobCategoryClassifier.cs`, `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs`, `JobPortal.Application/Abstractions/Jobs/JobSourceRunResult.cs`, `JobPortal.Application/Services/JobSourceRunner.cs`, `JobPortal.Application.Tests/PwcCategoryIngestionTests.cs`, and this document.
- No automated test sends real requests or writes production data. Public metadata inspection was separate and read-only. The entire historical 651-job inventory is not certified by these representative tests.

Release recommendation: NO-GO for a production ingestion claiming every historical rejection resolved until the exact inventory and production mapping overrides have been audited. Normal analyzer-clean test compilation also remains blocked by the unrelated baseline diagnostics.
