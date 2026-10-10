# Phase 3A backend integration and release verification

This report supersedes the route/database availability findings in the previous pre-deployment report. The frontend at `D:\career-portal-frontend\career-portal-frontend-Run` was inspected read-only. No frontend, business rule, payment, membership, quota or unrelated job-ingestion code was changed.

## Confirmed frontend destinations

| Destination | Route | Evidence |
| --- | --- | --- |
| Admin referral approval | `/admin/jobs` | `src/constants/routes.ts` ADMIN_JOBS; `src/routes/routeConfig.tsx` AdminRoute branch; `src/pages/admin/AdminJobsPage.tsx` pending referral buttons call approve/reject mutations in `features/admin/api/adminApi.ts`. |
| Referrer incoming requests | `/dashboard/referral-requests` | routeConfig maps to ReferralRequestsPage; individual requests use `/dashboard/referral-requests/:requestId`. |
| Candidate outgoing requests | `/dashboard/my-referral-requests` | routeConfig maps to CandidateReferralRequestsPage; details append `/:requestId`. |
| Referrer shared jobs | `/dashboard/referrals` | ROUTES.REFERRALS maps to AllReferralsPage. |
| Notification inbox | `/dashboard/notifications` | ROUTES.NOTIFICATIONS and routeConfig. |

Backend `appsettings.json` now configures `ReferralNotifications:AdminApprovalPath=/admin/jobs` and reminder age 24. Render environment overrides take precedence: replace any old/unverified route override. No `/admin/referrals` frontend page was invented. AdminRoute requires an authenticated Administrator; backend approval/rejection endpoints also enforce Administrator authorization.

The frontend changed independently during this verification. Its final `notificationDestination.ts` and `useNotificationActions.ts` now pass the current role and resolve Administrator submission alerts to `/admin/jobs`; candidates/employers cannot navigate to admin operations through this resolver. AdminDashboardLayout uses the notification bell. Its numeric enum matches backend values 0–16. These changes were only observed read-only, not made or frontend-tested by this backend task. Deploy the verified role-aware frontend revision alongside the configured backend. The full `/dashboard/notifications` page is under CandidateRoute; administrators use their notification bell rather than that candidate-only page.

## Events and delivery checks

| Event | Actual recipient | Meaning/behavior |
| --- | --- | --- |
| Job submitted | Active administrators | Awaiting approval; configured admin link. |
| Approved / rejected | Owning referrer | Approval says available; rejection includes only allowlisted public reason, never arbitrary review notes. |
| Candidate requested | Owning referrer | Review candidate request. |
| Accepted | Candidate | Accepted, explicitly not yet submitted. |
| Request rejected | Candidate | Respectful declined wording; private referrer notes excluded. |
| Marked submitted | Candidate | Referrer assertion, not employer receipt or selection. |
| Confirmed | Referrer | Existing candidate confirmation action. There is no separate employer/referrer-confirmed event that notifies the candidate. Preserved existing business semantics rather than adding a candidate notification. |
| Not received | Referrer | Candidate report needing review. |
| Effectively expired | Candidate and owning referrer | Existing pending 48-hour expiry; scheduler does not mutate business status. |
| Pending reminder | Owning referrer | At 24 hours, only still Requested and unexpired. |

Business operations enlist durable channel intents and save/commit them with their business changes; none send provider email directly. Failed business persistence leaves no observable committed intents. Transition idempotency, deterministic recipient/channel/event keys and unique database constraints prevent repeated intents. The dispatcher checks committed source state, active recipient, ownership and current Administrator role; emails additionally require the registered account EmailConfirmed. In-app delivery remains available for an unverified email account.

All referral types use the existing encoded branded HTML template plus text fallback and a configured internal CTA. Private request messages, contact details, resumes, employer submission references and arbitrary rejection notes are omitted. Leased delivery and transactional `ON CONFLICT DO NOTHING` inbox materialization precede email; retrying provider delivery does not create another inbox row. Realtime delivery failure does not roll back the inbox.

## Notification API contract

All routes are authenticated under **`/api/dashboard`**, not bare `/dashboard`:

