# Referral marketplace Phase 1 — implementation and rollout

## Implemented scope

The existing Refer-a-Job composer and administrator approval remain the opportunity source and moderation gate. Verified employee status is not required. This phase does not introduce an employee-verification administration workflow or User columns; all current referrers are labelled `Employee Referrer`. Company verification is never presented as employee verification. Stronger company-scoped employee verification is deferred.

The existing `ReferralContactAccess` membership/product, pricing, GST, payment verification and renewal logic are reused. No new paid plan is created. `CareerHarborMembership`, its 35/day application quota, Interview Insights policy and normal job/application implementation are unchanged by this pass.

`ReferralRequest` links an existing candidate User, JobReferral and referrer User. It stores requested/expiry, acceptance, rejection, submission, confirmation and not-received timestamps, candidate message, private rejection reason, optional submission reference and immutable acceptance membership/window attribution. Candidate profiles and resumes are read from existing data/storage, not copied into requests.

## State and quota

```text
Requested → Accepted → ReferralSubmitted → CandidateConfirmed
Requested → Rejected
Requested → Expired
```

There is no cancellation state/endpoint. NotReceived is an investigation timestamp on ReferralSubmitted, not a state rewrite or fraud determination. A later confirmation retains that historical flag for administrators.

Requests expire exactly 48 hours after RequestedAtUtc. Queries and domain transitions recognize overdue Requested records as Expired without a scheduler. ExpiredAtUtc is presented as the expiry deadline when no cleanup has materialized the terminal status. No new background worker is required.

Creating, rejecting or expiring a request consumes zero connections. A successful acceptance consumes exactly one. Repeated accepts return the existing successful record and create no additional quota use, audit transition or notification. Rejected/expired requests cannot be requested again for the same opportunity.

The quota is ten acceptances per applicable 30-day window, identified by membership ID and window start. Windows are anchored to the active membership term's StartsAtUtc. Early renewal extends the existing term and does not immediately reset quota; each successive 30-day window receives ten connections. Inactive-term reactivation uses its new StartsAtUtc. Each acceptance persists its original start/end window, so subsequent renewal does not change history. Membership end can shorten the last window.

`ReferralSlots` defaults to one for old records and omitted submission inputs. New submissions allow 1–1000. AcceptedReferralCount and RemainingReferralSlots are derived from records with AcceptedAtUtc, including historical soft-deleted requests. Capacity and quota are not based on ReferralUnlock/contact views. Exhausted opportunities reject new requests and acceptances. Public cards and owner submission responses expose server-derived capacity counts.

Every write is an EF execution-strategy transaction. PostgreSQL transaction-scoped locks serialize candidate first, opportunity second, across application instances. Membership and opportunity rows are additionally locked to coordinate renewal/moderation writes. The transaction rechecks ownership, active candidate, active approved/published opportunity, membership, request status, quota and slots before committing state, outbox and audit. A permanent database unique constraint enforces one candidate/opportunity request. Application checks and the domain state machine supplement database constraints. In-memory locking exists only for non-relational tests and is not claimed as database race proof.

## APIs and authorization

All private responses disable caching. Candidate APIs require JWT Candidate role plus active-user checks. Referrer APIs require an active authenticated user and matching request AND current opportunity ownership. Administrators require the existing Administrator role. Missing and another user's private request use the same not-found contract.

| Method / route | Audience / behavior |
| --- | --- |
| POST `/api/referrals/{referralId}/requests` | Candidate; body `{ "candidateMessage": "optional bounded message" }` |
| GET `/api/referral-requests/mine?pageNumber=1&pageSize=20` | Candidate requests plus quota summary |
| GET `/api/referral-requests/{id}` | Candidate-owned safe request detail |
| GET `/api/referral-requests/{id}/contact` | Candidate-owned protected contact |
| POST `/api/referral-requests/{id}/confirm` | Candidate owner; Submitted → Confirmed |
| POST `/api/referral-requests/{id}/not-received` | Candidate owner; flag Submitted for investigation |
| GET `/api/referrer/referral-requests` | Own opportunities' inbox; optional status and pagination |
| GET `/api/referrer/referral-requests/{id}` | Authorized candidate card/request detail |
| GET `/api/referrer/referral-requests/{id}/resume` | Authorized existing resume stream; unavailable for rejected/expired requests |
| POST `/api/referrer/referral-requests/{id}/accept` | Atomic acceptance/quota/capacity check |
| POST `/api/referrer/referral-requests/{id}/reject` | Body `{ "reason": "optional private reason" }` |
| POST `/api/referrer/referral-requests/{id}/mark-referred` | Body `{ "referralSubmissionReference": "optional reference" }` |
| GET `/api/referrer/metrics` | Server-derived posted/received/accepted/submitted/confirmed counts and confirmation/submission ratio; zero denominator returns zero |
| GET `/api/admin/referrals/requests` | Investigation list; optional issuesOnly, status and pagination |
| GET `/api/admin/referrals/requests/{id}` | Investigation detail with operational candidate/referrer IDs |

