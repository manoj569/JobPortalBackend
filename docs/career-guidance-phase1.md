# Career Guidance marketplace: architecture and Phase 1

## Repository findings

The .NET 9 solution separates API, Application, Domain, Persistence and Infrastructure;
PostgreSQL migrations/design-time factory live in Persistence.Postgres. Controllers
use ApiResponse/PagedResponse, FluentValidation and centralized exception mapping.
JWT identities use User/Role and GetRequiredUserId; Administrator is the existing
moderation role. Consultant is a capability, not a new login system or role.
Company is an existing managed directory with ownership/verification; linking to it
does not establish employment or employer endorsement.

BaseEntity supplies GUID, UTC audit timestamps and soft deletion. DbContext applies
audit/soft-delete behavior and configurations by assembly; repositories and UnitOfWork
share its scoped context. IAuditWriter appends immutable audit rows in that context.
Concurrency exceptions map to HTTP 409. Phase 1 uses these unchanged.

Existing Payment/PaymentHistory support Razorpay/PhonePe and optional MembershipId,
but PaymentService checkout, confirmation and membership entitlements are coupled.
Do not reuse that service for bookings without separating fulfillment later. Reuse
gateway contracts, signature validation and idempotency patterns—not membership rules.
No escrow or marketplace payout capability has been established.

IEmailService is currently purpose-specific (registration/password/application status).
Notification and interview reminder processing exist; there is no general marketplace
messaging/calendar/meeting infrastructure. Extend notification templates/outbox handling
later, not a duplicate email/chat stack. CandidateInterviewSchedule is interview-specific,
not a paid booking calendar. Candidate resumes use IResumeStorage and private User storage
metadata; do not expose those keys or treat that storage as consultant verification evidence.
Phase 1 collects no sensitive verification files; LinkedIn plus manual admin review suffice.

## Domain and database plan

Phase 1: CareerConsultant (one per User, even if soft deleted), optional CompanyId,
public display name/headline/bio/role/company claim, professional type, experience,
LinkedIn URL, policy acceptance version/time, verification state/method/time/reason,
private reviewer metadata, and application-managed GUID revision concurrency token.
CareerConsultantTag stores bounded, normalized language/expertise values with unique
consultant/kind/value. CareerConsultantService stores extensible service-type string,
title/description, duration, consultant-set decimal price, currency and active state.
All entities reuse BaseEntity. FKs use Restrict; public visibility requires Verified
and an active non-deleted User. No Job FK. No review/rating/session counters fabricated.

Indexes: unique consultant UserId; status/date/id for admin/discovery; CompanyId and
professional type/experience; tag kind/value/consultant; unique consultant/kind/value;
service consultant/active and type/currency/price. Price numeric(18,2). Revision protects
concurrent moderation, profile edits and service mutations; every service write also
updates its consultant revision. Client mutations supply the version last read.

Phase 2: availability rules (local weekday/time + IANA timezone), UTC blocked periods,
and Booking with immutable title/duration/price/currency snapshots, questionnaire and
participant IDs. Resolve DST explicitly; never persist only a local date/time. Use a
PostgreSQL exclusion constraint on consultant + UTC half-open range for occupying states,
or rigorously serialized database scheduling if extension permissions prohibit it.
Pending-payment holds expire. Reject self-booking, unavailable ranges and inactive/
unverified consultants inside the same transaction. No only-in-memory lock protection.

Phase 3: one booking payment association (with separate retry attempts/idempotency keys),
provider payment status separate from booking/earning/refund states. Capture commission
percent, fee, gross/net and currency at order creation, never recalculate historical fees.
Future CareerGuidance:PlatformCommissionPercent must be explicitly configured and validated
0–100 before enabling checkout. Captured -> HeldForSession -> DisputeWindow -> Payable;
Disputed/RefundPending block payout. This is internal accounting, not provider escrow.
No payout implementation until provider product/merchant permissions are confirmed.

Cancellation/refund policy version/windows must be snapshotted. Consultant cancellation,
confirmed no-show/platform failure allow refund review; candidate early/late cancellation
follows snapshotted policy. Subjective complaints and unsuccessful job searches do not
automatically refund. Admin-authorized partial/full refunds use provider idempotency and
reconciliation. Never mark money refunded solely by changing a local booking status.

