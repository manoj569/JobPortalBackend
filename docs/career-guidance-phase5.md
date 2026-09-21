# Career Guidance Phase 5: Reviews, Disputes & Trust

## Scope and product boundaries

Adds moderated verified-session reviews, derived public ratings, one complaint case per booking, text evidence, administrator decisions and dispute-aware earning holds. It extends the Phase 1 profile, Phase 2 booking, Phase 3 finance and Phase 4 session workflows; it does not replace them.

Career guidance is independent professional mentorship, not official employer representation, referral/job selling or a guarantee of an interview or placement. Consultants remain responsible for employer moonlighting/confidentiality obligations. A verified-session review does not guarantee the truth of its content.

No automatic payout, automatic complaint-triggered refund, custom video, frontend or attachment upload is implemented. No external evidence URLs are fetched. Existing resume/profile uploads are not repurposed as dispute attachments.

## Reviews and ratings

- Only the active authenticated Candidate owning both booking and payment may submit. The consultant cannot review themselves.
- Booking and session must both be Completed, payment must be Captured with PaidAtUtc, and no refund record may exist. Deleted records, unpaid/scheduled/in-progress sessions and both no-show outcomes are ineligible.
- One lifetime review per booking, enforced by an unfiltered unique index, including after withdrawal.
- Rating is an integer 1–5. Title is optional, at most 120 characters; comment optional, at most 2,000. HTML delimiters and non-whitespace control characters are rejected. Consumers must render all text as text, never HTML.
- Submission starts Pending/unpublished. An independent administrator may approve/publish, reject or hide with a bounded private moderation reason. Hidden/rejected reviews may be restored by administrator approval only after eligibility is rechecked.
- Candidate edits require the current revision and are permitted through CreatedAtUtc + ReviewEditWindowDays (inclusive; default 7). Hidden/rejected/withdrawn reviews are edit-locked. Changed content or rating resets Approved to Pending and unpublishes immediately.
- Candidate withdrawal is a soft delete and unpublishes. It does not permit another submission. Withdrawn records cannot be restored through moderation.
- Public list and public consultant search/detail use the same eligibility predicate. Only approved, published, nondeleted reviews from completed, captured, non-refunded bookings and active candidates count. Refund-pending cases are conservatively excluded too.
- Ratings are derived, not denormalized. A page-wide grouped query computes decimal sum/count, rounded to two places away from zero. No reviews means null average and count zero. This avoids aggregate update races and per-consultant N+1 queries.
- Public reviews contain only review ID, rating, title, comment, created time and the fixed display name `Verified candidate`. They do not expose booking/session/payment IDs, user identity/contact information, questionnaire, meeting links or moderation reasons. Lists are newest-first with ID tie-breaker.

Review transitions: Pending → Approved/Rejected/Hidden; Approved → Pending on edit; administrator transitions between moderation outcomes require revision/eligibility; withdrawal is irreversible in this API. Processed full refund → Hidden/unpublished, retaining the record.

## Disputes, evidence and moderation

- Only the active owning candidate can open a case for a payment with a captured-payment timestamp and earning ledger. A paid but never-provisioned booking may have null SessionId. A subsequent refund does not erase complaint history or block an otherwise timely complaint.
- Default deadline is 72 hours after the later of scheduled end or actual completion, inclusive. BillingIssue may be raised before the session. Other categories require scheduled start; ConsultantNoShow also requires the Phase 4 no-show grace period.
- Categories: ConsultantNoShow, SessionQuality, Misrepresentation, InappropriateConduct, ConfidentialInformationRequest, TechnicalFailure, BillingIssue, Other.
- **One lifetime case per booking**, stronger than one active case. This prevents repeated claims from continuously restarting holds. Further material belongs in the open case. Terminal cases are not reopened in this MVP; a future appeal policy needs an explicit design.
- Description is required plain text, at most 4,000 characters; RequestedRefund is a claim, not financial authorization.
- New case is Open. Administrator can move among UnderReview, AwaitingCandidate and AwaitingConsultant, then resolve. Terminal resolutions cannot be changed. Rejected/Cancelled enum values are reserved for future explicit workflows; current endpoints terminate via Resolved and a non-None resolution.
- Resolutions: NoAction, WarningIssued, RefundApproved, RefundDenied, ConsultantRestricted, ReviewHidden, Other. WarningIssued records a decision, not an automatic notification or penalty.
- ConsultantRestricted explicitly suspends a Verified/Suspended profile through its existing verification state and revision; it never happens automatically because of a complaint. Other profile states use the existing verification workflow. Self-moderation by the candidate/consultant is forbidden.
- Evidence is append-only bounded text (Text, Response, AdminNote), max 50 submissions per case. Use EvidenceType.Response on the same evidence endpoint for candidate/consultant responses; no duplicate response route is needed.
- Evidence requires a nonempty caller-generated RequestId and current dispute revision. Unique (DisputeId, RequestId) makes exact retries safe; changed payloads with the same identity conflict. New evidence is accepted only while active.
- Candidate/consultant evidence is participant-visible. Only administrators can add admin-private evidence/notes; AdminNote is always private even if the request flag is false. Consultant/candidate DTOs omit private evidence and AdminNotes. No uploads, links-as-attachments or external URL verification are supported.
- Administrator decision notes are required and bounded at 2,000 characters. Current decision notes and immutable evidence are retained; audit events record actor/context and fixed action codes, not confidential evidence text. This MVP does not retain every version of edited review text or replaced decision notes.

