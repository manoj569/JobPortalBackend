# Phase 3A pre-deployment verification

## Recommendation: NO-GO

The frontend administrator approval route cannot be verified from this backend checkout or its documented configuration. Backend `/api/admin/referrals` is Administrator-only but is not a frontend page. `AdminApprovalPath` remains unset; no route was invented. Admin inbox alerts still materialize, but admin emails without a route are cancelled. Already-cancelled emails are not automatically replayed after configuration changes.

The seven PostgreSQL integration tests remain BLOCKED, not passed: neither opt-in test connection is configured, no local PostgreSQL installation/service/listener was found, and Docker reports that its Linux engine pipe is unavailable. No production connection was used. Provision disposable local `careerharbor_referral_test` and `notification_test` databases, then supply `REFERRAL_TEST_POSTGRES` and `NOTIFICATION_TEST_POSTGRES` only to the test process. Fixtures guard the host/database and use disposable schemas.

## Reminder and delivery verification

The default pending reminder age is now 24 hours. Expiry stays 48 hours. Tests cover the exact threshold, duplicate sweeps and dispatch cancellation following acceptance, rejection and effective expiry. With the default 24-hour repeat interval, the next interval is at expiry and is suppressed. Scheduling is subject to worker polling and batch capacity, rather than a guarantee of sending at the exact second.

Recipients reviewed: active administrators for job submission; owning referrer for job approval/rejection; owning referrer for candidate request, confirmation, not-received and reminder; candidate for acceptance/rejection/submission; both participants for effective expiry. Source state, active account, ownership and current admin role are revalidated at dispatch. Emails use verified registered account addresses only. Private request messages, contacts, resumes, submission references and arbitrary review notes are excluded.

Links are internal allowlisted paths based on the configured frontend origin. Admin operations remain Administrator-only and candidate/referrer request operations retain authentication and ownership checks. The separate candidate/referrer frontend pages cannot be independently verified in this backend-only checkout; verify `/dashboard/my-referral-requests` and `/dashboard/referral-requests` in the frontend before release. Existing `/dashboard/referrals` is documented. Links confer no backend authorization.

Existing transactional outbox, deterministic recipient/channel/event IDs, unique constraints, lease ownership fencing, bounded retries and transactional inbox inserts are reused. In-app delivery commits before email/realtime. Email retry does not create another inbox entry. PostgreSQL concurrency guarantees still require the blocked integration run.

Brevo retries reuse the notification UUID as the provider idempotency key. Recognized duplicate-idempotency responses are treated as accepted. Provider acceptance is not proof of delivery. Current [Brevo documentation](https://developers.brevo.com/docs/heterogenous-versions-batch-emails) documents a finite 30-minute deduplication window; crashes, delayed recovery or configured backoff beyond it can duplicate email. Provider exactly-once delivery is not guaranteed. Tests use mock HTTP/senders; no real email was sent.

## Render configuration checklist

| Variable | Required value/action |
| --- | --- |
| `ReferralNotifications__AdminApprovalPath` | Supply the verified existing frontend approval path. Safe internal `/admin/` or `/dashboard/admin/` path only; no query/token/host. Missing release configuration. |
| `ReferralNotifications__AdminEmailEnabled` | `true` for admin email; `false` preserves inbox only. |
| `ReferralNotifications__RemindersEnabled` | `true` for required reminders. |
| `ReferralNotifications__PendingReminderAgeHours` | Explicitly set `24`, overriding any old deployment value of `48`. |
| `ReferralNotifications__ReminderIntervalHours` | `24`; preserves one reminder before 48-hour expiry. |
| `ReferralNotifications__SweepBatchSize` | Default `20`; size within validated 1–100. |
| `Email__Enabled` | Enable for production email, never for a test with real recipients. |
| `Email__FromName`, `Email__FromAddress` | Existing configured sender; address authorized in Brevo. |
| `Email__Brevo__ApiKey` | Existing secret, managed in Render; do not put in source/logs. |
| `AppUrls__FrontendBaseUrl` | Verified public HTTPS frontend origin. |
| `NotificationDelivery__PollSeconds`, `BatchSize`, `LeaseSeconds`, `MaxAttempts`, `RetrySeconds` | Existing defaults 15, 20, 180, 5, 60; preserve bounded retry and lease behavior. |

No new migration is required. Existing centralized-notification and referral-marketplace migrations must already be applied by the controlled deployment process. The model/snapshot test verifies no schema delta without connecting to a database. No database update was performed.

## Files changed in this verification pass

- `JobPortal.Application/Features/Referrals/ReferralNotifications.cs`: reminder default 24 hours.
- `JobPortal.Application.Tests/ReferralNotificationTests.cs`: three threshold/dedup/cancellation test cases.
- `docs/referral-notifications-phase3a.md`: corrected reminder defaults.
- `docs/referral-notifications-phase3a-predeployment.md`: this verification and release checklist.

The existing Phase 3A implementation and unrelated working-tree changes were preserved. No commit, push, deployment or production data change was performed.

## Test results

Release API build with normal analyzers: PASS, 0 warnings, 0 errors (`dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore`). Log: `../phase3a-predeployment-api.log`.

Normal-analyzer test compilation: FAIL/BLOCKED by 184 existing analyzer errors. The saved pre-change `../interview-insights-verification/baseline-build.log` and current diagnostics have identical sets after normalizing line/column locations (161 distinct normalized messages). No diagnostics appear in the Phase 3A test files. Analyzer/project configuration was not edited. Log: `../phase3a-predeployment-normal-tests.log`.

Focused runtime tests with the disclosed command-line-only `-p:RunAnalyzers=false` override: PASS, **158 passed, 0 failed, 7 skipped**, total 165. Includes the three new 24-hour boundary/cancellation cases and the no-migration model test. The seven database skips are BLOCKED verification, not successes. Log: `../phase3a-predeployment-focused.log`.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore --filter "(FullyQualifiedName~Referral|FullyQualifiedName~Notification|FullyQualifiedName~BrevoEmailServiceTests|FullyQualifiedName~Dashboard)&FullyQualifiedName!~PendingSchemaDeltaIsLimitedToTheNotificationFoundation&FullyQualifiedName!~CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment" -p:RunAnalyzers=false
```

Separate unrelated-test rerun: **2 failed, 0 passed, 0 skipped**. Failures: `NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation` (expects nonempty schema delta despite matching snapshot) and `NotificationEligibilityTests.CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment` (career-session fixture eligibility failure). Their production code and tests are unchanged. Separate rerun log: `../phase3a-predeployment-unrelated.log`.

`git diff --check`: PASS. Release gates remain: verified admin/frontend links, disposable PostgreSQL integration coverage, and the existing test-project analyzer/test failures under normal CI settings.
