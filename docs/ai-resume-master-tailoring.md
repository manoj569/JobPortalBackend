# AI Resume master-document tailoring — backend handoff

Implementation date: 2026-10-06. This supersedes the full-resume generation and summary-only
fallback portions of the earlier provider/stabilization documents. No frontend implementation,
real Anthropic/PhonePe request, deployment, commit, push, schema change, or migration was performed.

## Architecture and retained protections

Previously, the owned upload was parsed into `TailoredResumeContent`, Claude constructed a whole
replacement resume, and `ProfessionalResumeDocumentRenderer` rebuilt downloads. That changed the
original presentation and made an unsafe proposal capable of failing the entire operation.

Now the production DI path is:

1. Authorize the candidate and uploaded resume.
2. Copy the existing upload into an immutable, independently stored master and record its SHA-256.
3. Parse that same captured copy with the existing `StructuredResumeSourceParser`; build the existing
   `ResumeEvidenceCatalog`. For DOCX, bind eligible source text to exact, unique paragraph spans.
4. Persist the source facts, evidence, document binding, and master reference in the session.
5. Run the existing free analysis, payment/credit flow, and credit reservation.
6. Make one patch-generation operation; deterministically evaluate each suggestion separately.
7. Retain originals at rejected/omitted targets; rerun the complete `AIResumeContentGuard`.
8. Persist a separate edited DOCX artifact, or an explicitly declared PDF-input fallback artifact.
9. Persist the resume/artifact association and consume one reserved credit in the existing DB write.

Authentication, ownership, analysis bounds, matched-skill validation, evidence catalog, source parser,
existing full grounding rules, PhonePe verification, prices, leases, idempotency, provider HTTP retries,
timeouts, API key/workspace headers, endpoint, model configuration, and structured JSON remain in place.
The patch checks are additional constraints; no existing grounding check was relaxed.

## Claude and patch contracts

`IAIResumeProvider.GenerateTailoringPatchAsync` receives immutable source facts, JD, validated analysis,
source evidence, and the eligible editable targets. The Claude implementation rebuilds the factual
catalog from the source, ignoring an injected caller catalog. It restricts editable targets to canonical
source ID/text/scope matches. Storage keys, document bindings, and artifact hashes are never sent to
Claude or returned in public API DTOs.

Claude returns only:

```json
{
  "replacements": [
    {
      "targetId": "SRC-EXP-001-BULLET-001",
      "originalText": "Exact source bullet",
      "replacementText": "Conservative rewritten bullet",
      "sourceEvidenceIds": ["SRC-EXP-001-BULLET-001"],
      "matchedJdTerms": ["performance"],
      "reason": "Emphasize supported source work"
    }
  ],
  "emphasizedSkillEvidenceIds": ["SRC-SKILL-001"]
}
```

Stable source/evidence IDs identify targets across edits and regeneration. Paragraph positions are
private immutable-document bindings, not public permanent identities. Eligible targets are the
existing nonempty summary, experience bullets, project bullets, and additional-information entries.
Identity, skill, employer, role, date, project-name, education, and certification records are not targets.

The structured JSON schema enumerates target IDs and source evidence IDs. Each proposal must cite its
own target; additional citations must belong to that same source scope/entry. Original text must match
exactly. Invalid IDs, duplicate targets, scope mismatches, unmapped DOCX spans, empty/oversized/control
character text, unsupported claims, and changed or removed metrics reject the proposal.

`ResumePatchGuard` invokes the existing claim guard, preserves the complete metric multiset, and checks
new factual words against cited source vocabulary with a small explicit grammar/verb allowance.
This includes lowercase and Unicode words; it is not fuzzy matching or JD-based authorization.
Finally the complete assembled source projection passes the existing full content guard again.

Claude cannot reconstruct lists, identities, skill categories, section order, or formatting. The old
`GenerateTailoredResumeAsync` provider method remains a compatibility projection over this same patch
operation; it no longer asks Claude for a complete resume. The production service uses the patch method.

## JD matching, partial acceptance, summary, and immutable sections

