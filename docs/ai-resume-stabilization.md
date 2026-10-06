# AI Resume stabilization report

**Historical report:** generation has since moved to
[master-document tailoring](ai-resume-master-tailoring.md). Its patch-level validation and original DOCX
artifact path supersede the complete-resume generation and summary-only fallback described here.

## 1. Analysis failure: verified cause and limits of retrospective evidence

The real log's completion event is written after the Anthropic envelope and structured JSON have
deserialized successfully. Therefore the two reported `invalid_analysis` failures were not caused
by markdown fences, missing DTO constructor fields, property casing, enum/string mismatches or an
unknown response wrapper. The only post-deserialization rejection branches were a score outside
0-100, an analysis array/item exceeding its existing bounds, or a matched skill not equal to a
literal source skill (case insensitive).

A definite contract defect existed: the matched-skill validator required exact source labels while
the prompt allowed general factual analysis and the schema accepted arbitrary strings. With 37
source skills, the schema also failed to communicate the 20-item selection rule per category.
The particular offending field/value from those two historical responses cannot be recovered from
token counts or the existing logs; no raw response was stored, and this pass deliberately made no
real Claude call. It would be incorrect to claim a particular rejected skill as proven.

## 2. Exact analysis fix

The prompt now names all eight required camelCase fields, explicitly permits empty arrays, requires
exact source labels for matched skills, and specifies bounds and selection of the 20 most relevant
matches. The existing Anthropic JSON-schema integration now enumerates valid source matched skills
and integer scores 0-100. Supported schema descriptions communicate local length/count limits.
No unsupported `maxItems`/`maxLength` keywords were added.

Safe normalization trims formatting, restores source capitalization, and removes duplicate advisory
items. It does not turn synonyms into source skills, invent matches, clamp scores, silently truncate
oversized arrays, or repair malformed factual content. A single whole JSON code fence may be unwrapped;
prose-wrapped JSON, extra paid-resume fields, missing required fields and malformed JSON still fail.
Provider and application validation share the same analysis validator and rejection code. Event 7406
`AIResumeAnalysisRejected` reports only Category, Reason, Path, Count, HasValue, Model, InputTokens and
OutputTokens in Development. Production errors remain generic and sanitized.

## 3. Generation provenance contract

The owned uploaded-resume parser/source pipeline was not redesigned. The generation request rebuilds
the deterministic catalog from the persisted SourceJson. The prompt explicitly specifies
`evidence[].path`, `evidence[].text`, `evidence[].sourceEvidenceIds`, zero-based generated paths,
one to ten IDs, literal copied facts and exact original contact. The schema requires a nonempty
evidence array and nonempty source-ID arrays, enumerating the exact catalog IDs supplied in the input.

Rewritten summary, experience/project bullets and additional information require supported scoped
evidence. Copied skills, employment tuples, project names/technologies, education and certifications
retain independent exact-source checks; their provenance is also requested. Bullets cannot use
contact/identity/technology or unrelated-entry IDs as authorization. Concise selection of supported
skills/bullets keeps content and duplicated provenance text within the existing token budget.
Factual-grounding rules were not weakened. Full provider-path fixtures prove supported paraphrases
pass and invented employers, roles, dates, technologies, projects, metrics, education and certificates
remain rejected.

## 4. Free analysis and credit safety

Session creation and analysis do not touch credit reservations/consumption. Provider rejection,
invalid analysis, cancellation, timeout and unexpected exception release the owned analysis lease,
leaving a safely retryable Created session.

Generation reserves one credit atomically before provider work; the credit becomes consumed only in
the same database transaction that persists validated content. Failure matrices cover provider
failure, invalid JSON, grounding rejection, timeout, cancellation, persistence-boundary failure and
unexpected exceptions. Each restores one reserved credit, and replay/retry with the same key cannot
double-consume. The persistence fault test injects failure at the consumption write boundary; the
real PostgreSQL repository wraps writes in transactions and clears failed tracked state before the
next write. It is not a live PostgreSQL outage simulation.

The complete uploaded-DOCX fixture workflow checks purchase replay, reservation visibility during
generation, one successful consumption, retrieval, revision-safe editing, PDF/DOCX/TXT downloads,
a second consumption for regeneration, invented-employer rejection, one refund and source immutability.

## 5. Payment idempotency and separation

Package code selects price/quantity server-side: 1/₹19, 5/₹79, 10/₹129, 25/₹249. Orders have the `air_`
namespace and map to persisted AI Resume purchases. Ownership is enforced on authenticated reads.
The server verifies PhonePe order ID, exact minor-unit amount and completed transaction reference
before granting the snapshotted package quantity. Per-user database locks and unique purchase/ledger
keys prevent return, webhook and reconciliation replay from duplicating grants. Pending, failed,
cancelled, non-owner and amount-mismatched cases grant zero credits. AI Resume wallets remain
separate from Membership and Referral Contact Access.

