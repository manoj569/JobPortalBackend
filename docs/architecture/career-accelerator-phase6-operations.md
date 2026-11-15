# Career Accelerator Phase 6 operations

## Purchase and entitlement

`CAREER_ACCELERATOR` uses the existing membership plan catalogue and PhonePe checkout. The server snapshots the base price, GST, total, term and Accelerator plan version on the payment. The candidate request supplies only the plan code. The existing PhonePe status API and authenticated webhook are the only paths that can mark the payment paid. After a paid payment is persisted, `AcceleratorService.ActivateVerifiedPaymentAsync` creates one billing cycle per payment under the Accelerator write lock. A retry returns the existing cycle. A renewal starts at the later of the paid time and the previous cycle end, so it does not discard remaining access.

Accelerator checkout accepts a candidate request key, persists it with a unique user-key constraint, and stores the original provider checkout URL and expiry. Repeating the same key and checkout details returns that saved response without a second PhonePe initiation. Reusing the key for different details conflicts. If an interrupted first attempt left no saved URL, replay returns a pending-reconciliation conflict and does not blindly create a second provider order.

Payment completion and cycle activation use separate durable commits. Repeated callbacks, candidate status checks, admin reconciliation and the bounded payment worker recover a paid order whose cycle was not created before a process interruption. The worker checks at most 25 orders every two minutes, verifies unresolved orders with PhonePe, and isolates failures per order. There is no client-supplied activation endpoint.

The configuration under `Membership:Plans:CAREER_ACCELERATOR` is the authoritative checkout price. `CareerAccelerator:BaseAmountInr` must match it at startup. `CareerAccelerator:AIEnabled` defaults to false in the checked-in configuration. Provider credentials remain in the existing PhonePe configuration and must be supplied through the established secret mechanism.

## Operational controls

The existing Phase 1 immutable usage ledger remains authoritative. Global AI enablement, feature policy enablement and named operation switches gate new calls. Tutor, project and interview services select providers and limits from server configuration. Completed historical data remains readable when new AI calls are disabled. Admin cost aggregation separates confirmed cost today, current reserved exposure and current uncertain exposure, including uncertain dispatches whose reservation is still `Dispatched`.

The admin operations API exposes bounded subscriptions, payments, dispatches, usage costs and a read-only configuration summary. Its payment reconciliation action requires an administrator and a reason, then verifies PhonePe status server-side. Refund review is an internal audited flag; it does not call a refund API, mark a payment refunded or erase evidence. The existing PhonePe gateway has no refund operation.

Existing database readiness remains in place. Accelerator configuration readiness treats disabled AI as a feature state. Accelerator schema readiness checks Phase 3–6 tables and columns, so missing migrations cannot appear ready. Payment and AI worker exceptions are logged without request bodies, credentials or candidate responses.

## Retention and access boundaries

Payment and audit history remain financial records. Assessment, project and interview evidence remains historical candidate data after subscription expiry. AI dispatch metadata is operational data; candidate content follows the existing candidate erasure paths. Public Skill Passport access remains private by default and uses revocable opaque tokens. Payment or refund review does not delete historical evidence.

## Verification boundary

This source change does not generate or apply migrations. The ordered Phase 3, Phase 4, Phase 5 and Phase 6 migration chain is still required. PostgreSQL tests must verify payment activation races, unique payment and cycle constraints, renewal overlap, cost aggregation translation and reconciliation claims on a disposable database. No live PhonePe or AI provider calls are part of the focused tests.