The unchanged free analysis compares the JD against source facts. Matched skills must be literal
source skills; related experience belongs in partial matches. Missing CUDA, Triton, GPU programming,
and parallel-computing requirements remain advice/gaps, never factual authorization. A proposal's
matched JD terms must literally appear in the JD, but those terms cannot authorize replacement content.

Each proposal has `accepted`, `rejected`, or `original` status. Whitespace-only/no-op changes are not
useful accepted changes. Eight safe proposals among ten apply; two unsafe proposals retain their exact
source text. There is no repair request and no charge per proposal. Zero useful accepted proposals
fails with `no_safe_tailoring_changes` and releases the credit.

An unsafe summary is simply a rejected proposal; the authoritative original remains. A missing
summary creates no editable summary target and is not fabricated. Summary rejection does not affect
other safe proposals, but at least one useful safe change is necessary for paid generation.

All original skills, experiences, projects, education, certifications, contact, roles, dates, and project
technologies remain in the content projection. DOCX skill headings/categories and unparsed content
remain in the original package. Valid supported skill emphasis is review metadata only; it does not
rename, reorder, restyle, or rebuild the original skill section.

## Original storage, persistence, and document behavior

`User.ResumeStorageKey` and `ResumeFileName` reference private `IResumeStorage` objects. Production
`IResumeStorage` is PostgreSQL-backed (`ResumeDocumentBlobs`), with owner-scoped reads, original file
metadata, bounded `bytea` content, and SHA-256 integrity metadata. `LocalResumeStorage` is no longer
registered as the production provider. `ResumeStorage:RootPath` is used only as a transitional,
owner-reference-checked source for lazily promoting an old local object that still exists; new uploads
and AI Resume copies are stored in PostgreSQL. The local filesystem is never the durable target.

Upload persistence and candidate metadata cannot be committed in the same unit because blobs are saved
through an independent context. If metadata persistence fails, an unreferenced blob can remain; it is
preferable to preserve a recoverable document after an ambiguous commit. Replacement/deletion removes
an old blob only after application references have been checked. Every new AI Resume session freezes a
separate copy and parses that copy, so later uploads/deletions cannot change its truth source.

The additive PostgreSQL migration `20261006173014_AddDurableResumeDocumentStorage` creates the private
blob table; it must be applied before deploying the new application version. Existing JSON columns
continue to store version 2 envelopes:

- `AIResumeSession.SourceJson`: unchanged factual source JSON.
- `AIResumeSession.EvidenceJson`: `{ version, evidence, masterDocument }`.
- `TailoredResume.ContentJson`: `{ version, content, decisions, emphasizedSkillEvidenceIds,
  masterDocument, artifact }`.
- `TailoredResumeEdit.ContentJson`: previous complete envelope/artifact association for revision history.

Existing candidate/session/generation/version/revision relations associate the artifact with its owner
and source. Each revision has a separate stored artifact; previous artifacts remain for history.
SHA-256 verifies master/artifact integrity. Failed unpersisted artifacts are cleaned up best-effort;
ambiguous commits never intentionally delete an associated artifact or master. Blob and session writes
cannot form one atomic transaction, so database outages can leave orphan blobs; retention/garbage
collection is not introduced in this scoped change.

Old plain content/evidence records remain readable and downloadable. Old sessions without a frozen
master fail generation safely with `master_snapshot_required` and release the reservation. If an old
candidate file still exists in the configured legacy local directory and a candidate/job-application
record references that exact key, the first owner-scoped read promotes it into PostgreSQL. If no
recoverable object exists (including files lost from an ephemeral Render instance), session creation
returns `resume_source_reupload_required`; the UI should ask the candidate to upload again. Corrupt
files remain `invalid_resume_source`, and unsupported formats use `unsupported_resume_format`. The code
does not silently reread a changed current upload for an old session.

### DOCX

`OriginalResumeDocuments` and `DocxTextEditor` copy the original ZIP package and change only approved
spans in `word/document.xml`. Existing paragraph/run formatting, fonts, sizes, styles, alignment,
indentation, numbering, tables, section/page properties, headers, footers, and other package parts stay
in place. The CareerHarbor renderer never constructs a native DOCX artifact/download.

