
# Job aggregation: filter contracts and safe activation

## Scope and defaults

The JobSource pipeline registers Greenhouse, Lever and Ashby. Adzuna belongs to the
separate JobDiscovery pipeline; it does not implement `IExternalJobProvider`.
No schema changes or historical data backfill are part of this change.
`JobAggregation:Scheduler:Enabled` and `JobAggregation:AutoPublishEnabled` remain false.

## Provider audit

The following describes the fields mapped by the current adapters, not inferred data.

| Field           | Greenhouse                                             | Lever                                       | Ashby public job board                              |
| --------------- | ------------------------------------------------------ | ------------------------------------------- | --------------------------------------------------- |
| External ID     | `id`, numeric/numeric string                         | `id`                                      | Not in documented public contract; no fabricated ID |
| Title           | `title`                                              | `text`                                    | `title`                                           |
| Company         | Configured JobSource company                           | Configured JobSource company                | Configured JobSource company                        |
| Location        | `location.name`                                      | `categories.location`                     | `location`                                        |
| Description     | `content`, HTML extracted as text                    | `descriptionPlain`                        | `descriptionPlain`                                |
| Application URL | `absolute_url`                                       | `hostedUrl`                               | `jobUrl`                                          |
| Category        | Single`departments[].name`                           | `categories.department`, else team        | `department`, else team                           |
| Employment      | Unknown                                                | `categories.commitment`                   | `employmentType`                                  |
| Workplace       | Unknown                                                | `workplaceType`                           | `workplaceType`                                   |
| Experience      | Not mapped                                             | Not supplied by current contract            | Not supplied by current contract                    |
| Education       | No approved metadata mapping                           | Not supplied by current contract            | Not supplied by current contract                    |
| Salary          | Optional pay-transparency ranges not requested/mapped  | API has optional`salaryRange`; not mapped | Optional compensation data not requested/mapped     |
| Expiry          | Detail`application_deadline`, explicit offset/Z only | None                                        | None;`publishedAt` is not expiry                  |

Salary mapping is deliberately not added: the current raw contract has no currency
or pay-period representation. Copying hourly/annual/non-USD amounts into Job's existing
default currency would misrepresent compensation. That needs a separate contract decision.
Arbitrary Greenhouse metadata is not assumed to represent employment or education.
Category precedence stays explicit ID, configured external mapping, then source fallback.
Ashby only includes explicitly listed posts (`isListed=true`).

