# Ingestion reconciliation safety verification

Verified locally on 2026-10-11. Scope: completeness propagation and reconciliation diagnostics, with focused regression tests. No production database access, data changes, ingestion, activation, staging, commit, push or deployment.

## Defect and production evidence limits

`JobSourcePublicationPolicy.Select` previously returned `snapshot.IsComplete && !held && !limited`. Any geography decision of `Review` set `held=true` (`AmbiguousLocation`, `ContradictoryLocation`, or `UnknownCountry`). Consequently the runner could expose `ScanComplete=true` from the provider while checking a **different**, selected snapshot with `IsComplete=false` for reconciliation. `canReconcile=false` then caused `Succeeded=false` and `IncompleteOrUnsafeScan`, despite every upstream detail being read successfully.

A geography hold does not establish an incomplete upstream inventory. The runner retains every parsed upstream job in `observedJobs`, and the reconciliation ID list comes from that full inventory, including foreign and held jobs. Their existing source-owned records are protected against false absence. Geography decisions still determine which jobs may be ingested/published; they do not authorize closure of observed vacancies.

The supplied PTC counters are reproducible with this defect. However, `SelectionSkipped=132` alone does not identify the historical guard: a configured positive test cap also sets the selected snapshot incomplete. The historical `DiagnosticsJson` for source `cff65546-9161-4103-b013-0d170bf6a690`, run `8d3e3841-0698-4ed1-93cf-c307947ff984` has **not** been independently retrieved. The authorized read-only GET to `https://job-portal-lgjc.onrender.com/api/admin/job-sources/cff65546-9161-4103-b013-0d170bf6a690/runs/8d3e3841-0698-4ed1-93cf-c307947ff984` returned HTTP 401. No authorized credential was available; no token was requested or exposed, and no database credentials were used. An authorized administrator can inspect the existing response's `diagnostics.SelectionReasonCounts`, `EligibilityReasonCounts`, `Skipped`, `ScanComplete`, and `ReconciliationSafe` (HTTP JSON uses camelCase). Existing rows will not acquire the newly added blocking reasons retroactively.

## End-to-end checks

1. `WorkdayJobSourceProvider`: validates listing totals/counts, raw slots, unique paths, placeholders, repeated pages, omitted-total anchor consistency, suspected search cap, detail success and unique external IDs. Only the final batch certifies the entire scan. Provider code is unchanged.
2. `JobSourceRunner`: accumulates all observed jobs and provider skips. Selection is cumulative across batches. Native completeness is retained separately for `ScanComplete`.
3. `JobSourcePublicationPolicy`: India selection remains unchanged. Foreign and held jobs are excluded from ingestion; optional test caps remain unsafe, even when fewer jobs than the configured cap are eligible. Only the geography/completeness coupling is removed.
4. Runner reconciliation requires native provider completeness, selected snapshot completeness, zero snapshot skips, every observed job having a nonblank unique trimmed external ID, zero total skips and zero failures (including publication failures). Complete-provider run success uses the same gate. Partial results remain durable and resumable; prior successful timestamps are preserved on failure.
5. `JobRepository.CloseSourceJobsMissingFromSnapshotAsync`: receives **all observed IDs**, not just selected jobs. It stages closure only for nondeleted, nonclosed records belonging to that source and absent from the census. Final save commits closure and source success bookkeeping together. Cancellation and execution fencing remain unchanged.
6. `JobSourceRunWorker`: maps runner `Succeeded` directly to durable `Succeeded`/`Failed`; it does not override native completeness. Its final progress snapshot contains the runner result.
7. `JobSourceRunStore`: heartbeat and finish serialize the complete counter result to existing `DiagnosticsJson`. Finish uses the supplied status and stores `source_run_failed` for a failed run; source `LastError`, failure count and last-success timestamp follow the runner result.
8. `JobSourceRunResponse.From`: top-level success comes from the persisted run status; diagnostic counters deserialize from JSON. A provider-complete historical failed row remains correctly reported as failed.

## Generic correction and retained blockers

Selection completeness is now `snapshot.IsComplete && !limited`. No company special case, India policy change, provider pagination change, ID change, duplicate-rule change or publication change is introduced.

Additive `ReconciliationBlockReasons` diagnostics and corresponding structured reconciliation logs identify final guard failures:

| Reason | Guard |
| --- | --- |
| `ProviderSnapshotIncomplete` | Provider lacks final complete certification (including pagination/detail/snapshot anomalies) |
| `SelectionSnapshotIncomplete` | Native census complete but selection invalidates completeness, including a positive test cap |
| `ProviderSkippedItems` | Missing snapshot or nonzero provider skipped count |
| `IncompleteOrDuplicateSourceIdentities` | Missing or duplicate observed external IDs, including filtered jobs |
| `SkippedItems` | Nonzero cumulative provider/ingestion skips |
| `FailedItems` | Nonzero persistence/publication failures |

Reasons may overlap (provider skips also contribute to total skips). They describe guards, not additional job counters. Heartbeats before the final gate do not certify reconciliation. Provider/infrastructure exceptions still use the existing failed-run path and may lack a final guard evaluation. Historical JSON missing the new property defaults to an empty list; historical safety/status is not inferred from that list.

## Regression verification