Targets must occur exactly once, in a single eligible main-document paragraph. Ambiguous or overlapping
spans, fields/drawings/tabs/breaks/tracked changes/content-control paragraphs are not guessed. Unmapped
targets remain untouched; no eligible targets fails before a provider request. Multi-paragraph or
otherwise complex layouts may yield fewer editable targets. Existing parser extraction limits remain.

Replacement characters are distributed over the existing affected text nodes, retaining run properties
and outside text. Mixed bold/italic boundaries follow relative run lengths; semantic emphasis may shift
within a rewritten sentence. Natural wrapping can change. This is original-package preservation, not
a guarantee of identical pagination or pixel layout for arbitrary documents. Tests inspect original and
output package parts/paragraph/run/section properties; no Word visual rendering was performed.

Native DOCX capabilities:

```json
{
  "preservationMode": "original_docx",
  "originalFileType": "docx",
  "layoutPreserved": true,
  "defaultDownloadFormat": "docx",
  "downloadFormats": ["docx", "txt"],
  "limitation": "PDF conversion is unavailable. Text wrapping can change; mixed run styles are retained as closely as practical."
}
```

TXT is a textual projection and does not represent original visual formatting.

### PDF / legacy DOC input

PDF remains supported for analysis and patch generation. Reliable in-place PDF editing and DOCX-to-PDF
conversion are unavailable in the current stack. For PDF/legacy DOC inputs only, the existing professional
renderer persists a PDF fallback and supports rendered PDF/DOCX/TXT downloads. Capabilities declare
`rendered_fallback`, the original file type, `layoutPreserved: false`, and the explicit limitation that
original layout and skill categories are not preserved. Upload DOCX for original-format preservation.
No PDF byte-editing workaround or conversion dependency was added.

## Credits, review, and regeneration

Analysis is free. Package prices/credits remain ₹19/1, ₹79/5, ₹129/10, and ₹249/25. Existing checkout,
purchase verification, payment return, webhook replay protection, and wallet accounting are unchanged.
Generation reserves one credit, calls the provider, validates proposals, stores the artifact, then
persists its association and consumes one credit. Provider/timeout/cancellation/storage/persistence
failure or zero useful safe proposals releases the reservation. Some rejected proposals alongside safe
useful proposals still consume exactly one credit on success. Existing lease recovery remains available.

Accept/original/manual-edit/reset/download cost zero credits and call no provider. Manual edits apply
the same target, evidence, metric, factual-word, and full-content checks. Reaccepting an unsafe suggestion
does not override its validation. Optimistic `expectedRevision` prevents stale edits.

Regeneration is a new one-credit operation using the same immutable original source/master and the
stored session JD, never previous generated/edited claims. To tailor to a different JD, create a new
session. Existing idempotency keys continue to return the already persisted generation without charging
or calling Claude again. HTTP transient retry behavior is retained; there are no automatic repair calls.

## Frontend API handoff

All endpoints use the existing `ApiResponse<T>` wrapper, candidate authorization, and ownership checks,
except the existing anonymous package endpoint. Analysis, packages, credits, checkout, purchase status,
payment return, and session creation request shapes remain compatible.

Additions/behavior changes:

1. Session DTO adds nullable `documentCapabilities`. Generation/detail DTO retains `content` and adds
   nullable `tailoring` and `documentCapabilities`. Historical legacy records have null additions.
2. `templateCode` is `OriginalDocument` for native DOCX, `Professional` for rendered fallbacks.
3. `tailoring.replacements` contains decisions with `proposal`, `status`, `rejectionCategory`,
   `rejectionReason`, and nullable `editedText`. `proposal` contains the fields shown in the patch JSON.
   Original AI suggestions remain available after an edit; `editedText` is the current manual wording.
   Rejected proposals are review information only and have not been inserted into the document.
   `tailoring.emphasizedSkillEvidenceIds` contains only valid source skill IDs.
4. New free endpoint:

   `PATCH /api/ai-resume/resumes/{resumeId}/replacements/{targetId}`

   ```json
   { "expectedRevision": 0, "action": "edit", "replacementText": "Supported replacement" }
   ```

   Actions are `accept`, `original`, `edit`, and `reset`. Only `edit` needs replacement text. Returns
   the updated `AIResumeResponse`; invalid facts/targets return 400, stale revision 409, non-owner 404.
   `accept` restores the original AI proposal after an edit; original/reset restores source text.
