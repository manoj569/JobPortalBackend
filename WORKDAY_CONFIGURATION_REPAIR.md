# Generic Workday configuration repair — 14 October 2026

## Root cause found in code, and production evidence still needed

At starting commit `a81e6e1`, admin source validation checked that a Workday ATS identifier was non-empty, but did not validate its relationship to the career URL. `WorkdayJobSourceProvider.ValidateSource` then required that value to equal the URL-derived `tenant/site`. Matching tenant-only or site-only metadata was rejected before HTTP initialization. Admin source creation could therefore succeed, `/run` could return 202, and the provider could immediately throw InvalidOperationException.

The original mismatch throw is at line 402 of `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs` in that starting commit. It is invoked at line 204, before `clients.CreateClient` at line 219. Other original configuration throws are lines 363 (initial URL/provider/identifier checks), 369 (hostname), 389 (root path), and 394 (site characters). Invalid global fetch settings also throw before HTTP. The shared settings cannot by themselves explain a source-specific failure while another source runs successfully, without additional evidence of configuration changes.

The regex already accepted any numeric `wd` suffix, including wd504, and site case was already preserved. There was no company-specific branch in configuration parsing/endpoint construction. This repair does not claim that wd504 or Airbus casing were formerly unsupported.

**The exact saved Airbus/Walmart records were not available.** The question requesting their `atsIdentifier` and `careerPageUrl`, and J&J's corresponding fields, remains unanswered. Consequently, the metadata mismatch is a confirmed code defect and a reproduced supported input case, but attribution of the reported production failure to that particular throw branch is not yet confirmed. Canonical `ag/Airbus` and `walmart/walmartexternal` with the supplied root URLs already satisfy the original guard. If production already stores those exact configurations, further investigation of its saved fields/settings and enhanced logs is required; it would be incorrect to claim this identifier mismatch explains those rows.

J&J's successful 1,200-detail execution proves its configuration passed initialization and the existing HTTP contract worked. This patch preserves its canonical target and request behavior. It does not claim to have read its production source row or performed a live ATS synchronization.

## Execution trace

1. Administrator POST `/api/admin/job-sources/{id}/run` calls JobSourceRunService.EnqueueAsync.
2. The source is loaded, authorization/activity checked, and the existing durable store queues the run. This patch also validates Workday configuration before queue acceptance, returning a specific safe error for invalid metadata instead of accepting a predictably failing run.
3. JobSourceRunWorker reloads the source in its scope, acquires existing guards, binds its checkpoint and calls JobSourceRunner.RunAsync.
4. The runner persists the attempt and selects the registered IExternalJobProvider by AtsType.
5. Its batch dispatch reaches WorkdayJobSourceProvider.FetchBatchesAsync → producer → FetchSequentialBatchesAsync.
6. Workday fetch settings and source configuration are validated. Initialization emits sanitized structured diagnostics.
7. Only then is the named Workday HTTP client created, and ReadListingAsync issues the first POST listing request.

SourceMappingMissing is not used to reject provider initialization. Geographic selection, normalization, persistence and publication occur in their existing stages. The source-only configuration validation does not require a category mapping.

## Code changes

- `JobPortal.Application/Features/JobAggregation/WorkdaySourceConfiguration.cs`: one shared pure parser derives Host, Tenant and the exact CareerSite from the trusted career URL; constructs the same CXS root/listing endpoint; accepts canonical tenant/site, matching tenant/site shorthand, or omitted redundant metadata; rejects conflicting explicit values and unsafe URLs.
- `JobPortal.Application/Features/JobAggregation/JobSourceManagementValidators.cs`: validate Workday metadata during create/update; other ATS behavior stays unchanged.
- `JobPortal.Application/Features/JobAggregation/JobSourceManagementService.cs`: persist normalized tenant/site during an explicit source save.
- `JobPortal.Application/Features/JobAggregation/JobSourceRunService.cs`: reject invalid stored Workday configuration before enqueueing, with a safe specific code.
- `JobPortal.Infrastructure/Services/WorkdayJobSourceProvider.cs`: reuse the shared parser and retain WorkdayTarget compatibility; add safe initialization/fetch-settings diagnostics. Existing listing, detail, retry, filtering, pagination, batching, slicing and checkpoint logic remain intact.
- `JobPortal.Application.Tests/GenericWorkdayConfigurationTests.cs`: 41 new regression cases.
- `JobPortal.Application.Tests/SourceAutoPublicationConfigurationTests.cs`: the Workday create fixtures now use real-shaped Workday metadata under the new save-time validation. Other provider fixtures and publication assertions are preserved.
- `WORKDAY_CONFIGURATION_REPAIR.md`: this report.

No company-specific source validation was removed because none existed there. No company-specific production code was added. Named company source registrations remain separate existing configuration entries.

## Security and diagnostics

Host validation uses the exact structure `tenant.wd<number>.myworkdayjobs.com`, not a company or region-number allowlist. HTTPS and the standard HTTPS port are required. Arbitrary/fake domains, credentials, query/fragment components, IP hosts, unsafe paths, encoded separators and dot-segment traversal are rejected. The raw path is checked before Uri normalization can conceal traversal. Locale/site roots remain supported. Hostnames are normalized; career-site case comes from the configured URL and is preserved.

The existing transport still checks public resolved addresses and disables redirects/proxying. No SSRF transport protections were relaxed.

