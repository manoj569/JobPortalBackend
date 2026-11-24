# Controlled Coresignal hiring-contact evaluation

This is an opt-in backend trial integration, not approval to redistribute provider data.
Normal candidate discovery remains replay → licensed shared intelligence → enabled,
display-approved person providers (Coresignal, then Apollo) → Hunter fallback/verification.
Apollo and Hunter implementations remain available. No frontend or database schema change
is needed. No production configuration is enabled by this patch.

## Official API review

Reviewed 2026-11-24, without credentials or live search/contact calls:

- [Employee search schema](https://docs.coresignal.com/employee-data/api/elasticsearch-dsl)
- [Public OpenAPI schema](https://api.coresignal.com/cdapi/openapi.json)
- [Preview semantics and pagination](https://docs.coresignal.com/api-introduction/requests/search-preview)
- [Collect and field selection](https://docs.coresignal.com/employee-data/api/collect)
- [Contact enrichment](https://docs.coresignal.com/employee-data/api/contact-enrichment-api)
- [Authentication](https://docs.coresignal.com/api-introduction/authorization)
- [Credit accounting](https://docs.coresignal.com/api-introduction/credits)
- [Rate limits](https://docs.coresignal.com/api-introduction/rate-limits)
- [Pricing](https://docs.coresignal.com/pricing/pricing)

Implemented POST `/cdapi/v2/employee_multi_source/search/es_dsl/preview?page=1` and GET
`/cdapi/v2/employee_multi_source/collect/{employee_id}` on the fixed `api.coresignal.com` host.
Both use the `apikey` header. Preview's documented object fields include `id`, `full_name`,
`location_full`, `company_website`, `active_experience_title` and
`active_experience_department`. Provider `_score` never determines CareerHarbor ranking.
The documented `query` envelope is used; no invented `size` or other root parameters.

The numeric Preview `id` is the Employee ID for the matching Multi-source Collect family.
`/employee_multi_source/enrich` resolves `social_url`, so it is not used for an existing ID.
Collect requests only `bundle=core_employee_data` and repeated `fields` for `id`, `full_name`,
`is_deleted`, `is_working`, `active_experience_company_website`, `active_experience_title`.
No full profile, work history, phone, salary, interests or social profile URLs are requested.
The returned employee ID/name, current role and employer domain must still match the preview
identity and job company. Deleted/private, missing, non-working and mismatched employees
cannot reach same-person Hunter lookup.

**Collect has no published email or email-status fields.** The current public OpenAPI
`MultiSourceEmployeeResponseModel`, core dictionary and sample contain none. Consequently
we neither request nor parse guessed fields such as `email`, `emails` or `email_status`.
After successful collection validation, the existing bounded Hunter finder resolves a
professional email. This Collect-validation + Hunter-email path was explicitly approved
by the owner. A usable contact therefore has `Coresignal+Hunter` provenance; collection
alone does not create a verified contact or prove Coresignal-only email coverage.

The separate `/enrichment/contacts?member_id=...` product remains in the public docs, but
its cross-product identifier mapping is not established by the Multi-source contract.
Sending Preview IDs there produced observed HTTP 400s; this does not prove that the
separate endpoint itself is invalid or establish the precise account-side cause. It is
no longer called. An account-specific contract or Coresignal support confirmation would
be needed before adding a different contact-ID mapping; none is guessed here.

Preview is **not free**. It returns up to 20 records per paid query. The configured
candidate cap limits local processing; it does not reduce the provider's page price.
No second page, pagination cursor, snapshot or x-total-results value triggers another
request. Manual operators should verify trial access and current charges in the account.
Core-only collection currently costs 10 credits; field selection alone does not reduce
cost, so the core bundle is selected explicitly. Preview and Hunter costs remain
account-dependent. The trial rate limit is 5 requests/second. Requests in a workflow
are sequential with at least 250 ms spacing; this is not a distributed account-wide rate
limiter. Multiple replicas/probes can still share an account limit. A 429 is not retried.

## Gates and configuration

```text
HiringOutreach__Coresignal__Enabled=false
HiringOutreach__Coresignal__ApiKey=<secure secret store>
HiringOutreach__Coresignal__AllowCandidateDisplay=false
HiringOutreach__Coresignal__AllowSharedPersistence=false
HiringOutreach__Coresignal__MaximumSearchRequests=5
HiringOutreach__Coresignal__MaximumCandidatesPerSearch=10
HiringOutreach__Coresignal__MaximumEnrichments=2
HiringOutreach__Coresignal__TimeoutSeconds=10
```

Enabled options require a key, searches 1–5, processed identities per search 1–20,
enrichments 1–3, and request timeout 1–20 seconds. Disabled configuration needs no key.
Validation runs at startup and never includes the key in error messages.

`Enabled=true`, `AllowCandidateDisplay=false` permits only the protected diagnostic path.
Normal candidates do not trigger Coresignal calls. Persistence additionally rejects
Coresignal and composite Coresignal+Hunter contacts from candidate snapshots when display
is disabled. Existing snapshots are filtered from history/detail, draft recipients and
new sends when display approval is revoked.

Shared reads and writes require all of: existing `SharedContacts.Enabled`, Coresignal
Enabled, AllowCandidateDisplay and AllowSharedPersistence. Both row provenance and
protected payload provenance are checked on reads. Composite provenance retains Coresignal
restrictions after Hunter verification/finding. A Hunter email does not erase the original
identity's licensing obligations. Existing independent Hunter/Apollo cache entries remain
usable. No live Coresignal data is persisted by the admin diagnostic route.

Keep shared intelligence disabled for the trial. Written contractual permission is required
before candidate display, candidate-session retention, cross-candidate caching or persistent
reuse outside a permitted evaluation. Technical flags are operator controls, not proof of
rights. Confirm permitted internal evaluation with Coresignal before using trial data.

## Search, ranking and contact handling

Stages: exact-city recruiters (known city only), India recruiters, India HR/People,
India job-family managers, relevant global recruiter/manager/lead fallback. Maximum five
paid previews. Company domain and current-employment fields are required. Missing employer,
title, identity or obfuscated name is rejected; query filters never become person facts.
The actual returned location remains unknown when absent.

Existing job-family inference, HiringLocationPolicy aliases and HiringContactPolicy own
the ranking. Recruiters/TA/HR and relevant managers outrank ordinary employees. Ordinary
employees are not enriched. Engineering includes embedded/software/.NET, Operations
includes Trust & Safety/procurement, and remaining existing families stay intact.

Only top ranked identities are collected, once each, within the unchanged enrichment
budget. Two strong verified contacts stop later providers/stages. Collect HTTP 404 maps
to `CoresignalEmployeeNotFound`, without parsing its body or retrying. Empty selected
records map to `CoresignalEmployeeUnavailable`; private/deleted records map to
`CoresignalPrivacyRestricted`; identity/employer/role mismatches map to
`CoresignalIdentityMismatch`. These verified unusable outcomes do not initiate same-person
email finding and are distinct from ambiguous timeouts/transport failures. HTTP 400/422,
401/403, 402 and 429 retain invalid-request, configuration, quota and rate-limit codes.

After collection validation, at most two Hunter `/v2/email-finder/found` calls may resolve an exact
name/domain identity, preserving Coresignal+Hunter provenance. Returned name and domain,
found provenance and verification must match. No inferred-address patterns are generated.
Unknown emails may use the existing bounded Hunter verifier. Insufficient contacts use
the existing targeted Hunter Domain Search with limit=10. Earlier contacts survive later
failures. No uncertain request is automatically resent.

Existing candidate reservation/charge, replay/cache exemptions, durable claims, suppression,
encryption and expiration remain authoritative. One logical discovery costs at most one
CareerHarbor unit regardless of provider count. Confirmed rejection/empty outcomes release;
timeouts/transport errors retain uncertainty. Admin probes use no candidate quota/session.

## Protected manual probe

`POST /api/admin/outreach/providers/coresignal/probe`

Requires authenticated **Administrator**, existing HiringOutreach rate limiting and explicit
credit acknowledgment. Responses are no-store. Only one probe per instance runs at once;
the existing rate limiter also applies. This is intentionally not a cross-instance probe
idempotency/credit ledger. Do not automatically retry an ambiguous manual probe.

Example body (no secret):

```json
{
  "companyName": "Accenture",
  "companyDomain": "accenture.com",
  "jobTitle": "Trust & Safety Associate",
  "location": "Hyderabad",
  "confirmProviderCreditUse": true
}
```

Returns provider/state, bounded identity/relevance/contact counts, titles, actual locations,
company domains, evidence category, deterministic score and contact-availability boolean.
It never returns email, name, provider ID, profile URL or raw provider responses. It may
exercise Hunter fallback/finding/verification, so confirm both providers' credit use.
Credit consumption must be measured in provider dashboards; counts are not billed credits.
Requested/completed/uncertain/failed audits contain state and actor, not search/contact data.
Named HTTP clients remove request loggers, redirects and cookies; response bodies are
bounded. External HTTP telemetry must independently redact headers/query strings.

Run these cases only after review and explicit manual credit authorization:

| Company/domain | Job | Location | Quality to compare |
|---|---|---|---|
| Accenture / accenture.com | Trust & Safety Associate | Hyderabad | Local/India recruiter, TA, HRBP, operations or Trust & Safety manager |
| Accenture / accenture.com | Procurement Operations Associate | Mumbai | India recruitment/HRBP, procurement/operations manager |
| nVent / nvent.com | Principal Software Engineer – Embedded Systems | India or actual job city | India technical recruiter/TA, embedded/software engineering manager |
| PTC / ptc.com | Software Engineer | Actual Indian job city | India hiring contacts rank above US ordinary engineers |
| Infosys / infosys.com | .NET Developer | Pune | Pune/India recruiter, TA and relevant engineering manager |

Compare India matches, recruiting/HR/manager categories, contact availability, request counts
and dashboard credits against Hunter. Keep Apollo disabled unless separately authorized.
Keep candidate display and shared persistence false throughout evaluation. Obtain written
licensing approval before considering a later rollout. No trial-quality claim is possible
from mocked tests alone.

## Initial trial verification (before the Collect correction)

Automated provider tests use mocked HTTP only. Historical results for the initial
Contact Enrichment implementation, not certification of the subsequent Collect correction:

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Coresignal focused (`hiring-coresignal-focused-final.trx`) | 42 | 0 | 0 |
| Hiring providers/outreach/Data Protection (`hiring-coresignal-hiring-regression.trx`) | 200 | 0 | 0 |
| Wider related regression (`hiring-coresignal-regression.trx`) | 698 | 0 | 10 |
| Full application suite (`hiring-coresignal-full.trx`) | 3850 | 0 | 110 |

The final sequential `JobPortal.sln` build completed with zero warnings and zero errors.
EF `migrations has-pending-model-changes --no-build` reported no changes. No migration was
generated, edited or applied. Existing contact metadata supports this provider unchanged.
The existing explicitly disposable localhost Hiring PostgreSQL fixtures were enabled;
other opt-in integration fixtures account for the wider regression skips.

The first sandboxed focused invocation aborted before test execution because VSTest could
not connect to testhost within 90 seconds (zero tests executed). Its TRX is preserved.
The orphaned host was stopped. Outside-sandbox execution with
`VSTEST_CONNECTION_TIMEOUT=300` completed successfully; no tests or assertions were changed
to handle the infrastructure problem. Workload resolution was disabled with
`MSBuildEnableWorkloadResolver=false`; sequential builds used isolated
`bin/HiringCoresignalVerification/` output and `DOTNET_PROCESSOR_COUNT=2`.

TRX files are retained in `JobPortal.Application.Tests/TestResults/`. The full current
application run completed in 18 minutes 35 seconds: 3,960 total, including 42 additional
passing tests over the previous 3,808-pass/110-skip baseline. The disposable database
container was stopped after verification, preserving its data. No live Coresignal, Apollo, Hunter or Gmail
calls, production mutations, frontend edits, commits, pushes or deployments are authorized
or performed by this implementation. Mocked success does not verify real India coverage,
account access or licensing, and skipped fixtures are not integration certification.

## Collect correction verification

The owner approved Collect identity/employer validation followed by Hunter email finding
because the published Multi-source Collect model contains no email/status fields.
Current compiled-source results for this correction:

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Coresignal (`hiring-coresignal-collect-focused.trx`) | 64 | 0 | 0 |
| Hiring multi-provider/outreach/Data Protection (`hiring-coresignal-collect-hiring.trx`) | 222 | 0 | 0 |
| Wider related regression (`hiring-coresignal-collect-regression.trx`) | 720 | 0 | 10 |

The sequential solution build succeeded with zero warnings and zero errors. EF's compiled
model check reported no changes. No migration was generated, modified or applied. The
full application run above predates this correction and was not rerun for this narrow fix.
The 10 skipped related integration fixtures require separate opt-in database setups.

Added 22 collection/error/validation cases and updated existing provider/admin scenario
mocks. Tests assert the exact Preview-ID Collect path, six selected fields/core bundle,
404/402/error mappings, malformed responses, ID/name/role/current-domain/private-record
guards, unchanged duplicate/budget limits, and verified employer-email acceptance through
the actual mocked Hunter finder. Undocumented Collect email fields are ignored explicitly.

All provider responses were mocked. Only the existing disposable localhost Hiring database
was used for its opt-in regression fixtures. No live employee/provider data calls,
production database commands, frontend changes, commits, pushes or deployment occurred.
Real email availability remains unverified and can still be zero when Hunter has no
verified business email for the validated identity. Licensing gates remain disabled by
default and were not activated by this change.