5. Existing `PATCH /resumes/{id}` full-content editing remains, but native patch records may change only
   existing editable text. Skills, identities, dates, technology lists, and record/bullet counts remain
   immutable. It applies the same patch safety checks and persists a revised original-format artifact.
6. Download's default is now `?format=original`: native DOCX downloads DOCX, fallback/legacy downloads
   PDF. Explicit native DOCX `?format=pdf` returns 400 because conversion is unavailable. Use the response
   capability/history `downloadFormats`; do not show unavailable formats. Native DOCX/TXT and rendered
   fallback PDF/DOCX/TXT remain ownership protected.
7. Existing generate and regenerate request shapes still contain `idempotencyKey`; history includes
   format availability. A legacy session's `master_snapshot_required` means create a fresh session.

The separate frontend task should show original/suggested/manual wording, literal matched terms,
reason/status/rejections, safe target actions, and capability-specific downloads/PDF fallback guidance.
It must not attempt to modify identity fields or reconstruct skill/experience/project lists.

## Sanitized diagnostics

Development-only events remain `AIResumeSourceSnapshot` 7405 and `AIResumeGroundingRejected` 7404.
Patch rejection metadata contains canonical target/path, category, reason, redacted/valid evidence IDs,
and existence flags. No rejected wording or raw response is logged. New Development-only event
`AIResumeTailoringPatch` 7408 reports proposed/accepted/rejected counts and preservation mode. Existing
model/token/HTTP diagnostics remain sanitized. Credentials, complete resume/JD text, candidate contact
details, and private storage keys are not logged or exposed in new public metadata.

## Validation and exact commands

Commands below run from the backend repository. All provider/payment tests use fake transports.
No unrelated Career Guidance or Playwright failure was investigated or changed.

Initial focused run (before completing fixture/reload coverage): 188 total, 187 passed, 1 failed,
0 skipped. The failure was an obsolete test expecting an arbitrary ungrounded manual summary to pass;
the test now asserts rejection and exercises a grounded free edit.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --filter "FullyQualifiedName~AIResume|FullyQualifiedName~ClaudeAIResumeProviderTests|FullyQualifiedName~UploadedResumeSourcePipelineTests|FullyQualifiedName~ResumeTextExtractorTests" --logger "console;verbosity=minimal"
```

An attempt with normal test-project analyzers did not execute any tests: existing unrelated CA1707
underscore-name errors and CA1859 in `AggregationLockMaintenanceTests` prevent the test project from
compiling. Production/API analyzers were not disabled for the required final build.

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AIResume|FullyQualifiedName~ClaudeAIResumeProviderTests|FullyQualifiedName~UploadedResumeSourcePipelineTests|FullyQualifiedName~ResumeTextExtractorTests|FullyQualifiedName~PaymentReturnPathTests|FullyQualifiedName~PhonePeGatewayTests' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=ai-resume-master-tailoring-validation.trx' --results-directory 'C:\Users\Ashish\Documents\Codex\2026-07-27\ai-resume-master-tailoring-test-results'
```

Expanded focused command: first 278/278 passed; after Unicode factual-word and ambiguous-session-commit
regressions, 281/281 passed. After forwarding diagnostics through the complete final guard, the final
run again had **281 passed, 0 failed, 0 skipped**. The same command was run three times:

```powershell
dotnet test JobPortal.Application.Tests/JobPortal.Application.Tests.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false --filter 'FullyQualifiedName~AIResume|FullyQualifiedName~ClaudeAIResumeProviderTests|FullyQualifiedName~UploadedResumeSourcePipelineTests|FullyQualifiedName~ResumeTextExtractorTests|FullyQualifiedName~PaymentReturnPathTests|FullyQualifiedName~PhonePeGatewayTests|(FullyQualifiedName~CandidateModuleTests&FullyQualifiedName~Resume)' --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=ai-resume-master-tailoring-validation.trx' --results-directory 'C:\Users\Ashish\Documents\Codex\2026-07-27\ai-resume-master-tailoring-test-results'
```

