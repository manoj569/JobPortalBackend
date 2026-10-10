# Referral notifications Phase 3A

## Audit before implementation

- `JobReferralService.SubmitAsync` creates a draft job and pending referral; it has no administrator notification.
- `ReviewAsync` already creates an approval inbox entry and enlists approval/rejection email and in-app intents. Preserve the approval notification ID so the existing inbox and outbox do not create duplicates.
- `ReferralMarketplaceService.Event` already enlists requested, accepted, rejected, submitted, confirmed and not-received events inside `ReferralMarketplaceRepository.WriteAsync`. The repository commits requests and outbox together, using PostgreSQL transaction/advisory locks. Existing messages are generic and use `/dashboard/referrals` for every event.
- `NotificationDeliveries` is the durable outbox. Deterministic IDs, unique recipient/channel/business keys, recoverable leases, bounded exponential retries and committed-source eligibility checks already exist. `NotificationDeliveryHostedService` runs the dispatcher. Brevo runs outside business transactions.
- `Notifications` is the existing inbox; authenticated dashboard pagination, unread counts, mark-read endpoints and user-scoped SignalR are already implemented. The DTO does not expose business keys.
- `BrevoEmailService` uses the configured sender, named HTTP client and `AppUrls:FrontendBaseUrl`. Notification emails currently contain plain text; HTML notification templates are absent.
- Users have registered `Email` and `EmailConfirmed`; email/password sign-in requires confirmation and Google/admin bootstrap confirms account email. There is no separate referral contact-email field. `ShowEmail` exposes the registered email only through existing accepted-request contact authorization, not as an alternative delivery address.
- Requested referrals have an existing **48-hour expiry**, calculated by `EffectiveStatus`; no expiry writer or notification exists. Confirmation and not-received are real existing transitions. Candidate-facing request detail does not expose referrer rejection notes, so those private notes must stay out of candidate notifications.
- PostgreSQL is the configured provider; migrations live in `JobPortal.Persistence.Postgres`. The notification foundation and referral marketplace migrations already supply the necessary tables, columns and uniqueness constraints.
- The repository has backend admin approval APIs but no frontend implementation or confirmed administrator approval-page route. The frontend path must be supplied through configuration; do not invent a page or endpoint.

## Implementation scope

Extend the existing event producers and worker, rather than introduce duplicate handlers or a delivery framework. Add admin submission alerts, meaningful lifecycle wording and role-specific routes, verified-account referral email delivery, escaped branded HTML and an existing-worker sweep for effective expiry and configurable pending reminders. Keep all referral state transitions, membership/quota/contact/resume rules and inbox API shapes unchanged. No schema migration is required for additional integer enum values.

Pending reminders default to 24 hours after request creation, while the existing expiry remains 48 hours. Only still-pending requests are eligible, and repeated sweeps deduplicate the reminder. Expiry notifications observe the existing effective status and do not mutate request state.

## Events delivered

| Event | Before | Phase 3A |
| --- | --- | --- |
| Job submitted for approval | No alert | Active administrators receive durable inbox intents. Email requires a verified registered address, enabled email and a configured approval-page path. |
| Job approved | Already supported | Existing inbox identity retained; accurate availability wording and job/company included. |
| Job rejected | Already supported | Job/company included. Only a matching existing non-personal reason from the small public-reason allowlist is included; arbitrary review notes remain behind authentication. |
| Candidate requested | Already supported | Owning referrer receives job/company and `/dashboard/referral-requests`. |
| Request accepted | Already supported | Candidate receives `/dashboard/my-referral-requests`; explicitly not yet submitted. |
| Request rejected | Already supported | Respectful candidate status update; existing private rejection notes are excluded. |
| Marked submitted | Already supported | Candidate notified of the referrer's assertion, without employer confirmation or selection claims. |
| Candidate confirmed / not received | Already supported | Referrer notified with accurate wording and own request-inbox route. |
| Request effectively expired | No alert | Existing effective expiry observed by the worker; both participants notified once; status not mutated. |
| Pending reminder | No alert | Configurable existing-worker sweep, stable per-request interval keys and dispatch-time status/ownership checks. Newer reminders supersede stale retries. |

