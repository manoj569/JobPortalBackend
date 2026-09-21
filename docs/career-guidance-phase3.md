# Career Guidance Phase 3 — payments and pending earnings

Branch: `feature/career-guidance-phase3`, based on Phase 2 merge `a1bcf94`.
Backend only. No migration is applied by this implementation task; no provider API or
production/shared database is contacted during validation.

## Scope and architecture

Career Guidance financial records are separate from membership Payment/PaymentHistory
and membership entitlements. `CareerFinanceService` owns authorization, state transitions,
snapshots, idempotent reconciliation and audit. `ICareerPaymentGateway` isolates provider
HTTP and cryptography. `CareerFinanceRepository` shares the scoped EF context with audit,
booking and profile writes so each SaveChanges mutation is transactional on PostgreSQL.

The existing membership Razorpay gateway's test-key restriction is NOT removed. A new
Career Guidance gateway accepts test/live key modes, with checkout disabled by default.
There are no real provider calls in tests. Provider credentials must be explicitly
configured before enabling checkout; configuration names, not values, appear below.

CareerHarbor does **not** call this architecture escrow. Payments purchase a consultation,
not a job, referral, interview or hiring guarantee. Not getting a job is not a refund
condition. Subjective dissatisfaction/disputes belong to Phase 5. Consultant earnings
are not automatically bank-settled in Phase 3.

## Financial model and migration

Migration: `20260921045557_AddCareerGuidancePaymentsAndEarnings`.

- `CareerGuidancePayments`: booking/candidate/consultant identity, provider order/payment
  IDs, gross/currency, commission percentage/amount and net snapshots, state, safe
  failure code, paid timestamp, refund-review flag, policy version and revision.
- `CareerGuidancePaymentEvents`: append-only signed-body digest/type/payment association.
  Raw webhook bodies, signatures and payment instruments are never stored.
- `CareerGuidanceEarnings`: one earning per captured payment, immutable gross/commission/
  net/currency, Pending/Payable/Settled/Reversed status, future settlement metadata and
  revision. Status changes are audited; financial amounts are never rewritten on reversal.
- `CareerGuidanceRefunds`: one full approved refund per payment, candidate/booking/
  consultant/admin IDs, immutable amount/currency/reason, provider refund ID, lifecycle
  timestamps and revision. Coded reasons avoid unnecessary private free-text collection.
- `CareerGuidanceBookings.RequiresPayment`: false for existing rows, true on newly
  created bookings and when a legacy pending booking enters checkout. Exposed in the
  private booking response. No historical booking is retroactively treated as paid.

All amounts use numeric(18,2); commission percentage uses numeric(7,4). Restrict-delete FKs
protect financial history. Lifetime uniqueness (not filtered by soft deletion) covers
BookingId/payment, ProviderOrderId, ProviderPaymentId, PaymentId/earning, PaymentId/refund,
ProviderRefundId and event digest. Nullable provider ID uniqueness filters only nulls.
Checks enforce positive gross/refund, nonnegative commission/net, gross=commission+net,
commission 0–100 and valid enum ranges. Indexes support candidate/status/date,
consultant/earning-status/date, admin status/refund-review/date and FK lookups.

The context rejects deletion/soft deletion of financial history, edits to event rows,
changes to identity/amount snapshots and rebinding non-null provider IDs. Financial
queries ignore soft-delete filters on related users/profiles to preserve history;
caller identity must still be active. Database uniqueness/check constraints are the
cross-process authority; context immutability guards application writes, not arbitrary
privileged SQL. No database trigger is added. No Phase 1/2 migration is changed.

## Payment and booking state machines

Payment aggregate: `Created -> OrderCreated -> Authorized -> Captured -> RefundPending
-> Refunded`. Authorization is optional; provider capture may arrive first.

`payment.failed` describes a failed attempt against an order that can still be paid.
It records the fixed safe code `provider_attempt_failed`; it does not permanently fail
the aggregate or permit creating a second independently payable order. No raw provider
error message is retained. Capture clears this code. Late authorization/failure events
never regress captured/refunding/refunded state. Processed refunds never regress.