Final TRX suite counts:

| Suite | Passed | Failed |
|---|---:|---:|
| AIResumeContentGuardTests | 46 | 0 |
| AIResumePatchTests | 28 | 0 |
| ClaudeAIResumeProviderTests | 61 | 0 |
| AIResumeWorkflowTests | 28 | 0 |
| AIResumeMasterWorkflowTests | 11 | 0 |
| UploadedResumeSourcePipelineTests | 15 | 0 |
| ResumeTextExtractorTests | 4 | 0 |
| CandidateModuleTests (resume cases) | 10 | 0 |
| PaymentReturnPathTests | 63 | 0 |
| PhonePeGatewayTests | 13 | 0 |
| AIResumeCancellationMiddlewareTests | 2 | 0 |
| **Total** | **281** | **0** |

Realistic fixtures include contact/summary, 15 categorized skills, five experiences, five projects,
two education records, certification, additional information, supported JD concepts, and unsupported
CUDA/Triton/GPU/parallel-computing requirements. Tests prove partial acceptance, original summary at
unsafe proposals, immutable identities/list counts/categories, per-target scope/metric safety,
unchanged DOCX package parts/styles/run properties/sections/header/footer, separate artifact storage,
immutable regeneration, ownership, free safe review/reset, persistence reload, failed artifact cleanup,
idempotency, cancellation, restored credits, and no repair/provider calls on review/rejection.

Build commands executed:

```powershell
# Initial exploratory build; succeeded with 0 warnings/errors.
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore -p:RunAnalyzersDuringBuild=false
# Required normal-analyzer build, repeated after final code stabilization.
dotnet build JobPortal.API/JobPortal.API.csproj -c Release --no-restore --no-incremental
```

Normal-analyzer builds succeeded with **0 warnings and 0 errors**; no analyzer-disable flag was used.
The same build command was repeated after the final guard diagnostic change; the final build passed
in 1 minute 18.84 seconds with **0 warnings and 0 errors**.

## Exact files changed in this architecture pass

- `JobPortal.API/Controllers/AIResumeController.cs`
- `JobPortal.Application/Features/AIResume/AIResumeProviderContracts.cs`
- `JobPortal.Application/Features/AIResume/AIResumeWorkflowContracts.cs`
- `JobPortal.Application/Features/AIResume/AIResumeService.cs`
- `JobPortal.Application/Features/AIResume/ResumeTailoringPatch.cs` (new)
- `JobPortal.Application/Features/AIResume/AIResumeMasterDocumentContracts.cs` (new)
- `JobPortal.Infrastructure/AIResume/ClaudeAIResumeProvider.cs`
- `JobPortal.Infrastructure/AIResume/OriginalResumeDocuments.cs` (new)
- `JobPortal.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `JobPortal.Application.Tests/AIResumePatchTests.cs` (new)
- `JobPortal.Application.Tests/AIResumeMasterWorkflowTests.cs` (new)
- `JobPortal.Application.Tests/ClaudeAIResumeProviderTests.cs`
- `JobPortal.Application.Tests/AIResumeWorkflowTests.cs`
- `JobPortal.Application.Tests/UploadedResumeSourcePipelineTests.cs`
- `docs/ai-resume-master-tailoring.md` (new)
- `docs/ai-resume-provider.md` (supersession note)
- `docs/ai-resume-stabilization.md` (supersession note)

The checkout already contained unrelated modified/untracked files, including prior AI Resume entities,
migrations, payment-return changes, and candidate-resume changes. Those were preserved; they are not
changes made by this architecture pass. Source parser/evidence catalog/full content guard were not changed.

## Readiness and limitations

Backend contracts, DI, native DOCX generation/review/download, explicit PDF fallback, and focused
regressions are ready for the separate frontend implementation task. The final normal API build passed.
There is no new migration requirement. Existing uploaded originals can be reused via a
fresh session. Complex/unmapped DOCX targets remain untouched; DOCX-to-PDF conversion and exact PDF
editing are unavailable. Historical sessions lack a frozen master and must be recreated for generation.
These limits are intentional and exposed in the API; no live-provider or Word visual acceptance test
was performed. Frontend and payment code were untouched in this pass.