## Configuration and rollout

Existing Brevo settings are unchanged: `Email:Enabled`, `Email:FromName`, `Email:FromAddress`, `Email:Brevo:ApiKey` and `AppUrls:FrontendBaseUrl`. Use the intended public HTTPS frontend URL and a Brevo-authorized sender in production. No production credential values were read or changed, and no live email was sent.

New optional settings under `ReferralNotifications`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `AdminApprovalPath` | null | Set to the existing frontend approval-page path under `/admin/` or `/dashboard/admin/`. Only plain internal path segments are allowed; no queries, tokens, hosts or traversal. No page was invented. Without it, admin inbox alerts have no link and admin email is cancelled with `admin_route_missing`. Configure before rollout. |
| `AdminEmailEnabled` | true | Additional administrator email switch. Disabling it preserves inbox alerts. |
| `RemindersEnabled` | true | Enables the reminder sweep. |
| `PendingReminderAgeHours` | 24 | Pending referrer reminder starts 24 hours after request creation; existing expiry remains 48 hours. |
| `ReminderIntervalHours` | 24 | At most one intent per request per configured interval; no overdue reminder backfill. |
| `SweepBatchSize` | 20 | Bounded expiry and pending-request batches. |

`NotificationDelivery` polling, batch size, lease, maximum attempts and bounded backoff are preserved. The sweep may pick up historical effective expiries in bounded batches; account eligibility still applies. No scheduling framework, endpoint, user email field or business status was added.

The model-only test confirms no migration delta. Deployment still requires the existing centralized-notification and referral-marketplace migrations to have been applied through the normal controlled process. No database was modified during this work.

Inbox response shape, pagination, unread counts, ownership/read endpoints and SignalR payload fields are unchanged. Existing enum values remain intact; new referral event types are appended. Ensure the frontend renders unknown/additional notification types using the existing title, message and action fields. Authenticated backend controls still protect candidate cards, resume downloads, contacts and administrator actions.

## Delivery guarantees and limits

- Business writes and durable intents commit together. No business method calls Brevo. Failed persistence leaves no observable committed intent; email failure cannot reverse a committed business action.
- Existing stable IDs/business keys and unique constraints deduplicate lifecycle intents and inbox rows. Scheduler inserts use PostgreSQL `ON CONFLICT DO NOTHING` in a transaction, allowing concurrent sweeps and retries.
- In-app materialization commits before realtime or email. Active recipient, source state, ownership, administrator role and reminders' pending status are checked against committed data. Referral email uses only verified registered account email; unverified email is cancelled while inbox delivery remains available.
- Provider retries reuse the notification UUID as Brevo's `idempotencyKey`; a confirmed idempotency-duplicate response is treated as already accepted. Permanent failures stop; transient failures and rate limits use the existing bounded retry policy. Logs contain only safe status/correlation metadata, not provider response bodies, email addresses, template contents, tokens, keys or private references.
- HTML is branded, responsive, encoded and has one configured internal-route CTA. Private candidate messages, profile/contact data, resumes, submission references and arbitrary rejection notes are excluded.
- Brevo acceptance is not proof of inbox receipt. No live authorized integration test was run. Exactly-once provider delivery cannot be guaranteed across a crash after send and a recovery beyond Brevo's finite deduplication window. Current [Brevo documentation](https://developers.brevo.com/docs/heterogenous-versions-batch-emails) describes a 30-minute TTL; the default retry schedule fits inside that window, but prolonged outages can exceed it.
- A business transition can race the last eligibility check and an HTTP send. No business transaction is held open over external HTTP. Committed timestamps and event wording describe historical acceptance/submission rather than promising a current employer outcome.

## Exact files changed by Phase 3A

Unrelated pre-existing job ingestion edits were left unchanged.

