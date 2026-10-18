# Original regression baseline comparison

The isolated source snapshot is an archive of commit `31419dd7570bf236da57da1b03669941ffb8de6d`, outside the active checkout. It has no Talentd implementation. The two pre-existing uncommitted website adjustments were not included in this committed baseline; the 84 failing modules are unchanged by those adjustments or Talentd. No reset, stash, checkout, staging or commit was used.

All ten original failing classes were built and run against that snapshot. Result: **178 passed, 84 failed, 0 skipped, 262 total; 22 seconds**. Every one of the original 84 named failures has the same error message in the baseline TRX after normalizing line endings. `FAILURE_MATRIX.csv` contains every name/error/root cause/attribution/action, and `BASELINE_COMPARISON.json` contains the exact comparison totals.

| Cluster | Failures | Evidence/root cause |
|---|---:|---|
| Shared career lifecycle fixture/preconditions | 81 | 72 eligibility ConflictExceptions; six Confirmed vs AwaitingConsultant assertions; two missing expected session/provider rows; one provider-entry timeout. Existing lifecycle-v2 capture sets AwaitingConsultant, while old finance/session fixtures assume immediate Confirmed. All reproduced before Talentd. |
| Notification snapshot assertion | 1 | Test expects a pending notification schema delta; committed snapshot already has it. Same empty-delta failure on baseline. |
| Booking relationship assertion | 1 | Test expects four foreign keys; committed model has five. Same baseline result. |
| Civil Engineer category assertion | 1 | Existing explicit discipline classifier returns general, while test expects null. Same baseline result. |

Original-failure attribution: Talentd-caused **0**, environmental **0**, proven pre-existing **84**, unknown **0**. These are not 84 independent environmental failures. No unrelated tests, assertions or business behavior were changed to make them pass. This evidence does not turn the broad suite green or claim unconditional repository regression safety.