## 6. Timeout, cancellation and 499

The frontend had a definite 30-second timeout versus a 60-second default Claude deadline. The
provider's successful-response validation does not retry `invalid_analysis` or `unsupported_claims`;
its existing transient HTTP loop is separate from validation. The UI dispatches analysis on explicit
click and guards duplicate actions with a synchronous ref lock. Axios retries only authentication
refresh after 401, not deterministic 503 validation errors.

Only analyze/generate/regenerate now have a 180-second browser timeout, covering the supported
120-second maximum provider deadline plus database work. Ordinary calls still use 30 seconds;
provider timeout/retry limits were not globally increased. Error middleware no longer tries to write
an error after the client has disconnected, and handles cancellation occurring during error writing.
Two regression tests cover both races.

No timings were available to apportion the historical ~58 seconds between network and database
cleanup/Neon retries. This pass does not claim to have measured that historical latency. PostgreSQL
writes use the existing short transactional lease/refund operations and transient database strategy;
provider network work is outside those transactions.

## 7. Session state machine

Created -> Analyzing -> Analyzed -> Generating -> Generated. Analysis failures return to Created;
generation failures return to Analyzed or Generated if a prior version exists. Ownership checks guard
lease completion/release. Refresh recovers expired abandoned analysis/generation leases, with one
refund for an expired reservation; polling does not initiate another Claude request.

The frontend follows saved in-progress sessions by polling and suppresses duplicate actions until
the server finishes or recovers the lease. SourceJson remains immutable. Analysis derives its facts
from that same JSON with its existing contact redaction; generation reads the original authoritative
contact and facts. Old failed sessions cannot replace a new session's snapshot or use another session's
generation idempotency key.

## 8. Review, edit and download

Owned persisted content is retrieved independently of provider work. Manual edits check expected
revision, retain edit history, and consume no credits. The editor now treats backend-immutable
employers/roles/dates/project identities/education/certifications as read-only and disables additions
the backend rejects. Summary, skills and supported editable text sections remain available.

PDF, DOCX and TXT render persisted content, including saved edits, without Claude or payment calls.
The download query accepts only the fixed formats; safe filenames and fixed MIME types are produced
by the renderer, and there is no caller-selected filesystem path. Earlier generated versions remain
available in history.

## 9. Frontend/backend fixes

The existing resumeId -> sourceResumeId mapping, jobId-only Job Details requests, pasted external JD,
backend envelopes, generation/edit DTOs and blob downloads were verified. Functional fixes: operation
timeouts, restored-session polling/state updates, duplicate-action suppression while the server works,
checkout keys scoped by session/package and cleared on paid/terminal returns, recognition of owned
`air_` payment returns even if the browser marker is absent, and editor immutable-fact alignment.
Approved colors/fonts/layout were not redesigned; editor controls retain their positions.

## 10. Files changed in this pass

Backend root: `C:\Users\Ashish\Documents\Codex\2026-07-27\you-are-a-principal-net-9`

- `JobPortal.API/Middleware/GlobalExceptionMiddleware.cs`
- `JobPortal.Application/Features/AIResume/AIResumeAnalysisValidator.cs` (new)
- `JobPortal.Application/Features/AIResume/AIResumeContentGuard.cs` (analysis validation delegation only)
- `JobPortal.Application/Features/AIResume/AIResumeService.cs`
- `JobPortal.Infrastructure/AIResume/ClaudeAIResumeProvider.cs`
- `JobPortal.Application.Tests/AIResumeCancellationMiddlewareTests.cs` (new)
- `JobPortal.Application.Tests/AIResumeWorkflowTests.cs`
- `JobPortal.Application.Tests/ClaudeAIResumeProviderTests.cs`
- `docs/ai-resume-provider.md`
- `docs/ai-resume-stabilization.md` (this report)

Frontend root: `D:\career-portal-frontend\career-portal-frontend-Run`

- `src/features/aiResume/api/aiResumeApi.ts`
- `src/features/aiResume/api/aiResumeApi.test.ts`
- `src/features/aiResume/components/ResumeWorkflow.tsx`
- `src/features/aiResume/components/ResumePaymentReturn.tsx`
- `src/features/aiResume/components/ResumeSectionEditor.tsx`
- `src/features/aiResume/components/ResumeSectionEditor.test.tsx` (new)
- `src/features/aiResume/aiResume.workflows.test.tsx`
- `src/pages/PhonePeReturnPage.tsx`

Other repository changes and existing migration files predate this pass and were left intact.

## 11-13. Tests and builds

Final relevant backend regression: 335 passed, 0 failed, 0 skipped. Breakdown: AI Resume/source/
extraction/cancellation 122, candidate 137, payment-return 63, PhonePe gateway 13. The test-project
compilation uses `-p:RunAnalyzersDuringBuild=false` because of existing repository-wide test naming
analyzer diagnostics; this does not disable API Release analyzers.