Phase 4: one review per completed paid booking, candidate ownership, rating 1–5; unique
BookingId and moderation state. Initially calculate aggregates from approved reviews with
indexes; introduce transactional aggregates only when measured query load warrants it.
Disputes have deadlines, evidence metadata, admin resolutions/refund decisions and audit.
Notify via existing infrastructure with durable/outbox delivery; never email in a transaction.

Phase 5: provider-neutral private MeetingProvider/MeetingUrl/ExternalMeetingId accessible
only to participants/admin. No public meeting links, paid video API or WebRTC in Phase 1.
Discovery CompanyId/company/role filters support future job-page recommendations without
coupling consultants to jobs. Audit/status timestamps support funnel analysis; add bounded
view/conversion events later rather than introducing a large analytics system.

## Phase 1 API and authorization

- Public GET /api/career-guidance/consultants and /{id}: verified-only profiles and active
  services, bounded pagination; company ID/name, role, search, professional type, language,
  expertise, service type, currency and price filters. Sort newest, experience, price low/high.
  Price filters/sorts require currency to avoid comparing unrelated monetary units. Availability,
  relevance ranking and rating sorts await their real data/models; not silently faked.
- Authenticated POST /api/career-guidance/me/application, GET/PUT /me/profile: actor comes
  from claims; no supplied owner ID. Active users may apply; approval is always manual.
- Authenticated POST /me/services and PUT/DELETE /me/services/{id}: own profile only,
  no access by another consultant ID; suspended owners cannot modify offerings.
- Administrator GET /api/admin/career-guidance/consultants, GET /{id}, POST /{id}/verification:
  pending/status search, private verification metadata, explicit approve/reject/suspend/reactivate.
  Application service also checks active Administrator identity; no reliance on frontend checks.

Public DTOs exclude UserId, LinkedIn verification URL, admin reason/reviewer and revision;
owner/admin DTOs include the private review state. Public text must be rendered as plain text,
not HTML. Owner edits reset Verified/Rejected to Pending; suspension stays admin-controlled.
Reactivation applies only to a previously verified suspended profile. No booking endpoints;
IsAcceptingBookings stays false until booking safety exists. Policy acceptance affirms independent
advice, no job/interview/referral guarantees, no confidential employer data, and compliance with
the consultant's own employer/outside-work obligations. Not legal certification or endorsement.

## Review decisions and remaining risks

Phase 1 allows INR/USD/EUR/GBP price quotes, 15–180 minute duration and positive prices up
to 1,000,000 with two decimals; these are technical bounds, not recommended prices or a claim
of provider settlement support. Confirm launch currencies before checkout. LinkedIn URL ownership
is not automatically verified. Admins must substantiate claims manually; no employment check API.
Editing any public profile claim removes verification; service text remains owner-managed plain
text and is subject to suspension/admin review. No paid booking is possible in this phase.
Existing category-investigation test and SQL artifact are unrelated and must remain untouched.
Generate only a new PostgreSQL migration, inspect its scope, never apply it here.

## Implemented contracts and operations

Profile request: displayName, professionalHeadline, bio, optional companyId,
companyName, currentRole, yearsOfExperience, professionalType, linkedInUrl,
languages[], expertise[], acceptIndependentGuidancePolicy, revision (required for edits).
Professional types: 1 CurrentEmployee, 2 FormerEmployee, 3 Recruiter, 4 HiringManager,
5 CareerCoach. States: 1 Pending, 2 Verified, 3 Rejected, 4 Suspended.
Service request: serviceType, title, description, durationMinutes, price, currency,
isActive, revision. Service type is an extensible normalized string, not a fixed price tier.
Review request: action (1 Approve, 2 Reject, 3 Suspend, 4 Reactivate), reason, revision.
Delete service takes the current profile revision as a query parameter.

Owner/admin writes return ConsultantPrivateResponse with profile, review metadata,
services (including inactive but not deleted), and the new revision. Reuse that revision
for the next mutation; HTTP 409 requires reload. There is no owner-ID request field.
New applications/services return 201; reads/updates return 200 in ApiResponse wrappers.
List responses contain items/pageNumber/pageSize/totalCount/totalPages. Public services
are only active/non-deleted. Price sorts use the lowest matching active service price
per consultant, with Id tie-breaking. Language/expertise/type keys are uppercase;
descriptions and names retain casing and must be displayed as plain text.

