# Career Guidance Phase 4: private sessions and reminders

## Architecture and scope

Phase 4 extends the existing paid booking, financial ledger, audit log, in-app
notification inbox, scoped EF unit of work, and ASP.NET Data Protection patterns.
It does not add a video platform, frontend, recording, transcript, chat, reviews,
subjective disputes, automatic refunds, or automatic bank payouts.

Session provisioning is an explicit, idempotent POST by the booking's candidate,
consultant, or an administrator. GET is read-only and returns 404 until provisioned.
Only a confirmed booking with a captured payment, paid timestamp, Pending earning,
no refund review, and active participants/verified consultant is eligible. Existing
unpaid Phase 2 bookings are not retroactively charged or given fabricated payments;
their legacy lifecycle remains available. Paid bookings must use session actions
instead of the old booking completion/no-show endpoint.

Provisioning must be called after checkout/reconciliation by the integrating client
or operations workflow. A booking that is never provisioned has no session reminders.

## Storage and migration

Migration: `20260921061646_AddCareerGuidanceSessionsAndReminders`, PostgreSQL project
only. Generated offline; not applied. It creates only:

- `CareerGuidanceSessions`: one per BookingId, participant IDs, immutable scheduling
  snapshots by application policy, provider/meeting identity, encrypted participant
  and optional host link, lifecycle/no-show timestamps, nullable real attendance
  timestamps, durable provisioning intent, release-delay snapshot, revision, audits.
- `CareerGuidanceSessionReminders`: session, recipient, offset, due timestamp,
  Pending/Delivered/Cancelled status, inbox delivery timestamp, revision and audits.

Constraints: unique BookingId including soft-deleted records; unique non-null
(MeetingProvider, ProviderMeetingId); start < end; session status 1..7; earning
delay 24..2160 hours; reminder offset 1..10080 and status 1..3; unique reminder
(SessionId, RecipientUserId, OffsetMinutes). FKs restrict deletion. Indexes cover
consultant/status/start, candidate/status/start, reminder status/due and soft delete.
Revision GUIDs are application-managed optimistic concurrency tokens, not xmin.
No existing columns, financial precision, or old migrations are changed.

## State machine and booking integration

| Session action | Preconditions | Result |
| --- | --- | --- |
| Provision | eligible paid confirmed booking, before end | Scheduled + durable intent/reminders |
| Configure manual link | admin; Scheduled/Ready; eligible payment | Ready |
| Reconcile | admin; Scheduled/Ready; eligible payment | provider metadata, Ready if provider supplies link |
| Start | owning consultant; Ready; join window | InProgress; StartedAtUtc |
| Complete | owning consultant; InProgress; at/after scheduled end | Completed; booking Completed |
| Candidate no-show | owning consultant; start + grace elapsed; no recorded attendance | CandidateNoShow; booking NoShowCandidate |
| Report consultant no-show | owning candidate; grace elapsed | flag only, no terminal or financial action |
| Confirm consultant no-show | admin; grace elapsed; no recorded consultant attendance | ConsultantNoShow; booking NoShowConsultant; refund review |
| Booking cancellation/admin refund cancellation | existing Phase 2/3 policy | Cancelled; pending reminders cancelled |

Completed, CandidateNoShow, ConsultantNoShow and Cancelled are terminal. Duplicate
start/completion/final no-show requests return their existing result without
resetting timestamps. New transitions require the last returned revision; stale
revisions return 409. Consultant completion cannot precede scheduled end, even if
the consultant starts late. No early-completion/admin override is implemented.

Session transitions touch booking/payment/consultant revisions and, when relevant,
earning/reminder revisions within one EF SaveChanges transaction. The existing
scheduling/finance repositories synchronize cancellation before that same save.
Racing cancellation/refund/start/completion operations therefore conflict rather
than independently succeeding. Reload after a 409; do not replay stale revisions.
Reads always check the current booking/payment, not just the session status.

## Meetings, reconciliation and security

