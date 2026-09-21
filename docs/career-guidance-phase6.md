# Career Guidance Phase 6

Phase 6 adds operational analytics and launch-readiness hardening while preserving Phases 1–5 financial, session, trust and authorization behavior.

## APIs and metric definitions

- `GET /api/admin/career-guidance/analytics/overview?fromUtc=&toUtc=` is Administrator-only. It defaults to the previous 30 UTC days and rejects non-UTC, empty/reversed, or over-366-day ranges.
- `GET /api/career-guidance/me/analytics` is authenticated and resolves the consultant through the active authenticated user’s profile. It never accepts a consultant ID, preventing IDOR. It reports bookings, completed sessions, cancellations/no-shows, published ratings, ledger earnings, pending/held ledger values, processed refunds, open disputes and the next upcoming session.
- `GET /api/career-guidance/me/summary` requires the current Candidate role and an active account. It reports upcoming/complete/cancelled bookings, reviews awaiting moderation, active disputes and the next provisioned session. It is an all-time account summary, not a date-filtered list.

The overview and consultant reports use creation cohorts: select entities by `CreatedAtUtc` in the half-open `[fromUtc,toUtc)` interval, then report their current committed state. These are not historical snapshots or cash-flow-by-event-date reports. Refund and earning metrics belong to payments created in that interval, even when capture/refund occurs later. Exactly 366 days is accepted; one additional tick is rejected. Missing bounds default to the preceding 30 days; underflow and invalid UTC kinds produce safe 400 errors.

- Applications are consultant records created in the period, not a separate event count. Verification counts are current states of that cohort. Active verified consultants must also accept bookings and have an active, nondeleted user.
- Booking/session/no-show counts are current final/status counts of the respective creation cohorts. Gross booked value includes all nondeleted bookings in the cohort, including cancellations, and is not realized revenue.
- Captured payment count and GMV use the durable `PaidAtUtc` capture fact once per payment. Orders and payment events do not contribute. Subsequent refunds do not erase gross volume or turn captures into failed payments. FailedPaymentCount means currently uncaptured payments with a recorded failure code; it is not a count of all historical failed attempts.
- Refund count/amount include only nondeleted Processed refund records with ProcessedAtUtc, linked to cohort payments. Requested, pending and failed refunds are excluded.
- Commission and consultant earnings use nondeleted, non-Reversed ledger rows linked to captured cohort payments. These include pending/held amounts and are not payout totals. Reversed ledger amounts contribute zero.
- PendingEarnings and HeldEarnings partition Pending ledger rows without overlap. Held means refund-review flag, any retained refund record, an active dispute or RefundApproved resolution. A null AvailableAtUtc before session completion alone is not a dispute hold. Future payout eligibility still requires every Phase 5 safety check; analytics does not authorize payout.
- Review totals exclude withdrawn reviews. Published count/rating use Phase 5's exact approved/published/eligible predicate; averages are rounded to two decimal places away from zero. Open disputes use Open through AwaitingConsultant; resolved means Resolved.
- NextUpcomingSession is the next nondeleted Scheduled/Ready/InProgress provisioned session on a Confirmed booking, at or after the injected UTC clock. It is independent of the reporting cohort. An unprovisioned booking is not reported as a session. Candidate PendingReviewCount means submitted Pending moderation reviews, not unsubmitted review opportunities.

Money is decimal INR. These scalar-money endpoints explicitly reject a period containing any non-INR booking/payment/linked earning/refund with safe 409 `analytics_currency`; no currencies are summed or converted silently. Multi-currency reporting would require a separately reviewed grouped response contract. The currency check and totals share one database snapshot.

## Query and security review

Analytics uses server-side Count/Sum/Average and grouped status counts. Only aggregate results (bounded enum groups) and scalar projections reach memory; no entity graphs or per-row queries are loaded. Consultant identity lookup is a scalar projection. DTOs contain no passwords, tokens, payment provider secrets, meeting URLs, questionnaire text, evidence, admin notes or private verification reasons. All analytics endpoints use no-store responses and recheck active account/current role in addition to framework authorization. Consultant ownership is tied to the authenticated profile user; Candidate/Admin role checks remain distinct. Only range-normalization exceptions are mapped to a fixed 400 message; repository/provider exception details are never returned by the controllers.

Existing Phase 5 write limiter remains user-partitioned (10/minute, no queue). Existing Phase 1–4 write policies remain unchanged; reads are not aggressively limited. No distributed limiter or new vendor was introduced. Existing audit writers record safe result codes/IDs; Phase 6 analytics is read-only and emits no sensitive logs. Data Protection keys, meeting links and provider secrets are not logged by these APIs.

