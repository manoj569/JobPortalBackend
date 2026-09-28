# Centralized CareerHarbor notifications

## Status and deployment prerequisite

This extends the existing inbox, Brevo sender, and SignalR hub. It does not introduce a broker, a second inbox, or another Career Guidance schedule. No migration has been generated or applied by this implementation task.

After review, generate **one** PostgreSQL migration (command supplied for the operator; not run):

```powershell
dotnet ef migrations add AddCentralizedNotificationDelivery --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext --configuration Release
```

Review the generated migration before deployment. Expected DDL:

- Nullable `Notifications.BusinessKey` (220), filtered unique `(UserId, BusinessKey)` index. Existing nullable-key inbox rows remain valid.
- `NotificationDeliveries`: logical notification ID, user FK (Restrict), channel/source/revision/business key, server-owned title/message/internal action, UTC scheduling/retry timestamps, status/attempt/lease/completion/safe failure state, standard audit/soft-delete fields.
- Unique `(BusinessKey, UserId, Channel)`; status/due, status/lease, source/revision and logical-notification/channel indexes; channel/status/attempt checks.
- Interview schedule `ReminderOffsetMinutes` default 30, `TimeZoneId` required/max 100/default UTC, `ReminderRevision` UUID concurrency token. Offset check permits exactly 15/30/60/1440. Existing rows need a safe UUID value but are **not** retroactively queued.
- Referral approval status is now an EF concurrency token (no new column).

There must be no unrelated alterations, salary changes, shadow FKs, new concurrency columns, or extra reminder tables. Do not deploy this application version before the schema migration is reviewed/applied; the hosted worker and new business writes require the new table/columns. No database update command is provided or executed here.

## Events and atomicity

`NotificationOutbox` enlists two deterministic delivery rows without saving or making external calls. The originating service's existing SaveChanges commits business state and intent together. Database failures fail that transaction; Brevo availability cannot affect it. Email/SignalR never run inside business transactions.

| Source | Recipient | Stable key before recipient suffix | Inbox behavior |
|---|---|---|---|
| InterviewReminder | Schedule owner | `interview-reminder:{scheduleId}:{reminderRevision}` | Created when due |
| CareerConfirmation | Candidate and consultant's account user | `career-confirmation:{bookingId}` | Created after actual capture/confirmation |
| CareerReminder | Existing reminder recipient | `career-reminder:{reminderId}` | Reuses existing reminder's deterministic inbox ID |
| ReferralApproved | Referrer | `referral:{referralId}:Approved` | Reuses existing approval inbox row |
| ReferralRejected | Referrer | `referral:{referralId}:Rejected` | Created by dispatcher |

Repeated capture callbacks exit on existing PaidAtUtc; late capture/refund review does not enqueue confirmation. Reminder processor retains existing 1440/60/10-minute offsets, eligibility and CAS logic. `Delivered` still means its inbox row was committed, not that an email was sent. Its transaction also enlists outbox intent. No second schedule is created. Referral decisions remain terminal; repeated reviews retain their existing conflict response and create no new intent. ApprovalStatus concurrency prevents competing opposite decisions from both committing.

## Interview contract

Existing create/update/list routes are unchanged. Requests and responses append:

```json
{
  "interviewAtUtc": "2026-11-01T05:30:00Z",
  "reminderRequested": true,
  "reminderOffsetMinutes": 30,
  "timeZoneId": "America/New_York"
}
```

`interviewAtUtc` must be explicit UTC (`Z`); unspecified/local timestamps are rejected, not converted using server-local timezone. Frontend converts the chosen local time to an unambiguous UTC instant, including the user's choice during a DST overlap. `timeZoneId` must be valid on the host; use IANA IDs across environments. Canonical due = UTC instant minus offset. Display text includes local date/time, UTC offset and zone. Default offset is 30 and zone UTC; ReminderRequested retains its existing false default.

Only changes to instant, reminder flag, offset, timezone or status rotate ReminderRevision. Identical updates and edits to role/preparation/format/rounds do not enqueue another generation; existing message snapshots retain their original content. A new generation with a past/present due time is rejected. Unrelated edits do not revalidate an already elapsed reminder. Old generations remain audit history but fail eligibility. Cancelled/completed/deleted schedules or disabled reminders are ineligible. The separate “How did your interview go?” feedback worker is unchanged.