Policy acknowledgement is recorded with `career-guidance-v1` and UTC time. The
constant disclaimer must be shown with the acceptance checkbox; accepting does not
constitute employment verification. Rejected owners can revise and resubmit; suspended
owners cannot. Admin self-verification is prohibited. Review action/state is audited;
private current reason stays on the profile and is not copied to general audit metadata.

This is a foundation, not a booking launch. IsAcceptingBookings is deliberately false.
No payment/commission configuration is read or changed in Phase 1. No document upload,
ratings, reviews, messages, booking calendar, meeting links or payouts are implemented.
Large-scale text search indexing and page-view analytics remain future work. Deployment
requires separate approval/application of the new migration before using these endpoints.

## Migration and validation record

Generated `20260920171246_AddCareerGuidanceFoundation` in Persistence.Postgres only.
Up creates CareerConsultants, CareerConsultantServices and CareerConsultantTags, with
their FKs/indexes and service price/duration checks. No changes to existing tables,
no historical migration edits, no new xmin/JobId1, no business data changes. Designer
and snapshot model bodies match. No migration was applied; no live DB was accessed.

Commands actually executed:

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj --no-restore
dotnet build --no-restore
dotnet build --no-restore "-p:NoWarn=CA1707%3BCA1859%3BCA1861"
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CareerGuidanceTests'
```

API build passed without suppressions. Plain solution build hit 81 pre-existing test
analyzer errors (CA1707/CA1859/CA1861); established command-line workaround passed with
0 warnings/errors. Global warning settings unchanged. Initial new-test compilation
issues were corrected (target-typed validator overloads and a static helper).
Final focused tests: **23 passed, 0 failed, 0 skipped, total 23**.

Offline migration commands used a child-shell dummy connection, not production secrets:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Database=career_guidance_model_only;Username=postgres;SSL Mode=Disable'
dotnet ef migrations add AddCareerGuidanceFoundation --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext --no-build
dotnet ef migrations has-pending-model-changes --project JobPortal.Persistence.Postgres --startup-project JobPortal.API --context JobPortalDbContext --no-build
```

Generation passed; model check reported no pending changes. Tests verify migration
operation scope, metadata, SQL translation, identity ownership, admin role and self-
verification guards, public privacy/visibility, policy/price validation, moderation,
tag revival, service deletion, audit persistence and optimistic concurrency.

Full non-database regression command (environment removals affect this child shell only):

```powershell
Remove-Item Env:AIAPPLY_STEP5_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName!~AIApplyDistributedPostgresTests' --logger 'console;verbosity=minimal'
git diff --check
git status --short
git diff --stat
```

**941 passed, 0 failed, 0 skipped, total 941**. Three live PostgreSQL tests excluded,
not counted as skipped. No live concurrency or migration deployment claim is made.
Diff check passed (CRLF notices only). Nothing staged/committed/pushed/deployed.

## Exact change inventory

Created:

- JobPortal.API/Controllers/CareerGuidanceControllers.cs
- JobPortal.Application/Features/CareerGuidance/CareerGuidanceContracts.cs
- JobPortal.Application/Features/CareerGuidance/CareerGuidanceService.cs
- JobPortal.Application/Features/CareerGuidance/CareerGuidanceValidators.cs
- JobPortal.Application.Tests/CareerGuidanceTests.cs
- JobPortal.Domain/Entities/CareerGuidance.cs
- JobPortal.Persistence/Configurations/CareerGuidanceConfigurations.cs
- JobPortal.Persistence/Repositories/CareerGuidanceRepository.cs
- JobPortal.Persistence.Postgres/Migrations/20260920171246_AddCareerGuidanceFoundation.cs
- JobPortal.Persistence.Postgres/Migrations/20260920171246_AddCareerGuidanceFoundation.Designer.cs
- docs/career-guidance-phase1.md

Modified:

- JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Persistence/Context/JobPortalDbContext.cs
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs

`git diff --stat` (tracked files only): 4 files changed, 274 insertions; it excludes
the 11 newly created, untracked files above. Final status has those four modified
files and eleven new files, plus the two pre-existing untracked files below.

Untouched/excluded: JobPortal.Application.Tests/JobSourceCategoryResponseTests.cs
(prior investigation) and artifacts/AddJobAggregationDedupFoundation.idempotent.sql.
Stash and nested duplicate untouched. No .gitignore/appsettings changes. No existing
aggregation, referrals, payments, AI Apply or notification behavior modified.
