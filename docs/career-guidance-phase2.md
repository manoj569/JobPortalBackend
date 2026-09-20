# Career Guidance Phase 2 — scheduling and bookings

Backend-only implementation on `feature/career-guidance-phase2`, based on the merged
Phase 1 foundation. No payment collection, commission settlement, payouts, refunds,
reviews, disputes, video providers, notifications or frontend are implemented.

## Architecture and data

`CareerSchedulingService` owns validation, authorization, dynamic slot generation,
snapshots and lifecycle policy. `CareerSlotGenerator` is a pure timezone-aware
algorithm. `CareerSchedulingRepository` implements scoped queries and atomic EF
SaveChanges, including audit writes and optimistic revision checks. Existing Phase 1
profile/service/moderation revisions remain the concurrency coordination point.

- `CareerConsultants`: adds nullable `TimeZoneId` (varchar 100) and
  `IsAcceptingBookings` (false for existing profiles). A verified owner must configure
  availability and explicitly opt in. Phase 1 public profile DTO now reflects this flag.
- `CareerConsultantAvailability`: owner consultant FK, weekday, local TimeOnly start/end,
  active flag and normal BaseEntity audit/soft-delete fields. Multiple daily windows.
- `CareerConsultantAvailabilityExceptions`: consultant FK, local DateOnly and optional
  start/end; both null means full-day unavailable, both present means a blocking range.
- `CareerGuidanceBookings`: candidate, consultant and service FKs, UTC start/end,
  immutable timezone/title/type/duration/price/currency snapshots, questionnaire,
  lifecycle/cancellation metadata, revision and BaseEntity audit/soft-delete fields.
  No payment or membership relationship is invented.

All FKs use RESTRICT. Checks enforce weekdays 0–6, ordered time ranges, paired-null
exception endpoints, positive snapshot price, duration 15–180 minutes, valid status
and positive booking interval. Query indexes cover consultant/day/active windows,
consultant/date exceptions, consultant/status/start/end and candidate/status/start
bookings, FK columns and soft-delete flags. Booking price retains numeric(18,2).
Normal PKs identify all rows; the GiST exclusion constraint is the cross-row booking
uniqueness/overlap invariant rather than a misleading unique start-time index.

## API

All responses use existing ApiResponse/PagedResponse conventions. IDs in private
routes do not grant access: actor identity is always read from authenticated claims.

| Method | Route | Access |
| --- | --- | --- |
| GET, PUT | `/api/career-guidance/me/availability` | Owner; writes require verified active profile |
| GET, POST | `/api/career-guidance/me/availability/exceptions` | Owner; writes require verified active profile |
| DELETE | `/api/career-guidance/me/availability/exceptions/{id}?revision={guid}` | Verified active owner |
| GET | `/api/career-guidance/consultants/{consultantId}/services/{serviceId}/slots?from=YYYY-MM-DD&to=YYYY-MM-DD` | Public |
| POST | `/api/career-guidance/bookings` | Active authenticated user, never self-booking |
| GET | `/api/career-guidance/bookings/mine` | Candidate's own bookings |
| GET | `/api/career-guidance/bookings/{id}` | Owning candidate |
| POST | `/api/career-guidance/bookings/{id}/cancel` | Owning candidate |
| GET | `/api/career-guidance/me/bookings` | Owning consultant |
| GET | `/api/career-guidance/me/bookings/{id}` | Owning consultant |
| POST | `/api/career-guidance/me/bookings/{id}/cancel` | Owning consultant |
| POST | `/api/career-guidance/me/bookings/{id}/status` | Owning consultant |
| GET | `/api/admin/career-guidance/bookings` | Administrator |
| GET | `/api/admin/career-guidance/bookings/{id}` | Administrator |

Lists accept pageNumber (default 1), pageSize (default 20, maximum 100).
Exception listing uses the same local date range as slot discovery (maximum 31 dates).
Availability replacement and exception changes require the current profile Revision;
get it from GET availability and use the returned Revision after each successful edit.
Booking cancel/status requests require the current booking Revision. Stale writes are 409.

PUT availability example:

```json
{
  "timeZoneId": "Asia/Kolkata",
  "isAcceptingBookings": true,
  "windows": [{ "dayOfWeek": 1, "startTime": "09:00:00", "endTime": "12:00:00", "isActive": true }],
  "revision": "<current-profile-revision>"
}
```

POST booking requires consultantId, serviceId, startUtc (Z or +00:00), questionnaire:
targetCompany, targetRole, yearsOfExperience, currentRoleOrStatus, sessionGoal,
questions and notes. SessionGoal is required and cannot be whitespace. Optional text
is trimmed and whitespace becomes null. Limits: 200 for company/role/current status,
2000 for goal/notes, 4000 for questions; experience 0–70 with one decimal place.
No resume upload or sensitive identity documents. Audit metadata records only the
operation result and resource ID, not questionnaire contents.

## Timezone and slot algorithm

- Store named IANA region IDs (`Asia/Kolkata`, `America/New_York`, `Europe/London`,
  or `Etc/UTC`), not fixed offsets or Windows IDs. .NET TimeZoneInfo performs conversion;
  the server must have usable ICU/timezone data. Short aliases such as `UTC` are not
  accepted: use `Etc/UTC`.
- Weekly rules and exceptions have consultant-local semantics. Booking timestamps
  and returned slot timestamps are UTC; response also includes consultant TimeZoneId.
  Candidate timezone conversion is a presentation concern, not a stored scheduling rule.
- Windows must be minute-aligned, 15 minutes–12 hours, within one local day, max 28
  windows per profile. No overlapping/duplicate windows even when inactive. Overnight
  windows must be split across days. Exceptions support full-day/partial blocks only,
  no special opening hours; max 16 non-overlapping blocks per local date.
- Generate slots at the configured increment anchored to each window start, fitting
  the entire current service duration inside the window. Remove full/partial blocks,
  existing Pending/Confirmed bookings, past instants, insufficient notice and intervals
  ending beyond the horizon. Half-open intervals permit adjacent sessions.
- DST policy is intentionally conservative: skip slots touching an invalid or ambiguous
  minute, including the end boundary and any minute inside the interval. Neither fold
  occurrence is offered. Verify actual UTC duration equals service duration. No duplicate
  fall-back instants or silent fixed-offset arithmetic.
- Return no slots for unverified/suspended/inactive/not-accepting consultants, inactive
  users/services or a service not belonging to that profile. Missing profile returns 404.
- Date queries are bounded to 31 local dates, years 2000–2100, within the configured
  horizon. Slots are never persisted. Booking creation recalculates and matches the
  exact UTC slot again; a prior discovery response grants no reservation.
- Availability/block edits affect new bookings, not existing reservations. Timezone
  changes are rejected while future active bookings exist. Changes remain serialized
  against booking creation through the profile revision.

## Double-booking and transaction safety

Migration `20260920180130_AddCareerGuidanceSchedulingAndBookings` registers
`btree_gist` and creates the database constraint:

```sql
EXCLUDE USING gist (
  "ConsultantId" WITH =,
  tstzrange("StartUtc", "EndUtc", '[)') WITH &&
) WHERE ("IsDeleted" = FALSE AND "Status" IN (1, 2))
```

Constraint name: `EX_CareerGuidanceBookings_NoOverlap`. Pending=1 and Confirmed=2.
It prevents partially overlapping intervals across every service of a consultant,
not only identical starts. Cancellation/soft deletion leaves history but frees capacity.
GiST exclusion enforcement is the final guarantee across processes and direct SQL.
EF cannot model this constraint natively, so its SQL is explicitly in this NEW migration;
the generated snapshot/designer contain all EF-mapped objects and extension metadata.
Tests assert the exact predicate/status values so future lifecycle changes cannot silently
diverge from database enforcement. Future changes to active states need a migration.

Booking creation also updates the profile Revision in the SAME SaveChanges transaction
as the booking and audit row. This detects stale schedules, service snapshots and
moderation changes because Phase 1 edits already update the same profile Revision.
This may conservatively reject simultaneous non-overlapping requests; clients reload
and retry. PostgreSQL 23P01 for this specific constraint becomes safe HTTP 409
`booking_overlap`; optimistic concurrency becomes 409 `concurrency_conflict`. No raw
database details are returned by these conflict paths. No distributed/in-process lock
or check-then-insert race is relied upon for interval exclusivity.