Posting, mine, public browsing, URL extraction and existing administrator approval routes remain. POST `/api/referrals` additionally accepts optional `referralSlots` (default one).

Status values: Requested=1, Accepted=2, Rejected=3, Expired=4, ReferralSubmitted=5, CandidateConfirmed=6. Follow the application's existing JSON enum handling. Invalid status filters and oversized text fail validation.

Stable error codes include `REFERRAL_ACCESS_REQUIRED`, `REFERRAL_CONNECTION_LIMIT_REACHED`, `REFERRAL_ALREADY_REQUESTED`, `REFERRAL_SLOTS_FULL`, `REFERRAL_REQUEST_EXPIRED`, `INVALID_REFERRAL_STATUS`, `REFERRAL_CONTACT_NOT_AVAILABLE`, `INVALID_REFERRAL_REQUEST` and `FORBIDDEN_REFERRAL_REQUEST`; private missing/unauthorized ownership uses existing `not_found`. DTOs and existing ApiResponse/ApiError conventions are used, never EF entities or arbitrary status input.

## Privacy and frontend contract changes

Public referral response `referrerName` is removed and replaced by `referrerLabel`. `referrerProfileImageUrl`, exact company start date and tenure are removed. Public responses do not contain referrer user IDs or contact methods. Existing explicitly public professional role/company metadata can remain, without linking a personal profile. CompanyId/ReferralId identify the employer/opportunity, not the employee.

Candidate request DTOs contain a generic label and no referrer identity. Contact DTO `referrerName` becomes `referrerLabel`; only the individually opted-in email/phone/LinkedIn methods can be returned by the protected endpoint. Owner/admin submission DTO identity fields remain operationally restricted to those callers. Candidate inbox cards contain only evaluation information; they expose no raw resume storage key or public resume URL.

Contact requires the candidate-owned Accepted, ReferralSubmitted or CandidateConfirmed request, current active ReferralContactAccess membership, and an active approved opportunity/referrer matching the original request. Payment alone and historical unlocks cannot authorize contact. Opportunity reassignment cannot transfer an old acceptance's authorization to a new employee.

Legacy GET `/api/referrals/unlock/{jobId}` is safely adapted: it resolves only this candidate's successful request and calls the exact same contact authorization. Otherwise it returns RequestRequired (new unlock status value 4) with no contact. Existing LoginRequired/MembershipRequired/Granted values remain. Historical ReferralUnlock rows stay analytics only and are never converted into requests.

Public cards conservatively show RequestRequired for members, rather than advertising unlocked contacts based on membership. Candidate-owned request detail's CanViewContact indicates actual eligibility. Frontend work is required for the new label/status, capacity display, request actions, referrer inbox/accept/reject/submission and candidate confirmation/not-received screens. No frontend files were changed. Coordinate frontend/backend rollout: the old membership-only contact UI cannot remain the sole interaction.

## Notifications and auditing

The existing durable outbox handles requested → referrer, accepted/rejected/submitted → candidate, confirmed/not-received → referrer. Not-received issues are discoverable in the administrator issuesOnly list. Notification payloads are generic, contain no private rejection notes or contact data, and use the existing `/dashboard/referrals` action route. Recipient/source eligibility is checked by the dispatcher against committed request state/timestamps. Deterministic business keys prevent duplicate transition intents. In-app/email delivery runs after commit using the existing framework/provider; delivery failures cannot undo committed acceptance. Audit metadata records only request ID and event name, not contact/profile/message content.

## Database and deployment

Migration: `20261007195914_AddReferralMarketplacePhase1`, in JobPortal.Persistence.Postgres. Up adds ReferralSlots (default 1), ReferralRequests, bounded text columns, lifecycle timestamps, acceptance attribution, restricted FKs, positive-slot/status/48-hour/acceptance-attribution checks and required indexes. No existing data conversion, destructive Up operation, User columns, share-code columns or new plan. Down removes only the new objects; do not apply Down after marketplace records have been collected.