New bookings remain Pending until trusted provider capture. Consultant manual confirmation
is rejected when RequiresPayment=true. Legacy Phase 2 bookings retain manual confirmation
when the flag is false. Existing completion/no-show/cancellation behavior otherwise remains.
Creating an order alone does not confirm a booking or create earnings.

On capture, one atomic SaveChanges records payment, confirms an eligible future Pending
booking, updates revisions, creates one Pending earning and appends audit rows. A capture
after cancellation, deletion, start time or consultant suspension is still recorded as
money received, but the booking is NOT resurrected: RequiresRefundReview=true, earnings
remain Pending, and an administrator must review a full refund. Concurrent booking
cancellation/profile moderation conflicts roll back the capture mutation and are retried
through provider delivery or explicit reconciliation.

## Order, verification and reconciliation

1. Candidate-only route and active database role check; owner must match booking.
2. Require future Pending booking, active verified consultant/user and active service;
   no self payment. Amount comes exclusively from booking PriceSnapshot, not the client.
3. Require enabled payments, valid explicit commission and configured provider keys.
4. Persist unique payment intent/snapshots and booking revision BEFORE the external POST.
5. Send an order with receipt `cg_<payment-guid-without-hyphens>`, INR paise and
   partial_payment=false. Save validated returned provider order ID; expose only public
   KeyId/order/amount/currency for checkout.
6. Repeated checkout reuses the stored order. If an earlier response was lost, GET orders
   by the stable receipt and adopt only one matching order with exact amount/currency.
   A missing/ambiguous result returns a reconciliation conflict, NEVER another POST.

Verification checks candidate ownership, exact stored order, checkout HMAC and provider
GET payment (ID/order/amount/currency/status). Only captured status fulfills the booking.
Duplicate successful verification cannot create another earning. Provider payment ID
uniqueness and optimistic revision protect cross-record/concurrent binding.

Candidate-owned POST reconciliation fetches the stored/recovered provider order's payments
and applies the same capture path, without trusting client success/signature claims. This
recovers missed/early webhooks and lost checkout callbacks, including captures after a
booking becomes ineligible. More than one provider capture is blocked for operator review.

**Deliberate fail-closed boundary:** a crash after durable intent but before sending POST
cannot be distinguished automatically from a lost response. If receipt lookup finds no
order/refund, the record remains blocked for operator investigation. This phase does not
provide a force-retry/reset endpoint or claim exactly-once HTTP delivery. Do not manually
delete intent rows or create replacement orders until the provider outcome is proven.
This avoids assuming undocumented idempotency support for the Razorpay Orders API.

INR only is supported in this first integration (amount * 100 exactly, maximum matching
the existing service price bound). Other Phase 1 service currencies are rejected at
checkout rather than using incorrect currency exponents. No capture API is invoked;
configure appropriate Razorpay automatic capture and test it before enabling checkout.

## One webhook URL, separate fulfillment

The existing URL remains `POST /api/payments/razorpay/webhook`. The action moves from
PaymentsController to RazorpayWebhooksController; there is no competing duplicate route.

The controller reads bounded raw bytes (1 MiB maximum). Career Guidance HMAC is validated
before parsing; membership HMAC is independently validated before routing to the existing
membership handler. Known Career Guidance orders/refunds go only to CareerFinanceService.
Unknown valid events are acknowledged without financial changes. Known membership orders
still call the existing PaymentService; its test-only gateway is not constructed for
unknown or Career Guidance orders. Checkout/membership entitlement semantics are unchanged.

Supported Career Guidance events: payment.authorized, payment.captured, payment.failed,
refund.created, refund.processed, refund.failed. Each supported known event fetches current
provider entity state, avoiding reliance on delivery ordering. Event dedupe uses SHA-256
of the exact signed raw body, not the unsigned event-id header. Different byte payloads for
the same effect remain safe through payment revisions and unique earning/refund relations.
Membership retains its existing event-id handling. Invalid signature/JSON is rejected;
unknown supported order events can be recovered later through payment reconciliation.

Concurrent financial updates return safe 409s; the provider must retry failed webhook
deliveries. No 2xx is sent after a known event's transaction fails. Secrets, raw payloads,
card/bank/UPI information and questionnaire content are never logged or returned. Gateway
errors are replaced with a generic safe reconciliation error; HTTP redirects are disabled.

