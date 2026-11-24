# Known-person professional email waterfall

The existing Coresignal discovery and Collect validation are unchanged. After validation,
the existing professional-email-finder contract now tries Prospeo → Snov → Hunter, stopping
at the first verified, exact-domain email for the same name. The existing orchestrator
selects identities; there is no second provider architecture or billing ledger.

Official documentation reviewed:
- [Prospeo Enrich Person](https://prospeo.io/api-docs/enrich-person)
- [Prospeo person fields](https://prospeo.io/api-docs/person-object)
- [Prospeo company fields](https://prospeo.io/api-docs/company-object)
- [Snov API authentication and name/domain finder](https://snov.io/api)

Prospeo: POST `https://api.prospeo.io/enrich-person`, `X-KEY` header,
`only_verified_email=true`, `enrich_mobile=false`, actual name parts and company website.
Returned name/current company, `person.email.status=VERIFIED`, revealed email and exact
business domain must match. HTTP 400 `NO_MATCH` is a normal empty result. No mobile or
personal email is requested. Other profile data returned by the API is discarded.

Snov: POST `/v1/oauth/access_token` with form-encoded client_credentials; Bearer token on
POST `/v2/emails-by-domain-by-name/start` and GET
`/v2/emails-by-domain-by-name/result?task_hash=...`. One name/domain row per task, one task
start per identity, at most three bounded result polls. A pending task is not restarted.
Results require matching task hash/name, `smtp_status=valid`, valid format, exact company
email domain and explicit non-webmail/non-disposable/non-gibberish flags. Unknown/catch-all
results are rejected. Scoped tokens are reused only until bounded expiry, never persisted.

MaximumRequests means logical person lookups, capped at two/provider/discovery; Snov can
add one scoped authentication request and up to three result GETs per lookup. Every request
shares the lookup timeout; no paid task/start request is automatically retried. The existing
150-second discovery deadline and candidate quota/claims remain authoritative. Provider
failures continue to the next provider; ambiguous outcomes retain quota uncertainty even
when a later provider succeeds. Duplicate identities are not dispatched again.

Configuration (defaults; secret placeholders are not credentials):
```text
HiringOutreach__Prospeo__Enabled=false
HiringOutreach__Prospeo__ApiKey=<secret-store>
HiringOutreach__Prospeo__MaximumRequests=2
HiringOutreach__Prospeo__TimeoutSeconds=10
HiringOutreach__Prospeo__AllowCandidateDisplay=false
HiringOutreach__Prospeo__AllowSharedPersistence=false
HiringOutreach__Snov__Enabled=false
HiringOutreach__Snov__ClientId=<secret-store>
HiringOutreach__Snov__ClientSecret=<secret-store>
HiringOutreach__Snov__MaximumRequests=2
HiringOutreach__Snov__TimeoutSeconds=15
HiringOutreach__Snov__AllowCandidateDisplay=false
HiringOutreach__Snov__AllowSharedPersistence=false
```
Enabled options validate credentials, 1–2 lookups and 1–20 second timeouts. Disabled or
unconfigured providers do not dispatch. Admin probes may exercise enabled providers with
display disabled, using the existing protected credit-acknowledged endpoint. Responses add
`emailProvider` (Prospeo/Snov/Hunter), never the email itself. Candidate and shared snapshot
reads/writes require approval from every provider in composite provenance, in addition to
the unchanged Coresignal and global shared gates. Written rights are required before
candidate redistribution or sharing; configuration is not proof of permission.

Named HTTP clients suppress request loggers, redirects and cookies. Structured logs contain
provider names and fixed outcomes only. External tracing must separately redact URLs,
authentication headers and request bodies. No production configuration, frontend, Gmail,
Coresignal discovery or schema is changed. Automated provider tests use mocked HTTP only.

## Verification

Restore completed under the installed SDK 10.0.404 after the initial no-restore build
reported a missing targeting pack. No target framework or package reference was changed.
Sequential solution build then succeeded: zero warnings, zero errors. Focused Coresignal,
professional-email and orchestration tests: 109 passed, zero failed, zero skipped.
Related regression: 733 passed, zero failed, 13 skipped (unconfigured opt-in PostgreSQL
fixtures, including three Hiring checks). Reports are `hiring-email-waterfall-focused.trx`
and `hiring-email-waterfall-regression.trx` under `JobPortal.Application.Tests/TestResults/`.
EF's compiled pending-model check reported no changes; no migration was generated or
applied. No live provider calls, production changes, frontend edits, commits, pushes or
deployment occurred. Licensing and real provider coverage remain unverified.

## Files

Modified:
- `JobPortal.Application/Features/HiringOutreach/HiringOutreachContracts.cs`
- `JobPortal.Application/Features/HiringOutreach/CoresignalHiringContracts.cs`
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Infrastructure/HiringOutreach/HiringProviderOrchestrator.cs`
- `JobPortal.Infrastructure/HiringOutreach/HiringProviderDiagnostics.cs`
- `JobPortal.Persistence/Repositories/HiringOutreachService.cs`
- `JobPortal.Persistence/Repositories/HiringOutreachSharedContacts.cs`
- `JobPortal.Application.Tests/HiringOutreachTests.cs`

Added:
- `JobPortal.Application/Features/HiringOutreach/HiringEmailProviderOptions.cs`
- `JobPortal.Infrastructure/HiringOutreach/ProspeoHiringProfessionalEmailFinder.cs`
- `JobPortal.Infrastructure/HiringOutreach/SnovHiringProfessionalEmailFinder.cs`
- `JobPortal.Infrastructure/HiringOutreach/HiringProfessionalEmailWaterfall.cs`
- `JobPortal.Application.Tests/HiringProfessionalEmailProviderTests.cs`
- `JobPortal.Application.Tests/HiringEmailWaterfallPersistenceTests.cs`
- `docs/architecture/hiring-professional-email-waterfall.md`