## Worker and delivery guarantees

`NotificationDelivery:*` configuration (no appsettings changes):

| Setting | Default | Accepted |
|---|---:|---|
| PollSeconds | 15 | 1–3600 |
| BatchSize | 20 | 1–100 |
| LeaseSeconds | 180 | 120–3600 |
| MaxAttempts | 5 | 1–10 |
| RetrySeconds | 60 | 1–3600 |

Each worker claims one item just before processing, up to a bounded batch per poll. A conditional PostgreSQL UPDATE checks the attempt/status/expired lease and assigns a fresh lease owner. No process-local lock is the concurrency mechanism. Active leases cannot be stolen; expired leases recover. Completion is fenced by owner/status/expiry. Work has a 45-second cancellation deadline, shorter than the minimum lease.

InApp locks its delivery row, rechecks eligibility, inserts/reuses the inbox under database uniqueness, and completes in one transaction; no premature CompletedAtUtc is written. SignalR runs only after commit. Email normally waits until the inbox exists, avoiding retry exhaustion while InApp is pending; terminal InApp failures allow bounded email failure handling rather than a permanently unclaimable row. Eligibility and registered recipient are rechecked before provider dispatch. Career payment eligibility uses fresh non-tracking database state, not cached tracked entities.

Transient/disabled email delivery retries exponentially (capped at one day); permanent 4xx other than 408/429 fails immediately. MaxAttempts is terminal, including crashes at the final lease attempt. Safe failure codes exclude exception bodies, secrets and recipient details. Cancellation during work leaves a recoverable lease. Existing registration email behavior/queue remains separate.

Guarantees: one logical inbox row per recipient/business key and one intent per channel, durable intent, fenced claims, bounded retries. **Not exactly-once external email:** if Brevo accepts a message and the worker crashes before recording Sent, recovery may send it again. Cancellation/rescheduling concurrent with an already-started HTTP call cannot recall that email. No transaction is held over HTTP. An arbitrary process pause beyond a lease is another external side-effect risk; there is no provider idempotency guarantee claimed.

## Email and privacy

Existing `IEmailService` → `BrevoEmailService` → existing `Email:FromName`, `Email:FromAddress`, `Email:Brevo:ApiKey` and named HttpClient are reused. No provider, sender or credentials were added/hardcoded. TO is loaded from the active registered User record by the server-owned recipient ID. Candidate/consultant/referrer email addresses are never accepted in notification requests.

Content is plaintext, server-owned, with subject CR/LF stripping. No private admin rejection notes, preparation notes, meeting URLs/credentials, auth tokens or business keys are sent to realtime clients. Links use the existing frontend base configuration plus exact allowlisted internal paths (`/dashboard/interview-insights`, `/dashboard/referrals`). No verified Career Guidance frontend route exists in this backend checkout, so Career notifications deliberately have a null ActionUrl rather than an invented link. Confirm the frontend route before extending the allowlist.

## SignalR and frontend integration

Reuse `/hubs/ai-apply/external-session-capture` and existing JWT handling. The hub requires authentication; each of its nine browser-control methods separately requires Candidate. Consultant/referrer connections grant **no** browser-control permission. User targeting is derived from authenticated NameIdentifier and normalized to GUID `D` format. Clients cannot select a recipient.

`NotificationReceived` carries the existing NotificationResponse DTO (`id`, `title`, `message`, `type`, `actionUrl`, `isRead`, `readAtUtc`, `createdAtUtc`). Existing GET notifications/mark-read/mark-all endpoints and ownership rules are unchanged. Refresh the inbox/unread count on the event and on connect/reconnect; do not increment unread blindly or treat realtime as authoritative. Deduplicate by notification ID.

Realtime is best effort. Offline clients and SignalR failures do not undo the inbox. There is no durable push retry and no new backplane: a worker on another instance may not reach a local-only hub connection. Inbox polling/reconnect refresh is required in a multi-instance deployment unless an existing supported backplane is configured separately.