`ICareerMeetingProvider` exposes Create/Get with the persisted session ID as the
stable idempotency/reconciliation identity. The Manual implementation does no
network work and deterministically identifies the placeholder as `manual_<id>`.
An administrator supplies an existing meeting's HTTPS participant link and optional
host link. This does not create a Google Meet/Zoom/Teams account or external meeting.
MeetingCreatedAtUtc records acceptance of provider metadata, not real video activity.

The intent is committed before Create. If Create succeeds but its response or the
following save is lost, repeated provision returns the existing session. The admin
reconcile endpoint calls Get with the SAME session ID, never another Create. Unknown
lookup results stay unresolved with 409. Provider exceptions are sanitized without
inner exceptions, so private URLs cannot reach global exception logging.
Provider names/meeting IDs accept only bounded ASCII letters, digits, dots, hyphens
and underscores; URLs must never be supplied as public metadata identifiers.
When an adapter supplies a refreshed participant link without a host link, an old
host credential is cleared. Manual reconciliation without any URLs preserves the
administrator-configured links.

Google Meet/Zoom/Teams/Jitsi adapters must implement genuine provider idempotency or
stable-reference lookup before being enabled. Registering an adapter also requires
provider-specific configuration, tests, cancellation/revocation integration and
trusted webhook handling; this phase does not claim those integrations exist.

Links are encrypted as bytea using the existing `IDataProtectionProvider`, purpose
isolated by CareerHarbor/CareerGuidance/Meeting/v1/session ID/participant-or-host.
They never appear in metadata/public DTOs, audits, or reminders. Candidate join
returns only participant data; the consultant gets a host link if configured,
otherwise the participant link. Admin routes return metadata, not stored secrets.
Join responses disable HTTP caching. Use HTTPS and do not enable request/response
body logging on manual-meeting/join routes. Do not enable EF sensitive-data logging.

Manual links require HTTPS, no userinfo, default port, <=2048 characters, and an
exact allowlisted DNS host (no wildcard or arbitrary IP). Query tokens are allowed
but remain sensitive. No URL is fetched by this feature. Zoom regional subdomains
and Teams variants need explicit host configuration.

Persistent/shared Data Protection keys with the same application name are a
deployment prerequisite. The API already initializes Data Protection; its existing
AIApply external-session configuration can persist/protect a key ring. This phase
does not change that configuration or key storage. In ephemeral containers, default
local keys can be lost on restart; explicitly establish durable shared key storage
before production use. Lost keys yield a sanitized 409 and require an administrator
to reconfigure the meeting. Never log or copy keys into source control.

Application join gating cannot prevent reuse of an already-disclosed external URL.
Manual cancellation revokes API access only, not the external meeting itself.
Operators must use provider waiting rooms, passcodes/participant admission, and
cancel or rotate the external meeting when needed.

## Join windows and no-shows

All business comparisons use UTC and TimeProvider. Ready sessions expose links from
start - early through start + late, but strictly before scheduled end. InProgress
sessions remain joinable through end + CompletionGraceMinutes. Other states withhold
links and return status metadata plus a safe reason. Payment/refund/cancellation
eligibility is rechecked before returning any link.

Manual meetings have no attendance telemetry. Retrieving a link or pressing Start
does NOT set CandidateJoinedAtUtc/ConsultantJoinedAtUtc. Candidate no-show is an
auditable consultant assertion, not proven absence. Candidate consultant-no-show
reports are untrusted flags, not refund instructions. Admin confirmation produces
refund-review eligibility and no automatic financial transfer. Existing attendance,
if populated by a future trusted integration, blocks an inconsistent no-show claim.
Candidate reports and administrator confirmation remain available if the consultant
has been suspended/deactivated/soft-deleted or refund review is already flagged.
They still require an open confirmed/captured booking with a Pending earning and no
refund record. This exception is for review only: it never enables join/start/completion
for an ineligible participant or regresses a terminal booking/session.

## Finance

Completion leaves the existing earning Pending and sets AvailableAtUtc to completion
+ the release-delay snapshot captured at provisioning (default 48 hours). No processor
promotes earnings to Payable or Settled. AvailableAtUtc is a future eligibility hint,
not a payout authorization. A future release processor must recheck payment/refund,
dispute, session and account eligibility atomically.