Provider conventions checked against primary documentation:

- [Razorpay Orders SDK documentation](https://github.com/razorpay/razorpay-php/blob/master/documents/order.md)
- [Razorpay Refunds SDK documentation](https://github.com/razorpay/razorpay-php/blob/master/documents/refund.md)
- [Razorpay webhook validation](https://razorpay.com/docs/webhooks/validate-test/)

## Commission, earnings and refunds

Commission is required configuration, not a default percentage. At order intent creation:

`commission = decimal.Round(gross * percent / 100m, 2, MidpointRounding.AwayFromZero)`

`net = gross - commission`

Percentage range 0–100 inclusive, up to four decimal places. Snapshot values do not change
with future service prices or commission configuration. Gross always equals commission+net.
Taxes, provider processing fees and currency conversion are not silently deducted/modelled.

Captured -> earning Pending. Payable/Settled states and timestamps exist for future policy,
but there are NO endpoints/jobs that promote earnings or transfer funds in this phase.
Summary groups by currency AND status; reversed amounts are historical, not payable balances.

Only an active Administrator may approve/initiate a full refund. Reasons are consultant
cancellation, consultant no-show (both checked against booking status), platform failure,
or administrator correction. No candidate-controlled provider refund or subjective refund
reason is accepted. Full amount derives from the capture snapshot, never request data.
An active booking is administratively cancelled when its refund is approved; historical
Completed/NoShow states remain historical. No automatic refund on ordinary cancellation.

Refund: Requested (admin approved durable intent) -> ProviderPending -> Processed or Failed.
First request persists intent before provider POST. Repeated requests only GET/reconcile the
existing provider refund, never reissue POST. A failed/uncertain refund is blocked for review.
Authoritative processed confirmation may reconcile a prior failure; success cannot regress.
Only processed confirmation marks payment Refunded and earning Reversed, atomically with
audit/revision changes. Failure leaves money captured, payment RefundPending, earnings
Pending and refund review required. Payable/Settled earnings reject refunds pending future
settlement reconciliation. Refund amount/provider payment/currency/receipt must match exactly.

One full refund per payment, unique provider refund ID and unchanged amount snapshots prevent
backend over-refunding. Partial or manually issued external refunds are NOT auto-imported;
unknown refund events require operator reconciliation and do not silently mutate the ledger.
The refund list lookup examines at most 100 provider refunds; no match still blocks reissue.

## API and authorization

All private responses use ApiResponse/PagedResponse. List pagination defaults 1/20, maximum
100 rows. Candidate/admin role is checked both by controller and active DB identity. Consultant
queries scope through Consultant.UserId rather than a client-supplied consultant ID.

| Method | Route | Access |
| --- | --- | --- |
| POST | `/api/career-guidance/bookings/{bookingId}/payment/order` | Owning Candidate |
| POST | `/api/career-guidance/bookings/{bookingId}/payment/verify` | Owning Candidate |
| POST | `/api/career-guidance/bookings/{bookingId}/payment/reconcile` | Owning Candidate |
| GET | `/api/career-guidance/bookings/{bookingId}/payment` | Owning Candidate |
| GET | `/api/career-guidance/payments/mine` | Candidate's records |
| GET | `/api/career-guidance/refunds/mine` | Candidate's records |
| GET | `/api/career-guidance/me/earnings` | Own consultant earnings |
| GET | `/api/career-guidance/me/earnings/summary` | Own consultant summary |
| GET | `/api/admin/career-guidance/payments` and `/{id}` | Administrator |
| GET | `/api/admin/career-guidance/refunds` and `/{id}` | Administrator |
| POST | `/api/admin/career-guidance/refunds/{paymentId}` | Administrator |
| GET | `/api/admin/career-guidance/earnings` | Administrator |
| POST | `/api/payments/razorpay/webhook` | Raw-body authenticated provider |

Verification body: orderId, paymentId, signature. Refund body: reason enum (1–4). Neither
accepts an amount. No public discovery DTO exposes financial data. Earnings DTOs contain
gross/commission/net but no gateway secrets, payment instruments or candidate questionnaires.

## Configuration and rollout

No appsettings files or secrets are modified. Names only:

- `CareerGuidance__PaymentsEnabled` (default false)
- `CareerGuidance__PlatformCommissionPercent` (no implicit default; required when enabled)
- `CareerGuidance__Razorpay__KeyId`
- `CareerGuidance__Razorpay__KeySecret`
- `CareerGuidance__Razorpay__WebhookSecret`

The three provider settings optionally fall back to existing `Razorpay__KeyId`,
`Razorpay__KeySecret`, `Razorpay__WebhookSecret`. Configure all three overrides together
when using a separate account/mode. Public KeyId is checkout information, not KeySecret.
Disabling checkout does not disable financial reads, verification/reconciliation or
webhooks for outstanding payments. New bookings require payment even while checkout is
disabled; coordinate rollout so clients do not create unpayable bookings indefinitely.

Pre-deployment gates:

1. Review migration SQL and account/provider configuration without exposing secrets.
2. Run the opt-in localhost concurrency fixture; never point it at shared/production DB.
3. Exercise Razorpay test mode with real test callbacks, lost-response recovery and refund
   lifecycle before enabling live checkout. No live/test provider call was made in this task.
4. Confirm capture settings, webhook subscriptions/signature secret and retry behavior.
5. Establish operator procedures for unknown order/refund outcomes, external/partial refunds,
   late capture review and reconciliation. There is no automatic reconciliation worker.
6. Define pending reservation expiry and refund/dispute/payable windows before payout rollout.
   This phase does not introduce a new expiry scheduler or payout automation.

## Tests and validation commands

Focused suites cover commission/rounding, ownership/role/status checks, booking snapshots,
idempotent order recovery, signatures/provider mismatch, duplicate/out-of-order callbacks,
late capture, immutable amounts, earnings privacy, full-refund reversal/failure, webhook
routing, optimistic revisions, migration scope and database uniqueness metadata.

`CareerFinancePostgresTests` is opt-in via `CAREER_GUIDANCE_FINANCE_TEST_POSTGRES` and
rejects non-loopback hosts/database names other than `career_guidance_test`. It creates
an isolated GUID schema, minimal parent fixtures and the exact Phase 3 migration DDL,
then competes on order/event/earning/refund unique constraints and drops only its own
schema. It does not use DefaultConnection or call Database.Migrate. It was NOT executed:
no local PostgreSQL/Docker tools are installed. Phase 2's local DB test is also skipped.

```powershell
dotnet build JobPortal.API/JobPortal.API.csproj --no-incremental --no-restore
Remove-Item Env:CAREER_GUIDANCE_TEST_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:CAREER_GUIDANCE_FINANCE_TEST_POSTGRES -ErrorAction SilentlyContinue
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-restore "-p:NoWarn=CA1707%3BCA1859%3BCA1861" --filter 'FullyQualifiedName~CareerFinance|FullyQualifiedName~CareerScheduling|FullyQualifiedName~CareerGuidanceTests'

Remove-Item Env:AIAPPLY_STEP5_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
Remove-Item Env:CAREER_GUIDANCE_TEST_POSTGRES -ErrorAction SilentlyContinue
Remove-Item Env:CAREER_GUIDANCE_FINANCE_TEST_POSTGRES -ErrorAction SilentlyContinue
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName!~AIApplyDistributedPostgresTests&FullyQualifiedName!~CareerSchedulingPostgresTests&FullyQualifiedName!~CareerFinancePostgresTests' --logger 'console;verbosity=minimal'
```

The test-only command-line analyzer workaround is the established Phase 1/2 workaround;
no global warning configuration is changed. API builds use no suppression.
Migration generation/model checking use a dummy child-process localhost connection,
not production configuration. No migration is applied.

Final validation results:

- API build: PASS, 0 warnings, 0 errors, without suppression.
- Focused Phase 1/2/3: 92 passed, 0 failed, 2 skipped, total 94.
- Final broader non-database suite: 1010 passed, 0 failed, 0 skipped, total 1010.
- The broader filter excludes five live DB tests (three existing AI Apply, one scheduling,
  one finance), rather than counting them as skipped.
- An earlier broader run had 1008 passed/1 failed in the untouched scheduler parallelism
  test. Its non-atomic peak-counter max/exchange can lose a concurrent maximum; no unrelated
  test/scheduler code was changed. A complete rerun passed 1009/1009, and after adding the
  offline SQL-generation test the final complete rerun passed 1010/1010.
- Offline EF pending-model check: no changes since the last migration.
- Migration scope/metadata/generated PostgreSQL SQL checks: PASS. SQL generated in-memory,
  never executed; no new SQL artifact created.
- git diff --check: PASS (Git LF/CRLF notices only).
- Both preserved untracked files' SHA-256 hashes match the pre-work baseline.
- stash@{0}: On feature/job-aggregation-dedup: local-work-before-job-dedup-validation,
  unchanged. Git HEAD remains a1bcf9413aee2dd1f15d56f1c5817f64e2535a81; nothing staged.

## Future integration boundaries

Phase 4/5 can consume captured bookings and pending earnings; no such features are started.
Add versioned cancellation/dispute windows and durable hold expiry before payable promotion.
Add provider-neutral settlement batches, reversal entries and idempotent payout operations
only with explicit payout authorization. Preserve financial snapshots and never assume
booking completion alone authorizes a transfer. Subjective disputes require a separate
review workflow, not a new unchecked refund reason. No reviews/video/notifications,
consultant subscription, job guarantee or membership entitlement is added here.

## Exact change inventory

Created (15 files):

- JobPortal.API/Controllers/CareerFinanceControllers.cs
- JobPortal.API/Controllers/RazorpayWebhooksController.cs
- JobPortal.Application/Features/CareerGuidance/CareerFinanceService.cs
- JobPortal.Application/Features/CareerGuidance/FinanceContracts.cs
- JobPortal.Application.Tests/CareerFinanceTests.cs
- JobPortal.Application.Tests/CareerFinanceWebhookRoutingTests.cs
- JobPortal.Application.Tests/CareerFinanceMigrationTests.cs
- JobPortal.Application.Tests/CareerFinancePostgresTests.cs
- JobPortal.Domain/Entities/CareerGuidanceFinance.cs
- JobPortal.Infrastructure/Payments/CareerRazorpayGateway.cs
- JobPortal.Persistence/Configurations/CareerFinanceConfigurations.cs
- JobPortal.Persistence/Repositories/CareerFinanceRepository.cs
- JobPortal.Persistence.Postgres/Migrations/20260921045557_AddCareerGuidancePaymentsAndEarnings.cs
- JobPortal.Persistence.Postgres/Migrations/20260921045557_AddCareerGuidancePaymentsAndEarnings.Designer.cs
- docs/career-guidance-phase3.md

Modified (10 files):

- JobPortal.API/Controllers/PaymentsController.cs — move webhook action to single routing controller.
- JobPortal.Application.Tests/CareerSchedulingTests.cs — explicitly mark the legacy manual-confirmation fixture as pre-payment.
- JobPortal.Application/DependencyInjection/ServiceCollectionExtensions.cs — finance service registration.
- JobPortal.Application/Features/CareerGuidance/CareerSchedulingService.cs — new-booking payment requirement/manual-confirmation guard.
- JobPortal.Application/Features/CareerGuidance/SchedulingContracts.cs — private RequiresPayment response flag.
- JobPortal.Domain/Entities/CareerGuidanceScheduling.cs — booking payment requirement flag.
- JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs — validated options and gateway HTTP registration.
- JobPortal.Persistence/Context/JobPortalDbContext.cs — financial history immutability checks.
- JobPortal.Persistence/DependencyInjection/ServiceCollectionExtensions.cs — finance repository registration.
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs — generated Phase 3 model.

`git diff --stat` (tracked files only): 10 files changed, 482 insertions, 29 deletions.
The 15 newly created files remain untracked and are excluded from that statistic.
The pre-existing JobSourceCategoryResponseTests.cs and idempotent aggregation SQL artifact
retain their original SHA-256 hashes. Existing stash, nested duplicate, appsettings and
.gitignore remain untouched. No historical migration, membership service or existing
membership gateway is modified. Nothing staged/committed/pushed/merged/deployed.
