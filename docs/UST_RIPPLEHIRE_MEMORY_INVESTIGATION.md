# UST RippleHire memory investigation

Scope: backend only, based on local commit af848bc and the production evidence supplied in the request. No production database, heap dump, or Render event timeline was available. The implementation was reviewed before the separately authorized focused commit and feature-branch push. Merge and deployment remain outside this task.

## Findings before the fix

1. **Root cause identified in code:** full-run retention rather than bounded processing. RippleHireJobSourceProvider.FetchJobsAsync accumulates all detail DTOs in a List<RawExternalJob>. The runner keeps provider jobs, observedJobs (including serialized full SourceMetadataJson), normalized selected jobs, and rawJobs through the entire execution. JobIngestionService.PrepareRunAsync runs once for the full collection; RememberSavedOwned retains saved Job entities in _ownedJobs and _canonicalUrlDuplicateCache. These include descriptions and metadata. Detaching an entity from EF does not release these application references. A regression test also reproduced that Company.Jobs/Category.Jobs retain detached job objects, keeping descriptions alive through the tracked reference graph; tracking counts alone miss this retention. Publication reloads job/reference graphs and creates audit records in the same context. Existing ReleaseSavedAggregationTracking detaches only unchanged Job entities, not all saved operational/audit entities. These are concrete growth paths, but their individual byte contribution to the production OOM cannot be established without a heap trace or payload-size measurements. An exact numeric explanation of the 512 MB crossing at this point is not proven by the supplied counters.

2. **OOM versus lease expiry:** Render explicitly reports crossing 512 MB. Worker lease expiry is recorded by JobSourceRunStore.RecoverAsync when a running lease has elapsed; it is not an OOM detector. Process termination stops the heartbeat, so the reported sequence is consistent with OOM causing subsequent worker_lease_expired. Correlating exact kill/restart/heartbeat timestamps is still needed to prove the sequence for this run.

3. **Existing batching:** upstream requests are already paginated: 10 listing records per request, with a separate detail request per active job. Each JSON document is disposed; HTTP buffering is capped at 4 MiB per response. There is no single 1,213-job JSON response. The adapter nevertheless materializes the complete detailed feed before returning, making the runner see one large processing batch. RippleHire does not use the Workday batch-ingestion path. Ingestion/publication already save individual outcomes; an entire source run is not one database transaction.

4. **DbContext lifetime:** one DI scope/context spans the claimed worker execution, runner, ingestion, publisher, and checkpoint. Heartbeats and final queue updates use separate short-lived scopes. Existing item cleanup limits tracked Job entries in portions of ingestion, but application caches still retain their objects and publication/audit/vacancy tracking is not bounded at committed batch boundaries.

5. **Lease behavior:** 120 seconds; heartbeat every 20 seconds on a separately awaited task and a separate scope. Failed or rejected renewal cancels execution. Local source guard plus distributed source execution locks prevent parallel execution of the same source/run; distributed ingestion slots bound cross-source concurrency. Recovery must acquire the same source lock. No timeout increase is required.

6. **The remaining 573:** 1,213 - 586 - 54 = 573 records not accounted for in the reported successful/rejected totals. Because the original adapter fetches the whole feed and selection runs before ingestion, these were already fetched/mapped; the 54 selection rejections need not represent 54 interleaved ingestion attempts. Only if the other existing/matched/held counters are zero and these are exact final commit counts does the remainder correspond to 573 pending eligible jobs. Rejected combines non-India selection and ingestion rejections; other held or existing outcomes were not supplied. The periodic heartbeat can also lag durable job/publication commits, so the exact remaining set/count requires the complete diagnostics plus a read-only source/external-ID comparison in production. The evidence does not prove that all 573 were untouched.

7. **Existing duplicate risk:** source/external-ID lookup, creation locks, database uniqueness and canonical/fingerprint rules protect ingestion on replay. The publisher checks current status and returns AlreadyPublished for existing published jobs. A draft committed just before a crash can be published on retry. Existing RippleHire has no durable item receipts, so retry counters are per attempt and heartbeat progress can under-report committed work. The fix adds receipts for newly processed RippleHire runs; it cannot reconstruct original Created/Published counts retroactively for the already interrupted production run.