Candidate no-show leaves Pending with no release time, for later policy review.
Confirmed consultant no-show sets RequiresRefundReview and clears release eligibility.
The existing admin-controlled Phase 3 full-refund workflow remains authoritative.
Refund approval and refund processing clear AvailableAtUtc; processed refunds reverse
the earning as before. No bank payout or subjective refund decision was added.

## Reminders and audit

Provisioning snapshots each configured future offset for both participants; past
offsets are omitted and there is no retroactive catch-up scheduling. The unique key
prevents duplicate records. A bounded batch (50) delivers due reminders into the
existing in-app Notification inbox atomically with reminder state. Notification ID
equals reminder ID, with revision CAS providing multi-worker/retry safety.

Delivered/SentAtUtc means an inbox record was committed, NOT email/SMS receipt.
There is no generic existing session email interface; existing IEmailService is
purpose-specific and SMS is retired. This phase sends no email/SMS. Notifications
contain generic authenticated-session instructions, never meeting links. Cancelled,
ineligible, or already-started-time reminders are cancelled, not delivered late.
Failures roll back and retry on a later batch; logs contain only a generic failure.
The due query includes soft-deleted dependencies so ineligible pending reminders
can be cancelled rather than stranded behind global query filters. Soft-deleted
reminder records themselves are excluded.

`CareerSessionReminderHostedService` is disabled by default. Enabling it is a separate
operational decision after migration/key-ring checks. It is independent of job
aggregation and does not enable that scheduler.

Existing append-only AuditLog records provision, meeting creation/configuration,
reconciliation, start, completion, asserted/reported/confirmed no-shows and cancellation.
Metadata contains only a fixed result code and entity identity, no meeting URLs,
questionnaire text or payment secrets.

## Authorization and routes

All routes require authentication and an active database user. Candidate routes also
require Candidate role; admin routes require Administrator both in HTTP and service
checks. Consultant ownership is checked against the booking's profile owner, not an
arbitrary role claim. Unauthorized ownership returns 404. Lists are paged (1..100).

Candidate prefix `/api/career-guidance/bookings/{bookingId}/session`:

- GET metadata; GET `/join`; POST `/provision`;
- POST `/consultant-no-show-report` with `{ revision }`.

Consultant prefix `/api/career-guidance/me/sessions`:

- GET list; GET `/{id}`; GET `/{id}/join`;
- POST `/bookings/{bookingId}/provision`;
- POST `/{id}/start`, `/{id}/complete`, `/{id}/candidate-no-show` with `{ revision }`.

Admin prefix `/api/admin/career-guidance/sessions`:

- GET list; GET `/{id}`; POST `/bookings/{bookingId}/provision`;
- POST `/{id}/reconcile`, `/{id}/consultant-no-show` with `{ revision }`;
- POST `/{id}/manual-meeting` with `{ revision, participantUrl, hostUrl? }`.

## Configuration (CareerGuidance section)

| Key | Default | Limits/meaning |
| --- | --- | --- |
| SessionJoinEarlyMinutes | 10 | 0..120 |
| SessionJoinLateMinutes | 30 | 1..120 |
| NoShowGraceMinutes | 15 | 1..120 |
| CompletionGraceMinutes | 15 | 0..120, active join grace, not early completion |
| EarningReleaseDelayHours | 48 | 24..2160, snapshotted per session |
| ReminderOffsetsMinutes | [1440,60,10] | up to 6 unique positive offsets <=10080; future only |
| SessionRemindersEnabled | false | explicit operational enablement |
| SessionReminderPollSeconds | 60 | 10..3600 |
| AllowedMeetingHosts | meet.google.com, zoom.us, teams.microsoft.com, meet.jit.si | exact DNS hosts |

Explicit array configuration replaces defaults rather than appending. No appsettings
or environment values were modified. Configuration validates on application startup.

## Validation and pre-deployment