Migration was generated/reviewed but not applied to any database. Review the forward SQL, back up the deployment database and apply through the existing controlled migration process before starting the new backend. An illustrative operator command, after supplying the intended deployment configuration manually, is:

```powershell
dotnet ef database update 20261007195914_AddReferralMarketplacePhase1 --project JobPortal.Persistence.Postgres --startup-project JobPortal.Persistence.Postgres --configuration Release
```

Complete isolated PostgreSQL race validation before release, then coordinate frontend contract integration and test one request → accept → submitted → confirmed flow. No commit, push or deployment was performed.

## Isolated local PostgreSQL test setup

Safe inspection found no PostgreSQL executable/service, no PostgreSQL test credentials/environment variables, no compose or test appsettings configuration, and no listener on 127.0.0.1:5432. The repository has only an API Dockerfile, not a test database provisioner. Existing PostgreSQL integration tests use explicit opt-in environment variables. A Docker WSL component was listed, but querying its tools timed out; no container was created. No application, Neon, staging or production database was contacted. No PostgreSQL installation, container launch, database creation or machine configuration change was performed.

Three race/constraint tests are implemented but not executed without an isolated local server. They refuse to connect unless the host is exactly localhost/127.0.0.1 and database exactly careerharbor_referral_test. They never read application settings, create no database, and create/remove only a random schema inside the explicitly provided disposable database.

After the operator installs/starts local PostgreSQL and chooses valid existing local credentials, create the disposable database manually (the password is prompted, not placed on a command line):

```powershell
createdb --host=127.0.0.1 --port=5432 --username=<local-test-user> --password careerharbor_referral_test
```

Set test-only configuration in that shell, substituting actual local credentials without committing or printing them:

```powershell
$env:REFERRAL_TEST_POSTGRES = 'Host=127.0.0.1;Port=5432;Database=careerharbor_referral_test;Username=<local-test-user>;Password=<local-test-password>'
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --filter 'FullyQualifiedName~ReferralMarketplacePostgresTests'
Remove-Item Env:REFERRAL_TEST_POSTGRES
```

The local test user must own this disposable database or have schema/extension creation privileges. Tests build an isolated schema from the actual model, exercising the real repository transactions and constraints with independent contexts. They test the final slot race, the tenth-versus-eleventh connection race across different jobs, duplicate acceptance and the database's unique request constraint. No production concurrency result is claimed until these run.

## Files attributable to this pass

The working tree was clean before the first edit. Unrelated job-aggregation changes appeared during validation and were preserved; they are not part of this manifest.

Modified (13):

- JobPortal.Application.Tests/JobReferralServiceTests.cs
- JobPortal.Application/Abstractions/Referrals/IJobReferralService.cs
- JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Application/Features/Referrals/JobReferralService.cs
- JobPortal.Application/Features/Referrals/ReferralDtos.cs
- JobPortal.Domain/Entities/JobReferral.cs
- JobPortal.Domain/Entities/NotificationDelivery.cs
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs
- JobPortal.Persistence/Configurations/EntityConfigurations.cs
- JobPortal.Persistence/Context/JobPortalDbContext.cs
- JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs
- JobPortal.Persistence/Repositories/JobReferralRepository.cs
- JobPortal.Persistence/Repositories/NotificationDeliveryRepository.cs

Added (14):

- JobPortal.API/Controllers/AdminReferralRequestsController.cs
- JobPortal.API/Controllers/ReferralRequestsController.cs
- JobPortal.API/Controllers/ReferrerRequestsController.cs
- JobPortal.Application.Tests/ReferralMarketplaceMigrationTests.cs
- JobPortal.Application.Tests/ReferralMarketplacePostgresTests.cs
- JobPortal.Application.Tests/ReferralMarketplaceTests.cs
- JobPortal.Application/Features/Referrals/ReferralMarketplaceContracts.cs
- JobPortal.Application/Features/Referrals/ReferralMarketplaceService.cs
- JobPortal.Domain/Entities/ReferralRequest.cs
- JobPortal.Persistence.Postgres/Migrations/20261007195914_AddReferralMarketplacePhase1.cs
- JobPortal.Persistence.Postgres/Migrations/20261007195914_AddReferralMarketplacePhase1.Designer.cs
- JobPortal.Persistence/Configurations/ReferralRequestConfiguration.cs
- JobPortal.Persistence/Repositories/ReferralMarketplaceRepository.cs
- docs/referral-marketplace-phase1.md