## 8. Proposed code changes

- Implement IBatchedExternalJobProvider using the unchanged public listing/detail contract, validation, pagination, delays, retries, redirects, URLs, limits, and discovery rules. Yield after five listing pages (at most 50 active detail DTOs). The normal runner consumes batches with backpressure; compatibility FetchJobsAsync callers explicitly requesting a full collection still receive one.
- Keep cumulative lightweight identity/location/workplace evidence so the existing India selection and source-wide test cap work across all batches. Drop earlier descriptions, metadata, URLs and other payload from the runner's retained selection census.
- Close each batch's ingestion caches/creation session, commit remaining writes, and detach committed job/audit/operational tracking. Preserve source/company/category reference entities for final bookkeeping, while clearing their detached Job navigation collections after commit. Reject cleanup if pending changes would otherwise be discarded. Do not use forced GC.
- Use existing JobSourceRunItems storage for RippleHire durable ingestion/publication receipts keyed in memory by external ID. Positions are allocated only as receipt storage keys; a reordered live listing cannot replay another job's receipt. Receipts join the existing ingestion and publication saves. Retry refetches the live board and safely skips acknowledged outcomes; it does not trust an unstable positional cursor. No migration is needed.
- Preserve source-run counters across receipts/batches and the existing central publication/filter/deduplication paths. Keep Workday-only execution budgets/status semantics scoped to Workday. No absence reconciliation is enabled for RippleHire.
- Add source-correlated phase and batch telemetry: fetched, mapped/filtered, preloaded, committed batch, received/created/published/rejected totals, managed-memory and process working set, tracked entry count. Add confirmed lease-renewal telemetry. No payload, token, URL, description, resume, or candidate PII is logged by the new instrumentation.

## 9. Files affected

- JobPortal.Infrastructure/Services/RippleHireJobSourceProvider.cs
- JobPortal.Application/Services/JobSourceRunner.cs
- JobPortal.Application/Abstractions/Persistence/AuthenticationRepositories.cs
- JobPortal.Persistence/Repositories/AuthenticationRepositories.cs
- JobPortal.Persistence/Repositories/JobSourceRunCheckpoint.cs
- JobPortal.API/Services/JobSourceRunWorker.cs
- JobPortal.Application.Tests/RippleHireJobSourceProviderTests.cs
- JobPortal.Application.Tests/RippleHireMemoryTests.cs
- JobPortal.Application.Tests/JobSourceDurableRunTests.cs
- docs/UST_RIPPLEHIRE_MEMORY_INVESTIGATION.md

Pre-existing appsettings/CORS changes and unrelated untracked files were left outside this task.

## 10. Test results

- Full selected regression suite: **1,181 passed, 20 skipped, 9 failed; 1,210 total**, in 20 minutes 14 seconds. All 71 RippleHire-named cases passed, including all seven new memory/recovery cases. Both Greenhouse and RippleHire heartbeat renewal/loss cases passed, as did the real adapter pagination/backpressure test.
- All nine failures reproduced on an untouched archive of HEAD af848bc (9 failed, 1 passed). The failing test-name sets match exactly: four endpoint-count assertions (expected 9, actual 10), two due-source scheduling fixtures, one provider-count assertion (expected 7, actual 9), one existing enrichment assertion, and the existing 1,500-job non-RippleHire tracking assertion. No unrelated fixes were applied.
- The 20 skipped cases are opt-in database checks without the required local PostgreSQL settings. Real database migration/lease/checkpoint behavior covered only by those cases remains unverified in this run.
- Production API build with the repository's normal analyzers: **passed, 0 warnings, 0 errors**. Final persistence rebuild, including the corrected receipt replay guard, with normal analyzers: **passed, 0 warnings, 0 errors**.
- Functional test builds: **passed, 0 warnings, 0 errors**, using RunAnalyzers=false. The normal test-project analyzer build reported 204 errors: 202 in untouched files, plus two findings in new tests that were corrected. The repository-wide analyzer gate for tests is therefore not green; analyzer policy/configuration was not changed.
- git diff --check: passed. No merge or deployment performed.