Contracts: [Greenhouse](https://docs.greenhouse.io/job-board.html),
[Lever](https://github.com/lever/postings-api),
[Ashby public posting API](https://developers.ashbyhq.com/docs/public-job-posting-api).
Ashby's authenticated API is not the public endpoint used by this pipeline.

Adzuna's separate adapter maps ID, title, company/category labels, redirect URL,
location, description, contract type and created timestamp into `ExternalJobCandidate`.
It does not provide this pipeline with workplace, experience, education, salary or expiry.

### Provider failures and request volume

Malformed optional record fields are treated as absent; remaining records survive.
Invalid list envelopes or exhausted list failures fail the source, not other sources.
Optional Greenhouse detail HTTP errors, malformed JSON, transport failures and timeout
exhaustion leave expiry null. Caller cancellation still propagates.
Greenhouse costs one list GET plus one sequential detail GET per usable ID/title.
For N usable records, nominal request count is 1+N; the existing HTTP handler permits
up to three attempts per GET, uses 10-second attempt timeouts, and respects Retry-After
with bounded waits/provider cooldown. No added parallel detail fan-out or new retry layer.
An outage can still lengthen a large scan; measure duration and provider rate limits.
Lever/Ashby use one list request in the current implementation.

## Canonical candidate filters

Normalization applies to new ingestion only; duplicate refresh does not rewrite curated fields.

| Filter     | Stored representation and query contract                                                                                                                                                                                                                                                                             |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Location   | Whole-value alias map plus whitespace/comma normalization. Facets group by trimmed lowercase stored value; multi-select uses exact membership. Legacy single`Location` keeps substring matching. No city/state inference.                                                                                          |
| Workplace  | Existing enum (OnSite/Remote/Hybrid), explicit enum wins; only unambiguous source labels mapped. UI`Other` is not a new backend enum.                                                                                                                                                                              |
| Employment | Existing six-value enum; known structured aliases only. Ambiguous phrases stay unknown.                                                                                                                                                                                                                              |
| Experience | Nullable numeric bounds; inclusive overlap. Missing one bound uses the known bound as a point; both missing fail numeric filters. 10+ means min=10, max omitted. Adjacent buckets can both match boundary jobs by design. Negative values reach ingestion's rejection guard.                                         |
| Salary     | Nullable amounts, inclusive overlap with known-bound fallback. Existing query has no currency dimension; this is not currency conversion.                                                                                                                                                                            |
| Education  | Exact stored-string/facet matching. Clearly equivalent whole aliases canonicalize to B.E., B.Tech, B.Sc; SSC/HSC/ITI/Diploma/Graduate casing is stabilized. Bachelor's, Degree, Any Graduate and complex requirements remain distinct. Legacy records are not backfilled. Clients should send returned facet values. |
| Freshness  | `PublishedAtUtc >= UTC now - FreshnessDays`, supported windows 1/3/7/15/30.                                                                                                                                                                                                                                        |

All predicates remain IQueryable/database-side. No `AsEnumerable` or pre-filter materialization.
Location facets round-trip to the jobs represented; education source aliases store identical
canonical strings. Company/category, multi-enums, internship duration, sorting and pagination
retain existing behavior. Page size stays 1–100; offset multiplication overflow is rejected.
Tests compile combined PostgreSQL SQL with `ToQueryString` without connecting to a database.
They do not replace a production-database verification.

## Publication and admin review

Provider -> normalize -> category resolution -> URL/fingerprint/fuzzy dedup (creation lock)
-> persist new Draft -> `JobAutoPublishService` -> feature flag -> `JobQualityGate`
-> only Eligible invokes existing `IJobService.PublishAsync` -> existing validators,
reference checks, future expiry, publication state change and audit -> candidate search.

When the flag is off the service exits before quality evaluation; Draft creation still occurs.
When on, NeedsReview and quality-Rejected jobs remain Draft. Invalid ingestion records may
be rejected before any Job is created. Duplicates are never sent to auto-publish; only
aggregation timestamps/missing fingerprint metadata are refreshed.

`GET /api/admin/jobs/{id}/quality` is Administrator-only and read-only. It computes current
quality/reason codes even when auto-publish is off. It is not a historical assessment and
Eligible is not a guarantee that all publication validators will pass. Existing admin list,
detail, update and `POST /api/admin/jobs/{id}/publish` remain the review workflow.
No publish-all endpoint or validator bypass is added.

**ExperienceLevel publication safeguard:** `JobService.PublishAsync` requires a valid
ExperienceLevel enum. None of the current three ATS adapters supplies a trustworthy
ExperienceLevel value, so the quality gate classifies those jobs as NeedsReview rather than
allowing them to reach automatic publication. No ExperienceLevel is fabricated and the
existing publication validator is not weakened. Admins can review and supply a valid value
before publishing, or future provider mappings may populate it only from trustworthy
structured source data.

## Run observability and scheduler

Existing ingestion counters remain TotalReceived, Created, Matched/ExistingDuplicate,
Skipped/Rejected and Failed. Rejected remains the existing alias of Skipped for compatibility.
New independent publication counters: Published, NeedsReview, QualityRejected,
PublishFailed, AutoPublishDisabled; `QualityReasonCounts` carries gate reasons.
`AutoPublishFailed` distinguishes publication failure from persistence errors.
Created and Failed can overlap: the Draft was saved before publication failed. Do not sum
all counters as mutually exclusive buckets. Disabled means not assessed, not Eligible.
With no auto-publish service registered, publication counters are zero/not assessed.

Run responses expose counts/reasons; runner logs structured counts and reason codes without
source payloads, exception text or credentials. Existing manual-run audit metadata now includes
publication counters. Scheduled-run totals are in logs, not a new persistent history table.
Use the read-only quality endpoint for current Draft diagnosis after the run response is gone.
Ingestion/job-discovery's existing rejection tracking is not duplicated or migrated.

The existing hosted service waits after each batch. Defaults: poll 60 seconds, batch 25,
maximum concurrent sources 3; options validate bounds. Each source has its own DI scope.
The process guard and PostgreSQL session advisory lock are shared by scheduler/manual runs
and source edits; due state is rechecked after locking. Creation locks handle cross-source
dedup races. Direct/session-capable PostgreSQL is required; known Neon pooler hosts are rejected.
Cancellation unwinds guards/scopes and interrupts retry delays. Source/item failures are isolated.
No second scheduler, Redis, new locks or scheduler activation is included.

## Manual production activation checklist (later, not executed here)

1. Review validation safeguards first, especially ExperienceLevel handling and any failing
   regression fixtures. Review and deploy the validated commit through the normal process.
   No migration is required for this change. Do not enable flags simply because a build passes.
2. Keep both flags false initially. Confirm source category mappings, active company/category
   references, provider identifiers and direct PostgreSQL session support. Run the existing
   approved `job-aggregation-lock-test` manually before scheduler activation; protect credentials.
3. Start with one explicitly approved active canary source; keep other sources inactive as an
   operator decision. Set `JobAggregation__Scheduler__Enabled=true`,
   `JobAggregation__Scheduler__BatchSize=1`,
   `JobAggregation__Scheduler__MaxConcurrentSources=1`, and a conservative poll interval
   (for example 60 seconds). Keep `JobAggregation__AutoPublishEnabled=false`. Restart/redeploy
   so the IOptions-backed hosted service receives these settings.
4. Inspect first-run counts, errors, Draft records and `/api/admin/jobs/{id}/quality`.
   Verify source expiry, canonical facets and category mapping; verify duplicates do not create
   new jobs or overwrite curated data. Watch detail-request latency/429s and advisory-lock contention.
5. Only after the publication safeguards and tests are verified, separately approve
   `JobAggregation__AutoPublishEnabled=true` and restart/redeploy for that same limited canary.
   This applies ONLY to newly created jobs. Existing Draft duplicates will not auto-publish on
   rerun; review/publish them individually through the admin endpoint, never delete to force reingestion.
6. Verify a genuinely source-complete NEW job reaches PublishAsync, audit and candidate search.
   Review Published, NeedsReview, QualityRejected, PublishFailed, Failed and reason tallies;
   inspect filters and public cache TTL (automatic publication does not evict controller cache tags).
   A low/zero publication count is valid for incomplete source data.
7. Increase active sources, batch size and concurrency gradually within existing bounds and
   provider limits. BatchSize counts sources, not jobs. Measure throughput and scan duration
   before aiming for hundreds of jobs/day. Roll back by disabling AutoPublishEnabled first,
   then Scheduler.Enabled and restarting; already published jobs need normal admin review.

No production endpoint, database, scheduler or provider job feed was invoked during implementation.
