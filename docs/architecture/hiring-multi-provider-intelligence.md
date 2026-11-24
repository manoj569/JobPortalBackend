# Multi-provider hiring-team intelligence

This extends the existing Hiring Outreach feature. No Accelerator, Gmail OAuth/send,
resume, membership or frontend implementation is replaced. Sending remains an explicit
candidate action after review. Existing routes/fields remain; contact location, source,
match score/reason, evidence-based `contactType` and usage provider-attempt balance are
additive projections. The existing `type` field remains compatible.

## Provider order and contracts

Candidate-owned session/request replay → licensed shared database cache → Apollo people
search → selected Apollo enrichment → Hunter targeted fallback/verification → deterministic
selection. `IHiringContactProvider` remains the service facade; its structured result
preserves earlier contacts, failure codes and uncertain outcomes. Separate person discovery,
enrichment, email verification and fallback interfaces isolate providers from persistence.
The legacy facade has a default structured-result adapter for existing implementations.

Apollo is disabled by default. It uses only documented fixed-host POST endpoints:
`https://api.apollo.io/api/v1/mixed_people/api_search` and
`https://api.apollo.io/api/v1/people/match`, with `x-api-key` header authentication.
Official references reviewed 2026-11-24:
[search](https://docs.apollo.io/reference/people-api-search),
[enrichment](https://docs.apollo.io/reference/people-enrichment).
Account API access, eligibility and credit availability require owner verification.

Search sends documented `q_organization_domains_list[]`, `person_titles[]`,
`person_locations[]`, `per_page=20`, `page=1`. Stages are local recruiting (when a known
city exists), India recruiting, India HR/people, relevant India managers, then a global
recruiter/manager fallback. There is no automatic pagination. Known managers are selected
from deterministic job families, never fabricated identities. A search-domain filter can
match former employers, so enrichment validates the returned person's exact ID and current
organization primary domain before accepting the professional email.

Search previews do not reveal emails and may obfuscate names or provide location flags
instead of actual locations. Those flags/filter locations are never converted into person
facts. Identities are ranked first; only the best hiring-role identities are enriched.
Ordinary employees do not consume Apollo enrichment budget. Exact provider IDs deduplicate
attempts; a complete exact name/domain/title/location compound is a conservative fallback
only for future sources lacking IDs. Similar/obfuscated names alone are never merged.

Enrichment submits the approved ID. Personal email, phone and waterfall flags are false.
Low/none identity-match confidence is rejected. Email status must be explicitly verified,
either on the person or the matching contact/email record. A high identity-match confidence
alone is not email verification. Unverified approved business addresses may use the bounded
Hunter verifier; catch-all/unknown results do not become verified. No guessed email patterns,
personal phone requests, webhooks, LinkedIn scraping or AI inference are used.

## Ranking and cache

Title → department → bounded description determine Engineering, Operations/Trust & Safety,
Data, Product, Finance, Design, Marketing or Sales. Recruiters/talent acquisition precede
HR partners/managers and relevant managers/leads. Unrelated specialists, sales employees for
engineering, founders/CEOs and account executives are rejected. Ordinary relevant employees
are a low-score fallback with `recommended=false` and an explicit warning that hiring
responsibility is not established. Two strong hiring contacts exclude ordinary employees
from the final provider result. Output remains bounded to four/MaximumContacts.

Indian city aliases include Bangalore/Bengaluru, Bombay/Mumbai, Gurgaon/Gurugram and
NCR/Delhi NCR. Ranking prefers local city, India, another Indian city, then global/unknown
and foreign locations. This is a small deterministic normalization policy, not a geographic
authority. Missing locations remain unknown. Strong global recruiters are not hard-rejected.

Existing shared intelligence remains company/domain-correct, verified, fresh, encrypted and
job-relevant. TTL configuration is unchanged (30 days by default). Reuse never refreshes
verification timestamps or TTL. Suppressed identities cannot be resurrected by either
provider. Fresh stronger role/location/confidence evidence is protected from downgrade.
Per-contact provider/source is retained inside the protected payload and safe provenance
metadata; names, email, profile URLs and provider references remain protected.

## Budgets, failures and quotas

Defaults/bounds:

| Setting | Default | Bounds |
|---|---:|---|
| Apollo Enabled | false | feature gate |
| Apollo MaximumSearchRequests | 5 | 1–5 |
| Apollo MaximumEnrichments | 3 | 1–5 |
| Apollo TimeoutSeconds | 15 | 5–30 per request, including body |
| Hunter MaximumRequests | 4 | 1–4 domain searches |
| Hunter MaximumVerifications | 2 | 0–2 for the logical operation |

Stop once two strong verified contacts exist. One strong Apollo contact reduces Hunter's
target to one additional contact. Calls remain sequential to avoid speculative spend.
Search/enrichment budgets are independently bounded; repeated identities are not enriched
again. An operation-wide 150-second cancellation budget is shorter than the existing
three-minute durable claim. Unknown original requests are never automatically retried.

Hunter's plan-compatible `limit=10` remains fixed. Do not raise it without account approval.
Targeted recruiting/HR/manager/department passes use official filters; overly restrictive
verification/name-field filters are removed, with bounded local verification/ranking.
API keys use headers, clients suppress request loggers/cookies/redirects, and bodies are
bounded to 512 KiB. Stage/count/fixed-code diagnostics never contain names, emails, raw
responses, credentials or full URLs. External telemetry must redact query identifiers and
email-verifier query values as well as authentication headers.

Provider errors distinguish authentication/configuration, invalid requests, rate limits,
quota exhaustion, outages, invalid responses and unknown transport outcomes. Only bounded
machine-code metadata affects classification; raw error messages are never exposed.
Apollo failures permit safe Hunter fallback. Later failures never discard usable earlier
contacts. Mixed uncertain results retain the uncertainty even when another provider succeeds.

One candidate operation has at most one reservation/charge, independent of provider count.
Cache and replay are free. Confirmed zero usable contacts/configuration/rate/quota rejection
release the reservation. Unknown outcomes retain it. Known success with contacts consumes
once; a resolved fallback rejection does not produce a candidate failure banner. Existing
daily provider-attempt limits include released and soft-deleted attempts. `usage.remaining`
is candidate credit; `usage.providerAttemptsRemaining` separately reports the safety ceiling.
Cache hits bypass both new provider dispatch and provider-attempt exhaustion.

Existing short PostgreSQL advisory writes and durable domain/family/location claims remain.
City aliases share scope buckets. Network calls never run in a retryable database transaction.
Concurrent competing candidates get a retryable conflict without a debit; after completion,
their retry reads the shared cache. Expired claims recover after restart; ambiguous outcomes
retain the existing cooldown/ledger semantics. Drain old in-flight deployments before rollout
when changing scope normalization, to avoid mixed-version duplicate work.

## Configuration and staged rollout

New optional Render variables:

```text
HiringOutreach__Apollo__Enabled=false
HiringOutreach__Apollo__ApiKey=<secret-store value; never paste into chat>
HiringOutreach__Apollo__MaximumSearchRequests=5
HiringOutreach__Apollo__MaximumEnrichments=3
HiringOutreach__Apollo__TimeoutSeconds=15
HiringOutreach__Hunter__MaximumVerifications=2
```

Enabled Apollo requires valid settings; disabled Apollo requires no key. Existing Hunter,
shared-cache, quota, Google and certificate-backed database Data Protection settings remain.
No new cryptographic key or billing ledger is introduced.

Phase A: deploy with Apollo disabled; Hunter remains available. Phase B: enable Apollo while
shared reuse remains disabled and manually validate the account/plan on authorized searches.
Phase C: enable shared reuse only after **separate Apollo and Hunter agreement reviews**
permit storing/reusing their professional data across candidates. Existing Hunter approval
does not establish Apollo approval. Technical integration does not certify licensing.
Keep `SharedContacts.Enabled=false` until those reviews; no production gate is changed here.

The additive migration adds only location/provider/source/match-score snapshot columns to
existing candidate-owned contacts, with empty/zero defaults for historical unknown data.
The shared tables, Data Protection and all previously applied migrations are preserved.
Production migration execution, live provider calls and Gmail sends are outside this task.

## Verification on 2026-11-24

The current solution compiled with zero warnings and zero errors using SDK 10.0.403,
`MSBuildEnableWorkloadResolver=false`, sequential MSBuild and isolated
`bin/HiringMultiProviderVerification/` output. No analyzer or test assertion was suppressed.

Migration `20261124105858_AddHiringContactMatchMetadata` contains exactly four additive
columns on `HiringOutreachContacts`: Location, MatchScore, Provider and Source. It creates
no tables or indexes and does not alter existing migrations. The inspected SQL was applied
only to the explicitly disposable local `hiring_outreach_verify` PostgreSQL database.
EF's pending-model check reported no changes. No production database was contacted.

Completed TRX reports under `JobPortal.Application.Tests/TestResults/`:

| Report | Passed | Failed | Skipped |
|---|---:|---:|---:|
| hiring-multi-focused.trx | 134 | 0 | 0 |
| hiring-multi-regression.trx | 473 | 0 | 8 |
| hiring-multi-full.trx | 3807 | 1 | 110 |
| hiring-multi-classification-isolation.trx | 64 | 0 | 0 |
| hiring-multi-full-repeat.trx | 3808 | 0 | 110 |

The first full run failed `JobClassificationTests.SalaryUsesOnlyDisclosedAmounts` with
`RegexMatchTimeoutException` in the unchanged salary classification implementation.
Its existing non-backtracking regex has a 100 ms wall-clock limit. The complete unchanged
classification group subsequently passed in isolation. These results support a
timing-sensitive failure; they do not establish a previously failing baseline. The failed
TRX remains preserved. One controlled full repeat completed successfully with no source
or assertion changes: 3,808 passed, zero failed, 110 skipped, 3,918 total. The earlier
timing-sensitive failure is still disclosed rather than erased by the successful repeat.

Focused Hiring PostgreSQL fixtures were enabled against the disposable local database.
Other opt-in integration fixtures remained unconfigured and are reported as skipped.
Live account entitlements, real contact availability, provider licenses and paid-provider
smoke tests remain unverified; this is not full production or integration certification.