## Lifecycle, ownership and Phase 3

Creation -> Pending; consultant may confirm before start -> Confirmed.
Pending/Confirmed -> CancelledByCandidate or CancelledByConsultant before start,
subject to candidate cancellation notice. Confirmed -> Completed, NoShowCandidate or
NoShowConsultant only after end, by the owning consultant. Terminal states cannot be
cancelled/reconfirmed. Candidate cannot arbitrarily set statuses. There is no payment
confirmation implied by any state. Snapshots never change with later service edits.

Private queries are scoped in the repository to candidate ID or consultant owner ID;
cross-owner access is 404. List/detail/cancel/status paths use the same ownership scope.
Admin reads require both the controller role and active database Administrator role.
All actors must be active; anonymous access exists only for slot discovery. Public
responses contain only slot instants and timezone, never questionnaire/participant IDs.
Historical bookings remain accessible to authorized participants after profile/service
soft deletion. Audit and revision fields follow existing application conventions.

Phase 3 must add payment intent/authorization and idempotent webhook processing before
any paid confirmation policy, while preserving the overlap invariant. Introduce
explicit timed reservation expiry and coordinate status/payment transitions atomically.
Commission/payout ledgers and cancellation/refund decisions are separate future models;
do not recalculate historical prices or invent refunds in this phase. Pending bookings
currently hold their interval until cancelled; no automated expiry/no-show scheduler.
There is no automatic rescheduling, buffer time, calendar sync, candidate cross-consultant
overlap prevention, or durable create-request idempotency in Phase 2.

## Configuration

`CareerGuidance` options bind with validated defaults and startup validation. No settings
file is changed and no secret is introduced. Optional environment overrides:

| Key under CareerGuidance | Default | Valid range |
| --- | --- | --- |
| SlotIncrementMinutes | 15 | 5–60 |
| MinimumBookingNoticeMinutes | 120 | 0–10080, less than horizon |
| MaximumBookingDaysAhead | 60 | 1–365 |
| CandidateCancellationNoticeMinutes | 120 | 0–10080 |

For example use `CareerGuidance__SlotIncrementMinutes`. Business bounds are enforced
server-side, not delegated to frontend input.

## Migration and deployment gate

The migration adds only the two consultant settings, three Phase 2 tables, their
constraints/indexes and `btree_gist`. No Phase 1 tables are recreated; no Jobs, salary,
referral, aggregation or payment schema is altered. Existing migration files and
legacy SQL Server migrations are untouched. Down drops the new tables/columns but
deliberately retains the database-wide extension because other objects may use it.
Rolling back destroys Phase 2 data and requires normal operator review/backups.

Generation and model checks use a child-process dummy localhost connection and do not
connect to or update a database. **No migration has been applied in this task.** Before
deployment, confirm the target supports btree_gist and the deployment role may install
it (or have an operator preinstall it), review generated SQL, and run the isolated
PostgreSQL concurrency test. No live PostgreSQL validation is claimed here.

`CareerSchedulingPostgresTests` is opt-in through `CAREER_GUIDANCE_TEST_POSTGRES` only;
it never reads DefaultConnection. It rejects non-loopback hosts and any database name
other than `career_guidance_test`. Provision that disposable local database and btree_gist
yourself; then run the focused test filter. It creates a GUID-named private schema,
applies the EXACT migration exclusion SQL to a minimal table, concurrently attempts
overlapping inserts on two connections, verifies only one succeeds, then verifies
cancellation release, adjacent sessions and separate consultants. Cleanup drops only
that generated schema. This is intentionally a database-writing test and must never
target shared/production infrastructure. There is no local PostgreSQL/Docker runtime
available in the current environment, so the test is skipped by default.

## Validation