Tests cover lifecycle/IDOR/window boundaries, encryption purpose isolation, uncertain
provider outcomes, cancellation/refund integration, reminder idempotency/privacy,
revision conflicts, safe defaults/binding, migration scope and offline PostgreSQL SQL.
The opt-in PostgreSQL test uses only an explicitly configured localhost database named
`career_guidance_test`, via `CAREER_GUIDANCE_SESSION_TEST_POSTGRES`, with a generated
private schema. It is skipped when absent. It must never use DefaultConnection or a
production/shared endpoint. No database integration test was executed for this task.

Before deployment: review the migration/SQL; run the isolated PostgreSQL tests in an
approved disposable environment; verify durable shared encryption keys; restrict
logging and meeting hosts; establish the provisioning/manual-link operational flow;
keep reminders disabled until schema and notification inbox readiness are verified.
Migration deployment is a separate approval, not part of this implementation.

Phase 5 can add review eligibility from terminal sessions, evidence-backed disputes,
hold/release rules, and admin review of no-show assertions. None is implemented here.

### Implementation validation (2026-09-21)

- API `dotnet build --no-restore --no-incremental`: passed, 0 warnings/0 errors.
- Focused Career Guidance Phases 1–4: 123 passed, 0 failed, 3 skipped, 126 total.
- New Phase 4 coverage: 31 passed and 1 opt-in PostgreSQL test skipped.
- Full non-database regression: 1041 passed, 0 failed, 0 skipped, 1041 total.
- Test-only analyzer workaround: `-p:NoWarn=CA1707%3BCA1859%3BCA1861`.
- EF `has-pending-model-changes`: none, using a dummy design-time localhost setting.
- Scoped idempotent SQL generated offline from AddCareerGuidancePaymentsAndEarnings
  through AddCareerGuidanceSessionsAndReminders and inspected; only the two new tables,
  their indexes/constraints and the standard migration-history insert. Never executed.
- Migration scope/model tests passed; no JobId1, xmin, unrelated ALTER/DROP or business DML.
- `git diff --check` and new-file whitespace checks passed (Git emits CRLF conversion notices).
- PostgreSQL scheduling/finance/session integration tests were skipped because no
  disposable local PostgreSQL was configured. No production/shared database used.

### Commit/PR readiness review

The complete Phase 4 diff was reviewed, including unchanged Phase 2/3 call paths
that interact with it. Review fixes were confined to the session service/repository:

- Separated no-show reporting/admin review from join eligibility, so suspension,
  deactivation, soft deletion or an existing review flag cannot block safe review.
  An existing refund, non-Pending earning, or terminal booking still blocks it.
- Bounded/validated provider identifiers to reject URLs in public meeting metadata.
- Cleared stale host credentials when a provider refresh no longer supplies one.
- Bounded session pagination to avoid integer overflow in Skip.
- Included deleted dependencies in reminder eligibility checks, while excluding
  deleted reminder records themselves, so pending records can be cancelled safely.

Added 13 review regression cases covering those fixes, disabled-worker isolation,
stale completion versus refund/no-show, a provider result after cancellation, and
competing reminder workers. In-memory race tests establish optimistic-conflict
and deterministic-notification behavior, not PostgreSQL transaction rollback;
real PostgreSQL integration remains a pre-deployment validation requirement.
The disabled-worker test waits for execution before stopping, avoiding a test-only
background-task scheduling race observed under full-suite load.

Reviewed and retained: terminal state guards; atomic EF unit-of-work transitions;
Pending-only earning release scheduling; no automatic refunds/payouts; active-user
and ownership checks; private host/participant purpose separation; generic audit
and notification payloads; default-disabled worker; durable provisioning intent and
lookup-only recovery; legacy unpaid-booking behavior. Designer model content exactly
matches the snapshot. No migration change was needed for the review fixes.

Final review validation: API build passed with 0 warnings/0 errors; focused Phases
1–4 tests 136 passed, 0 failed, 3 skipped (139 total); full non-database regression
1054 passed, 0 failed, 0 skipped. EF reports no pending model changes; scoped offline
idempotent SQL contains only the two new tables/indexes/constraints and the standard
history insert. Whitespace checks pass. No migration was applied and no database was
accessed. Code is ready for commit/PR; this is not deployment approval. The three
PostgreSQL integration tests and durable/shared key storage remain deployment gates.
