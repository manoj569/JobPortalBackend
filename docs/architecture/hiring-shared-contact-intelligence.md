# Shared hiring-contact intelligence

This extends Hiring Outreach only. Candidate sessions, drafts, mailbox credentials and sends
remain candidate-owned. The two previously applied Outreach/Data Protection migrations are
unchanged. A separate additive migration supplies the intelligence and expiring claim tables.
Migration: `20261122234234_AddHiringContactIntelligence` (two new tables only).

## License gate and deployment

`HiringOutreach__SharedContacts__Enabled` defaults **false**. Confirm the Hunter agreement
permits persistent storage and cross-candidate reuse of professional contact data before
enabling it in production. Technical implementation is not legal/provider-license approval.
When disabled, no shared intelligence is read or written. No production settings, provider
secrets, database migrations or live sends are changed by this implementation task.

Keep the existing PostgreSQL/certificate-backed Data Protection configuration. The shared
payload uses a separate versioned `SharedContacts` protection purpose scoped to the trusted
company domain. It encrypts the email, person names, LinkedIn URL and provider reference.
Search metadata contains normalized title/department/location/family, not names or email.
An unkeyed SHA-256 normalized email hash deduplicates within a domain. This hash is an index,
not anonymization; restrict database access and protect backups. No plaintext identity or
credentials are logged. No candidate messages/resumes are used as reusable intelligence.

After reviewing the additive migration, apply it through normal authorized deployment
procedures before enabling the gate. Keep the same key ring and decrypting certificate
across deployments. This task verifies only explicitly disposable localhost PostgreSQL.

## Discovery and quota rules

Existing candidate/job session and request-key replay checks run first. New sessions resolve
the public job's existing trusted company domain and bounded job context. Shared lookup
requires the same company ID and domain, active/non-deleted data, unexpired TTL, recent
verified observation and `valid` email verification. At most 200 recent rows are decrypted
and ranked. Insufficient, corrupt, unrelated or expired records cannot satisfy the cache.

The default sufficient cache is two contacts scoring at least 70. Return at most four
(also respecting MaximumContacts). Cached contacts need no provider configuration or new
discovery allowance, incur no provider call, and have `usage.charged=false`. They never
grant a new paid send entitlement; the existing send checks and limits still apply.
The additive session `source` field identifies `CareerHarborCache` or the provider name;
existing fields/routes remain compatible. Cache-backed session expiration is bounded by
the earliest reused intelligence expiration, including partial cache enrichment.

Options under `HiringOutreach__SharedContacts__`:

| Setting | Default | Bounds |
|---|---:|---|
| Enabled | false | license rollout gate |
| CacheDays | 30 | 1–90 |
| MinimumContacts | 2 | 1–4, bounded by MaximumContacts |
| MinimumScore | 70 | 50–150 |

Expired rows remain retained, not automatically deleted. A new verified provider observation
may refresh them. Fresh higher-confidence metadata cannot be replaced by weaker data.
Higher email confidence alone cannot downgrade a fresh hiring role or erase known title,
department or location evidence. Equivalent or stronger complete observations can refresh
verified timestamps; reuse alone cannot.
Explicitly suppressed/deleted identities are not resurrected. Freshness and previous reuse
break ranking ties; reuse does not reset verified timestamps or extend TTL.
While shared intelligence is enabled, those suppressed identities are also filtered from
fresh provider results; rediscovery cannot bypass the suppression.

Provider discovery reserves one candidate allowance before dispatch. A confirmed response
with usable provider contacts consumes it once. Confirmed zero usable contacts releases it,
including contacts rejected by relevance. Confirmed configuration/rate-limit rejection
releases it. Timeout, transport uncertainty and unverified provider failures retain the
existing uncertain reservation; a request-key replay never dispatches again. Partial cache
contacts may supplement a provider result without being mistaken for new verified data.
The existing `DailyDiscoveries` setting also caps actual provider attempts per candidate/day,
including released and soft-deleted attempts. Thus refunded searches do not create unlimited
provider spend. Reaching this ceiling yields `ProviderUnavailable` with
`DiscoveryProviderDailyLimit`, without consuming credit. Shared-cache hits bypass this
attempt ceiling. Remaining credits and remaining provider attempts are distinct.

## Ranking and provider cost control

Deterministic inference uses title first, then bounded department/description hints:
Engineering, Operations/Trust & Safety, Data, Finance, Product, Design, Marketing and Sales.
A specialist recruiter or manager for another known family is rejected. Broad recruiters
can serve multiple families; department managers/peers require positive family evidence.
Recruiters/talent acquisition outrank relevant managers and peers. Local matches gain a
bonus, national/global recruiting remains eligible, and other-city mismatch reduces rank.
No guaranteed recruiter identity is claimed.
An unknown job family does not justify reusing a known specialist. Common personal-mail
domains are rejected as company discovery domains. Missing professional identity is rejected.

