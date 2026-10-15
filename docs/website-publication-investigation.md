# Website publication investigation

Investigated checkout: `bcc72a5`. No production connection, data change, migration, deployment, worker edit, or SerpApi call was performed.

## Finding and release decision

Production attribution is resolved by the subsequently supplied logs: the import returned 200 at 20:37:37 UTC, an admin listing request occurred at 20:39:22, and POST /api/admin/jobs/ca1c769b-d0c2-44a6-bb98-322a9f94bb6f/publish returned 200 at 20:40:01. PublishedAtUtc=20:40:00.610293 matches that manual request. AutoPublishEnabled=false worked as designed. No publication-boundary change is needed or made. The current checkout rejects automatic acquisition publication unless the owning source exists, is enabled, is not deleted, and has AutoPublishEnabled=true.

The interval supplied is 146.023653 seconds. SourceOwnsPublication=true is assigned at Draft creation, even with auto-publication disabled; it is not evidence that an automatic publisher performed the transition. JobsController requires Administrator authorization, so this manual publication is expected and retains all existing validation.

## Publication path inventory

- JobIngestionService creates Draft, leaves PublishedAtUtc unset, and assigns source ownership from either source ID.
- FresherBulkImportService.ImportAsync gates publication queueing on the source AutoPublishEnabled flag. Chunk receipts use Pending when requested, otherwise NotRequested. Replay only queues Pending/Failed receipts when current source auto-publication is enabled. Each attempt calls IJobAutoPublishService.TryPublishAsync.
- JobAutoPublishService.TryPublishAsync reloads the job after taking its publication lock, evaluates source policy and quality, then calls JobService.PublishAsync. EvaluateAsync resolves the acquisition source through FresherAcquisitionStore.GetSourceByIdAsync (AsNoTracking, deleted excluded), and explicitly checks enabled/deleted/auto-publication. No prior receipt grants publication permission. The acquisition branch applies when JobSourceId is null; inspect both IDs in production.
- JobSourceRunner invokes the same automatic service for ATS execution. JobSourceRunWorker and hosted scheduler recover/execute ATS source runs and checkpoints; they do not independently write Job publication state.
- JobService.PublishAsync assigns Published status and PublishedAtUtc, validates job fields, references, expiry and geography, and writes a Publish audit. JobsController exposes it under Administrator authorization. Acquisition AutoPublishEnabled does not prohibit this intended manual override. Its ATS source validation remains unchanged.
- JobReferralService.ReviewAsync directly assigns Published/PublishedAtUtc on authorized referral approval. Inspect any linked referral before attributing publication to a background service.
- JobService unpublish/close/archive/hide operations can clear SourceOwnsPublication; unpublish also clears PublishedAtUtc. Other PublishedAtUtc assignments concern portfolios/interview insights, not Jobs. Persistence publication references outside migrations are reads; no independent repair SQL was found.
- No acquisition receipt polling hosted service exists in this checkout. Publication retry is request replay inside FresherBulkImportService. CompleteRunAsync aggregates receipts; UpdatePublicationAsync changes receipt state only, not Job.Status.

## Data quality (separate; no mapping changes)

ComposeLocation concatenated LocationText, City, State, Country, RemoteEligibility and WorkplaceType. The retained website contract fixture supplies LocationText="Multiple project locations across India" and Country="India", producing the reported duplication. The only production-code change omits Country from the composed website display location when LocationText already contains that complete country token (case-insensitive). CountryCodes and eligibility inputs are preserved, and non-website imports keep the existing composition. Tests cover the exact fixture, case differences, country addition when absent, and avoiding substring matches.

FresherImportCandidate has no duration or stipend members. Its mapper sends neither duration nor salary to RawExternalJob. It reconstructs SourceMetadataJson from selected provenance/eligibility values, rather than retaining arbitrary sourceMetadata. Consequently extracted facts held only in unrecognized JSON members or unselected metadata do not persist. The local contract fixture contains neither value; the exact production payload is needed to establish where those facts were dropped upstream. Job has InternshipDurationMonths and salary amount/currency/period fields. Explicit, validated duration=1 month and stipend=20000 INR/month could use these existing fields through a separate contract/mapping change; RawExternalJob needs a duration mapping too. Preserve “as announced” as provenance/text rather than treating it as a numeric fact. Do not repurpose experience, education or benefits fields. Existing description-based salary classification cannot recover facts absent from the description. No schema expansion is justified by this evidence.

CategoryId is optional. ImportAsync resolves an explicit ID or a supplied category name/slug; omitted values remain null. The local website fixture supplies neither. The mapper does not infer a taxonomy category from the title. Invalid supplied categories produce a rejection instead of silently mapping to null. Verify the production payload before claiming the exact omission occurred there.

CanonicalJobUrl and ApplicationUrl can intentionally identify the verified application portal while DiscoveredUrl retains the article. The mapper preserves those separate supplied URLs and receipts. This agrees with the current website contract. A shared portal root can deduplicate distinct vacancies, so that design requires vacancy identity review separately; do not change canonicalization in this fix.

## Worker response semantics

HTTP 200 means the batch response was handled, not that every item succeeded. Validate response shape, ImportId, counts and every item outcome before acknowledging an outbox row. ACQUISITION_SOURCE_DISABLED with accepted=0/failed=1 is a failed submission: preserve the stored bytes and ImportId, retain a retryable or blocked state, and surface the source configuration issue. Do not call finish(). For mixed batches, retain item-level failures and distinguish terminal validation rejection from transient/configuration failure. Replay unchanged bytes with the same ImportId through the backend's durable receipt semantics; do not blindly mint new IDs or assume failed terminal receipts are reprocessed. published=0 alone is expected for this controlled source and must not cause failure. No Python changes were made.

## Validation and release assessment

SAFE_TO_DEPLOY for the limited website display-location fix. No publication behavior, ATS ingestion, SerpApi behavior, production data, schema or migrations changed. Existing incident data is not repaired automatically.

72 selected tests passed, zero failed/skipped: WebsiteCrawlerContractTests, AcquisitionPublicationEligibilityTests, SourceAutoPublicationTests and JobAutoPublishServiceTests. Added three location cases plus an exact-fixture assertion, four current-source policy cases and one manual override case. These cover Draft retention for disabled auto-publication, permitted automatic publication, disabled/deleted sources, manual publication and blocked geography. There is no acquisition background recovery service to exercise in this checkout. Test compilation used RunAnalyzers=false; this is not a claim of a full normal-analyzer solution build. The first sandbox test run aborted on test-host connection timeout; the compiled tests passed outside the sandbox in 22 seconds. Evidence: `../website-data-quality-verified.log` and `JobPortal.Application.Tests/TestResults/website-data-quality-verified.trx`. `git diff --check` passed.

## Production attribution and verification (read-only)

The supplied production logs already establish manual publication. Optional reconciliation SQL is provided in `website-publication-readonly.sql`; it was not run against production. Do not change the already published incident row.

After review, deploy the tested location mapping revision through the existing release process. No migration is required. On the next independently authorized new website import with LocationText="Multiple project locations across India" and Country="India", read back Location without the duplicate suffix, Draft status, null PublishedAtUtc, and receipt NotRequested. CountryCodes must still contain IN. Do not manually publish/unpublish or replay the incident row for verification. Existing rows will not be rewritten. Confirm ordinary website locations without a country token still append Country, and observe existing ATS/SerpApi flows without initiating discovery. Duration/stipend/category are unchanged by this patch and need the exact production payload plus a separately reviewed mapping contract before implementation.