- `JobPortal.API/HostedServices/NotificationDeliveryHostedService.cs`
- `JobPortal.API/Program.cs`
- `JobPortal.Application/Abstractions/Referrals/IJobReferralService.cs`
- `JobPortal.Application/Features/Notifications/NotificationDispatcher.cs`
- `JobPortal.Application/Features/Notifications/NotificationOutbox.cs`
- `JobPortal.Application/Features/Referrals/JobReferralService.cs`
- `JobPortal.Application/Features/Referrals/ReferralMarketplaceService.cs`
- `JobPortal.Application/Features/Referrals/ReferralNotifications.cs` (new)
- `JobPortal.Domain/Entities/NotificationDelivery.cs`
- `JobPortal.Domain/Enums/DomainEnums.cs`
- `JobPortal.Infrastructure/Services/BrevoEmailService.cs`
- `JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Persistence/Repositories/JobReferralRepository.cs`
- `JobPortal.Persistence/Repositories/NotificationDeliveryRepository.cs`
- `JobPortal.Persistence/Repositories/ReferralNotificationScheduler.cs` (new)
- `JobPortal.Application.Tests/BrevoEmailServiceTests.cs`
- `JobPortal.Application.Tests/JobReferralServiceTests.cs`
- `JobPortal.Application.Tests/NotificationDispatcherTests.cs`
- `JobPortal.Application.Tests/ReferralMarketplacePostgresTests.cs`
- `JobPortal.Application.Tests/ReferralNotificationTests.cs` (new)
- `docs/referral-notifications-phase3a.md` (new)

## Verification results

- Final API Release build, including referenced Application, Domain, Infrastructure, Persistence and PostgreSQL migration projects: **PASS**, zero warnings and zero errors with normal analyzer settings. Command: `dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore`.
- Final focused test set: **155 passed, 0 failed, 7 skipped**. Covers referral membership/ownership/quotas/approval, transition idempotency, event recipients/wording, failed persistence, verified email, administrator delivery switches/routes, escaped HTML, provider failure/deduplication, reminder cancellation/interval keys, expiry without state mutation, notification DTO/realtime/dashboard compatibility and the no-migration model check.
- Compilation for the test invocation uses `-p:RunAnalyzers=false` because this repository already has test-project analyzer failures; no project or analyzer settings were changed. Production sources were independently built with normal analyzers.
- Seven real-PostgreSQL integration tests (including new concurrent sweep and delivery-failure tests) are skipped: neither `REFERRAL_TEST_POSTGRES` nor `NOTIFICATION_TEST_POSTGRES` is configured. They only permit named disposable localhost test databases and never consume application connection settings.
- Two untouched tests found through the broader notification filter remain failing and were explicitly excluded from the final focused set: `NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation` expects a nonempty delta, although the existing snapshot now matches the model; `NotificationEligibilityTests.CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment` fails in existing career-session fixture setup before the notification repository is invoked. They were rerun separately and reproduced. The new Phase 3A model test confirms an empty delta. No unrelated test or career code was changed.
- The interview worker's timing-sensitive retry test failed in the initial parallel run; all four existing interview-worker tests passed on separate rerun and in the final focused run.
- `git diff --check`: PASS. No migration, database update, live Brevo send, commit, push or deployment was performed.

Final focused command:

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore --filter "(FullyQualifiedName~Referral|FullyQualifiedName~Notification|FullyQualifiedName~BrevoEmailServiceTests|FullyQualifiedName~Dashboard)&FullyQualifiedName!~PendingSchemaDeltaIsLimitedToTheNotificationFoundation&FullyQualifiedName!~CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment" -p:RunAnalyzers=false
```

Before production rollout, configure and verify the existing admin approval-page route, ensure the existing migrations and frontend enum fallbacks are deployed, verify the configured 24-hour reminders before the unchanged 48-hour expiry, run the opt-in PostgreSQL tests and perform an explicitly authorized Brevo integration test with intended recipients. Existing full-suite/analyzer failures remain separate CI blockers; this change does not repair unrelated fixtures or analyzer findings.