Hunter uses the official fixed-host Domain Search API, personal addresses, valid verification,
required name/position and limit 20. Searches proceed through recruiter/talent job titles,
HR, family department manager/lead, then the family department fallback. Stop after two
strong contacts. `HiringOutreach__Hunter__MaximumRequests` defaults 4 and is bounded 1–4.
At most 80 raw records are fetched. Each stage can consume provider credits independently;
one candidate discovery allowance does not mean one Hunter credit. No automatic paid retry,
unbounded pagination, LinkedIn scraping, inferred local email patterns or live AI is used.
Keys use the supported X-API-KEY header and existing provider HTTP clients disable logging,
cookies and redirects.
An operation-wide cancellation budget (30–90 seconds according to the request cap) also
bounds response-body reads; a timeout retains uncertain quota semantics.

Official API reference reviewed 2026-11-23:
[Hunter Domain Search](https://hunter.io/api-documentation/v2#domain-search).
Email Finder was evaluated but is not called: this flow already receives verified addresses
with licensed person identities. It has no independent approved person-only source requiring
an additional Finder purchase. Missing/unverified addresses are rejected, never guessed.

## Concurrency and recovery

The existing short PostgreSQL advisory transaction lock serializes quota/idempotency/upsert
changes. A unique durable scope hash (domain + family + normalized location) claims provider
discovery for three minutes. No network call runs in a database transaction or an EF retry
delegate. A competing candidate receives retryable 409 `DiscoveryInProgress` without a
reservation/request record; retrying after completion reads committed shared intelligence.
There is no global lock held for provider latency. Unique domain/email hashes and optimistic
revision tokens protect shared identity writes. The in-memory gate is only a unit-test
fallback, not production concurrency control.

Known outcomes release the scope claim. Uncertain outcomes retain a five-minute scope
cooldown, without releasing candidate quota. Expired claims can recover after a crashed
instance; recovery does not redispatch that instance's original request key. This bounds
duplicate work, but cannot guarantee zero duplicate external cost after process failure.
Count-only diagnostics distinguish bounded cache candidates/relevant results and provider
results from local filtering, without logging people or emails.

## Completed local verification — 2026-11-24

SDK 10.0.403, net9.0, `MSBuildEnableWorkloadResolver=false`. The final solution build
completed with **0 warnings and 0 errors**, using sequential MSBuild and an isolated
output directory (`/m:1 /nr:false /p:UseSharedCompilation=false
/p:OutputPath=bin/DataProtectionVerification/`). No analyzer suppression was added.
The last build session spanned a suspended environment; its elapsed wall time is not a
representative compilation benchmark.

| Test run | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Focused Hiring Outreach/provider/shared intelligence | 82 | 0 | 0 | 82 |
| Relevant Hiring/OAuth/authentication/AIApply/career-session regressions | 421 | 0 | 8 | 429 |
| Complete application suite | 3756 | 0 | 110 | 3866 |

Valid final TRX files are `hiring-intelligence-focused.trx`,
`hiring-intelligence-regression.trx` and `hiring-intelligence-full.trx` in
`JobPortal.Application.Tests/TestResults`. The initial pre-migration focused TRX is
retained separately and is superseded by the final run.

The two Hiring PostgreSQL tests and Data Protection PostgreSQL test were enabled against
the explicitly disposable localhost fixture. Verified shared contact reuse across fresh
contexts, concurrent claim exclusion and actual database unique-constraint enforcement.
The 8 relevant-suite skips and 110 full-suite skips are existing opt-in integration
fixtures not configured for this task; no new Hiring tests were skipped. This is not
complete integration or production regression certification.

Migration `20261122234234_AddHiringContactIntelligence` was applied **only** to disposable
`hiring_outreach_verify`. Its reviewed SQL creates two tables and eight indexes, with
restricted foreign keys and no alterations/drops of existing tables. The generated delta
SQL is `JobPortal.Application.Tests/TestResults/hiring-intelligence-migration.sql`.
Final `dotnet ef migrations has-pending-model-changes --no-build` reported no changes.
Both previously applied migration files and the Gmail provider implementation remain
unchanged. No production migration, live Hunter request, live Gmail send, frontend edit,
commit, push or Render configuration change was performed. License approval and an
authorized deployment/live-provider smoke test remain rollout prerequisites.