## Finance and no-show integration

An active dispute clears AvailableAtUtc on a Pending earning, even when the old date has passed. It does not mark the earning Payable/Settled, change payment status, call Razorpay, fabricate a refund or move money.

For a no-refund resolution, a Pending earning may regain eligibility only if payment is still Captured, there is no refund or refund-review flag, booking/session are nondeleted Completed, consultant is active/Verified, and candidate remains active. The release clock restarts at resolution time plus the session's snapshotted earning delay (normally 48 hours); it never backdates. Otherwise availability stays null.

RefundApproved records an administrator recommendation and refund-review flag and retains the hold. **An administrator must separately execute the existing Phase 3 refund endpoint**; that workflow is authoritative for provider calls, reconciliation and ledger reversal. Candidate complaints never trigger refunds by themselves. An existing Payable/Settled earning requires settlement reconciliation before a refund recommendation.

Processed full refunds hide/unpublish reviews and keep earnings reversed. Even before the hiding transaction, the public query excludes payments/refunds that invalidate eligibility.

Phase 4 no-show reporting/confirmation remains authoritative for session outcome. A candidate may also open a ConsultantNoShow dispute for evidence/review; no automatic second case is generated. Resolving a complaint with NoAction/RefundDenied does not erase a confirmed no-show or its RequiresRefundReview flag. No-show outcomes do not receive standard star ratings.

**Future payout processors must atomically recheck active disputes (including anomalously soft-deleted cases), RefundApproved resolutions, refund/refund-review state, completed booking/session, consultant/account eligibility and all relevant revisions before settlement. AvailableAtUtc alone is not permission to pay.** No payout processor exists here.

## Concurrency, integrity and authorization

- Trust writes share the tracked payment/booking revision with completion, no-show and refund changes. Relevant earning/dispute/review/profile revisions are updated in the same SaveChanges transaction. Conflicting revisions produce 409 and clear tracking; clients must reload.
- Finance and session repositories run trust consistency before their single save: an active case/refund recommendation cannot be overwritten by completion; processed refunds hide reviews. Tracked case state takes precedence over old persisted state during resolution, including newly added cases.
- Review withdrawal/edit/publication conflicts are protected by the review revision. Review approval also touches payment/booking, so it cannot successfully publish using stale financial eligibility.
- Exact safe retries of submissions, evidence and administrator terminal actions return the existing result. A simultaneous duplicate create may return 409; retrying/reloading yields the existing result. Incompatible requests conflict.
- Review/dispute identity and original dispute submission cannot be changed by EF saves. Evidence and audit records are append-only; trust hard deletes are prohibited. Explicit IgnoreQueryFilters with eligibility/ownership predicates preserve financial/trust history without accidentally exposing soft-deleted records.
- Candidate endpoints require Candidate role and active account. Consultant endpoints require an active authenticated account and payment-linked profile ownership. Administrator endpoints require Administrator role and active account. Unauthorized record ownership is 404.
- Private responses are no-store. Every trust write has the existing per-user rate-limiter pattern (10/minute, no queue). This limiter is process-local, not a distributed abuse-control guarantee.
- Page sizes 1–100 and page numbers 1–1,000,000 prevent skip arithmetic overflow and unbounded responses. Stable ordering supports page traversal (not a cross-request snapshot).

## API routes

All paths below begin `/api` and return existing ApiResponse/PagedResponse envelopes.

| Audience | Method/path |
| --- | --- |
| Candidate | GET/POST/PUT `/career-guidance/bookings/{bookingId}/review` |
| Candidate | DELETE `/career-guidance/bookings/{bookingId}/review?revision={revision}` |
| Candidate | GET/POST `/career-guidance/bookings/{bookingId}/dispute` |
| Candidate | POST `/career-guidance/disputes/{id}/evidence` |
| Consultant | GET `/career-guidance/me/disputes` and `/{id}` |
| Consultant | POST `/career-guidance/me/disputes/{id}/evidence` |
| Public | GET `/career-guidance/consultants/{consultantId}/reviews` |
| Administrator | GET `/admin/career-guidance/reviews` and `/reviews/{id}` |
| Administrator | POST `/admin/career-guidance/reviews/{id}/moderation` |
| Administrator | GET `/admin/career-guidance/disputes` and `/disputes/{id}` |
| Administrator | POST `/admin/career-guidance/disputes/{id}/evidence`, `/status`, `/resolve` |