Existing `CareerGuidance:SessionRemindersEnabled` remains unchanged; this work does not enable it or any aggregation scheduler.

## Validation

Production: `dotnet build JobPortal.API/JobPortal.API.csproj --configuration Release --no-restore`.

Tests: build the test project with `--configuration Release --no-restore -p:RunAnalyzers=false` for the acknowledged existing analyzer debt, then run `dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build` (use focused filters first).

New/extended coverage includes interview idempotency/UTC/offsets/DST, existing feedback prompt, capture confirmation/late capture, all career offsets, approval/rejection recipients/retry conflicts, eligibility after claim, fresh refund state, dispatcher retry/privacy/offline behavior, Brevo sender/classification, real authorization policy checks and realtime targeting.

`NotificationPostgresTests` is opt-in via `NOTIFICATION_TEST_POSTGRES`, restricted to a disposable **localhost** database named `notification_test`. It creates/removes only a randomly named test schema and exercises actual repository claims concurrently, active/expired leases, stale-owner completion, and idempotent inbox materialization. It does not read application connection settings or apply migrations. If not configured it is explicitly skipped; in-memory unit tests are not evidence of live PostgreSQL concurrency correctness.

## Change inventory

Repository-relative paths below are the combined notification working-tree changes, including the partially implemented files present at handoff. The large formatting expansions in the interview service/validators/tests were already present and were preserved, not reverted.

Modified tracked files:

```text
JobPortal.API/Hubs/ExternalSessionCaptureHub.cs
JobPortal.API/Program.cs
JobPortal.Application/Abstractions/Authentication/AuthenticationContracts.cs
JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs
JobPortal.Application/Features/CareerGuidance/CareerFinanceService.cs
JobPortal.Application/Features/CareerGuidance/CareerSessionReminderProcessor.cs
JobPortal.Application/Features/CareerGuidance/CareerSessionService.cs
JobPortal.Application/Features/InterviewInsights/InterviewInsightDtos.cs
JobPortal.Application/Features/InterviewInsights/InterviewInsightServices.cs
JobPortal.Application/Features/InterviewInsights/InterviewInsightValidators.cs
JobPortal.Application/Features/Referrals/JobReferralService.cs
JobPortal.Domain/Entities/InterviewInsights.cs
JobPortal.Domain/Entities/Notification.cs
JobPortal.Infrastructure/Services/BrevoEmailService.cs
JobPortal.Persistence/Configurations/EntityConfigurations.cs
JobPortal.Persistence/Configurations/InterviewInsightConfigurations.cs
JobPortal.Persistence/Context/JobPortalDbContext.cs
JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs
JobPortal.Application.Tests/BrevoEmailServiceTests.cs
JobPortal.Application.Tests/CareerFinanceTests.cs
JobPortal.Application.Tests/CareerReservationExpiryTests.cs
JobPortal.Application.Tests/CareerSessionReviewTests.cs
JobPortal.Application.Tests/CareerSessionTests.cs
JobPortal.Application.Tests/ExternalSessionTransportTests.cs
JobPortal.Application.Tests/InterviewInsightsTests.cs
JobPortal.Application.Tests/JobReferralServiceTests.cs
JobPortal.Application.Tests/ReferralJobSkillsTests.cs
```

New/untracked implementation files (several outbox foundation files already existed untracked at handoff):

```text
JobPortal.API/HostedServices/NotificationDeliveryHostedService.cs
JobPortal.API/Hubs/NotificationRealtime.cs
JobPortal.Application/Features/Notifications/NotificationDispatcher.cs
JobPortal.Application/Features/Notifications/NotificationOutbox.cs
JobPortal.Domain/Entities/NotificationDelivery.cs
JobPortal.Persistence/Configurations/NotificationDeliveryConfiguration.cs
JobPortal.Persistence/Repositories/NotificationDeliveryRepository.cs
JobPortal.Persistence/Repositories/NotificationOutboxRepository.cs
JobPortal.Application.Tests/NotificationDispatcherTests.cs
JobPortal.Application.Tests/NotificationEligibilityTests.cs
JobPortal.Application.Tests/NotificationOutboxTests.cs
JobPortal.Application.Tests/NotificationPostgresTests.cs
JobPortal.Application.Tests/NotificationRealtimeTests.cs
JobPortal.Application.Tests/NotificationTestSupport.cs
docs/centralized-notifications.md
```

