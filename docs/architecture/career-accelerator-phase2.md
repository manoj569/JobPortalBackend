# Career Accelerator Phase 2 architecture

## Scope and phase boundary

Phase 2 adds deterministic diagnostic assessments, evidence-based proficiency and gap analysis, prerequisite-aware roadmap versions, learning schedules, progress events, and explainable recommendations. It does not enable live AI calls, provider billing, tutor chat, simulations, or interview products.

Phase 1 remains the source of truth for candidate identity, canonical skill IDs, immutable taxonomy snapshots, goals, payment-backed Accelerator entitlement, quota reservations, usage events, audit records, and PostgreSQL advisory-lock write serialization. Phase 2 must not duplicate any of them.

## Historical and ownership model

An assessment attempt pins its career goal, taxonomy version, assessment-template version, selected question versions, and selection strategy/version. Candidate answers remain private; answer keys and grading rubrics are never returned before final submission. Attempts are candidate-owned and transition through Draft, Ready, InProgress, Submitted, Evaluating, Completed, Failed, or Retryable.

Objective questions are deterministically evaluated from versioned answer data. Written/scenario questions retain a rubric and remain explicitly pending/unsupported until a future configured evaluator returns structured evidence. No candidate code is executed.

Skill evidence is append-only and records source, score, confidence, scoring version, and demonstrated proficiency. Gap analyses pin the goal/taxonomy/evidence set and calculate `max(0, required-current)`, priority, prerequisites and explainable evidence references.

Roadmaps are immutable versions belonging to one candidate goal. A version contains deterministic nodes and edges keyed by stable source competency IDs, learning activities, schedules and append-only progress events. Recalculation creates a new version; it preserves completed evidence and never rewrites history. Graph validation rejects cycles and missing prerequisites before persistence.

## Deterministic baseline algorithms

- Question selection: stable sort by target competency, then difficulty progression and question ID; exclude prior selected question versions for retake variation where alternatives exist.
- Proficiency: evidence weights are configuration-controlled; self-report/resume evidence cannot exceed Developing without demonstrated assessment/assignment evidence.
- Gap priority: descending required-minus-current, then prerequisite depth, then canonical skill ID.
- Roadmap: topological order of required competencies; prerequisite nodes precede dependent nodes. Hours are proportional to gap and difficulty. The generator emits an explicit feasibility warning when required hours exceed available weekly minutes before the target date.
- Scheduling: persist UTC task slots derived from the candidate's declared timezone; completed activity events are never removed during rescheduling.
- Recommendations: deterministic rules over available nodes, weak evidence and overdue activities; each stores its reason and evidence IDs.

## Security and quota rules

Candidate APIs scope every read/write by authenticated user ID and goal/roadmap ownership. Admin APIs require the existing Administrator role and produce audit records. Submission and progress commands use user-scoped idempotency keys plus revision/concurrency tokens. Phase 2 does not create a second quota system: any future chargeable recalculation must reserve the existing `AcceleratorFeature.Roadmap` allowance before dispatch, and must reconcile through the Phase 1 immutable usage ledger.

## PostgreSQL verification

The Phase 2 migration must be additive, reviewed before application, and run only against an explicitly confirmed local database ending in `_test`. Required integration checks cover advisory-lock serialization, unique idempotency, candidate isolation, append-only events, historical roadmap versions and migration round-trip. Existing Phase 1 test-host and disposable-PostgreSQL blockers remain visible; Phase 2 development does not certify Phase 1.
