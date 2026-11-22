# Phase 1: candidate-authorized hiring-team email

This feature is independent of Accelerator activation and its paid plan. Registration,
password-reset and other transactional email continue using their existing services.
Nothing sends during job browsing, contact discovery, OAuth, or draft generation.
Only the authenticated candidate POST send action dispatches Gmail messages.

## Contracts

Base route: `/api/candidate/outreach` (Candidate role, no-store, bounded bodies,
20 requests/minute per user; anonymous OAuth callback is IP-limited).

| Method | Path | Request / result |
|---|---|---|
| POST | `/discover` | `{jobId,requestKey}` -> session, contacts, mailbox, usage |
| GET | `/{sessionId}` | Owned session only |
| GET | `/history?page=1&pageSize=20` | Owned history, maximum 50 per page |
| GET | `/resumes` | Existing owned document IDs and safe filenames; no storage keys |
| POST | `/{sessionId}/draft` | `{recipientIds,resumeId}` -> immutable draft ID, editable subject/body |
| POST | `/{sessionId}/send` | `{draftId,recipientIds,subject,body,resumeId,requestKey}` |
| GET | `/mailbox` | `{connected,provider:"Google",email}`; no credentials |
| POST | `/mailbox/google/connect` | `{authorizationUrl}` containing a short-lived protected browser-handshake ticket |
| GET | `/mailbox/google/authorize` | Top-level protected handshake; sets first-party binding cookie, redirects only to Google |
| GET | `/mailbox/google/callback` | Google code/state callback; fixed configured frontend redirect |
| DELETE | `/mailbox` | Local revoke/credential removal, then best-effort Google revoke |

IDs serialize as UUIDs and product states as strings. Send responses include individual
recipient results, not bodies or credentials. `Sent`, `PartiallySent`, `Failed` and
`SendingUnresolved` are distinct. Unresolved means do not resend: Gmail does not offer
an exactly-once send API. Message-ID helps investigation but is not a Gmail dedup guarantee.
An interrupted reservation or dispatch stays durable and is never automatically dispatched
by a worker or replay. A replay with the same key returns recorded results; changed content
under that key conflicts. Reserved/sent/unresolved job+recipient combinations block a new
send key. There are no campaigns, sequences, arbitrary recipient uploads or follow-ups.

## Data and concurrency

New tables are HiringOutreachSessions, HiringOutreachDiscoveryRequests, HiringOutreachContacts, HiringOutreachDrafts,
HiringOutreachSends, HiringMailboxSecrets and HiringMailboxAuthorizations. Existing
CandidateMailboxConnections holds provider account metadata and an opaque secret reference.
Refresh credentials and PKCE verifiers use owner-bound ASP.NET Data Protection. Access
tokens are refreshed on demand, remain in memory and never enter database/API DTOs.

Writes use PostgreSQL advisory transaction locks, unique request/contact indexes,
concurrency revisions, and EF retry execution strategy. No external call occurs inside a
database retry transaction. Tests use a nonrelational gate only for their in-memory store.
Drafts, contacts, job snapshots, send content and usage provenance are immutable. Audit
events reuse the existing append-only audit infrastructure. No new financial billing ledger
or Accelerator allowance is created. Discovery records are the feature's durable usage
reservations; global counts include soft-deleted reservations to prevent quota resetting.

Credits are reserved before external discovery. Verified 401/403/configuration or 429
rejections release them. Completed searches consume them; timeouts/unknown outcomes retain
an uncertain reservation. Same-candidate+job discoveries reuse successful results for the
configured TTL (including after sending), without another provider call or charge. Cache
reuse across candidates/companies is deliberately not enabled without licensed reuse policy.
Expired/crashed requests are not blindly redispatched. Support must investigate durable
unresolved records; no manual-resolution API that assumes delivery exists in this release.

## Provider behavior

Hunter uses the fixed official domain-search API, trusted stored company domain/website,
at most ten fetched records per search, bounded responses and no automatic paid retries. Missing official domain fails closed;
no guessed domains or LinkedIn scraping. Results are normalized, ranked, deduplicated by
email and limited to five; invalid and cross-domain emails are rejected. Unknown/catch-all
contacts are disabled by default and require explicit configuration and high confidence.
LinkedIn URLs can only come from the licensed provider and must be HTTPS profile URLs.