- `GET /api/dashboard/notifications?pageNumber=1&pageSize=20&isRead=false`: optional isRead filter; pageNumber >=1, pageSize 1–100, defaults 1/20. Invalid pagination returns 400. Descending createdAtUtc then id. Owner-scoped, excludes soft-deleted rows. unreadCount counts all unread notifications for the user, independently of filter/paging.
- `PUT /api/dashboard/notifications/{notificationId:guid}/read`: 204 No Content, idempotent; 404 for missing or another user's notification. Sets isRead and readAtUtc only on first read.
- `PUT /api/dashboard/notifications/read-all`: 204 No Content; updates only current user's unread rows.

GET success envelope is `{ "data": { "page": { "items": [...], "pageNumber": 1, "pageSize": 20, "totalCount": 0, "totalPages": 0 }, "unreadCount": 0 }, "message": null }`. Notification fields are `id` (UUID), `title`, `message`, `type` (numeric), `actionUrl` (nullable), `isRead`, `readAtUtc` (nullable UTC timestamp), `createdAtUtc` (UTC timestamp). Timestamps serialize as ISO-8601; unread rows have isRead=false/readAtUtc=null. No business key, provider payload, delivery status or private source data is exposed. Follow null action URLs as non-clickable items. Bearer authentication and existing API error conventions apply; no API contract changed.

| Value | NotificationType |
| --- | --- |
| 0–5 | Application, Profile, Payment, Membership, Security, System |
| 6 | ReferralApproved |
| 7 | ReferralJobSubmitted |
| 8 | ReferralRejected |
| 9 | ReferralRequested |
| 10 | ReferralAccepted |
| 11 | ReferralRequestRejected |
| 12 | ReferralSubmitted |
| 13 | ReferralConfirmed |
| 14 | ReferralNotReceived |
| 15 | ReferralExpired |
| 16 | ReferralRequestReminder |

## Render scheduling, Brevo and migration checklist

The scheduler runs inside the existing API BackgroundService registered by Program.cs, before each delivery poll (default 15 seconds). It uses persisted request timestamps/outbox rows rather than an in-memory timer per request. PostgreSQL unique keys and delivery leases support multiple active instances and restart recovery. The default reminder age/interval 24/24 yields one reminder before the unchanged 48-hour expiry; accepted, rejected or expired requests are excluded and previously queued reminders are cancelled during dispatch. Actual sending depends on worker uptime and queue capacity. A sleeping/scaled-to-zero Render service cannot run the sweep at 24 hours; if it wakes after expiry, it sends no stale pending reminder. Keep an API instance continuously running for this requirement. This checkout does not prove the actual Render instance plan, uptime, secrets or deployed schema; those remain operator checks.

Required production configuration:

- `ReferralNotifications__AdminApprovalPath=/admin/jobs`
- `ReferralNotifications__PendingReminderAgeHours=24`
- `ReferralNotifications__ReminderIntervalHours=24`
- `ReferralNotifications__RemindersEnabled=true`
- `ReferralNotifications__AdminEmailEnabled=true` for admin email (false preserves inbox).
- `ReferralNotifications__SweepBatchSize=20` default, validated 1–100.
- `AppUrls__FrontendBaseUrl`: exact verified public HTTPS frontend origin.
- Existing `Email__Enabled=true`, `Email__FromName`, Brevo-authorized `Email__FromAddress`, secret `Email__Brevo__ApiKey` in Render secret settings.
- Existing `NotificationDelivery__PollSeconds=15`, `BatchSize=20`, `LeaseSeconds=180`, `MaxAttempts=5`, `RetrySeconds=60` defaults; do not configure unbounded retries or unsafe leases.

Sender and API key configuration names and usage are verified in code; actual deployed credentials/sender authorization were not inspected. Mock transport tests verify escaped HTML, registered recipient, stable idempotency header and status handling. Logs retain safe failure codes/correlation metadata, not keys, recipient addresses, provider response bodies or private template data. No real emails were sent.

Provider retries reuse the notification UUID as Brevo's idempotency key, treating recognized duplicate-key acceptance as Sent. Current [Brevo documentation](https://developers.brevo.com/docs/heterogenous-versions-batch-emails) describes a 30-minute deduplication TTL. Default backoff normally fits, but delayed recovery, outages, custom retry settings or a crash after acceptance can exceed it. Exactly-once external email delivery is not guaranteed; inbox uniqueness is independent of provider deduplication. Already-cancelled admin emails are not automatically replayed after changing the route.

