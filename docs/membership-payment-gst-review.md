# Membership payment hardening — migration approval required

## Business policy and pricing

The approved policy is **tax-exclusive base prices plus configurable GST**. Before this change the backend charged the configured price directly and did not record a tax breakdown; that historical behavior did not establish a GST policy.

Configure `Membership__Tax__GstRate` (configuration key `Membership:Tax:GstRate`). The backend default is 18 percent. Values must be 0–100 with at most four decimal places. This common membership setting applies to the configured membership catalogue, including AI plans; it does not change Career Guidance payment pricing. Normal environment configuration changes require an application restart.

| Plan | Base | GST at 18% | Charged total | Duration |
| --- | ---: | ---: | ---: | ---: |
| CareerHarborMembership / Job Application Access | INR 99.00 | INR 17.82 | INR 116.82 | 30 days |
| ReferralContactAccess / Referral Contact Access | INR 299.00 | INR 53.82 | INR 352.82 | 30 days |

Money uses decimal; GST is rounded once to two decimal places, midpoint away from zero. Total is base plus rounded GST. Provider minor units come from that total. Checkout input still only accepts a plan code, not money or entitlement state.

## Audit findings and fixes

- Razorpay checkout used a global unresolved-payment check. PhonePe's pending-membership path also used that global query. The repository now filters by plan and includes durable Created intents even before a provider order ID exists. A unique filtered `(UserId, PlanCode)` index backs the application check.
- The generic reconciliation method unconditionally called Razorpay. PhonePe payments now use the PhonePe verifier.
- Activation previously compared stored payment amount against current plan configuration and used current duration/name. New payments snapshot plan name, duration, base, rate, and GST before provider creation; existing `Amount` remains the stored final total. Activation uses these snapshots.
- PhonePe checkout exceptions previously marked payment Failed even when provider creation or local persistence had an ambiguous outcome. The durable Created intent now remains reconcilable; no failure is invented from a timeout. PhonePe return verification also checks non-paid terminal local states for late provider success. Refunded payments are not reactivated.
- Repeated failure from an older attempt cannot cancel a newer pending membership intent.
- The old generic status response paired a default-plan membership with the latest payment from any plan. It now pairs the latest payment with its plan and additionally exposes the user's memberships. Membership DTOs include PlanCode.
- PhonePe duplicate completion does not append a second success transition or generate another purchase notification.

These are reproducible code-path defects, not a verified root cause of a particular production transaction. No production transaction, logs, credentials, or database were accessed.

## Flow, authorization, entitlements

Checkout saves Created + membership intent, calls the provider, then saves Pending. Only server-verified successful amount/order/transaction identity can move the payment to Paid and activate membership. Pending/Failed do not grant access. Provider-confirmed failures/cancellations retain existing enum semantics; no new PhonePe state or cancellation API was invented. The browser arriving at a return URL is not proof of payment.

PhonePe webhook authentication and authenticated provider status lookup remain intact. User-facing lookup/reconciliation requires ownership and Candidate authorization. No contact details are added to payment contracts. The existing Job Application Access and ReferralContactAccess entitlement checks remain plan-specific: the former does not unlock referral contacts. Existing same-plan PhonePe active-purchase rejection and Razorpay renewal behavior are retained.

No payment endpoint signs the user out, clears cookies, or revokes tokens. Return/status APIs require existing authentication. Frontend auth persistence, browser redirects, and deployment-specific webhook configuration still need separate incident evidence; backend-only inspection cannot prove the reported logout cause.

## Contracts and notifications

- Plan, order, checkout and payment DTOs expose a `pricing` object: baseAmount, taxRate, taxAmount, totalAmount. Legacy payments have null pricing rather than fabricated tax. Existing `price` is base and payment `amount` is total.
- Payment results include snapshotted planName/durationDays. PhonePe return results include payment and membership status/start/end.
- `GET /api/payments/pending-membership-checkout?planCode=...` can target a plan; omitting it retains latest-pending compatibility. Pending responses identify PlanCode.
- The existing status response additionally includes memberships. Clients must select entitlements by PlanCode, not assume any active membership grants all features.
- ReturnTo permits the existing interview route and `/dashboard/jobs/referral/{guid}/contact` only; no query strings, external origins or arbitrary relative paths. The validated destination is saved on the payment and returned after verified activation.
- First activation enlists deterministic `membership-purchase:{paymentId}:{userId}` InApp + Email delivery intents in the **same SaveChanges transaction** as Paid and membership activation. Existing dispatcher, registered user email and Brevo implementation are reused. No external email occurs inside payment processing.
- Confirmation text contains saved plan/pricing and UTC activation/expiry. Eligibility checks require the matching paid payment and recipient. One logical outbox intent per channel is enforced; **exactly-once external email is not promised** (existing post-send/pre-ack crash window remains).

## Historical compatibility

No existing payment is repriced or backfilled. Nullable snapshots distinguish old rows. Known legacy INR amounts (99/299/999/1499) use the frozen pre-change 30-day catalogue, which the old configuration provider explicitly enforced. Unknown or mismatched legacy amounts/codes fail closed for review instead of consulting mutable current configuration. Original historical GST cannot be reconstructed, so it is not invented.

## Migration review

Generated normally through EF Core: `20260928163829_AddMembershipPurchaseSnapshots`.

Up affects **Payments only**:

- nullable BaseAmount numeric(18,2), TaxAmount numeric(18,2), TaxRate numeric(7,4);
- nullable PlanName varchar(100), DurationDays integer, ReturnTo varchar(256);
- unique unresolved user/plan index, for non-deleted membership payments with a plan code and status Pending/Authorized/Created;
- check constraint allowing wholly legacy-null snapshots or complete, mathematically consistent positive-price snapshots.