Added coverage includes one and 1,213 jobs; 25 processing batches; cumulative source-wide import caps; filtering and counters; repeat import without duplicate publication or publication timestamp changes; interrupted recovery with reordered identities; the ingestion/publication commit gap; 1,250 saved jobs across repeated EF cleanup boundaries; saved audit/entity navigation cleanup; refusal to discard pending changes; and lease renewal/loss.

Evidence artifacts in the task workspace:

- [Full regression results](../../ripplehire-memory-results/ripplehire-memory-regression.trx)
- [Unchanged HEAD comparison](../../ripplehire-memory-results/ripplehire-memory-baseline.trx)
- [Corrected recovery cases](../../ripplehire-memory-results/ripplehire-memory-recovery-verified.trx)
- [Focused import memory samples](../../ripplehire-memory-results/ripplehire-memory-verified.trx)
- [API analyzer build](../../ripplehire-memory-api-release-check.log) (Debug build; no release/deployment performed)
- [Final persistence analyzer rebuild](../../ripplehire-memory-persistence-analyzers-final.log)
- [Normal test-project analyzer diagnostics](../../ripplehire-memory-analyzers.log)

### Final review validation (2026-10-31)

- Fresh focused run: **72 passed, 0 failed, 0 skipped**, comprising 71 RippleHire cases and the Greenhouse heartbeat case.
- Fresh 1,213-record import: **25 batches, maximum 50 records per batch, 1,092 created/published, 121 filtered, three retained tracked entries**, and **222.4 MB** maximum sampled working set at commit boundaries (277.7 MB in the fresh shared broader-suite host). The one-record case also passed.
- Fresh API build and forced persistence analyzer rebuild: **passed, zero warnings/errors**. Functional test build with RunAnalyzers=false: **passed, zero warnings/errors**.
- Fresh normal test-project analyzer gate: **202 errors, all in untouched files; none in the three changed test files**.
- Fresh broader regression run: **1,181 passed, 20 skipped, nine failed; 1,210 total**, in 19 minutes 33 seconds. All 71 RippleHire cases passed again. The nine failure names match the fresh unchanged-HEAD rerun exactly; no new regression was found. [Fresh broader results](../../ripplehire-memory-results/ripplehire-final-regression.trx).
- Fresh unchanged-HEAD rerun: **nine failures and one passing theory case**, with exactly the same failure-name set as the previous broader regression run.
- The four unrelated local files were fingerprinted and excluded from the commit scope. No frontend, discovery/security, timeout, concurrency-protection, or unrelated business-rule changes are included.

Fresh evidence: [focused tests](../../ripplehire-memory-results/ripplehire-final-focused.trx), [unchanged HEAD](../../ripplehire-memory-results/ripplehire-final-baseline.trx), [API build](../../ripplehire-final-api.log), [persistence analyzers](../../ripplehire-final-persistence.log), [functional test build](../../ripplehire-final-test-build.log), [test-project analyzers](../../ripplehire-final-test-analyzers.log).

## 11. Expected memory profile and 512 MB assessment

Before: fetch grows with all raw descriptions; selection adds normalized descriptions and serialized metadata; ingestion retains saved entities/cache state; publication adds tracked state. Peak can increase throughout the run despite per-item saves.

After: full job payloads and entity caches are bounded by a processing batch (plus an in-flight detail/current batch). A small source census still grows with job count under the existing 2,000-job feed limit. Compact receipts retain external identities across attempts and can grow if a live board changes between retries, subject to the 10,000 receipt-ordinal storage guard; they do not retain full descriptions. The existing DbContext is deliberately retained for source bookkeeping, but committed per-batch entities are detached.

The synthetic 1,213-job case (8,000-character description bodies) completed 25 batches with 1,092 created/published jobs, 121 filtered records, and three tracked entries at committed boundaries. The largest sampled working set at those boundaries was 209.7 MB in the focused test host and 296.0 MB in the final shared regression host. These are boundary samples, not continuous peaks or Render measurements. A 512 MB instance should reasonably handle a normal 1,200-job feed with this structure, subject to the API's baseline memory, actual description sizes, database/provider/native buffers and configured concurrent sources. Fifty near-maximum 4 MiB responses or high concurrent application traffic could still exceed that budget. This is not a measured production guarantee; verify batch working-set telemetry in a controlled run before deployment.