- New complete-census test runs for Workday, Greenhouse, Lever, Ashby and SuccessFactors with real ingestion, repository and publication services against isolated EF in-memory databases.
- Reproduces 176 observed identities, zero new jobs, 44 existing eligible jobs, 132 selection skips (88 foreign, 44 held), zero ingestion/provider skips/failures, zero new publication, complete/safe/successful result. This is a synthetic fixture, not a claim about production selection breakdown.
- An observed source-owned job now held for geography remains open. A genuinely absent source-owned job closes. Every existing identity retains its database GUID. A repeated run creates/publishes/closes nothing further.
- Native incomplete census, provider detail skips, duplicate and missing IDs in filtered jobs, test cap without exceeding it, invalid eligible data, and persistence failure all block closure and preserve the previous last-success timestamp.
- Existing multi-provider geography tests retain their inclusion expectations while treating a native complete census as complete after selection.
- Existing batched Workday diagnostics test now includes a geography hold and checks native completeness, safe reconciliation and noninflated repeated-import counts.
- JSON round-trip tests cover successful/failed status responses and backward compatibility with historical diagnostics without the new property.
- Existing provider regressions cover changed totals/anchors, repeated/placeholder pages, early empty pages, bounded retries/timeouts, failed details, exact-total/terminal pagination, and unsafe reconciliation.

- Release API build: **PASS, 0 warnings, 0 errors**, normal repository analyzers; final build elapsed 57.63 seconds. Command: `dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`. Evidence: parent-workspace `reconciliation-api-release-build.log`.
- Normal test-project analyzer build: **BLOCKED by the same 184 pre-existing diagnostics**. Final comparison against `workday-snapshot-tests-normal-analyzers.log`, grouped by file/rule/message with occurrence counts, has zero added/removed diagnostics. One existing diagnostic location shifts with the geography-test comment. Breakdown: CA1707=126, CA1861=29, CA1869=12, CA1859=4, CA1305=3, CA1725=1, CA1826=1, xUnit2031=7, xUnit2030=1. No new diagnostics were introduced. These unrelated issues were not fixed. Evidence: `reconciliation-tests-normal-analyzers-final.log`, `reconciliation-analyzer-comparison.json` in the parent workspace.
- First focused execution: 192 passed / 6 failed / 0 skipped. All 13 new safety regressions passed; the six failures were existing geography cases asserting the superseded completeness coupling. Their job-inclusion expectations were retained, and only census-completeness expectations were corrected.
- Final focused execution: **PASS, 198 passed / 0 failed / 0 skipped**, elapsed 2m11s. Evidence: parent-workspace `reconciliation-focused-tests-final.log` and `reconciliation-test-results/reconciliation-focused-final.trx`. Runtime tests use `-p:RunAnalyzers=false -p:BuildProjectReferences=false`, leaving normally built API references and repository analyzer configuration intact. No benchmarks, live database tests, real emails or public ingestion runs are included.
- Focused filter covers `WorkdayBatchIngestionTests` (except `MeasureSyntheticWorkdayPipelineWithRealRepositories` throughput benchmarks), `WorkdayJobSourceProviderTests`, `WorkdayIngestionDiagnosticsTests`, `JobSourceReconciliationSafetyTests`, `JobSourceRunnerTests`, `GenericIndiaJobIngestionTests`, `MultiCompanyProviderTests`, `JobAggregationPipelineTests`, `PtcWorkdaySourceRegistrationTests`, and `PwcWorkdaySourceRegistrationTests`.
- `git diff --check`: PASS. No staged changes.

## Exact files changed

- `JobPortal.Application/Features/JobAggregation/JobSourcePublicationPolicy.cs`
- `JobPortal.Application/Services/JobSourceRunner.cs`
- `JobPortal.Application/Abstractions/Jobs/JobSourceRunResult.cs`
- `JobPortal.Application.Tests/WorkdayIngestionDiagnosticsTests.cs`
- `JobPortal.Application.Tests/MultiCompanyProviderTests.cs` (geography census expectations only)
- `JobPortal.Application.Tests/JobSourceReconciliationSafetyTests.cs` (new)
- `docs/ingestion-reconciliation-safety-verification.md` (new)

Pre-existing untracked `JobPortal.Application.Tests/TestResults/`, `accenture-workday-page1.json` and `generic-india-migration.sql` are preserved. Build/test evidence is kept in the parent workspace.

## Deployment requirements and operational limits

- Deployment of the rebuilt API/worker assemblies is required for production behavior to change; it has not been performed.
- **No database migration**, backfill, new endpoint, new environment variable or source activation is required. The diagnostics property uses the existing JSON column.
- Check the existing PTC configuration `JobAggregation__SourceApprovals__cff65546-9161-4103-b013-0d170bf6a690__TestImportLimit`. A normal full run needs an absent/zero limit; any positive value intentionally continues to block reconciliation. This task does not alter production configuration or disable this protection.
- Retrieve the historical diagnostics through an authorized administrator before claiming its exact guard has been confirmed. Geography-only rejection/hold must not block a future fully certified safe census; genuine missing IDs/details, anomalies, ingestion skips/failures or an import cap still must block it.
- A future approved run with the new deployment will write fresh diagnostics and normal source success bookkeeping. The existing failed historical run is not rewritten or automatically reclassified.
- No production import or deployment is authorized or executed by this verification task.