No model/index migration was added. Existing identity, status and trust indexes are reused; large-data analytics execution plans still need staging measurement before adding speculative date-only indexes. The API does not call external providers. Global soft-delete filters are respected. Hold checks intentionally include retained refund/dispute history, with explicit payment/ledger soft-delete predicates so IgnoreQueryFilters cannot accidentally resurrect deleted financial rows.

## Configuration, errors and operations

Analytics is configuration-free and fail-closed on invalid date ranges (400). Framework authentication/role handling supplies 401/403; consultant profile absence is 404. Existing exception middleware sanitizes unexpected provider/SQL errors. No secret files or appsettings were changed.

The existing global database health endpoint remains authoritative. No duplicate database probe, Razorpay call, meeting-provider call, or guessed Data Protection persistence check was added. Persistent/shared Data Protection keys and manual/provider meeting configuration remain deployment requirements from Phase 4. Reminder worker settings remain unchanged and should stay disabled until staging validation.

## Race and performance review

Analytics executes SELECT queries in a PostgreSQL RepeatableRead transaction through the configured retry execution strategy. All counters, currency checks and financial totals in one response see the same committed database snapshot, avoiding partial refund/dispute transitions. There are no business writes. Existing Phase 1–5 mutation paths are unchanged. Their list queries bound pages to 1,000,000 and sizes to 100, with ID tie-breakers; skip arithmetic stays below Int32 overflow. Analytics has no pagination or per-row N+1 calls; bounded date cohorts and server-side aggregates return fixed-size responses. Lifetime candidate counts can still scan account history and require staging performance checks at scale.

## Pre-deployment checklist

1. Run all opt-in disposable PostgreSQL suites; they must reject non-local hosts and unexpected database names.
2. Review migrations in order (Phase 1 through Phase 5); Phase 6 has no migration.
3. Confirm `btree_gist`, payment settings, Razorpay test-mode E2E, persistent Data Protection keys and meeting-provider/manual flow in staging.
4. Keep reminders disabled until schema/configuration and staging worker behavior are validated.
5. Validate reviews/disputes/refunds, earning holds, public privacy, rate limits and analytics totals against seeded known data.
6. Smoke-test global health/readiness and verify logs contain no credentials, meeting links, questionnaire content or private evidence.
7. Run candidate, consultant and administrator end-to-end workflows before launch.

## Validation

Final commit-readiness validation:

- Clean API build (`--no-incremental --no-restore`): 0 warnings, 0 errors.
- Focused Career Guidance Phases 1–6: 193 passed, 0 failed, 4 skipped, 197 total.
- Full non-database regression: 1,111 passed, 0 failed, 0 skipped, 1,111 total, including Playwright fixtures. Seven database-only tests were excluded: four Career Guidance tests and three AIApplyDistributedPostgresTests. No browser-dependent tests were excluded.
- The earlier Windows `spawn EPERM` was a sandbox process-launch restriction. The unchanged failing fixture passed outside that restriction, followed by the entire non-database suite. No Playwright code, browser flags or application configuration was changed to bypass it.
- EF has-pending-model-changes: no changes since the latest migration. Existing model/snapshot/migration tests also passed in the focused suite. No migration was created or applied.
- Tracked and new-file whitespace checks passed. No staged changes. Both protected unrelated files retain their original SHA-256 hashes, and the stash remains unchanged.
- Test compilation uses the existing documented analyzer workaround `-p:NoWarn=CA1707%3BCA1859%3BCA1861`; the API build has no warning suppression.

New regression coverage checks capture retries/refunds, ledger reversal, unsupported booking/payment/earning/refund currencies, disjoint pending/held amounts, soft-deleted ledger exclusion, moderation eligibility, UTC/366-day and cohort boundaries, no-shows, ownership, account/role revalidation, no-store metadata, upcoming provisioned sessions and offline PostgreSQL aggregate translation.

The four skipped Career Guidance tests require an explicitly configured disposable database named `career_guidance_test` on localhost, 127.0.0.1 or ::1. Existing opt-in variables are CAREER_GUIDANCE_TEST_POSTGRES, CAREER_GUIDANCE_FINANCE_TEST_POSTGRES, CAREER_GUIDANCE_SESSION_TEST_POSTGRES and CAREER_GUIDANCE_TRUST_TEST_POSTGRES. They were unset for this validation, along with AIAPPLY_STEP5_POSTGRES and DefaultConnection for test processes. No production/shared database was accessed. Actual PostgreSQL transaction/constraint execution, query plans at scale and staging E2E remain pre-deployment gates; offline SQL translation and in-memory tests do not replace them.