## Deferred functionality and release limits

Employee-verification administration, cancellation, share codes/attribution analytics, points/rewards/wallet/leaderboards, proof uploads, ATS integration and automatic fraud determination are deferred. Existing admin opportunity approval remains sufficient for launch participation. There are no critical workflow TODOs. Frontend integration and actual local PostgreSQL race execution remain release requirements; broader unrelated test failures must be tracked separately.

## Executed validation

Results overlap and must not be summed:

| Run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Initial targeted referral tests | 56 | 0 | 0 |
| Full application test project | 1921 | 125 | 9 |
| Expanded regression including existing Career Guidance reminder fixture | 285 | 1 | 3 |
| Final relevant regressions, excluding that unrelated Career Guidance fixture | 291 | 0 | 3 |

Final suite breakdown: ReferralMarketplaceTests 35, ReferralMarketplaceMigrationTests 2, JobReferralServiceTests 17, ReferralJobSkillsTests 13, PortalMembershipTests 63, MembershipPricingTests 13, CandidateModuleTests 69, PublicJobSearchTests 32, InterviewInsightsMembershipAuthorizationTests 9, NotificationDispatcherTests 11, NotificationEligibilityTests 10, NotificationRealtimeTests 6, PostgresPendingModelTests 1, PostgresPersistenceConfigurationTests 10. Three ReferralMarketplacePostgresTests are skipped because the isolated local PostgreSQL prerequisite is absent. The pending-model test passes with the generated migration/snapshot; the new migration tests verify additive scope and permanent duplicate protection.

The full suite is not green. Its 125 failures are CareerAnalyticsTests 5, CareerFinanceTests 3, CareerReservationExpiryTests 3, CareerSchedulingMigrationTests 1, CareerSessionReviewTests 11, CareerSessionTests 29, CareerTrustTests 29, NotificationEligibilityTests 1, NotificationOutboxTests 1, PlaywrightBrowserAdapterTests 38 and PlaywrightRuntimeReadinessTests 4. No referral test failed. Browser tests reported launch permission/process errors. Many Career tests fail their existing session/paid-booking fixture setup. Static baseline inspection confirms five CareerGuidanceBooking foreign keys despite the old test expecting four, and notification foundation objects already present in the committed snapshot despite the old pending-delta test expecting them absent. These unrelated tests/business services were not changed to make the full suite pass.

Full run:

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --logger 'trx;LogFileName=referral-phase1-full.trx' --results-directory 'C:\Users\Ashish\Documents\Codex\2026-07-27\referral-phase1-test-results'
```

Final relevant run:

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --filter '(FullyQualifiedName~ReferralMarketplace|FullyQualifiedName~JobReferralServiceTests|FullyQualifiedName~ReferralJobSkillsTests|FullyQualifiedName~PortalMembershipTests|FullyQualifiedName~MembershipPricingTests|FullyQualifiedName~CandidateModuleTests|FullyQualifiedName~PublicJobSearchTests|FullyQualifiedName~InterviewInsightsMembershipAuthorizationTests|FullyQualifiedName~NotificationDispatcherTests|FullyQualifiedName~NotificationEligibilityTests|FullyQualifiedName~NotificationRealtimeTests|FullyQualifiedName~PostgresPendingModelTests|FullyQualifiedName~PostgresPersistenceConfigurationTests)&FullyQualifiedName!~CareerReminderEligibilityDoesNotReusePreviouslyTrackedPayment' --logger 'trx;LogFileName=referral-phase1-relevant.trx' --results-directory 'C:\Users\Ashish\Documents\Codex\2026-07-27\referral-phase1-test-results'
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore --no-incremental
```

Test compilation uses the existing workaround disabling build analyzers for the test project; normal API Release analyzers remain enabled. An intermediate overlapping rebuild collided with test compilation; those invalid runs were rerun sequentially. Unrelated job-aggregation edits also appeared during validation and briefly produced missing-symbol compilation errors; they were preserved, not patched by this pass. TRX artifacts live outside the repository in the results directory above.

Final sequential Release API build succeeded with **0 warnings and 0 errors**, using normal analyzers, in 1 minute 7.80 seconds. Scoped diff checking found no whitespace errors (Git emitted its existing LF/CRLF normalization notices). The backend implementation and available regressions are complete; this is not a claim of release readiness before actual PostgreSQL race validation and frontend contract rollout.