Logs expose SourceId, company display name as SourceName, Provider, validated Host/Tenant/CareerSite, Stage, ErrorType, ReasonCode and a fixed safe diagnostic message. Failure events are `WorkdayProviderFailure`; initialization stages include ConfigurationValidation and FetchSettings. Codes include WORKDAY_HOST_INVALID, WORKDAY_TENANT_MISSING, WORKDAY_CAREER_SITE_MISSING, WORKDAY_ENDPOINT_INVALID and WORKDAY_CONFIGURATION_INVALID. Raw rejected identifiers/URLs and arbitrary exception text are not logged or persisted. Messages are controlled validation messages, so credentials, query tokens and userinfo cannot leak through exception text.

## Configuration administrators should use

The existing fields suffice; no new Host/Tenant/CareerSite database columns or frontend contract are introduced. Use AtsType=Workday (existing numeric enum value 5), the public career-site root and the canonical identifier:

| Site | CareerPageUrl | AtsIdentifier | Listing endpoint |
|---|---|---|---|
| J&J | `https://jj.wd5.myworkdayjobs.com/JJ` | `jj/JJ` | `https://jj.wd5.myworkdayjobs.com/wday/cxs/jj/JJ/jobs` |
| Airbus | `https://ag.wd3.myworkdayjobs.com/Airbus` | `ag/Airbus` | `https://ag.wd3.myworkdayjobs.com/wday/cxs/ag/Airbus/jobs` |
| Walmart | `https://walmart.wd504.myworkdayjobs.com/walmartexternal` | `walmart/walmartexternal` | `https://walmart.wd504.myworkdayjobs.com/wday/cxs/walmart/walmartexternal/jobs` |

Example fields within the existing complete admin create/update payload:

```json
{
  "careerPageUrl": "https://ag.wd3.myworkdayjobs.com/Airbus",
  "atsType": 5,
  "atsIdentifier": "ag/Airbus"
}
```

Include the existing required company/source settings when using POST/PUT. Matching shorthand such as `Airbus` or `ag` is safely normalized to `ag/Airbus`; omission derives the complete identity from the validated URL. Prefer explicit tenant/site to make the stored configuration obvious. A contradictory value such as `other/Airbus` is rejected rather than silently overriding explicit configuration.

## Tests and build results

| Verification | Result |
|---|---|
| Standard solution Release build, analyzers enforced | Failed: 186 existing test-project analyzer errors; no new configuration-file errors |
| API Release build | Passed: 0 warnings, 0 errors |
| Diagnostic solution Release build with invocation-only TreatWarningsAsErrors=false | Passed; analyzers remain enabled; incremental build |
| Broad provider/source/Workday resilience and batch regressions | 348 passed, 1 failed, 0 skipped; 349 total, about 4 minutes |
| Final focused configuration/source/provider regressions | 262 passed, 0 failed, 0 skipped |
| New configuration cases | All 41 passed |
| git diff --check | Passed |

The new tests cover exact J&J/Airbus/Walmart endpoints; site case; wd1/2/3/5/10/12/504 plus an arbitrary larger number; canonical/shorthand/omitted identity normalization; malformed/fake/non-HTTPS/IP hosts; nonstandard ports; credentials; missing tenant/site; path traversal; conflicting metadata; safe logs before HTTP-client creation; canonical admin persistence; and rejection of an invalid stored source before a run is queued. Mocked successful listing/detail responses for all three sites verify POST/JSON fields (appliedFacets, limit=20, offset, searchText), Accept/Accept-Language, GET detail paths and the retained non-authoritative snapshot behavior.

The broad failure is `WorkdayBatchIngestionTests.TestCapIsGlobalAcrossBatchesAndNeverClosesStaleJobs`: the unchanged runner reports Succeeded=true for a capped validation run. The test still observed exactly one created job and no closures. JobSourceRunner, selection policy and publication logic were not modified by this task. This failure is retained in the report and logs, not excluded or fixed by changing run-status/slicing/publication policy. Its starting-commit runtime baseline was not independently rerun.

The final focused run checks the finished assertion set and shared source/provider contracts; it does not turn the earlier broad run into a green result. Complete-solution runtime tests were not repeated: the preceding full-suite attempt was already incomplete after several minutes with 96 reported failures, and this broader Workday run also demonstrated a retained failing assertion. No full-suite success is claimed. No live ATS requests or PostgreSQL/Neon integration tests were performed.

Evidence in the parent workspace:

- `workday-configuration-build.log` — standard build/analyzer failure.
- `workday-configuration-api-build.log` — API Release success.
- `workday-configuration-diagnostic-build.log` — diagnostic solution build.
- `workday-configuration-tests.log` and `workday-configuration-results/workday-configuration.trx` — broad run with its failure.
- `workday-configuration-final-tests.log` and `workday-configuration-results/workday-configuration-final.trx` — final focused run.

## Existing-source updates and deployment boundaries

No migration required, and no database/default/model changes were made. Existing canonical J&J sources need no configuration change. Legacy matching shorthand is tolerated at runtime and becomes canonical only when an administrator explicitly saves that source. Conflicting or unsafe metadata requires an individual source correction through the existing PUT endpoint; use the same source ID. No mass normalization or production data update was performed.

The derived target identity retains the existing origin/tenant/site values for canonical sources, so this change does not deliberately reset their checkpoints or jobs. AutoPublishEnabled, India eligibility, fresher/intern filtering, source ownership, deduplication, category mapping and reconciliation safeguards are unchanged. No frontend edits, commit, push or deployment were performed.

The unresolved production check is still to obtain Airbus/Walmart/J&J's saved careerPageUrl and atsIdentifier values, compare them with this table, and identify the precise original branch. Enhanced server diagnostics make that distinction visible without exposing raw sensitive exceptions in run records.
