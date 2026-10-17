# CareerHarbor Points and mentor eligibility

`UserPointTransactions` is the source of truth for CareerHarbor Points. The authenticated candidate response on `GET /api/candidate/interview-insights/my-contributions` retains its `careerHarborPoints` total and adds `pointsBalance` with the total, 3,000 point threshold, remaining points, points-based mentor eligibility, and totals by reward reason. Its paid-eligibility fields are explicitly unavailable/false until a dedicated verified eligibility payment flow exists. No balance field is stored on `User`.

Rewards are derived from server-side domain transitions and written with the transition's existing database save or transaction:

| Event | Award | Trigger |
| --- | ---: | --- |
| Helpful interview insight | +10 | Existing unique helpful-feedback insertion |
| Interview insight published | +5 | First admin moderation transition to Published |
| Referral opportunity shared | +5 | Admin approval that publishes the opportunity's job |
| Referral candidate confirmed | +300 | Candidate confirms a referral already marked submitted |

The unique source indexes enforce one transaction per insight, opportunity, or referral request. The existing unique helpful-feedback index remains. The database check constraint binds each reason to its award amount and exactly one source. Rejected or pending insights and referrals earn no points. Re-publishing an insight, editing it, retrying a request, or repeating confirmation does not create a second award.

Helpful points keep their existing moderation behavior: deleted feedback, deleted insights, or insights that are not currently Published do not count toward the active balance; they count again if the existing moderation flow republishes the insight. The new publication, approved opportunity, and confirmed referral rewards are historical earned points. Later edits, closure, soft deletion, or moderation do not erase those awards. The referral model currently has no reversal state for a candidate confirmation; a future dispute reversal would need an explicit ledger reversal policy and event before changing this behavior. Soft-deleting a point transaction removes it from the active balance, while its source uniqueness still prevents re-awarding that event.

Mentor eligibility is `totalPoints >= 3000`. The Career Guidance draft can be started and edited below threshold; final onboarding submission and the legacy direct consultant application require points eligibility. Eligibility does not change the user's role, create a verified consultant, or skip existing identity, experience, expertise, moderation, and review steps.

The existing Career Guidance payment lifecycle is checkout for a consultant booking. It is not an eligibility or onboarding payment and is not reused for the proposed ₹3,000 path. Paid eligibility is therefore deferred. A future implementation needs its own verified payment purpose and durable payment-to-user eligibility record, with idempotent provider confirmation and a defined refund/reversal policy; the paid route must still enter the current Career Guidance verification and review lifecycle.

The new migration adds source references and constraints only. It does not backfill historical insights, referral opportunities, or confirmed referrals, and it does not alter existing Helpful point rows.