Existing untracked files intentionally untouched: `artifacts/AddJobAggregationDedupFoundation.idempotent.sql`, `artifacts/CareerGuidancePhase1To5.idempotent.sql`, and `notification-build.txt`. No source/config files outside the notification work were discarded, staged or committed. No stash, nested duplicate directory, migration files, appsettings or .gitignore changes were made.

## Validation results — 2026-09-28

Final normal API Release build: PASS, 0 warnings / 0 errors.

Test compilation with the explicitly approved `-p:RunAnalyzers=false`: PASS, 0 warnings / 0 errors. Production builds did not disable analyzers.

Exact build commands:

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj --configuration Release --no-restore
dotnet build JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-restore -p:RunAnalyzers=false
```

Focused commands/results (groups overlap on the feedback worker; do not sum as a distinct-test count):

| Filter group | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Interview + feedback worker | 25 | 0 | 0 | 25 |
| Career finance/session/review | 76 | 0 | 0 | 76 |
| Referral + composed skills | 30 | 0 | 0 | 30 |
| Notification/email/SignalR/registration/inbox | 122 | 0 | 1 | 123 |
| Offline outbox/model audit alone (included above) | 7 | 0 | 0 | 7 |

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~InterviewInsightsTests|FullyQualifiedName~InterviewScheduleNotificationWorkerTests"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~CareerFinanceTests|FullyQualifiedName~CareerSessionTests|FullyQualifiedName~CareerSessionReviewTests"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~JobReferralServiceTests|FullyQualifiedName~ReferralJobSkillsTests"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Notification|FullyQualifiedName~BrevoEmailServiceTests|FullyQualifiedName~ExternalSessionTransportTests|FullyQualifiedName~RegistrationEmailHostedServiceTests|FullyQualifiedName~CandidateModuleTests"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~NotificationOutboxTests"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --configuration Release --no-build
```

Broadest suite: **1225 passed / 10 failed / 6 skipped / 1241 total**. No exclusions. Failures were not hidden or weakened:

- Seven `InterviewInsightsMembershipAuthorizationTests` failures already observed before this task: six exact forbidden-message expectations and `ActivePaidNonExpiredMemberIsAuthorized`. Their policy/fixtures are unrelated to notification delivery and were not changed.
- `CareerReservationExpiryTests.MigrationChangesOnlyStatusConstraintAndKeepsOverlapProtection`, `PostgresPendingModelTests.CandidateContractChangesRequireNoPostgresMigration`, and `CareerTrustMigrationTests.OfflineSqlModelAndSnapshotAreConsistentWithoutUnrelatedChanges` currently fail their zero-pending-model assertions. The notification model is intentionally ahead of the untouched snapshot. These are migration-dependent failures, **not** a claim that this task is schema-neutral. The trust test additionally had an older migration-designer versus latest-snapshot discrepancy in the prior baseline.
- Six PostgreSQL integration tests are explicitly skipped because their disposable local test connections are not configured (five existing tests plus the new notification lease test). No production/shared database was contacted.

The new offline model-difference test passes and restricts pending DDL to the one outbox table, four approved existing-table columns, intended indexes and offset constraint; no unrelated alterations, drops, salary changes, xmin or JobId1. It writes no migration files and executes no SQL against a database.

`git diff --check`: PASS (Git emits LF/CRLF normalization warnings on three test files, not whitespace errors). Index remains empty. Final state is the unstaged tracked modifications/new files listed above plus the three pre-existing untracked files; no commit/push/cleanup/stash operation.

Remaining gates: generate/review the authorized migration, run local PostgreSQL integration verification, and separately resolve/triage the existing membership/snapshot test debt. This is not a database deployment approval. Multi-instance realtime remains best effort without a backplane; frontend refresh is required. Existing pre-migration schedules are not retroactively queued, and the Career Guidance frontend action route still needs verification before adding a link.
