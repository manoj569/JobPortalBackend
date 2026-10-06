# AI Resume backend

**Generation architecture update (2026-10-06):** see
[master-document tailoring](ai-resume-master-tailoring.md) for the current patch-only Claude contract,
original DOCX preservation, review API, download capabilities, and session JSON envelopes. That document
supersedes full-resume generation/summary-fallback/download assumptions below; configuration and payment
behavior remain applicable.

The application depends on `IAIResumeProvider`; Infrastructure registers `ClaudeAIResumeProvider`
through `IHttpClientFactory`. Automated tests use a fake provider and do not make Anthropic requests.

## Configuration

`AIResume` defaults to Provider `Claude`, Model `claude-haiku-4-5`, 1,200 analysis tokens, 6,000
generation tokens and a 60-second timeout. Bounds are validated. An absent API key fails closed when
the provider is called, while unrelated application endpoints can still start.

Set the development key locally from the backend root (do not paste it into chat):

```powershell
dotnet user-secrets set "AIResume:ApiKey" "<your-Anthropic-key>" --project JobPortal.API
```

The existing local user-secrets also supply `AIResume:WorkspaceId`; the provider sends it as
`anthropic-workspace-id` when configured. Production injects `AIResume__ApiKey` and
`AIResume__WorkspaceId` as deployment secrets. Never put real secrets in source control, logs or URLs.

## Workflow and endpoints

Candidate-only endpoints live under `/api/ai-resume`:

- `GET packages`, `GET credits`
- `POST sessions`, `GET sessions/{id}`, `POST sessions/{id}/analyze`
- `POST checkout`, `GET purchases/{merchantOrderId}`
- `POST sessions/{id}/generate`, `POST resumes/{id}/regenerate`
- `GET resumes`, `GET resumes/{id}`, `PATCH resumes/{id}`
- `GET resumes/{id}/download?format=pdf|docx|txt`

Packages are server-owned consumable credit packages: AI_RESUME_1 ₹19, AI_RESUME_5 ₹79,
AI_RESUME_10 ₹129 (popular), AI_RESUME_25 ₹249. They are not memberships. Analysis is free.
Purchase and generation writes are serialized per user and protected by unique database idempotency
keys. Verified PhonePe order status/webhooks grant credits atomically once; pending, failed and
cancelled orders grant none. Generation reserves one available credit before calling Claude, consumes
only after validation and persistence, and releases the reservation on provider/validation failure.
Manual edits and document downloads cost no credits. Earlier generated versions remain in history.

PhonePe uses the existing shared gateway and authenticated purchase ownership. AI Resume order IDs use
the `air_` namespace; the shared webhook verifies its signature, then the service independently
reconciles with PhonePe's order-status API and validates the exact order and amount before granting.
The success return path is the fixed internal `/dashboard/resume-maker`; arbitrary return URLs and
tokens are not accepted.

## Resume source and factual grounding

The source parser reads the candidate-owned private upload with the existing `IResumeStorage` and
`IResumeTextExtractor`. The upload is authoritative: its supported literal layouts become typed
contact, summary, skills, experience, projects, education and certification facts. Profile tables
are not a substitute for uploaded facts. The typed result and deterministic evidence IDs are
snapshotted into the owned session. External job descriptions are stored on the session and
never create a CareerHarbor Job. CareerHarbor job sessions load a visible, currently published job
from the database and snapshot its authoritative description.

Both resume text and job descriptions are untrusted data. The Claude system prompt explicitly says
embedded instructions cannot override system instructions. Free analysis returns only match score,
skills/matches, requirements, strengths, improvement areas and short suggestions; it does not return
rewritten resume content. All eight analysis fields are required, arrays may be empty, and every
array is limited to 20 items of 240 characters. The structured schema enumerates valid source
matched skills and integer scores 0-100. Whitespace/capitalization and duplicate advisory items are
normalized; aliases and invented matches are rejected. Unsupported JSON Schema length/count limits
are expressed in descriptions/prompts and checked locally, rather than sent as unsupported schema
keywords. See [Anthropic's schema limitations](https://platform.claude.com/docs/en/build-with-claude/structured-outputs#json-schema-limitations).
Analysis results and token/model usage are persisted and cached.

Generation content carries evidence references on rewritten summaries and bullets. A deterministic
guard verifies evidence IDs and scope, unchanged contact/identity fields, source-backed skills and
technologies, exact education/certification facts, new numbers, known technology claims, high-risk
leadership/award/ranking claims, and negation preservation. Numbered claims must map to an individual
evidence item with matching claim context. This is a conservative deterministic safeguard, not a
complete semantic proof; the configured provider must not be treated as its own independent verifier.
Token usage, model and timestamps are stored. Monetary Anthropic cost is not calculated because a
stable pricing schedule is not captured by this feature.

The existing deterministic upload parser supports common literal section layouts; it does not infer
ambiguous employers/roles/dates from arbitrary page layouts. Unsupported layouts fail before a
provider request. Session snapshots stay immutable; a new upload affects new sessions only.
Generation uses the session's original contact, independently of the contact anonymization used for
analysis. Review generated content before downloading or using it.

## Diagnostics, retries and recovery

Development-only diagnostics: event 7404 `AIResumeGroundingRejected` contains sanitized paths,
categories, reasons, evidence IDs/existence and offsets; event 7405 `AIResumeSourceSnapshot` contains
only the permitted session/source IDs, counts and contact/summary presence; event 7406
`AIResumeAnalysisRejected` contains only Category, Reason, Path, Count, HasValue, Model, InputTokens
and OutputTokens. No raw responses, resume/JD text, credentials or candidate PII are included.

Deterministic validation failures and malformed successful responses are not retried automatically.
The provider retries only its existing transient HTTP statuses, at most three attempts within one
configured deadline. Analysis cleanup releases the owned lease on every exception; generation
cleanup restores an owned reserved credit once. Refresh recovers expired analysis/generation leases.
AI-only frontend mutations allow 180 seconds for the maximum 120-second provider deadline and
database cleanup; ordinary API calls retain their 30-second timeout. This does not change the
provider timeout. Aborted error serialization is handled as a client disconnect.

## Documents, data and migration

PDF, DOCX and TXT are rendered server-side from persisted structured content; re-download has no AI
call. The MVP has one `Professional` template. Source snapshots, analyses, edit revisions, purchases,
wallets, generations and an append-only credit ledger use the existing AI Resume PostgreSQL schema.
This stabilization pass introduces no schema change or migration and does not modify existing
credits. See [the stabilization report and clean manual procedure](ai-resume-stabilization.md).
