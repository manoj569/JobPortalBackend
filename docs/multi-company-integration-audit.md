# Multi-company integration audit — 2026-10-09

Historical audit: its mandatory publication-approval checks have since been removed. See the
[current optional source-metadata behavior](multi-company-job-providers.md#update-optional-source-metadata-not-an-approval-gate).
The other safety, expiry, Saved Jobs and referral fixes remain in effect.

This follow-up preserves the existing local provider implementation. It does not activate sources, import live jobs, assert partnerships, or grant republication/logo rights. No production/unknown database was accessed.

## Confirmed defects fixed

- `/api/candidate/saved-jobs` discarded company ID/logo from the existing dashboard SQL projection. Its existing flat record now adds `companyId` and nullable `companyLogoUrl`, retaining all prior fields. `/api/dashboard/saved-jobs` keeps the existing nested `job` contract. Both use `Company.LogoUrl`; no extra per-job query or logo guessing.
- Approval was checked before a run but not by the final `JobService` publication authority, and could expire during a long import. Source-owned publication, public edits, featuring and unhiding now validate the matching active source and current approval. Source metadata is scoped/cached, not the permission decision. Manual/referral jobs without source ownership retain their rules. The runner checks permission before each item and before reconciliation/final success; previous per-item commits survive an interrupted/failed run. Hiding, closing, archiving and unpublishing remain available without renewed publication rights.
- A blank configured reviewed location could match a blank job location. It no longer establishes India eligibility.
- Public referral repository results could include jobs without a publication timestamp. Eligibility now matches the public job visibility rule, with one `TimeProvider` instant for count/page expiry checks. Tie-breakers make pending/referrer pagination deterministic; referrer page-offset overflow is rejected before querying.
- Public/Saved/referral totals now explicitly exclude soft-deleted required company/category references, matching the projection's global-filter joins rather than reporting invisible rows in the count.
- A source-owned update with missing deadline previously erased existing expiry, potentially making an expired published row visible again. It now retains existing expiry unless the provider supplies a valid explicit UTC deadline. New jobs still receive no fabricated expiry; missing metadata does not revive expired jobs.
- `/api/jobs/referrals` did not exclude inactive/deleted referrers. Its SQL filter now does. Shared public job summaries exposed private referrer names even though the dedicated referral API uses `Employee Referrer`. Summaries retain the existing fields but use that generic label and omit referral metadata for unapproved/inactive referrers. Contact authorization is unchanged.

## Existing pipeline reviewed and retained

Greenhouse, Lever and Ashby implement complete snapshots with stable provider IDs and official application links. Greenhouse validates `meta.total`, excludes prospect posts and requires successful identity-matched deadline details for strict imports. Lever enumerates 100-record pages to a terminal page, rejects repeats and enforces the 20,000-record safety limit. Ashby requires recognized version/visibility, excludes unlisted posts and obtains the UUID from the official board-owned job URL. Missing deadlines are not invented; existing quality/publication rules require manual review rather than unattended publication with fabricated expiry.

The generic SuccessFactors adapter and Deloitte fixtures remain unchanged. Complete pagination/detail checks, bounded detail concurrency, spacing, public-IP DNS-pinned transport and sanitized failure codes remain intact. GET clients retain bounded attempt timeout/retry/body size, cancellation and 429 cooldown; TLS validation is not weakened and redirects are not followed. No new provider/network mechanism was added here.

The runner retains the observed identity set before geographic filtering. Incomplete/capped/failed/cancelled snapshots cannot close unseen jobs or advance successful-run bookkeeping. Ingestion preloads owned identities/canonical URL matches, caches run reference data, rechecks creation under existing locks and preserves per-item durable saves. Source-owned updates reuse `(JobSourceId, ExternalJobId)`; cross-provider canonical URL/fingerprint/fuzzy matches do not seize manual/source ownership or overwrite curated business fields. Heuristic fingerprint/fuzzy matches are not proof of distinct open seats, so feed IDs must not be marketed as a unique-vacancy count.

Durable runs retain PostgreSQL active-source uniqueness, lease-owner fencing, heartbeat, source execution locks, bounded interrupted retry/cooldown and separate cleanup scope. Admin POST remains HTTP 202 with the existing status URL. Scheduler-disabled manual execution remains supported subject to approval. No in-memory queue or synchronous HTTP import was introduced.

Public job/referral filtering/counting/paging and ordering remain server-side. `/api/jobs/referrals` supports the existing public job search/filter query; `/api/referrals/public` remains the compatible page-only card endpoint. The latter's bounded split-query includes and batched accepted counts are not per-item N+1 queries. Discover/details/referral responses already expose shared company logos and need no new logo fields.

## Rechecked inventory (aggregate public GET observations only)

The 32 exact identifiers and their original official-careers evidence are in [company-board-inventory.md](company-board-inventory.md). No new guessed identifier was used. Counts below supersede the earlier *measurement* without replacing its original evidence or implying approval. Descriptions/application data were not persisted or imported. Lever was enumerated with explicit pagination (Palantir four pages, Nium one). Every reachable board had distinct observed IDs within that board; this is not a cross-board vacancy-equivalence audit.

`India evidence` means explicit India location text on Greenhouse, or structured IN/IND/India country data on Lever/Ashby. It deliberately excludes ambiguous cities and generic remote labels. It is geography evidence, not independently verified continuing availability, legal hiring eligibility, deadlines, licensed publication or seats.

| Company | Exact identifier | Distinct observed posting IDs | India evidence |
| --- | --- | ---: | ---: |
| Razorpay | razorpaysoftwareprivatelimited | 27 | 0 |
| Fivetran | fivetran | 196 | 19 |
| Vercel | vercel | 87 | 2 |
| Sentry | sentry | 43 | 0 |
| Cohere | cohere | 120 | 0 |
| Mistral | mistral | Not measured: HTTP 404 | Unknown |
| Fireworks AI | fireworks | 85 | 0 |
| AssemblyAI | assemblyai | 9 | 0 |
| Deepgram | Deepgram | 91 | 0 |
| Baseten | baseten | 111 | 0 |
| Abridge | Abridge | 47 | 0 |
| Anyscale | anyscale | 20 | 0 |
| Figma | figma | 151 | 1 |
| Notion | notion | 133 | 4 |
| Linear | Linear | 31 | 0 |
| Mercury | mercury | 64 | 0 |
| Render | render | 38 | 0 |
| PlanetScale | planetscale | 12 | 0 |
| Supabase | supabase | 52 | 0 |
| Sourcegraph | sourcegraph91 | 10 | 0 |
| Palantir | palantir | 313 | 0 |
| Nium | nium | 17 | 9 |
| Tide | tide | 91 | 29 |
| CloudZero | CloudZero | 17 | 0 |
| G2 | g2crowd | Not measured: HTTP 404 | Unknown |
| LangChain | langchain | 100 | 0 |
| Unstructured | unstructured | 5 | 0 |
| Runpod | runpod | 29 | 0 |
| Exa | exa | 58 | 0 |
| Cartesia | cartesia | 29 | 3 |
| Inngest | inngest | 1 | 0 |
| Trigger.dev | triggerdev | 6 | 0 |

Summary: **32 documented identifiers; 30 reachable feeds; 1,993 distinct board-scoped posting identities; 67 with explicit India geography evidence**. Verified active India vacancies: **not established** (no complete detail/deadline/hiring-eligibility audit). Cross-board unique active vacancies: **not established**. Approved publishable jobs: **0** for this discovery inventory. Successfully imported real jobs: **0**. Gap to 10,000 *new verified imported active* vacancies: **10,000**. Existing platform totals/total-platform shortfall remain unknown because production was not queried.

All 32 sources require documented publication approval and separate logo-rights review; they remain discovery-only/disabled, not database seeds. Mistral/G2 additionally need re-verification of the official current board link; a 404 was not bypassed and is not a zero-vacancy claim. Capgemini remains disabled pending permission. Existing Deloitte is not exempt from the gate: review/configure its actual rights before deploying this release or execution will be refused. No production configuration was changed.

Additional research targets (NOT counted, registered, approved or assigned guessed ATS tokens): [Postman](https://www.postman.com/company/careers/), [Rippling](https://www.rippling.com/careers), [Chargebee](https://www.chargebee.com/company/careers/), [Freshworks](https://www.freshworks.com/company/careers/) and [BrowserStack](https://www.browserstack.com/careers). Their official careers pages were checked as starting points. Freshworks links SmartRecruiters and BrowserStack links Workday, so neither should be forced into the current SuccessFactors/public-feed adapters. Follow actual official links, assess per-vacancy India eligibility and obtain redistribution permission before considering coverage.

Technical contract references: [Greenhouse](https://docs.greenhouse.io/job-board.html), [Lever](https://github.com/lever/postings-api), [Ashby](https://developers.ashbyhq.com/docs/public-job-posting-api). These describe access, not a commercial republication grant.

## Local validation and rollout prerequisites

Final validation: non-incremental Release API build (`--no-restore`) **PASS, 0 warnings, 0 errors**. Relevant provider/Deloitte/aggregation/dedup/normalization/expiry/quality/durable-run/candidate Saved Jobs/public-search/referral/lifecycle regressions: **854 passed, 0 failed, 5 skipped, 859 total**. `git diff --check`: **PASS** (line-ending conversion warnings only). Five skips are the explicitly opt-in disposable-local PostgreSQL tests (one durable-run test and four referral tests, including the new saved/referral SQL regression). No database connection was opened. The skipped tests remain a rollout validation prerequisite.

Synthetic fixtures exercise the actual runner/ingestion/repositories at limits 20, 100 and 1,000 over a 1,500-job fixture, repeat/idempotency, ownership and no stale closure. Saved/null-logo contracts, source publication expiry, referral visibility/privacy/counting/ties/overflow and PostgreSQL SQL translation are separately covered. Synthetic inserted records are **not real imported vacancies**.

Broad regression triage also corrected a pre-existing normalization test that incorrectly expected success despite skipped records (the HEAD runner already failed such snapshots closed), and supplied the missing publication timestamp in two referral-skill test cases. Their valid-record persistence and skill/proficiency assertions are retained; production safety was not weakened to satisfy stale fixtures.

No disposable PostgreSQL connection was configured (`JOB_SOURCE_RUN_TEST_POSTGRES` and `REFERRAL_TEST_POSTGRES` absent). PostgreSQL integration tests are therefore skipped, never redirected to application/Neon credentials. An additional opt-in referral/saved SQL regression reuses the existing isolated local schema fixture. Run those tests in their explicitly named disposable local databases before production rollout; do not interpret EF InMemory/SQL translation as PostgreSQL execution proof.

Activation checklist:

1. Review current source rights, exact company/source binding and independent asset rights, including Deloitte; keep unapproved sources inactive and Capgemini disabled.
2. Re-verify current official identifiers/application domains and actual per-job India eligibility; investigate the two 404s without guessing replacement tokens.
3. Provision only explicitly disposable local test databases and run opt-in PostgreSQL regressions. No schema migration is needed for this follow-up.
4. Keep scheduler and automatic publication disabled locally; apply reviewed approval with `TestImportLimit=20`, then 100, then 1000. Use the existing enqueue/poll commands in [multi-company-job-providers.md](multi-company-job-providers.md). Repeat each run; check source ownership, no duplicate identities, Closed=0 and no successful-snapshot advance.
5. Review real approved full snapshots, provider deadlines/quality and deduplicated visible counts before removing the cap. Lack of expiry/metadata remains review-required; do not fabricate it to reach a target.
6. Review this diff/tests, configure approvals securely and monitor durable status/cooldown before independently authorizing deployment/activation. This task does not deploy or activate anything.

Frontend follow-up: no UI/backend route redesign is necessary. Read `companyId`, `companyName`, `companyLogoUrl` from the candidate flat saved-job response, or existing `job.*` fields from dashboard saved jobs; keep a null-logo fallback. If referral search/filtering is required, use the existing `/api/jobs/referrals` query rather than assuming `/api/referrals/public` accepts those filters. No frontend was edited.

## Follow-up changed-file manifest

```text
JobPortal.Application/Features/Candidates/CandidateDtos.cs
JobPortal.Application/Features/Candidates/CandidateService.cs
JobPortal.Application/Features/Jobs/JobService.cs
JobPortal.Application/Features/PublicJobs/PublicJobProjections.cs
JobPortal.Application/Features/Referrals/JobReferralService.cs
JobPortal.Application/Features/JobAggregation/JobSourcePublicationPolicy.cs
JobPortal.Application/Services/JobSourceRunner.cs
JobPortal.Application/Services/JobIngestionService.cs
JobPortal.Persistence/Repositories/JobReferralRepository.cs
JobPortal.Persistence/Repositories/PublicJobRepository.cs
JobPortal.Persistence/Repositories/DashboardRepository.cs
JobPortal.Application.Tests/CandidateModuleTests.cs
JobPortal.Application.Tests/JobReferralServiceTests.cs
JobPortal.Application.Tests/PublicJobSearchTests.cs
JobPortal.Application.Tests/ReferralMarketplacePostgresTests.cs
JobPortal.Application.Tests/ReferralJobSkillsTests.cs
JobPortal.Application.Tests/ExternalJobNormalizationTests.cs
JobPortal.Application.Tests/JobProviderIntegrationAuditTests.cs
JobPortal.Application.Tests/MultiCompanyProviderTests.cs
docs/multi-company-integration-audit.md
docs/company-board-inventory.md
docs/multi-company-job-providers.md
```

The prior provider/fixture/documentation changes remain intact. No historical migration/model/schema, frontend, appsettings, .gitignore, SQL artifact, stash, staged file or commit was changed by this follow-up.