Focused tests include Phase 1 regression plus availability, ownership, blocks, Kolkata
conversion, New York DST gaps/folds, revalidation, immutable snapshots, questionnaire,
lifecycle, IDOR, controller authorization, profile CAS, safe conflict translation,
migration scope and PostgreSQL model metadata.

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj --no-incremental --no-restore
Remove-Item Env:CAREER_GUIDANCE_TEST_POSTGRES -ErrorAction SilentlyContinue
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-restore "-p:NoWarn=CA1707%3BCA1859%3BCA1861" --filter 'FullyQualifiedName~CareerScheduling|FullyQualifiedName~CareerGuidanceTests'

Remove-Item Env:AIAPPLY_STEP5_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
Remove-Item Env:CAREER_GUIDANCE_TEST_POSTGRES -ErrorAction SilentlyContinue
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName!~AIApplyDistributedPostgresTests&FullyQualifiedName!~CareerSchedulingPostgresTests' --logger 'console;verbosity=minimal'
```

The test build uses the established Phase 1 command-line analyzer workaround for
pre-existing CA1707/CA1859/CA1861; no global suppression or unrelated test cleanup.
Focused result: **55 passed, 0 failed, 1 skipped, total 56**. The skipped test is the
explicit localhost PostgreSQL overlap integration test, not an application failure.
API build: **PASS, 0 warnings, 0 errors**, without analyzer suppression.
Broader non-database regression: **973 passed, 0 failed, 0 skipped, total 973**.
The three existing live PostgreSQL tests and the new local integration test are excluded
from this broader command, not counted as skipped. The initial sandboxed broader run
had 931 passes and 42 failures caused by existing Playwright browser fixtures failing
to launch (spawn EPERM); the complete rerun with process-launch permission passed.

Offline `dotnet ef migrations has-pending-model-changes --project
JobPortal.Persistence.Postgres --startup-project JobPortal.API --context
JobPortalDbContext --no-build`, using the same dummy localhost connection as migration
generation, reports **no pending model changes**. Migration operation/model tests pass.
No live database test, migration application or database connectivity was attempted.

`git diff --check`: **PASS** (Git line-ending notices only).
`git diff --stat` for tracked files: **8 files changed, 296 insertions, 1 deletion**.
This excludes the 14 new untracked implementation/documentation files listed below.
The final status has these 8 modified and 14 new files plus the two pre-existing
untracked files. Both pre-existing files retain their original SHA-256 hashes.

## Exact change inventory

Created:

- JobPortal.API/Controllers/CareerSchedulingControllers.cs
- JobPortal.Application/Features/CareerGuidance/CareerSchedulingService.cs
- JobPortal.Application/Features/CareerGuidance/CareerSlotGenerator.cs
- JobPortal.Application/Features/CareerGuidance/SchedulingContracts.cs
- JobPortal.Application/Features/CareerGuidance/SchedulingValidators.cs
- JobPortal.Application.Tests/CareerSchedulingTests.cs
- JobPortal.Application.Tests/CareerSchedulingMigrationTests.cs
- JobPortal.Application.Tests/CareerSchedulingPostgresTests.cs
- JobPortal.Domain/Entities/CareerGuidanceScheduling.cs
- JobPortal.Persistence/Configurations/CareerSchedulingConfigurations.cs
- JobPortal.Persistence/Repositories/CareerSchedulingRepository.cs
- JobPortal.Persistence.Postgres/Migrations/20260920180130_AddCareerGuidanceSchedulingAndBookings.cs
- JobPortal.Persistence.Postgres/Migrations/20260920180130_AddCareerGuidanceSchedulingAndBookings.Designer.cs
- docs/career-guidance-phase2.md

Modified:

- JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Application/Features/CareerGuidance/CareerGuidanceService.cs
- JobPortal.Domain/Entities/CareerGuidance.cs
- JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Persistence/Configurations/CareerGuidanceConfigurations.cs
- JobPortal.Persistence/Context/JobPortalDbContext.cs
- JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs

The two pre-existing untracked files (JobSourceCategoryResponseTests.cs and the
aggregation idempotent SQL artifact), stash, nested duplicate, .gitignore and appsettings
remain outside this change. Nothing staged, committed, pushed or deployed.