Gmail uses authorization code+PKCE, hashed one-time state, browser binding, ten-minute
expiry, audience-validated Google identity and verified email. Scopes are only `openid`,
`email` and `https://www.googleapis.com/auth/gmail.send`. Mail is plain-text MIME with a
separate To address for every recipient and the actual selected existing resume bytes.
Send rechecks ownership, approved recipients, mailbox, entitlement, immutable draft/resume
selection, attachment hash/size, quotas and idempotency. A different resume needs a new
review draft. There is no SMTP, app password or platform-owned sending mailbox.
Drafting reads that selected existing resume through IResumeStorage/IResumeTextExtractor;
only bounded relevant skill mentions and actual profile facts enter the composed draft.
Resume bytes and extracted text are never logged or supplied wholesale to the AI provider.
Legacy default-resume recovery uses the existing storage adapter rather than a new upload system.

Administrator-only `GET /api/admin/outreach?page=1&pageSize=20` exposes bounded operational
state/counts and safe failure codes. It never returns message bodies, recipient emails,
OAuth credentials or protected secret material. Structured logs include session/user/job,
provider, safe outcome, counts and elapsed milliseconds, never full draft/resume content.

The existing IAIApplyLanguageModel is reused, currently registered to DisabledAIApplyLanguageModel.
Default drafting is deterministic and factual. Optional AI can order approved factual
sentences; arbitrary generated candidate claims are never accepted. Missing/invalid/failed
AI output falls back to the approved draft. No new AI credentials or live AI activation is
required to use the feature.

## PROVIDER CONFIGURATION REQUIRED FROM MANOJ

Store values in environment/secret management, never in committed configuration or chat.
The following keys were absent in the implementation process environment; deployed secret
configuration must be checked separately. Configuration uses standard .NET `__` nesting.

| Key | Purpose | Required environments |
|---|---|---|
| `HiringOutreach__Enabled=true` | Explicit feature activation; defaults false | Local live testing and production |
| `HiringOutreach__Hunter__ApiKey` | Licensed Hunter domain search credential | Both for real discovery |
| `HiringOutreach__Google__ClientId` | Separate web OAuth client for Gmail | Both for real OAuth/send |
| `HiringOutreach__Google__ClientSecret` | Server-only OAuth secret | Both |
| `HiringOutreach__Google__CallbackUri` | Exact API callback URL | Both |
| `HiringOutreach__Google__FrontendReturnUri` | Fixed allow-listed frontend return URI | Both |
| `HiringOutreach__DataProtection__CertificateBase64` | Password-protected RSA PFX as Base64, loaded in memory | Production mandatory |
| `HiringOutreach__DataProtection__CertificatePassword` | PFX password from secret storage | Production mandatory |

Keys persist through the existing PostgreSQL `DefaultConnection`, encrypted with the PFX.
No persistent disk or certificate file is required. Production startup fails closed when
outreach is enabled without this configuration. Existing explicit AIApply filesystem key
rings require a planned migration before enabling the shared database repository; they
are never silently replaced. See [Data Protection operations](hiring-data-protection.md).

Register `https://<api-host>/api/candidate/outreach/mailbox/google/callback` as an exact
authorized redirect URI in a Google Cloud **Web application** OAuth client. Enable Gmail
API, configure branding/support/privacy-policy/domain verification, appropriate consent
screen audience and test users, and complete Google's required OAuth verification before
public rollout. Configure the real frontend HTTPS origin in the existing CORS allow-list.
The browser navigates to the returned authorizationUrl: its protected top-level handshake
sets a first-party binding cookie before redirecting to Google, avoiding third-party-cookie
dependencies when API and frontend are on different sites. The callback state remains
one-time and browser-bound. The handshake ticket is encrypted, expires in ten minutes and
cannot redirect to arbitrary hosts.
Only the configured fixed return URI is used, never a request-supplied redirect URL.
HTTPS is required outside loopback. Never reuse Google login client tokens as mailbox tokens.
Reverse proxies/APM must redact callback query strings (code/state), Authorization headers,
cookies, OAuth handshake tickets, token POST bodies and Hunter query API keys. Named provider HttpClients remove
their request loggers; existing API request logging logs paths, not callback query values.