No new migration is required: configuration changes only, with existing Phase 3A model/snapshot parity test. Existing centralized-notification and referral-marketplace migrations must be present in the deployed database, verified through the controlled release process. No production database was accessed or updated.

## Validation and recommendation

Release API build with normal analyzers: PASS, 0 warnings, 0 errors. Log: `../phase3a-integration-api.log`.

Focused functional/outbox tests: PASS, 158 passed, 0 failed, 0 skipped; PostgreSQL tests excluded because they run separately. Log: `../phase3a-integration-focused.log`. Run uses `dotnet test ... -c Release --no-build --no-restore`; existing Release test binaries were previously compiled with command-line `-p:RunAnalyzers=false`. The fixture-only rebuild also uses `-p:RunAnalyzers=false -p:BuildProjectReferences=false`; no global analyzer settings were edited. Production API compilation uses normal analyzers.

Unrelated tests rerun separately: 2 failed, 0 passed, 0 skipped. Failures remain `NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation` (stale expectation of a nonempty model delta) and `NotificationEligibilityTests.CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment` (career-session fixture setup eligibility). Log: `../phase3a-integration-unrelated.log`. The 184 pre-existing test-project analyzer errors remain separately recorded with identical normalized saved-baseline diagnostics in the preceding verification. Unrelated tests/production code were not changed to hide failures.

Disposable PostgreSQL verification: **PASS, all 7 passed, 0 failed, 0 skipped on final runs**. Six referral tests passed in `../phase3a-integration-postgres-referral-final.log`; the corrected lease test passed in `../phase3a-integration-postgres-lease-final.log`. Coverage includes concurrent acceptance/quota enforcement, unique requests/intents, concurrent expiry sweeps, committed acceptance despite provider failure, non-repeated successful delivery, lease exclusivity/recovery, old-owner fencing and single inbox materialization.

Tests used a fresh PostgreSQL 16 container bound to localhost only, with no persistent volume or production credentials, holding only `careerharbor_referral_test` and `notification_test`. First full run: 1 passed, 6 failed, 0 skipped; errors were connection/query/cleanup read timeouts. The isolated lease retry also timed out. A direct localhost startup handshake took 10.5 seconds, exceeding the original five-second fixture timeout. Only these test fixtures were adjusted to 60-second connections and 120-second commands; no production timeout was changed. The next lease run exposed missing nullable `Notifications.UserId1` in its hand-written minimal schema. This column already exists in the initial PostgreSQL migration and current model snapshot; the disposable fixture alone was corrected. A rebuild copy failure caused by overlap with the referral test process was resolved after that process exited. Final fixture rebuild passed with 0 warnings/errors using the disclosed analyzer override. No provider email was sent in these tests.

PostgreSQL commands used process-scoped `REFERRAL_TEST_POSTGRES`/`NOTIFICATION_TEST_POSTGRES` pointing only at `127.0.0.1:54760`, named disposable databases, username postgres, SSL disabled only for this local test container. Classes ran separately with `dotnet test ... -c Release --no-build --no-restore --filter FullyQualifiedName~ReferralMarketplacePostgresTests` and `FullyQualifiedName~NotificationPostgresTests`. The container is stopped/removed after verification; no test database is retained.

`git diff --check`: PASS. **Overall recommendation: NO-GO until the existing normal-CI analyzer/test blockers are resolved or explicitly accepted through the release process.** Scoped Phase 3A backend verification passes, and route/database verification is no longer blocked. Before controlled deployment, verify Render has an always-running API instance, the environment values above, the existing migrations, a Brevo-authorized sender, and the verified role-aware frontend revision. Actual Render configuration and provider authorization are operator checks, not claimed as verified from local tests.

Files changed in this pass:

- `JobPortal.API/appsettings.json`: confirmed admin approval route and explicit reminder age.
- `JobPortal.Application.Tests/ReferralMarketplacePostgresTests.cs`: disposable fixture timeouts only.
- `JobPortal.Application.Tests/NotificationPostgresTests.cs`: disposable fixture timeouts and missing existing `UserId1` column.
- `docs/referral-notifications-phase3a-integration-release.md`: this contract, audit and release report.

Existing implementation and unrelated working-tree edits are preserved. No frontend writes, production data, real emails, commit, push or deployment.