Existing Amount stores TotalAmount; no duplicate total column is introduced. No data updates, unrelated alterations, new xmin mapping, or destructive Up operations. Designer and model snapshot are generated by EF, not hand-edited. Down removes the index/constraint and six columns, losing new snapshot data if rolled back. **Not applied.**

Before approval/deployment, an authorized operator must inspect duplicate unresolved rows (read-only):

```sql
SELECT "UserId", "PlanCode", COUNT(*)
FROM "Payments"
WHERE "IsDeleted" = FALSE AND "MembershipId" IS NOT NULL
  AND "PlanCode" IS NOT NULL AND "Status" IN (1, 2, 7)
GROUP BY "UserId", "PlanCode"
HAVING COUNT(*) > 1;
```

Duplicates make migration fail safely; reconcile them with provider evidence, do not delete or cancel automatically. Existing legacy null-plan rows are not covered by the new index but are checked through their membership in the application lookup. Apply only after explicit approval and dependency migration review; deploy application changes after schema availability. Do not run mixed old/new checkout writers during rollout.

## Concurrency and remaining limitations

Existing PostgreSQL xmin concurrency checks on Payment and Membership protect the activation SaveChanges; PostgreSQL unique event/outbox/membership indexes and the new pending index add database fences. Losing requests receive conflict responses and must reload/retry in a fresh scope; there is no static lock or claim of transparent retry. Live separate-connection PostgreSQL webhook/return race testing is still required; unit/fake replay tests and model metadata tests are not a substitute.

No new reconciliation scheduler was added/enabled. A provider-unavailable or provider-unknown Created intent remains pending investigation instead of being falsely declared cancelled/expired. Manual server verification can resolve known completed/failed orders. Definitively unknown/abandoned orders need a documented provider-specific recovery policy before automatic expiry/retry can be claimed.

The existing PhonePe gateway remains explicitly Sandbox-only; this change does not silently enable live credentials/endpoints. Frontend integration must display `pricing.totalAmount` and retain authentication across return navigation before production rollout.

## Validation

See the accompanying completion report for exact final build/test totals. A migration was generated but not applied. No database update, provider charge, outbound email, commit, push, or staging was performed during this implementation. Existing SQL artifacts and unrelated review files remain untouched.

Recorded validation:

- API Release build with analyzers: PASS, 0 warnings / 0 errors.
- Solution Release build: blocked by test-project analyzer errors; test build with `RunAnalyzers=false` passes.
- Focused Payment/Membership/PhonePe/Referral/Notification: 180 passed / 1 failed / 1 skipped / 182 total.
- Full backend suite: 1,257 passed / 2 failed / 6 skipped / 1,265 total.
- The focused failure is `NotificationOutboxTests.PendingSchemaDeltaIsLimitedToTheNotificationFoundation`, which still expects an ungenerated notification delta despite that migration already existing in HEAD.
- The additional full-suite failure is `CareerTrustMigrationTests.OfflineSqlModelAndSnapshotAreConsistentWithoutUnrelatedChanges`, comparing an older Career Guidance migration model against the latest expanded snapshot. These existing test files were not changed.
- Six opt-in PostgreSQL tests skip without their dedicated test database settings; no production database was substituted.
- EF `has-pending-model-changes`: PASS, no pending model changes.
- `git diff --check`: PASS (line-ending notices only).

## Files changed

- JobPortal.API/Controllers/PaymentsController.cs — optional plan filter for pending lookup.
- JobPortal.API/Middleware/GlobalExceptionMiddleware.cs — safe conflicts for payment-related PostgreSQL uniqueness races.
- JobPortal.Application/Abstractions/Payments/PaymentContracts.cs — tax policy and plan-filtered lookup contracts.
- JobPortal.Application/Abstractions/Persistence/MembershipPaymentRepositoryContracts.cs — plan-scoped unresolved query.
- JobPortal.Application/Features/Memberships/MembershipDtos.cs — PlanCode.
- JobPortal.Application/Features/Payments/PaymentDtos.cs — pricing, membership and plan result fields.
- JobPortal.Application/Features/Payments/PaymentReturnPath.cs — allowlisted referral return context.
- JobPortal.Application/Features/Payments/PaymentService.cs — snapshots, plan isolation, verification, activation and outbox.
- JobPortal.Application/Features/Payments/MembershipPricing.cs — decimal calculation and validation.
- JobPortal.Domain/Entities/Payment.cs — nullable purchase snapshots and return context.
- JobPortal.Domain/Entities/NotificationDelivery.cs — membership purchase source discriminator.
- JobPortal.Infrastructure/Payments/RazorpayGateway.cs — existing configuration plan provider reads GST setting; Razorpay gateway protocol unchanged.
- JobPortal.Persistence/Configurations/EntityConfigurations.cs — snapshot mapping, check constraint and pending index.
- JobPortal.Persistence/Repositories/MembershipPaymentRepositories.cs — filtered query and snapshot/plan projections.
- JobPortal.Persistence/Repositories/NotificationDeliveryRepository.cs — purchase receipt eligibility.
- JobPortal.Persistence.Postgres/Migrations/20260928163829_AddMembershipPurchaseSnapshots.cs — generated migration.
- JobPortal.Persistence.Postgres/Migrations/20260928163829_AddMembershipPurchaseSnapshots.Designer.cs — generated target model.
- JobPortal.Persistence.Postgres/Migrations/JobPortalDbContextModelSnapshot.cs — generated snapshot.
- JobPortal.Application.Tests/PortalMembershipTests.cs — service regressions and multi-payment fake.
- JobPortal.Application.Tests/MembershipPricingTests.cs — configuration, rounding/security/model/eligibility coverage.
- docs/membership-payment-gst-review.md — audit and rollout notes.