Review edit, moderation, evidence, status and resolve bodies require Revision; evidence also requires RequestId. List query supports PageNumber/PageSize and applicable Status or ModerationStatus. Enums use the existing numeric JSON convention.

## Configuration and schema

CareerTrustOptions binds the existing `CareerGuidance` section with startup validation. ReviewEditWindowDays defaults 7 (allowed 1–30); DisputeOpenWindowHours defaults 72 (allowed 24–720). No appsettings change is required.

PostgreSQL migration: `20260921074724_AddCareerGuidanceReviewsDisputesAndTrust`.

- CareerGuidanceReviews: booking/session/payment/consultant/candidate references, rating/content/publication/moderation fields, revision and normal audit/soft-delete fields. Unique BookingId; consultant/moderation/created index; FK lookup indexes. Rating and publication/moderation check constraints.
- CareerGuidanceDisputes: booking/optional session/payment/candidate/consultant references; category/description/requested-refund, status/resolution/private notes, submitted/resolved/actor, revision and audit fields. Unique BookingId; payment/status, status/created, candidate/status/created, consultant/status/created and FK lookup indexes. Enum, nonblank description and terminal-resolution consistency checks.
- CareerGuidanceDisputeEvidence: dispute/author/request identity, type/description/privacy and audit fields. Unique DisputeId/RequestId, author lookup index; nonblank description and type/private-note constraints.
- All new FKs are restrictive. Up contains only three CreateTable operations and their CreateIndex operations, with no existing-table alterations. No JobId1/xmin or salary changes. Down would drop these new tables and destroy trust history; do not use it on live data without an approved recovery plan.

## Validation and deployment gates

Validated on 2026-09-21:

- Clean API build: 0 warnings, 0 errors.
- Focused Career Guidance Phase 1–5 tests: 172 passed, 0 failed, 4 explicitly skipped, 176 total.
- Full non-database regression suite: 1,090 passed, 0 failed, 0 skipped, 1,090 total (database-only classes excluded).
- EF has-pending-model-changes: no pending changes. Migration scope/model/snapshot tests passed.
- Scoped idempotent SQL generated offline from AddCareerGuidanceSessionsAndReminders to AddCareerGuidanceReviewsDisputesAndTrust: only the three new tables, 17 indexes, constraints and normal migration-history insert. No DROP, unrelated ALTER, JobId1, xmin or salary precision change. SQL was inspected, never executed.
- Test builds use the repository's existing test-only analyzer workaround `-p:NoWarn=CA1707%3BCA1859%3BCA1861`; the API build uses no warning suppression.

Tests use in-memory EF graphs and fake payment/meeting providers; they do not call live providers. Migration tests generate SQL and compare the runtime/design/snapshot models without connecting. The second review added inactive-candidate release checks and consultant revision protection and exercised stale refund/no-show/completion/moderation races, privacy, decimal ratings, evidence ownership and pagination bounds.

`CareerTrustPostgresTests` is opt-in through CAREER_GUIDANCE_TRUST_TEST_POSTGRES, restricted to localhost/127.0.0.1/::1 and database career_guidance_test. It creates/drops only a randomized test schema and checks actual generated migration constraints, concurrent review/dispute uniqueness and resolution compare-and-swap. It never reads DefaultConnection. No disposable local PostgreSQL was configured during implementation, so real PostgreSQL integration remains an explicit deployment gate.

Before deployment:

1. Review the scoped migration SQL and ensure Phase 1–4 schema history is present. Apply the new PostgreSQL migration through the approved deployment process before running this code (public discovery now reads the review table).
2. Run opt-in tests against a disposable local PostgreSQL database, then staging API flows and transaction-race tests; in-memory tests cannot prove relational atomic rollback or actual PostgreSQL locking behavior.
3. Confirm moderation/refund operator procedures, independent administrator availability, edit/dispute windows, the one-lifetime-case/no-reopen policy, and restart-of-delay policy with product/operations.
4. Validate existing Phase 3 payment reconciliation and Phase 4 Data Protection/key persistence/reminder settings in staging. Phase 5 does not alter these settings.
5. Maintain backups and audit retention. Treat evidence as private user content. Add a separately designed, authenticated attachment subsystem and appeals/history retention if required later.
6. Do not enable automatic payouts; any future processor must enforce the full hold predicate atomically. Recheck deployment account authorization and multi-instance rate-limit needs.

No migration was applied, no production/shared database was accessed, and no commit/push/PR/deployment was performed during implementation.