An earlier broader substring filter selected unrelated Candidate-named methods in other features:
415 passed, 26 failed, 0 skipped. Observed failures included Career Guidance session/trust fixture
eligibility and Playwright's sandbox `spawn EPERM`. They were not fixed as AI Resume issues. The
explicit relevant-class filter above is green; this is not a claim that the entire repository suite
is green.

Frontend AI Resume/payment-return regressions: 114 passed, 0 failed across 8 files. TypeScript check
passed. Changed-file ESLint passed with zero warnings. Frontend production build passed, including
its TypeScript build. Existing build warnings: SignalR PURE comment placement and a vendor chunk
above 500 kB. A development Redux serializable-state timing warning occurred during tests.

Release API build with normal analyzers: passed, 0 warnings, 0 errors, using
`dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore --no-incremental`.

Backend regression command (from backend root):

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --filter "FullyQualifiedName~AIResume|FullyQualifiedName~UploadedResumeSourcePipelineTests|FullyQualifiedName~ResumeTextExtractorTests|FullyQualifiedName~PaymentReturnPathTests|FullyQualifiedName~PhonePeGatewayTests|FullyQualifiedName~JobPortal.Application.Tests.Candidate"
```

Frontend regression command (from frontend root):

```powershell
npm run test -- src/features/aiResume src/api/apiClient.paymentReturn.test.ts src/pages/paymentFlow.test.tsx src/features/payments/api/paymentsApi.test.ts src/features/payments/services/phonePeCheckout.test.ts src/features/payments/utils/paymentReturnUrl.test.ts
npm run typecheck
npm run build
```

## 14-17. Remaining limits and existing data

No known failing code/test blocker remains in the focused AI Resume workflow. Real provider/payment
acceptance remains unperformed by design: no real Claude request or PhonePe payment was made.
Conservative grounding can still reject unsupported provider content, and automatic restoration
requires database availability; an interrupted/unavailable database leaves a lease that refresh/retry
recovers after expiry. External-service success is not guaranteed by mocked tests.

No DB/schema change or migration is required. Existing master resumes need no re-upload. Existing
credits remain valid and were not changed. Start a new session for the clean test instead of reusing
an old session with a stale source snapshot. Normal Debug support remains intact. Temporary scratch/
audit/test-result artifacts from this pass are removed; no bin/obj/dist artifacts are committed.

## 18. One clean manual acceptance procedure

1. Stop Visual Studio debugging and any old API launch through their normal Stop/Ctrl+C controls.
   Do not run two API instances against the same port. From the backend root, run:

   ```powershell
   dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-incremental
   dotnet run --project JobPortal.API/JobPortal.API.csproj -c Release --no-build --launch-profile http
   ```

   The `http` profile sets Development and binds `http://localhost:5259`. Verify the executable path
   ends in `JobPortal.API\bin\Release\net9.0\JobPortal.API.exe` using:

   ```powershell
   Get-Process JobPortal.API | Select-Object Id, Path
   ```

2. From `D:\career-portal-frontend\career-portal-frontend-Run`, run `npm run dev` with the existing
   local API configuration pointing to `http://localhost:5259/api`. Open Vite's printed URL and sign
   in as the candidate with the already uploaded master resume.
3. Open a suitable Job Details page and choose **Tailor Resume for This Job**. This starts a fresh
   workflow rather than loading an old Resume Maker session. Select the existing uploaded resume.
4. Click **Analyze My Resume for This Job** once and wait. The new session should return 201 and
   the same uploaded resume should produce the verified 7405 counts (37 skills, 5 experience,
   5 projects, 2 education, 128 evidence). Analysis should display its match result and consume zero
   credits. Do not repeatedly click Analyze during the request.
5. Continue to Payment. For a complete sandbox purchase test, select the 5-resume ₹79 package and
   finish the existing PhonePe sandbox checkout. Wait for server verification and return to Resume
   Maker. Record the credit baseline; the paid package adds exactly five once. Refresh/revisit the
   return and confirm it does not add credits again. If already using valid purchased credits, the
   purchase can be skipped; they remain usable.
6. Click **Generate Resume — Uses 1 Credit** once. Wait for review content. Available credits fall
   by one and reserved returns to zero. Confirm contact, employers/roles/dates, projects, education,
   technologies and metrics match the master resume. Refresh the page; the saved resume remains.
7. Edit a permitted summary/bullet field and save. It persists with no additional credit charge.
8. Continue to Download. Download PDF, DOCX and TXT from saved content, then download PDF again;
   no further credit is consumed and no AI call is triggered by downloads.
9. Back in Review & Edit, choose **Regenerate with AI**, confirm **Use 1 Credit**, and wait for a
   second saved version. Exactly one more credit is consumed. Continue to Download and download
   the final PDF. This concludes the single complete acceptance run.

No commits, pushes, deployments, database writes against the real runtime, or schema changes were
performed by this stabilization pass. The running API process was not stopped/restarted automatically.