Limits are configuration-driven: `CacheDays` (7), `MaximumContacts` (5),
`PromotionalLifetimeDiscoveries` (1), `DailyDiscoveries` (3), `DailySends` (3),
`MonthlySends` (10), `MaximumAttachmentBytes` (5242880), `AllowUnverifiedContacts` (false),
`MinimumUnverifiedConfidence` (90), `AiDraftingEnabled` (false).
`HiringOutreach__PlanMonthlyDiscoveries__<existing-plan-code>=<limit>` grants monthly
discovery allowance only to an existing active membership. No billing plan is hardcoded or
activated. Promotion and monthly allowance use the larger available entitlement, not two
charges or additive balances. Paid entitlement is rechecked at send; promotional sessions
remain valid only through their discovery TTL. Send quotas are per recipient.

Before rollout: review provider license/contact retention policy, verify durable protected
key storage and secret injection, review the additive migration, apply it through normal
authorized deployment procedures, then smoke-test with consenting test Gmail accounts.
No migration execution or production send is authorized by this implementation task.

References: [Hunter API](https://hunter.io/api-documentation/),
[Google server OAuth](https://developers.google.com/identity/protocols/oauth2/web-server),
[Gmail MIME sending](https://developers.google.com/workspace/gmail/api/guides/sending).

## Implementation file inventory

Added:

- `JobPortal.Domain/Entities/HiringOutreachEntities.cs`
- `JobPortal.Application/Features/HiringOutreach/HiringOutreachContracts.cs`
- `JobPortal.Application/Features/HiringOutreach/HiringContactPolicy.cs`
- `JobPortal.Infrastructure/HiringOutreach/HiringProviderAdapters.cs`
- `JobPortal.Persistence/Configurations/HiringOutreachConfigurations.cs`
- `JobPortal.Persistence/Context/HiringOutreachPersistencePolicy.cs`
- `JobPortal.Persistence/Repositories/HiringOutreachService.cs`
- `JobPortal.API/Controllers/CandidateHiringOutreachController.cs`
- `JobPortal.API/Controllers/AdminHiringOutreachController.cs`
- `JobPortal.Application.Tests/HiringOutreachTests.cs`
- `JobPortal.Application.Tests/HiringProviderAdapterTests.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261122180401_AddCandidateHiringTeamOutreach.cs`
- `JobPortal.Persistence.Postgres/Migrations/20261122180401_AddCandidateHiringTeamOutreach.Designer.cs`
- `docs/architecture/hiring-team-outreach-phase1.md`

Modified:

- `JobPortal.Persistence/Context/JobPortalDbContext.cs`
- `JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.API/Program.cs`
- `JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs`

Migration inspection: seven new tables, 29 indexes and 17 restrictive foreign keys.
The Up method has no alterations, renames or drops of existing objects. It was applied
only to the explicitly disposable localhost `hiring_outreach_verify` database for tests.
Existing migrations and `accelerator-production-migration.sql` are unchanged.

Verification uses `MSBuildEnableWorkloadResolver=false`, sequential builds, and
`/p:OutputPath=bin/HiringOutreachVerification/` so a running local API cannot lock the
normal build output. Tests use the same output property with `--no-build --no-restore`.
PostgreSQL tests require `HIRING_OUTREACH_TEST_POSTGRES` and reject any host/database
other than `127.0.0.1:55439/hiring_outreach_verify`. Other databases, including Neon,
are never used by the outreach tests. All providers and their HTTP transports are fakes.

Final verification:

- Solution build succeeded: zero warnings, zero errors.
- 41 new outreach/provider tests and eight existing job-aggregation DI checks: 49 passed,
  zero failed, zero skipped. Includes real PostgreSQL duplicate-discovery/send constraints.
- Full application suite: 3,691 passed, zero failed, 110 skipped (3,801 total).
- The initial broad run found an existing duplicate scoped IJobRepository registration.
  The identical duplicate was removed without changing repository/ingestion implementation
  or assertions; the original DI tests and final full suite then passed.
- EF reports no pending model changes after the new migration.
- Older PostgreSQL integration fixtures were not enabled; their skips are not passes.
- Hunter/Gmail live credentials and Google Cloud configuration were not available or used.
  Production activation still requires the setup and consenting-account smoke test above.

TRX evidence is retained under `JobPortal.Application.Tests/TestResults/`:
`hiring-outreach-focused-final.trx`, `hiring-outreach-regression.trx` (initial diagnostic
run), and `hiring-outreach-regression-final.trx` (final passing regression).
